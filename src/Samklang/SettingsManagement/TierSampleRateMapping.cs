namespace Samklang.SettingsManagement;

/// <summary>
/// The user's per-Audio-Tier sample-rate mapping (CONTEXT.md: "a tier bounds the sample rate but
/// does not determine it"), meant for a future tier-derived resolver layer to pick a rate when
/// only the Audio Tier is known (Confidence.TierDerived — see docs/PLAN.md's Layered Resolver).
/// Bit depth is not part of this mapping — it's pinned to 24-bit everywhere per docs/PLAN.md's
/// settled decision.
///
/// No resolver layer reads this mapping yet (only the catalog and tier-fallback layers exist so
/// far); it is persisted now so the Settings page has somewhere to save the user's choice ahead
/// of that layer landing. <see cref="Domain.AudioTier.Unknown"/> deliberately has no entry here:
/// nothing is known about the track in that case, so the tier-fallback layer's fixed default
/// applies instead.
/// </summary>
public sealed record TierSampleRateMapping(
    int LossyStereoHz,
    int LosslessHz,
    int HiResLosslessHz,
    int DolbyAtmosHz)
{
    /// <summary>Seed values new Settings are populated with on first run: the safest, most universally supported rate for each tier.</summary>
    public static readonly TierSampleRateMapping Default = new(
        LossyStereoHz: 44_100,
        LosslessHz: 44_100,
        HiResLosslessHz: 96_000,
        DolbyAtmosHz: 48_000);
}
