using Samklang.Domain;

namespace Samklang.ViewModels;

/// <summary>
/// Derives a best-effort Audio Tier badge for the now-playing panel from a Format Resolution.
///
/// This is a heuristic, not the real thing: per CONTEXT.md, Audio Tier properly comes from the
/// catalog's <c>audioVariants</c> attribute (docs/PLAN.md's M2 catalog layer), and neither
/// <see cref="Track"/> nor <see cref="FormatResolution"/> carries it directly yet. Until a
/// resolver layer produces a real Audio Tier, this class approximates one from what a resolution
/// does carry, purely for the dashboard badge — it is never used for switching decisions.
///
/// <para>
/// <b>Losslessness must come from <see cref="FormatResolution.IsLossless"/>, never from
/// <see cref="ResolutionConfidence"/>.</b> Confidence is about how certain the *rate* is, not
/// whether the codec is lossless — <c>PlayCacheFormatResolverLayer</c> reports
/// <see cref="ResolutionConfidence.Exact"/> for a plain lossy AAC/MP3 file just as confidently as
/// for genuine ALAC, because it read the container header directly either way. Treating any
/// non-Fallback confidence as "lossless enough to badge by rate" would badge that lossy AAC file
/// "Lossless" — a real, user-visible false signal, not just an imprecise one. So a resolution only
/// ever classifies as <see cref="AudioTier.Lossless"/>/<see cref="AudioTier.HiResLossless"/> when
/// <see cref="FormatResolution.IsLossless"/> is explicitly <see langword="true"/>; explicitly
/// <see langword="false"/> classifies as <see cref="AudioTier.LossyStereo"/> (the one lossy tier
/// this heuristic can actually tell apart, now that a real lossy/lossless signal exists); and
/// <see langword="null"/> (no layer said either way) falls back to <see cref="AudioTier.Unknown"/>
/// — Dolby Atmos still can't be distinguished from Lossy Stereo this way, so it's never produced.
/// </para>
/// </summary>
public static class AudioTierClassifier
{
    /// <summary>
    /// Classifies a resolution into an Audio Tier. A <c>null</c> resolution, or one at
    /// <see cref="ResolutionConfidence.Fallback"/> (nothing actually known about the track),
    /// always classifies as <see cref="AudioTier.Unknown"/> — a Fallback resolution's rate is just
    /// a default, not evidence of the track's real tier.
    /// </summary>
    public static AudioTier Classify(FormatResolution? resolution)
    {
        if (resolution is null || resolution.Confidence == ResolutionConfidence.Fallback)
        {
            return AudioTier.Unknown;
        }

        return resolution.IsLossless switch
        {
            false => AudioTier.LossyStereo,
            true => resolution.Target.SampleRateHz switch
            {
                <= 48_000 => AudioTier.Lossless,
                >= 88_200 and <= 192_000 => AudioTier.HiResLossless,
                _ => AudioTier.Unknown,
            },
            null => AudioTier.Unknown,
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
