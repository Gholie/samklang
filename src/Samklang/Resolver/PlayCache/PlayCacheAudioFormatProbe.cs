using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace Samklang.Resolver.PlayCache;

/// <summary>
/// Reads sample rate and bit depth straight out of an audio file's container header for the
/// container types the Apple Music Windows app's PlayCache stores — ISO-BMFF <c>.m4a</c>/<c>.m4p</c>
/// (AAC/ALAC, including FairPlay-protected variants — see <see cref="ReadStsdFirstSampleEntry"/>)
/// and raw <c>.mp3</c> frames — by walking box/frame headers only; no audio sample is ever
/// decoded. Bit depth is null when the container doesn't expose one (AAC/MP3 are lossy and carry
/// no PCM bit depth) — only ALAC's magic-cookie config box exposes a real one.
/// </summary>
public sealed class PlayCacheAudioFormatProbe : IAudioFileFormatProbe
{
    public AudioFileFormat? Probe(string filePath)
    {
        // Apple Music may still hold the file open (writing a download, or reading it for
        // playback); ReadWrite|Delete sharing lets a probe succeed alongside that instead of
        // spuriously throwing for the common case.
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        try
        {
            return Path.GetExtension(filePath).ToLowerInvariant() switch
            {
                // .m4p is the real on-disk extension for FairPlay-protected PlayCache entries
                // (issue #20) — same ISO-BMFF container shape as .m4a, just with a DRM-flavored
                // stsd sample entry (see ReadStsdFirstSampleEntry).
                ".m4a" or ".mp4" or ".m4p" => ProbeIsoBmff(stream),
                ".mp3" => ProbeMp3(stream),
                _ => null,
            };
        }
        catch (Exception ex) when (ex is not IOException and not UnauthorizedAccessException)
        {
            // A malformed/unexpected container structure (bad box size, truncated header, ...) —
            // treat as "nothing readable" rather than propagating, so a corrupt cache entry can't
            // be mistaken by a caller for a locked file that's worth retrying later.
            return null;
        }
    }

    // --- ISO-BMFF (.m4a/.mp4): moov -> trak (audio) -> mdia -> minf -> stbl -> stsd -> sample entry ---

    private readonly record struct Box(string Type, long PayloadStart, long PayloadEnd);

    private static AudioFileFormat? ProbeIsoBmff(Stream stream)
    {
        if (FindBox(stream, "moov", 0, stream.Length) is not { } moov)
        {
            return null;
        }

        foreach (var trak in FindBoxes(stream, "trak", moov.PayloadStart, moov.PayloadEnd))
        {
            if (FindBox(stream, "mdia", trak.PayloadStart, trak.PayloadEnd) is not { } mdia ||
                !IsAudioHandler(stream, mdia))
            {
                continue;
            }

            if (FindBox(stream, "minf", mdia.PayloadStart, mdia.PayloadEnd) is not { } minf ||
                FindBox(stream, "stbl", minf.PayloadStart, minf.PayloadEnd) is not { } stbl ||
                FindBox(stream, "stsd", stbl.PayloadStart, stbl.PayloadEnd) is not { } stsd)
            {
                continue;
            }

            if (ReadStsdFirstSampleEntry(stream, stsd) is { } sampleEntry &&
                ParseAudioSampleEntry(stream, sampleEntry) is { } format)
            {
                return format;
            }
        }

        return null;
    }

    /// <summary>Whether the trak's <c>mdia/hdlr</c> box declares this an audio (<c>soun</c>) track, not e.g. video.</summary>
    private static bool IsAudioHandler(Stream stream, Box mdia)
    {
        if (FindBox(stream, "hdlr", mdia.PayloadStart, mdia.PayloadEnd) is not { } hdlr)
        {
            return false;
        }

        const int HandlerTypeOffset = 8; // version+flags(4) + pre_defined(4)
        if (hdlr.PayloadEnd - hdlr.PayloadStart < HandlerTypeOffset + 4)
        {
            return false;
        }

        stream.Position = hdlr.PayloadStart + HandlerTypeOffset;
        Span<byte> typeBuffer = stackalloc byte[4];
        return stream.Read(typeBuffer) == 4 && Encoding.ASCII.GetString(typeBuffer) == "soun";
    }

    private static Box? ReadStsdFirstSampleEntry(Stream stream, Box stsd)
    {
        const int EntryCountFieldsLength = 8; // version+flags(4) + entry_count(4)
        var childStart = stsd.PayloadStart + EntryCountFieldsLength;
        if (childStart >= stsd.PayloadEnd)
        {
            return null;
        }

        stream.Position = childStart;
        if (ReadBoxHeader(stream, stsd.PayloadEnd) is not { } header)
        {
            return null;
        }

        // "drms" (FairPlay-protected AAC) is the real sample entry type found in every one of a
        // real Windows install's .m4p cache entries (issue #20) — confirmed via a hand-rolled
        // harness against a real file. The box tree there was
        // moov>trak>mdia>minf>stbl>stsd>'drms'{esds,dmix,udi2,udc2,udex,sbtd,sinf,uuid,free}, and
        // critically the *outer* AudioSampleEntry common fields this probe reads (channelcount /
        // samplesize / samplerate — see ParseAudioSampleEntry) sit at the same fixed offsets as
        // any other sample entry and parsed a real 44100 Hz value even under "drms". Only the
        // *sample payload* is FairPlay-encrypted; this container-header metadata is not, so
        // reading it here never touches DRM-protected bytes and never decodes/decrypts audio —
        // same guarantee as the mp4a/alac paths.
        //
        // "enca" (generic encrypted sample entry, ISO/IEC 14496-12) is accepted defensively for
        // the same reason — but this has NOT been observed on real hardware (the verified real
        // cache only ever produced "drms" entries), so treat it as an untested, best-effort
        // extension rather than a confirmed-correct path. For both encrypted entry types the real
        // underlying codec is recovered from the nested sinf/frma box (see ParseAudioSampleEntry):
        // a frma naming "alac" gets the full ALAC magic-cookie treatment (real bit depth/rate),
        // anything else falls through to the lossy "outer sample rate, no bit depth" handling —
        // the verified real cache's "drms" entries all wrapped lossy AAC.
        return header.Type is "mp4a" or "alac" or "drms" or "enca" ? new Box(header.Type, header.PayloadStart, header.BoxEnd) : null;
    }

    private static AudioFileFormat? ParseAudioSampleEntry(Stream stream, Box sampleEntry)
    {
        // AudioSampleEntry common fields (ISO/IEC 14496-12 §8.5.2): 6-byte reserved, 2-byte
        // data_reference_index, 8-byte reserved, 2-byte channelcount, 2-byte samplesize, 2-byte
        // pre_defined, 2-byte reserved, then a 16.16 fixed-point samplerate at byte offset 24.
        const int CommonFieldsLength = 28;
        const int SampleRateFieldOffset = 24;
        if (sampleEntry.PayloadEnd - sampleEntry.PayloadStart < CommonFieldsLength)
        {
            return null;
        }

        stream.Position = sampleEntry.PayloadStart + SampleRateFieldOffset;
        Span<byte> rateBuffer = stackalloc byte[4];
        if (stream.Read(rateBuffer) != 4)
        {
            return null;
        }

        var outerSampleRateHz = (int)(BinaryPrimitives.ReadUInt32BigEndian(rateBuffer) >> 16);

        var childrenStart = sampleEntry.PayloadStart + CommonFieldsLength;

        // Encrypted sample entries ("drms" FairPlay, "enca" generic) wrap a real codec whose
        // four-char type is named by a nested sinf/frma box (ISO/IEC 14496-12 §8.12) — recover it
        // so an encrypted ALAC entry gets the same magic-cookie treatment a plain "alac" entry
        // does instead of being misread as lossy. The frma box is plain container metadata, never
        // encrypted payload, so reading it keeps the "no DRM-protected byte is ever touched"
        // guarantee.
        var codec = sampleEntry.Type is "drms" or "enca"
            ? ReadSinfOriginalFormat(stream, childrenStart, sampleEntry.PayloadEnd) ?? sampleEntry.Type
            : sampleEntry.Type;

        if (codec == "alac")
        {
            // The magic-cookie config box sits among the sample entry's child boxes (first child
            // for a plain "alac" entry; alongside sinf/esds/... for an encrypted one).
            if (FindBox(stream, "alac", childrenStart, sampleEntry.PayloadEnd) is { } cookieBox &&
                ParseAlacMagicCookie(stream, cookieBox) is { } cookieFormat)
            {
                return cookieFormat;
            }

            // No readable magic cookie — still report the outer sample-entry rate, just without
            // a known bit depth, rather than giving up on the whole file.
            return outerSampleRateHz > 0 ? new AudioFileFormat(outerSampleRateHz, null) : null;
        }

        // mp4a (AAC) — plain or recovered from an encrypted entry's frma — is lossy from this
        // probe's point of view: no PCM bit depth to report, just the outer sample-entry rate.
        return outerSampleRateHz > 0 ? new AudioFileFormat(outerSampleRateHz, null) : null;
    }

    /// <summary>
    /// Reads the original (pre-encryption) sample-entry type out of an encrypted sample entry's
    /// <c>sinf/frma</c> box — e.g. "mp4a" for a FairPlay AAC "drms" entry — or null when the box
    /// is missing/truncated.
    /// </summary>
    private static string? ReadSinfOriginalFormat(Stream stream, long rangeStart, long rangeEnd)
    {
        if (FindBox(stream, "sinf", rangeStart, rangeEnd) is not { } sinf ||
            FindBox(stream, "frma", sinf.PayloadStart, sinf.PayloadEnd) is not { } frma ||
            frma.PayloadEnd - frma.PayloadStart < 4)
        {
            return null;
        }

        stream.Position = frma.PayloadStart;
        Span<byte> formatBuffer = stackalloc byte[4];
        return stream.Read(formatBuffer) == 4 ? Encoding.ASCII.GetString(formatBuffer) : null;
    }

    /// <summary>
    /// Parses the ALAC magic cookie (<c>ALACSpecificConfig</c>) — the only source of ALAC's real
    /// bit depth; the outer sample-entry box only ever carries a placeholder <c>samplesize</c>.
    /// </summary>
    private static AudioFileFormat? ParseAlacMagicCookie(Stream stream, Box cookie)
    {
        // frameLength(4), compatibleVersion(1), bitDepth(1), pb(1), mb(1), kb(1), numChannels(1),
        // maxRun(2), maxFrameBytes(4), avgBitRate(4), sampleRate(4) — all big-endian.
        const int ConfigLength = 24;
        if (cookie.PayloadEnd - cookie.PayloadStart < ConfigLength)
        {
            return null;
        }

        stream.Position = cookie.PayloadStart;
        Span<byte> config = stackalloc byte[ConfigLength];
        if (stream.Read(config) != ConfigLength)
        {
            return null;
        }

        var bitDepth = config[5];
        var sampleRateHz = (int)BinaryPrimitives.ReadUInt32BigEndian(config[20..24]);

        return sampleRateHz > 0 ? new AudioFileFormat(sampleRateHz, bitDepth) : null;
    }

    private readonly record struct BoxHeader(string Type, long PayloadStart, long BoxEnd);

    /// <summary>
    /// Reads one box header starting at the stream's *current* position (caller must seek first).
    /// Handles the 64-bit large-size and "extends to end of range" (<c>size == 0</c>) forms.
    /// Returns null on anything that doesn't check out (truncated read, size running past
    /// <paramref name="rangeEnd"/>) rather than trusting a corrupt/adversarial size.
    /// </summary>
    private static BoxHeader? ReadBoxHeader(Stream stream, long rangeEnd)
    {
        var boxStart = stream.Position;
        if (boxStart + 8 > rangeEnd)
        {
            return null;
        }

        Span<byte> header = stackalloc byte[8];
        if (stream.Read(header) != 8)
        {
            return null;
        }

        long size = BinaryPrimitives.ReadUInt32BigEndian(header);
        var type = Encoding.ASCII.GetString(header[4..8]);
        long headerSize = 8;

        if (size == 1)
        {
            Span<byte> largeSize = stackalloc byte[8];
            if (stream.Read(largeSize) != 8)
            {
                return null;
            }

            size = BinaryPrimitives.ReadInt64BigEndian(largeSize);
            headerSize = 16;
        }
        else if (size == 0)
        {
            size = rangeEnd - boxStart;
        }

        var boxEnd = boxStart + size;
        if (size < headerSize || boxEnd > rangeEnd)
        {
            return null;
        }

        return new BoxHeader(type, stream.Position, boxEnd);
    }

    /// <summary>
    /// Walks sibling boxes in <c>[rangeStart, rangeEnd)</c>. Tracks its own explicit position and
    /// re-seeks before every read rather than trusting the stream's ambient position across a
    /// <c>yield</c> — callers (e.g. <see cref="ProbeIsoBmff"/>'s per-trak loop body) seek the same
    /// shared stream elsewhere between iterations, so relying on ambient position here would read
    /// garbage.
    /// </summary>
    private static IEnumerable<Box> WalkBoxes(Stream stream, long rangeStart, long rangeEnd)
    {
        var position = rangeStart;
        while (position + 8 <= rangeEnd)
        {
            stream.Position = position;
            if (ReadBoxHeader(stream, rangeEnd) is not { } header)
            {
                yield break;
            }

            yield return new Box(header.Type, header.PayloadStart, header.BoxEnd);

            if (header.BoxEnd <= position)
            {
                yield break; // malformed/zero-progress box — avoid an infinite loop
            }

            position = header.BoxEnd;
        }
    }

    private static Box? FindBox(Stream stream, string type, long rangeStart, long rangeEnd)
    {
        foreach (var box in WalkBoxes(stream, rangeStart, rangeEnd))
        {
            if (box.Type == type)
            {
                return box;
            }
        }

        return null;
    }

    private static IEnumerable<Box> FindBoxes(Stream stream, string type, long rangeStart, long rangeEnd) =>
        WalkBoxes(stream, rangeStart, rangeEnd).Where(box => box.Type == type);

    // --- .mp3: raw MPEG audio frames, no container ---

    private static readonly int[] Mpeg1SampleRates = [44100, 48000, 32000];
    private static readonly int[] Mpeg2SampleRates = [22050, 24000, 16000];
    private static readonly int[] Mpeg25SampleRates = [11025, 12000, 8000];

    private static AudioFileFormat? ProbeMp3(Stream stream)
    {
        var scanStart = SkipId3v2Header(stream);

        // Scan for the first valid 11-bit frame sync within a bounded window — enough to skip
        // any leading padding/junk without reading anywhere near actual audio sample data.
        const int MaxScanBytes = 8192;
        Span<byte> header = stackalloc byte[4];

        for (var offset = 0; offset < MaxScanBytes; offset++)
        {
            stream.Position = scanStart + offset;
            if (stream.Read(header) != 4)
            {
                return null;
            }

            if (header[0] != 0xFF || (header[1] & 0xE0) != 0xE0)
            {
                continue;
            }

            var versionBits = (header[1] >> 3) & 0x03;
            var sampleRateIndex = (header[2] >> 2) & 0x03;
            if (sampleRateIndex == 0x03)
            {
                continue; // reserved
            }

            var sampleRateHz = versionBits switch
            {
                0b11 => Mpeg1SampleRates[sampleRateIndex],
                0b10 => Mpeg2SampleRates[sampleRateIndex],
                0b00 => Mpeg25SampleRates[sampleRateIndex],
                _ => 0, // 0b01 is reserved
            };

            if (sampleRateHz > 0)
            {
                // MP3 is lossy — no PCM bit depth to report.
                return new AudioFileFormat(sampleRateHz, null);
            }
        }

        return null;
    }

    private static long SkipId3v2Header(Stream stream)
    {
        stream.Position = 0;
        Span<byte> header = stackalloc byte[10];
        if (stream.Read(header) != 10 || header[0] != (byte)'I' || header[1] != (byte)'D' || header[2] != (byte)'3')
        {
            return 0;
        }

        // Synchsafe 28-bit size: 7 significant bits per byte across the last 4 header bytes.
        var size = ((header[6] & 0x7F) << 21) | ((header[7] & 0x7F) << 14) | ((header[8] & 0x7F) << 7) | (header[9] & 0x7F);
        return 10 + size;
    }
}
