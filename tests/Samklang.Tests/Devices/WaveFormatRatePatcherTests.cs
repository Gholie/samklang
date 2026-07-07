using System.Runtime.InteropServices;
using NAudio.Wave;
using Samklang.Devices;
using Xunit;

namespace Samklang.Tests.Devices;

public class WaveFormatRatePatcherTests
{
    [Theory]
    [InlineData(44_100, 96_000)]
    [InlineData(48_000, 44_100)]
    [InlineData(48_000, 192_000)]
    public void PatchesRateAndAverageBytes_OnPlainPcm(int fromRateHz, int toRateHz)
    {
        var original = new WaveFormat(fromRateHz, 16, 2);

        var patched = WaveFormatRatePatcher.WithSampleRate(original, toRateHz);

        Assert.Equal(toRateHz, patched.SampleRate);
        Assert.Equal(toRateHz * original.BlockAlign, patched.AverageBytesPerSecond);
        Assert.Equal(original.Encoding, patched.Encoding);
        Assert.Equal(original.Channels, patched.Channels);
        Assert.Equal(original.BitsPerSample, patched.BitsPerSample);
        Assert.Equal(original.BlockAlign, patched.BlockAlign);
    }

    [Fact]
    public void PreservesExtensibleLayout_ByteForByte_ExceptRateFields()
    {
        // The real-world case: drivers like the FiiO K11's expose WAVE_FORMAT_EXTENSIBLE and
        // reject anything whose bytes differ from that layout beyond the rate fields.
        var original = new WaveFormatExtensible(48_000, 32, 2);

        var patched = WaveFormatRatePatcher.WithSampleRate(original, 176_400);

        Assert.IsType<WaveFormatExtensible>(patched);
        Assert.Equal(176_400, patched.SampleRate);
        Assert.Equal(176_400 * original.BlockAlign, patched.AverageBytesPerSecond);

        var originalBytes = MarshalToBytes(original);
        var patchedBytes = MarshalToBytes(patched);
        Assert.Equal(originalBytes.Length, patchedBytes.Length);
        for (var i = 0; i < originalBytes.Length; i++)
        {
            // nSamplesPerSec occupies bytes 4-7, nAvgBytesPerSec bytes 8-11.
            if (i is >= 4 and < 12)
            {
                continue;
            }

            Assert.Equal(originalBytes[i], patchedBytes[i]);
        }
    }

    [Fact]
    public void PatchingBackToOriginalRate_RoundTripsExactly()
    {
        var original = new WaveFormatExtensible(96_000, 24, 2);

        var roundTripped = WaveFormatRatePatcher.WithSampleRate(
            WaveFormatRatePatcher.WithSampleRate(original, 44_100), 96_000);

        Assert.Equal(MarshalToBytes(original), MarshalToBytes(roundTripped));
    }

    private static byte[] MarshalToBytes(WaveFormat format)
    {
        var pointer = WaveFormat.MarshalToPtr(format);
        try
        {
            // Marshaled size: WAVEFORMATEX header (18 bytes) + cbSize extra bytes.
            var length = 18 + format.ExtraSize;
            var bytes = new byte[length];
            Marshal.Copy(pointer, bytes, 0, length);
            return bytes;
        }
        finally
        {
            Marshal.FreeHGlobal(pointer);
        }
    }
}
