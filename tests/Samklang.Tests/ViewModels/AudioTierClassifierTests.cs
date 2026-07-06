using Samklang.Domain;
using Samklang.ViewModels;
using Xunit;

namespace Samklang.Tests.ViewModels;

public class AudioTierClassifierTests
{
    [Fact]
    public void Classify_returns_unknown_for_a_null_resolution()
    {
        Assert.Equal(AudioTier.Unknown, AudioTierClassifier.Classify(null));
    }

    [Fact]
    public void Classify_returns_unknown_for_a_fallback_resolution_regardless_of_rate()
    {
        var resolution = new FormatResolution(new DeviceFormat(96_000, 24), ResolutionConfidence.Fallback, "Tier fallback");

        Assert.Equal(AudioTier.Unknown, AudioTierClassifier.Classify(resolution));
    }

    [Theory]
    [InlineData(44_100, AudioTier.Lossless)]
    [InlineData(48_000, AudioTier.Lossless)]
    [InlineData(88_200, AudioTier.HiResLossless)]
    [InlineData(192_000, AudioTier.HiResLossless)]
    [InlineData(352_800, AudioTier.Unknown)]
    public void Classify_derives_tier_from_sample_rate_when_confidence_is_not_fallback(int sampleRateHz, AudioTier expected)
    {
        var resolution = new FormatResolution(new DeviceFormat(sampleRateHz, 24), ResolutionConfidence.Exact, "Catalog match");

        Assert.Equal(expected, AudioTierClassifier.Classify(resolution));
    }

    [Theory]
    [InlineData(AudioTier.Unknown, "Unknown")]
    [InlineData(AudioTier.LossyStereo, "Lossy Stereo")]
    [InlineData(AudioTier.Lossless, "Lossless")]
    [InlineData(AudioTier.HiResLossless, "Hi-Res Lossless")]
    [InlineData(AudioTier.DolbyAtmos, "Dolby Atmos")]
    public void ToDisplayString_returns_the_expected_label(AudioTier tier, string expected)
    {
        Assert.Equal(expected, tier.ToDisplayString());
    }
}
