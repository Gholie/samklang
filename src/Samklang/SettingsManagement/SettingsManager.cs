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
    public Settings Current { get; private set; } = new(FallbackRestingFormat, Settings.DefaultGracePeriod);

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

        var seeded = new Settings(currentDeviceFormat ?? FallbackRestingFormat, Settings.DefaultGracePeriod);
        store.Save(seeded);
        Current = seeded;
        OnPropertyChanged(nameof(Current));
        return Current;
    }

    /// <summary>Updates and persists the Resting Format, e.g. from the settings view.</summary>
    public void UpdateRestingFormat(DeviceFormat restingFormat)
    {
        Current = Current with { RestingFormat = restingFormat };
        store.Save(Current);
        OnPropertyChanged(nameof(Current));
    }

    /// <summary>Updates and persists the Grace Period, e.g. from the settings view.</summary>
    public void UpdateGracePeriod(TimeSpan gracePeriod)
    {
        Current = Current with { GracePeriod = gracePeriod };
        store.Save(Current);
        OnPropertyChanged(nameof(Current));
    }

    /// <summary>
    /// Updates and persists the catalog storefront override (e.g. "gb"), or clears it (null/blank)
    /// to fall back to auto-detecting the storefront from the Windows region. See
    /// <see cref="Resolver.Catalog.WindowsRegionStorefrontProvider"/>.
    /// </summary>
    public void UpdateStorefrontOverride(string? storefrontOverride)
    {
        Current = Current with { StorefrontOverride = string.IsNullOrWhiteSpace(storefrontOverride) ? null : storefrontOverride.Trim() };
        store.Save(Current);
        OnPropertyChanged(nameof(Current));
    }

    private void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
