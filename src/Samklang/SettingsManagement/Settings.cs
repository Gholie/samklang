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
/// <para>
/// <paramref name="ControlAppleMusicApp"/> opts into the album-track play/queue actions that drive
/// the Apple Music app through UI Automation (clicking a song to play it, and the per-row Play Next
/// / Play Last buttons — see <see cref="Sessions.AppleMusicPlaybackController"/>). It is off by
/// default because it is intrusive: it briefly takes over the app and brings it to the foreground.
/// With it off, clicking a song only opens its album (a plain deep link) and the queue buttons are
/// hidden. Missing from older settings.json files deserializes to false, keeping the feature opt-in.
/// </para>
/// <para>
/// <paramref name="FormatSwitchBehavior"/> chooses what happens to playback and the device's mute
/// state around a device format switch — see <see cref="SettingsManagement.FormatSwitchBehavior"/>
/// for the three options. Defaults to <see cref="SettingsManagement.FormatSwitchBehavior.MuteThroughSwitch"/>,
/// which is also what settings.json files persisted before this property existed (and files
/// persisted with the two booleans it replaced) deserialize to, preserving today's mute-by-default
/// behavior for existing users.
/// </para>
/// <para>
/// <paramref name="StartMinimized"/> skips showing the main window on startup, leaving the app in
/// the tray from the moment it launches — for users who only ever interact with it via the tray
/// icon. Off by default; missing from older settings.json files deserializes to false.
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
    bool EnableDetailedLogging = false,
    bool ControlAppleMusicApp = false,
    FormatSwitchBehavior FormatSwitchBehavior = FormatSwitchBehavior.MuteThroughSwitch,
    bool StartMinimized = false)
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
