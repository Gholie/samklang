using System.ComponentModel;
using Samklang.Domain;

namespace Samklang.SettingsManagement;

/// <summary>
/// Owns the in-memory <see cref="Settings"/> and their persistence: loads them at startup,
/// seeding the Resting Format from the device's current Device Format (and the Grace Period from
/// its default) on first run, and persists updates made from the settings view. Exposes
/// <see cref="Current"/> as a notifying property so a UI can bind to it directly.
/// </summary>
public sealed class SettingsManager(ISettingsStore store) : INotifyPropertyChanged
{
    /// <summary>Used only when the device's current format can't be read on first run (see <see cref="Devices.IDeviceController.GetCurrentFormat"/>'s null case).</summary>
    private static readonly DeviceFormat FallbackRestingFormat = new(44_100, 24);

    /// <summary>
    /// The live Settings. Populated by <see cref="LoadOrSeed"/>; do not read before calling it.
    /// </summary>
    public Settings Current { get; private set; } =
        new(FallbackRestingFormat, Settings.DefaultGracePeriod, Settings.DefaultDeviceTargetingMode, PinnedDeviceId: null,
            TierSampleRates: TierSampleRateMapping.Default);

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Loads persisted Settings if they exist. On first run (no settings file yet), seeds
    /// Resting Format from <paramref name="currentDeviceFormat"/> (falling back to a fixed
    /// default if it can't be read) and Grace Period from <see cref="Settings.DefaultGracePeriod"/>,
    /// then persists that seed immediately so it survives restart even without an edit.
    /// </summary>
    public Settings LoadOrSeed(DeviceFormat? currentDeviceFormat)
    {
        var loaded = store.Load();
        if (loaded is not null)
        {
            Current = loaded;
            OnPropertyChanged(nameof(Current));
            return Current;
        }

        var seeded = new Settings(
            currentDeviceFormat ?? FallbackRestingFormat,
            Settings.DefaultGracePeriod,
            Settings.DefaultDeviceTargetingMode,
            PinnedDeviceId: null,
            TierSampleRates: TierSampleRateMapping.Default);
        store.Save(seeded);
        Current = seeded;
        OnPropertyChanged(nameof(Current));
        return Current;
    }

    /// <summary>
    /// Applies the settings view's whole Save — Resting Format, Grace Period, device targeting,
    /// and tier mappings — as one persisted write and one <see cref="PropertyChanged"/>, instead
    /// of a disk round trip (and change-notification storm) per field.
    /// <paramref name="pinnedDeviceId"/> is ignored (and cleared) when
    /// <paramref name="deviceTargetingMode"/> is <see cref="DeviceTargetingMode.FollowDefault"/>,
    /// so a stale pinned ID never lingers once the user has switched back to Follow. Fields with
    /// no settings-view control (the storefront override) are left untouched.
    /// </summary>
    public void UpdateFromSettingsView(
        DeviceFormat restingFormat,
        TimeSpan gracePeriod,
        DeviceTargetingMode deviceTargetingMode,
        string? pinnedDeviceId,
        TierSampleRateMapping tierSampleRates)
    {
        Current = Current with
        {
            RestingFormat = restingFormat,
            GracePeriod = gracePeriod,
            DeviceTargetingMode = deviceTargetingMode,
            PinnedDeviceId = deviceTargetingMode == DeviceTargetingMode.Pinned ? pinnedDeviceId : null,
            TierSampleRates = tierSampleRates,
        };
        store.Save(Current);
        OnPropertyChanged(nameof(Current));
    }

    /// <summary>
    /// Updates and persists the catalog storefront override (e.g. "gb"), or clears it (null/blank)
    /// to fall back to auto-detecting the storefront from the Windows region. See
    /// <see cref="Resolver.Catalog.WindowsRegionStorefrontProvider"/>. Not part of
    /// <see cref="UpdateFromSettingsView"/> because the settings view has no control for it yet.
    /// </summary>
    public void UpdateStorefrontOverride(string? storefrontOverride)
    {
        Current = Current with { StorefrontOverride = string.IsNullOrWhiteSpace(storefrontOverride) ? null : storefrontOverride.Trim() };
        store.Save(Current);
        OnPropertyChanged(nameof(Current));
    }

    /// <summary>
    /// Updates and persists the rich now-playing toggle (see <see cref="Settings.RichNowPlaying"/>).
    /// Not part of <see cref="UpdateFromSettingsView"/> because — like Start-with-Windows — the
    /// checkbox applies immediately rather than waiting for the Save button, so the dashboard
    /// flips between rich and simple the moment it's clicked.
    /// </summary>
    public void UpdateRichNowPlaying(bool enabled)
    {
        Current = Current with { RichNowPlaying = enabled };
        store.Save(Current);
        OnPropertyChanged(nameof(Current));
    }

    private void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
