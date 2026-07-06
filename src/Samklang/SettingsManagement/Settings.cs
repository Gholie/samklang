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
    string? StorefrontOverride = null)
{
    /// <summary>The Grace Period new Settings are seeded with on first run, per docs/PLAN.md.</summary>
    public static readonly TimeSpan DefaultGracePeriod = TimeSpan.FromSeconds(30);

    /// <summary>The device-targeting mode new Settings are seeded with on first run: follow the Windows default.</summary>
    public static readonly DeviceTargetingMode DefaultDeviceTargetingMode = DeviceTargetingMode.FollowDefault;
}
