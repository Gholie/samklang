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
    public void Classify_derives_tier_from_sample_rate_when_a_layer_confirms_it_is_lossless(int sampleRateHz, AudioTier expected)
    {
        var resolution = new FormatResolution(new DeviceFormat(sampleRateHz, 24), ResolutionConfidence.Exact, "Catalog match", IsLossless: true);

        Assert.Equal(expected, AudioTierClassifier.Classify(resolution));
    }

    /// <summary>
    /// Regression test for the bug this fixes: PlayCacheFormatResolverLayer reports
    /// ResolutionConfidence.Exact for a plain lossy AAC/MP3 file just as confidently as for
    /// genuine ALAC (it read the container header either way) — Confidence alone must never be
    /// used as a lossless signal, or a lossy AAC track at a Lossless-range sample rate gets
    /// badged "Lossless", a real user-visible false signal.
    /// </summary>
    [Theory]
    [InlineData(44_100)]
    [InlineData(96_000)]
    public void Classify_does_not_report_lossless_for_an_exact_resolution_known_to_be_lossy(int sampleRateHz)
    {
        var resolution = new FormatResolution(new DeviceFormat(sampleRateHz, 24), ResolutionConfidence.Exact, "PlayCache", IsLossless: false);

        Assert.Equal(AudioTier.LossyStereo, AudioTierClassifier.Classify(resolution));
    }

    [Theory]
    [InlineData(ResolutionConfidence.Exact)]
    [InlineData(ResolutionConfidence.TierDerived)]
    public void Classify_returns_unknown_when_no_layer_says_whether_the_source_is_lossless(ResolutionConfidence confidence)
    {
        var resolution = new FormatResolution(new DeviceFormat(44_100, 24), confidence, "Some layer", IsLossless: null);

        Assert.Equal(AudioTier.Unknown, AudioTierClassifier.Classify(resolution));
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
