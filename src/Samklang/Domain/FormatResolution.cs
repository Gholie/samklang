namespace Samklang.Domain;

/// <summary>How certain a format resolution is.</summary>
public enum ResolutionConfidence
{
    /// <summary>Nothing known about the track; the default mapping applies.</summary>
    Fallback,

    /// <summary>Only the audio tier is known; the rate comes from the user's per-tier mapping.</summary>
    TierDerived,

    /// <summary>The track's true sample rate is known.</summary>
    Exact,
}

/// <summary>
/// The outcome of resolving a track: the device format to switch to,
/// how certain we are, and which resolver layer produced it.
/// </summary>
/// <param name="IsLossless">
/// Whether the source is actually lossless, when a layer can tell — <see langword="null"/> when
/// it can't. This is a genuinely separate signal from <see cref="Confidence"/>: Confidence is
/// about how certain the *rate* is, not whether the codec is lossless. A layer can be certain of
/// the exact rate of a lossy file (e.g. <c>PlayCacheFormatResolverLayer</c> reading an AAC/MP3
/// container's real sample rate) — that's still <see cref="ResolutionConfidence.Exact"/> with
/// <see cref="IsLossless"/> <see langword="false"/>, not evidence the track is Lossless/Hi-Res
/// Lossless. Producing layers should set this explicitly rather than leaving it null whenever
/// they know one way or the other (see <c>CatalogFormatResolverLayer</c> — always true, it only
/// ever matches ALAC — and <c>PlayCacheFormatResolverLayer</c> — keyed off whether the container
/// probe found a real PCM bit depth).
/// </param>
public sealed record FormatResolution(
    DeviceFormat Target,
    ResolutionConfidence Confidence,
    string SourceLayer,
    bool? IsLossless = null);
