using Samklang.Domain;

namespace Samklang.ViewModels;

/// <summary>
/// Derives a best-effort Audio Tier badge for the now-playing panel from a Format Resolution's
/// resolved sample rate.
///
/// This is a heuristic, not the real thing: per CONTEXT.md, Audio Tier properly comes from the
/// catalog's <c>audioVariants</c> attribute (docs/PLAN.md's M2 catalog layer), and neither
/// <see cref="Track"/> nor <see cref="FormatResolution"/> carries it yet. Until a resolver layer
/// produces a real Audio Tier, this class approximates one from the numbers we do have, purely
/// for the dashboard badge — it is never used for switching decisions. Lossy-stereo and
/// Dolby Atmos can't be told apart from sample rate alone (they overlap with Lossless/Hi-Res
/// ranges), so this only ever classifies into <see cref="AudioTier.Lossless"/>,
/// <see cref="AudioTier.HiResLossless"/>, or <see cref="AudioTier.Unknown"/>.
/// </summary>
public static class AudioTierClassifier
{
    /// <summary>
    /// Classifies a resolution's target sample rate into an Audio Tier. A <c>null</c> resolution,
    /// or one at <see cref="ResolutionConfidence.Fallback"/> (nothing actually known about the
    /// track), always classifies as <see cref="AudioTier.Unknown"/> — a Fallback resolution's
    /// rate is just a default, not evidence of the track's real tier.
    /// </summary>
    public static AudioTier Classify(FormatResolution? resolution)
    {
        if (resolution is null || resolution.Confidence == ResolutionConfidence.Fallback)
        {
            return AudioTier.Unknown;
        }

        return resolution.Target.SampleRateHz switch
        {
            <= 48_000 => AudioTier.Lossless,
            >= 88_200 and <= 192_000 => AudioTier.HiResLossless,
            _ => AudioTier.Unknown,
        };
    }

    /// <summary>Short, user-facing label for an Audio Tier badge.</summary>
    public static string ToDisplayString(this AudioTier tier) => tier switch
    {
        AudioTier.LossyStereo => "Lossy Stereo",
        AudioTier.Lossless => "Lossless",
        AudioTier.HiResLossless => "Hi-Res Lossless",
        AudioTier.DolbyAtmos => "Dolby Atmos",
        _ => "Unknown",
    };
}
