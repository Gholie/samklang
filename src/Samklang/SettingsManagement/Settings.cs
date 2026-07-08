using Samklang.Domain;

namespace Samklang.SettingsManagement;

/// <summary>
/// The user-configurable knobs persisted as a whole to %APPDATA%\Samklang\settings.json. Kept as
/// a single flat record so the file is read/written atomically; later issues (tier mappings,
/// autostart) are expected to add more properties here rather than introduce separate files.
/// <para>
/// <paramref name="RichNowPlaying"/> toggles the dashboard's rich now-playing card (album artwork,
/// playback controls, playing animation) versus a plain text-only line. Defaults to true, which is
/// also what settings.json files persisted before the property existed deserialize to — missing
/// JSON properties take the constructor parameter's default.
/// </para>
/// <para>
/// <paramref name="ShowSwitchLog"/> chooses which dashboard tab (Playing Next — album tracks, the
/// default; or History — recent switches) is selected on startup. It seeds
/// <see cref="Samklang.ViewModels.DashboardViewModel.SelectedTabIndex"/> once in that view model's
/// constructor and is not read again afterward — before the Apple Music-style redesign this
/// governed a live either/or visibility toggle between two always-present lists, but the
/// TabControl that replaced them lets the user switch tabs freely, so there is nothing left for
/// this setting to do after startup beyond picking where they land.
/// </para>
/// <para>
/// <paramref name="EnableDetailedLogging"/> turns on verbose file logging for debugging. Defaults
/// to false (off) — detailed logging is opt-in — which is also what settings.json files persisted
/// before the property existed deserialize to.
/// </para>
/// </summary>
public sealed record Settings(
    DeviceFormat RestingFormat,
    TimeSpan GracePeriod,
    DeviceTargetingMode DeviceTargetingMode,
    string? PinnedDeviceId,
    string? StorefrontOverride = null,
    TierSampleRateMapping? TierSampleRates = null,
    bool RichNowPlaying = true,
    bool ShowSwitchLog = false,
    bool EnableDetailedLogging = false)
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
