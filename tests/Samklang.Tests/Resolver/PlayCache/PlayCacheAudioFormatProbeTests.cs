using System.Buffers.Binary;
using System.Text;
using Samklang.Resolver.PlayCache;
using Xunit;

namespace Samklang.Tests.Resolver.PlayCache;

/// <summary>
/// Exercises <see cref="PlayCacheAudioFormatProbe"/> against hand-built minimal ISO-BMFF (.m4a)
/// and raw MPEG (.mp3) byte streams — synthetic fixtures built in-memory rather than checked-in
/// binary files, so every byte's meaning stays visible at the call site.
/// </summary>
public sealed class PlayCacheAudioFormatProbeTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "samklang-playcache-probe-tests", Guid.NewGuid().ToString("N"));
    private readonly PlayCacheAudioFormatProbe _probe = new();

    public PlayCacheAudioFormatProbeTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
        }
    }

    private string WriteFile(string fileName, byte[] bytes)
    {
        var path = Path.Combine(_directory, fileName);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    // --- box builders ---

    private static byte[] Concat(params byte[][] parts) => parts.SelectMany(p => p).ToArray();

    private static byte[] BuildBox(string type, params byte[][] payloadParts)
    {
        var payload = Concat(payloadParts);
        var buffer = new byte[8 + payload.Length];
        BinaryPrimitives.WriteUInt32BigEndian(buffer, (uint)buffer.Length);
        Encoding.ASCII.GetBytes(type).CopyTo(buffer, 4);
        payload.CopyTo(buffer, 8);
        return buffer;
    }

    private static byte[] BuildHdlr(string handlerType) =>
        BuildBox(
            "hdlr",
            new byte[4], // version + flags
            new byte[4], // pre_defined
            Encoding.ASCII.GetBytes(handlerType), // handler_type
            new byte[12]); // reserved

    private static byte[] BuildAudioSampleEntryCommonFields(int channelCount, int outerSampleRateHz)
    {
        var buffer = new byte[28]; // 6 reserved + 2 data_reference_index + 8 reserved = 16 zero bytes
        BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(16, 2), (ushort)channelCount);
        BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(18, 2), 16); // samplesize placeholder
        // pre_defined(2) + reserved(2) at offset 20..24 stay zero
        BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(24, 4), (uint)outerSampleRateHz << 16);
        return buffer;
    }

    private static byte[] BuildAlacMagicCookie(byte bitDepth, byte numChannels, int sampleRateHz)
    {
        var payload = new byte[24];
        // frameLength(4) left as 0 — not exercised by the probe
        payload[4] = 2; // compatibleVersion
        payload[5] = bitDepth;
        payload[6] = 40; // pb
        payload[7] = 10; // mb
        payload[8] = 14; // kb
        payload[9] = numChannels;
        // maxRun(2), maxFrameBytes(4), avgBitRate(4) left as 0 — not exercised by the probe
        BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(20, 4), (uint)sampleRateHz);
        return BuildBox("alac", payload);
    }

    private static byte[] BuildAlacSampleEntry(int outerSampleRateHz, byte bitDepth, int cookieSampleRateHz) =>
        BuildBox("alac", BuildAudioSampleEntryCommonFields(2, outerSampleRateHz), BuildAlacMagicCookie(bitDepth, 2, cookieSampleRateHz));

    private static byte[] BuildMp4aSampleEntry(int sampleRateHz) =>
        BuildBox("mp4a", BuildAudioSampleEntryCommonFields(2, sampleRateHz));

    /// <summary>
    /// A minimal "drms" (FairPlay-protected AAC) sample entry — the real sample entry type found
    /// in every cache file on a real Windows install (issue #20). Real entries carry DRM child
    /// boxes (esds, sinf, uuid, ...) after the common fields, but those aren't read by the probe
    /// (only the outer AudioSampleEntry common fields are), so this fixture omits them.
    /// </summary>
    private static byte[] BuildDrmsSampleEntry(int sampleRateHz) =>
        BuildBox("drms", BuildAudioSampleEntryCommonFields(2, sampleRateHz));

    private static byte[] BuildEncaSampleEntry(int sampleRateHz) =>
        BuildBox("enca", BuildAudioSampleEntryCommonFields(2, sampleRateHz));

    private static byte[] BuildM4aFile(byte[] sampleEntryBox, string handlerType = "soun")
    {
        var stsd = BuildBox("stsd", new byte[8], sampleEntryBox); // version+flags(4) + entry_count(4)
        var stbl = BuildBox("stbl", stsd);
        var minf = BuildBox("minf", stbl);
        var mdia = BuildBox("mdia", BuildHdlr(handlerType), minf);
        var trak = BuildBox("trak", mdia);
        var moov = BuildBox("moov", trak);
        var ftyp = BuildBox("ftyp", Encoding.ASCII.GetBytes("M4A "), new byte[4], Encoding.ASCII.GetBytes("M4A mp42isom"));
        return Concat(ftyp, moov);
    }

    // --- .m4a (ISO-BMFF) ---

    [Fact]
    public void Probe_reads_the_real_bit_depth_and_sample_rate_from_an_alac_magic_cookie()
    {
        var sampleEntry = BuildAlacSampleEntry(outerSampleRateHz: 44_100, bitDepth: 24, cookieSampleRateHz: 96_000);
        var path = WriteFile("track.m4a", BuildM4aFile(sampleEntry));

        var result = _probe.Probe(path);

        Assert.NotNull(result);
        // The cookie's own sample rate wins over the outer (placeholder) sample-entry rate.
        Assert.Equal(96_000, result!.SampleRateHz);
        Assert.Equal(24, result.BitDepth);
    }

    [Fact]
    public void Probe_reports_the_outer_sample_rate_and_a_null_bit_depth_for_an_aac_mp4a_entry()
    {
        var sampleEntry = BuildMp4aSampleEntry(48_000);
        var path = WriteFile("track.m4a", BuildM4aFile(sampleEntry));

        var result = _probe.Probe(path);

        Assert.NotNull(result);
        Assert.Equal(48_000, result!.SampleRateHz);
        Assert.Null(result.BitDepth);
    }

    [Fact]
    public void Probe_reads_the_sample_rate_from_a_real_world_drms_FairPlay_sample_entry()
    {
        // Regression test for issue #20: a real Windows install's cache files all had a "drms"
        // stsd sample entry, which the probe used to reject outright.
        var sampleEntry = BuildDrmsSampleEntry(44_100);
        var path = WriteFile("track.m4p", BuildM4aFile(sampleEntry));

        var result = _probe.Probe(path);

        Assert.NotNull(result);
        Assert.Equal(44_100, result!.SampleRateHz);
        // FairPlay-protected AAC is still lossy from a container-header point of view — no PCM
        // bit depth to report, same as plain mp4a.
        Assert.Null(result.BitDepth);
    }

    [Fact]
    public void Probe_reads_the_sample_rate_from_a_generic_enca_encrypted_sample_entry()
    {
        // "enca" is accepted defensively (its real codec is named by a nested sinf/frma box this
        // probe doesn't read) — unlike "drms" this was NOT observed on real hardware in issue #20,
        // but falls through the same outer-sample-rate handling as mp4a/drms.
        var sampleEntry = BuildEncaSampleEntry(48_000);
        var path = WriteFile("track.m4a", BuildM4aFile(sampleEntry));

        var result = _probe.Probe(path);

        Assert.NotNull(result);
        Assert.Equal(48_000, result!.SampleRateHz);
        Assert.Null(result.BitDepth);
    }

    [Fact]
    public void Probe_reads_a_m4p_file_the_same_as_an_equivalent_m4a_file()
    {
        // .m4p is the real extension for cache files on a real Windows install (issue #20) — the
        // probe used to return null for it outright regardless of container contents.
        var sampleEntry = BuildDrmsSampleEntry(44_100);
        var path = WriteFile("track.m4p", BuildM4aFile(sampleEntry));

        var result = _probe.Probe(path);

        Assert.NotNull(result);
        Assert.Equal(44_100, result!.SampleRateHz);
    }

    [Fact]
    public void Probe_returns_null_when_the_only_track_is_not_an_audio_handler()
    {
        var sampleEntry = BuildMp4aSampleEntry(48_000);
        var path = WriteFile("track.m4a", BuildM4aFile(sampleEntry, handlerType: "vide"));

        Assert.Null(_probe.Probe(path));
    }

    [Fact]
    public void Probe_returns_null_for_a_truncated_file_instead_of_throwing()
    {
        var wholeFile = BuildM4aFile(BuildAlacSampleEntry(44_100, 24, 96_000));
        var path = WriteFile("track.m4a", wholeFile[..(wholeFile.Length / 2)]);

        Assert.Null(_probe.Probe(path));
    }

    [Fact]
    public void Probe_returns_null_for_a_file_with_no_moov_box()
    {
        var path = WriteFile("track.m4a", BuildBox("ftyp", Encoding.ASCII.GetBytes("M4A ")));

        Assert.Null(_probe.Probe(path));
    }

    [Fact]
    public void Probe_falls_back_to_the_outer_sample_rate_when_the_alac_cookie_child_box_is_missing()
    {
        // The outer field's integer part is only 16 bits wide (max 65535), so this fallback path
        // is only exercisable/meaningful for a rate that actually fits in it — real encoders rely
        // on the magic cookie's own (unbounded) sampleRate for hi-res rates precisely because this
        // field can't represent them, which is exactly why the cookie is preferred when present.
        var sampleEntry = BuildBox("alac", BuildAudioSampleEntryCommonFields(2, 44_100));
        var path = WriteFile("track.m4a", BuildM4aFile(sampleEntry));

        var result = _probe.Probe(path);

        Assert.NotNull(result);
        Assert.Equal(44_100, result!.SampleRateHz);
        Assert.Null(result.BitDepth);
    }

    // --- .mp3 (raw MPEG frames) ---

    private static byte[] BuildMp3Frame(int versionBits, int sampleRateIndex)
    {
        var frame = new byte[4];
        frame[0] = 0xFF;
        frame[1] = (byte)(0xE0 | (versionBits << 3) | (0x01 << 1)); // sync continuation + version + Layer III
        frame[2] = (byte)((0x09 << 4) | (sampleRateIndex << 2)); // an arbitrary non-reserved bitrate index + sample-rate index
        frame[3] = 0xC4;
        return frame;
    }

    [Fact]
    public void Probe_reads_an_mpeg1_sample_rate_from_a_bare_frame_header()
    {
        var path = WriteFile("track.mp3", BuildMp3Frame(versionBits: 0b11, sampleRateIndex: 0)); // MPEG1, 44100 Hz

        var result = _probe.Probe(path);

        Assert.NotNull(result);
        Assert.Equal(44_100, result!.SampleRateHz);
        Assert.Null(result.BitDepth);
    }

    [Fact]
    public void Probe_reads_an_mpeg2_sample_rate()
    {
        var path = WriteFile("track.mp3", BuildMp3Frame(versionBits: 0b10, sampleRateIndex: 1)); // MPEG2, 24000 Hz

        var result = _probe.Probe(path);

        Assert.NotNull(result);
        Assert.Equal(24_000, result!.SampleRateHz);
    }

    [Fact]
    public void Probe_reads_an_mpeg25_sample_rate()
    {
        var path = WriteFile("track.mp3", BuildMp3Frame(versionBits: 0b00, sampleRateIndex: 2)); // MPEG2.5, 8000 Hz

        var result = _probe.Probe(path);

        Assert.NotNull(result);
        Assert.Equal(8_000, result!.SampleRateHz);
    }

    [Fact]
    public void Probe_skips_a_leading_id3v2_tag_before_finding_the_frame_sync()
    {
        var id3Header = new byte[]
        {
            (byte)'I', (byte)'D', (byte)'3', 3, 0, 0,
            0x00, 0x00, 0x00, 0x0A, // synchsafe size = 10
        };
        var id3Body = new byte[10]; // arbitrary tag content
        var frame = BuildMp3Frame(versionBits: 0b11, sampleRateIndex: 0);
        var path = WriteFile("track.mp3", Concat(id3Header, id3Body, frame));

        var result = _probe.Probe(path);

        Assert.NotNull(result);
        Assert.Equal(44_100, result!.SampleRateHz);
    }

    [Fact]
    public void Probe_returns_null_when_no_frame_sync_is_found()
    {
        var path = WriteFile("track.mp3", new byte[256]);

        Assert.Null(_probe.Probe(path));
    }

    [Fact]
    public void Probe_returns_null_for_an_unsupported_extension()
    {
        var path = WriteFile("track.wav", new byte[16]);

        Assert.Null(_probe.Probe(path));
    }
}
