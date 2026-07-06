using Samklang.Domain;

namespace Samklang.Resolver;

/// <summary>
/// The tier-fallback layer of the Layered Resolver: the last resort when neither a catalog
/// match nor PlayCache analysis (both out of scope for this issue — see docs/PLAN.md M2/M3)
/// has told us anything about the Track. Always resolves, so it belongs at the end of the
/// chain. Bit depth is pinned to 24-bit per docs/PLAN.md's settled decision; the sample rate
/// defaults to the safest, most universally supported rate (44.1 kHz) until a real tier
/// mapping lands.
/// </summary>
public sealed class FallbackFormatResolverLayer : IFormatResolverLayer
{
    public const int FallbackSampleRateHz = 44_100;
    public const int PinnedBitDepth = 24;

    public string Name => "Tier fallback";

    public FormatResolution? TryResolve(Track track) =>
        new FormatResolution(
            new DeviceFormat(FallbackSampleRateHz, PinnedBitDepth),
            ResolutionConfidence.Fallback,
            Name);
}
