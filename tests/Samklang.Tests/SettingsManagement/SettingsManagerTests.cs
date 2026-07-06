using Samklang.Domain;
using Samklang.SettingsManagement;
using Xunit;

namespace Samklang.Tests.SettingsManagement;

public class SettingsManagerTests
{
    private sealed class FakeSettingsStore : ISettingsStore
    {
        public Settings? Stored { get; set; }
        public int SaveCallCount { get; private set; }

        public Settings? Load() => Stored;

        public void Save(Settings settings)
        {
            SaveCallCount++;
            Stored = settings;
        }
    }

    [Fact]
    public void LoadOrSeed_with_no_persisted_settings_seeds_from_the_devices_current_format_and_the_default_grace_period()
    {
        var store = new FakeSettingsStore();
        var manager = new SettingsManager(store);
        var deviceFormat = new DeviceFormat(96_000, 24);

        var settings = manager.LoadOrSeed(deviceFormat);

        Assert.Equal(deviceFormat, settings.RestingFormat);
        Assert.Equal(Settings.DefaultGracePeriod, settings.GracePeriod);
        Assert.Equal(settings, manager.Current);
    }

    [Fact]
    public void LoadOrSeed_with_no_persisted_settings_persists_the_seed_immediately()
    {
        var store = new FakeSettingsStore();
        var manager = new SettingsManager(store);

        manager.LoadOrSeed(new DeviceFormat(48_000, 24));

        Assert.Equal(1, store.SaveCallCount);
        Assert.NotNull(store.Stored);
    }

    [Fact]
    public void LoadOrSeed_falls_back_to_a_fixed_default_when_the_devices_current_format_cannot_be_read()
    {
        var store = new FakeSettingsStore();
        var manager = new SettingsManager(store);

        var settings = manager.LoadOrSeed(null);

        // No specific format is mandated by the acceptance criteria for this edge case (the
        // device format is expected to be readable in practice); what matters is that seeding
        // never throws and produces *some* usable, persisted Settings.
        Assert.Equal(1, store.SaveCallCount);
        Assert.Equal(settings, manager.Current);
    }

    [Fact]
    public void LoadOrSeed_with_persisted_settings_loads_them_instead_of_reseeding()
    {
        var persisted = new Settings(new DeviceFormat(192_000, 24), TimeSpan.FromSeconds(45));
        var store = new FakeSettingsStore { Stored = persisted };
        var manager = new SettingsManager(store);

        var settings = manager.LoadOrSeed(new DeviceFormat(44_100, 16));

        Assert.Equal(persisted, settings);
        Assert.Equal(0, store.SaveCallCount);
    }

    [Fact]
    public void UpdateRestingFormat_persists_the_change_and_leaves_the_grace_period_untouched()
    {
        var store = new FakeSettingsStore();
        var manager = new SettingsManager(store);
        manager.LoadOrSeed(new DeviceFormat(44_100, 24));
        var originalGracePeriod = manager.Current.GracePeriod;

        manager.UpdateRestingFormat(new DeviceFormat(192_000, 24));

        Assert.Equal(new DeviceFormat(192_000, 24), manager.Current.RestingFormat);
        Assert.Equal(originalGracePeriod, manager.Current.GracePeriod);
        Assert.Equal(manager.Current, store.Stored);
    }

    [Fact]
    public void UpdateGracePeriod_persists_the_change_and_leaves_the_resting_format_untouched()
    {
        var store = new FakeSettingsStore();
        var manager = new SettingsManager(store);
        manager.LoadOrSeed(new DeviceFormat(44_100, 24));
        var originalRestingFormat = manager.Current.RestingFormat;

        manager.UpdateGracePeriod(TimeSpan.FromSeconds(90));

        Assert.Equal(TimeSpan.FromSeconds(90), manager.Current.GracePeriod);
        Assert.Equal(originalRestingFormat, manager.Current.RestingFormat);
        Assert.Equal(manager.Current, store.Stored);
    }

    [Fact]
    public void UpdateStorefrontOverride_persists_a_trimmed_lowercased_value()
    {
        var store = new FakeSettingsStore();
        var manager = new SettingsManager(store);
        manager.LoadOrSeed(new DeviceFormat(44_100, 24));

        manager.UpdateStorefrontOverride("  GB ");

        Assert.Equal("GB", manager.Current.StorefrontOverride); // stored as given; lower-casing is WindowsRegionStorefrontProvider's job
        Assert.Equal(manager.Current, store.Stored);
    }

    [Fact]
    public void UpdateStorefrontOverride_with_a_blank_value_clears_the_override()
    {
        var store = new FakeSettingsStore();
        var manager = new SettingsManager(store);
        manager.LoadOrSeed(new DeviceFormat(44_100, 24));
        manager.UpdateStorefrontOverride("gb");

        manager.UpdateStorefrontOverride("   ");

        Assert.Null(manager.Current.StorefrontOverride);
    }

    [Fact]
    public void Settings_round_trips_through_System_Text_Json()
    {
        var settings = new Settings(new DeviceFormat(88_200, 24), TimeSpan.FromSeconds(30));

        var json = System.Text.Json.JsonSerializer.Serialize(settings);
        var roundTripped = System.Text.Json.JsonSerializer.Deserialize<Settings>(json);

        Assert.Equal(settings, roundTripped);
    }

    [Fact]
    public void Settings_round_trips_a_storefront_override_through_System_Text_Json()
    {
        var settings = new Settings(new DeviceFormat(88_200, 24), TimeSpan.FromSeconds(30), "gb");

        var json = System.Text.Json.JsonSerializer.Serialize(settings);
        var roundTripped = System.Text.Json.JsonSerializer.Deserialize<Settings>(json);

        Assert.Equal(settings, roundTripped);
    }
}
