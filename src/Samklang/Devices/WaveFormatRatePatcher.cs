using System.Runtime.InteropServices;
using NAudio.Wave;

namespace Samklang.Devices;

/// <summary>
/// Produces a byte-exact copy of a <see cref="WaveFormat"/> with only the sample rate (and the
/// rate-derived average-bytes-per-second field) changed. Everything else — format tag, channel
/// count, channel mask, container size, valid bits, subformat GUID — is preserved verbatim by
/// patching the marshaled WAVEFORMATEX(TENSIBLE) bytes rather than constructing a new format.
///
/// This matters because audio drivers accept exactly the format layouts they expose and nothing
/// else: the FiiO K11, for example, reports its Device Format as WAVE_FORMAT_EXTENSIBLE with a
/// 32-bit container holding 24 valid bits, and rejects a hand-built packed 24-bit PCM format at
/// every rate — while accepting its own layout at every rate the hardware supports. So both
/// capability probing and format switching must vary the rate on the device's own structure.
/// </summary>
public static class WaveFormatRatePatcher
{
    // WAVEFORMATEX byte offsets: nSamplesPerSec at 4, nAvgBytesPerSec at 8.
    private const int SamplesPerSecOffset = 4;
    private const int AvgBytesPerSecOffset = 8;

    public static WaveFormat WithSampleRate(WaveFormat format, int sampleRateHz)
    {
        var pointer = WaveFormat.MarshalToPtr(format);
        try
        {
            Marshal.WriteInt32(pointer, SamplesPerSecOffset, sampleRateHz);
            Marshal.WriteInt32(pointer, AvgBytesPerSecOffset, sampleRateHz * format.BlockAlign);
            return WaveFormat.MarshalFromPtr(pointer);
        }
        finally
        {
            Marshal.FreeHGlobal(pointer);
        }
    }
}
