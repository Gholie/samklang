using System.Runtime.InteropServices;
using NAudio.Wave;
using Samklang.Devices;
using Xunit;

namespace Samklang.Tests.Devices;

public class WaveFormatBitDepthTests
{
    [Fact]
    public void Extensible_24_in_32_container_reports_the_valid_24_bits()
    {
        // The FiiO K11 (and effectively every modern DAC) set to "24 bit, 48000 Hz" in Windows'
        // Sound settings: wBitsPerSample (the container) is 32, wValidBitsPerSample is 24.
        var format = CreateExtensibleWithValidBits(sampleRateHz: 48_000, containerBits: 32, validBits: 24, channels: 2);

        Assert.Equal(24, WaveFormatBitDepth.Effective(format));
    }

    [Fact]
    public void Plain_16bit_pcm_reports_its_own_bits_per_sample()
    {
        // A plain (non-extensible) WAVEFORMATEX has no container/valid split — BitsPerSample is
        // already the real depth.
        var format = new WaveFormat(44_100, 16, 2);

        Assert.Equal(16, WaveFormatBitDepth.Effective(format));
    }

    [Fact]
    public void Extensible_32bit_float_with_matching_valid_bits_reports_32()
    {
        // Some devices' native/mix format is 32-bit float, extensible, with valid bits equal to
        // the container — no truncation is happening, so 32 is correct.
        var format = CreateExtensibleWithValidBits(sampleRateHz: 48_000, containerBits: 32, validBits: 32, channels: 2);

        Assert.Equal(32, WaveFormatBitDepth.Effective(format));
    }

    [Fact]
    public void Extensible_with_zero_valid_bits_falls_back_to_the_container_size()
    {
        // A driver that leaves the valid-bits field unset (0) rather than mirroring the container
        // isn't truncating anything we can act on — trust the container size instead of reporting
        // a nonsensical 0-bit depth.
        var format = CreateExtensibleWithValidBits(sampleRateHz: 48_000, containerBits: 24, validBits: 0, channels: 2);

        Assert.Equal(24, WaveFormatBitDepth.Effective(format));
    }

    [Fact]
    public void Extensible_with_valid_bits_wider_than_the_container_falls_back_to_the_container_size()
    {
        // Shouldn't happen on real hardware, but a corrupt/malformed valid-bits field wider than
        // the container itself is nonsensical — fall back rather than report an impossible value.
        var format = CreateExtensibleWithValidBits(sampleRateHz: 48_000, containerBits: 16, validBits: 24, channels: 2);

        Assert.Equal(16, WaveFormatBitDepth.Effective(format));
    }

    /// <summary>
    /// Builds a <see cref="WaveFormatExtensible"/> whose valid-bits field differs from its
    /// container size — exactly what <see cref="Samklang.Devices.PolicyConfigInterop.GetDeviceFormat"/>
    /// hands back for a real 24-in-32 device, and something NAudio's own constructor can't produce
    /// (it sets valid bits equal to the container). Patches the raw marshaled bytes and
    /// re-marshals, the same technique <see cref="WaveFormatRatePatcher"/> and
    /// <see cref="WaveFormatBitDepth"/> themselves use.
    /// </summary>
    private static WaveFormat CreateExtensibleWithValidBits(int sampleRateHz, int containerBits, int validBits, int channels)
    {
        var seed = new WaveFormatExtensible(sampleRateHz, containerBits, channels);
        var pointer = WaveFormat.MarshalToPtr(seed);
        try
        {
            // Samples.wValidBitsPerSample sits right after the 18-byte WAVEFORMATEX header.
            Marshal.WriteInt16(pointer, 18, (short)validBits);
            return WaveFormat.MarshalFromPtr(pointer);
        }
        finally
        {
            Marshal.FreeHGlobal(pointer);
        }
    }
}
