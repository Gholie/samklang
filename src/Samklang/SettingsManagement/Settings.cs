using Samklang.Domain;

namespace Samklang.SettingsManagement;

/// <summary>
/// The user-configurable knobs persisted as a whole to %APPDATA%\Samklang\settings.json. Kept as
/// a single flat record so the file is read/written atomically; later issues (tier mappings,
/// autostart) are expected to add more properties here rather than introduce separate files.
/// </summary>
public sealed record Settings(
    DeviceFormat RestingFormat,
    TimeSpan GracePeriod,
    DeviceTargetingMode DeviceTargetingMode,
    string? PinnedDeviceId,
    string? StorefrontOverride = null,
    TierSampleRateMapping? TierSampleRates = null)
{
    /// <summary>The Grace Period new Settings are seeded with on first run, per docs/PLAN.md.</summary>
    public static readonly TimeSpan DefaultGracePeriod = TimeSpan.FromSeconds(30);

    /// <summary>The device-targeting mode new Settings are seeded with on first run: follow the Windows default.</summary>
    public static readonly DeviceTargetingMode DefaultDeviceTargetingMode = DeviceTargetingMode.FollowDefault;

    /// <summary>
    /// <see cref="TierSampleRates"/>, falling back to <see cref="TierSampleRateMapping.Default"/>
    /// when null — which it always was before this property existed, so settings.json files
    /// persisted before it shipped still deserialize to something usable instead of null-refing
    /// the settings page.
    /// </summary>
    public TierSampleRateMapping EffectiveTierSampleRates => TierSampleRates ?? TierSampleRateMapping.Default;
}
