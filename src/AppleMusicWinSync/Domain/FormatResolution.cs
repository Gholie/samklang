namespace AppleMusicWinSync.Domain;

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
public sealed record FormatResolution(
    DeviceFormat Target,
    ResolutionConfidence Confidence,
    string SourceLayer);
