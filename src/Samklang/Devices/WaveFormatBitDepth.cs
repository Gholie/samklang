using System.Runtime.InteropServices;
using NAudio.Wave;

namespace Samklang.Devices;

/// <summary>
/// Resolves the audible bit depth of a <see cref="WaveFormat"/> — distinct from
/// <see cref="WaveFormat.BitsPerSample"/>, which for WAVE_FORMAT_EXTENSIBLE formats is the
/// CONTAINER size (e.g. 32 bits for a 24-in-32 layout), not the depth actually converted.
///
/// The real depth lives in WAVEFORMATEXTENSIBLE's <c>Samples.wValidBitsPerSample</c> field.
/// NAudio's <see cref="WaveFormatExtensible"/> does parse that field out of a marshaled
/// WAVEFORMATEX(TENSIBLE) — <see cref="WaveFormat.MarshalFromPtr"/> — but keeps it in a private
/// field with no public property (checked against NAudio 2.2.1), so it can't be read off the
/// object directly. This re-marshals the format back to bytes (the same technique
/// <see cref="WaveFormatRatePatcher"/> uses to patch the rate) and reads the field out of them.
///
/// The FiiO K11 (and effectively every modern DAC) reports its Device Format this way: set to
/// "24 bit, 48000 Hz" in Windows' Sound settings, its Device Format has wBitsPerSample = 32 and
/// wValidBitsPerSample = 24 — so <see cref="PolicyConfigAudioEndpoint.GetCurrentFormat"/> reading
/// <c>BitsPerSample</c> alone reports "32-bit" for a device that is, and remains, 24-bit.
/// </summary>
public static class WaveFormatBitDepth
{
    // WAVEFORMATEXTENSIBLE byte offset: the Samples union (wValidBitsPerSample when the format
    // isn't ADPCM) sits immediately after the 18-byte WAVEFORMATEX header.
    private const int ValidBitsPerSampleOffset = 18;

    // cbSize for WAVEFORMATEXTENSIBLE: Samples (2 bytes) + dwChannelMask (4) + SubFormat (16).
    private const int ExtensibleExtraSize = 22;

    /// <summary>
    /// The bit depth a listener actually hears: <c>wValidBitsPerSample</c> for a
    /// WAVE_FORMAT_EXTENSIBLE format carrying a sane (nonzero, no wider than the container)
    /// valid-bits field, otherwise <see cref="WaveFormat.BitsPerSample"/> — the container size,
    /// which for plain WAVEFORMATEX formats (16-bit PCM, 32-bit float without an extensible
    /// wrapper) is already the real depth, since those have no container/valid split at all.
    /// </summary>
    public static int Effective(WaveFormat format)
    {
        if (format is not WaveFormatExtensible || format.ExtraSize < ExtensibleExtraSize)
        {
            return format.BitsPerSample;
        }

        var pointer = WaveFormat.MarshalToPtr(format);
        try
        {
            var validBits = Marshal.ReadInt16(pointer, ValidBitsPerSampleOffset);
            return validBits > 0 && validBits <= format.BitsPerSample ? validBits : format.BitsPerSample;
        }
        finally
        {
            Marshal.FreeHGlobal(pointer);
        }
    }
}
