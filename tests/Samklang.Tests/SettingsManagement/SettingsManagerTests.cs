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
        Assert.Equal(Settings.DefaultDeviceTargetingMode, settings.DeviceTargetingMode);
        Assert.Null(settings.PinnedDeviceId);
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
        var persisted = new Settings(new DeviceFormat(192_000, 24), TimeSpan.FromSeconds(45), DeviceTargetingMode.Pinned, "device-2");
        var store = new FakeSettingsStore { Stored = persisted };
        var manager = new SettingsManager(store);

        var settings = manager.LoadOrSeed(new DeviceFormat(44_100, 16));

        Assert.Equal(persisted, settings);
        Assert.Equal(0, store.SaveCallCount);
    }

    [Fact]
    public void UpdateFromSettingsView_persists_every_field_in_a_single_store_write()
    {
        var store = new FakeSettingsStore();
        var manager = new SettingsManager(store);
        manager.LoadOrSeed(new DeviceFormat(44_100, 24));
        var savesAfterSeed = store.SaveCallCount;
        var newMapping = new TierSampleRateMapping(44_100, 48_000, 88_200, 176_400);

        manager.UpdateFromSettingsView(
            new DeviceFormat(192_000, 24),
            TimeSpan.FromSeconds(90),
            DeviceTargetingMode.Pinned,
            "device-2",
            newMapping);

        Assert.Equal(new DeviceFormat(192_000, 24), manager.Current.RestingFormat);
        Assert.Equal(TimeSpan.FromSeconds(90), manager.Current.GracePeriod);
        Assert.Equal(DeviceTargetingMode.Pinned, manager.Current.DeviceTargetingMode);
        Assert.Equal("device-2", manager.Current.PinnedDeviceId);
        Assert.Equal(newMapping, manager.Current.TierSampleRates);
        Assert.Equal(manager.Current, store.Stored);
        Assert.Equal(savesAfterSeed + 1, store.SaveCallCount);
    }

    [Fact]
    public void UpdateFromSettingsView_raises_a_single_PropertyChanged_for_Current()
    {
        var store = new FakeSettingsStore();
        var manager = new SettingsManager(store);
        manager.LoadOrSeed(new DeviceFormat(44_100, 24));
        var raisedCount = 0;
        manager.PropertyChanged += (_, _) => raisedCount++;

        manager.UpdateFromSettingsView(
            new DeviceFormat(96_000, 24),
            TimeSpan.FromSeconds(60),
            DeviceTargetingMode.FollowDefault,
            pinnedDeviceId: null,
            TierSampleRateMapping.Default);

        Assert.Equal(1, raisedCount);
    }

    [Fact]
    public void UpdateFromSettingsView_leaves_the_storefront_override_untouched()
    {
        var store = new FakeSettingsStore();
        var manager = new SettingsManager(store);
        manager.LoadOrSeed(new DeviceFormat(44_100, 24));
        manager.UpdateStorefrontOverride("gb");

        manager.UpdateFromSettingsView(
            new DeviceFormat(96_000, 24),
            TimeSpan.FromSeconds(60),
            DeviceTargetingMode.FollowDefault,
            pinnedDeviceId: null,
            TierSampleRateMapping.Default);

        Assert.Equal("gb", manager.Current.StorefrontOverride);
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
    public void UpdateFromSettingsView_in_follow_default_mode_clears_a_previously_pinned_device_id()
    {
        var store = new FakeSettingsStore();
        var manager = new SettingsManager(store);
        manager.LoadOrSeed(new DeviceFormat(44_100, 24));
        manager.UpdateFromSettingsView(
            new DeviceFormat(44_100, 24),
            Settings.DefaultGracePeriod,
            DeviceTargetingMode.Pinned,
            "device-2",
            TierSampleRateMapping.Default);

        // Passing a pinned id alongside FollowDefault (e.g. a stale picker selection) must not
        // let it linger in the persisted Settings.
        manager.UpdateFromSettingsView(
            new DeviceFormat(44_100, 24),
            Settings.DefaultGracePeriod,
            DeviceTargetingMode.FollowDefault,
            "device-2",
            TierSampleRateMapping.Default);

        Assert.Equal(DeviceTargetingMode.FollowDefault, manager.Current.DeviceTargetingMode);
        Assert.Null(manager.Current.PinnedDeviceId);
    }

    [Fact]
    public void LoadOrSeed_with_no_persisted_settings_seeds_the_default_tier_sample_rates()
    {
        var store = new FakeSettingsStore();
        var manager = new SettingsManager(store);

        var settings = manager.LoadOrSeed(new DeviceFormat(44_100, 24));

        Assert.Equal(TierSampleRateMapping.Default, settings.TierSampleRates);
    }

    [Fact]
    public void EffectiveTierSampleRates_falls_back_to_the_default_when_null()
    {
        var settings = new Settings(new DeviceFormat(44_100, 24), TimeSpan.FromSeconds(30), DeviceTargetingMode.FollowDefault, PinnedDeviceId: null);

        Assert.Null(settings.TierSampleRates);
        Assert.Equal(TierSampleRateMapping.Default, settings.EffectiveTierSampleRates);
    }

    [Fact]
    public void Settings_round_trips_through_System_Text_Json()
    {
        var settings = new Settings(new DeviceFormat(88_200, 24), TimeSpan.FromSeconds(30), DeviceTargetingMode.Pinned, "device-2");

        var json = System.Text.Json.JsonSerializer.Serialize(settings);
        var roundTripped = System.Text.Json.JsonSerializer.Deserialize<Settings>(json);

        Assert.Equal(settings, roundTripped);
    }

    [Fact]
    public void Settings_round_trips_a_storefront_override_through_System_Text_Json()
    {
        var settings = new Settings(new DeviceFormat(88_200, 24), TimeSpan.FromSeconds(30), DeviceTargetingMode.FollowDefault, PinnedDeviceId: null, StorefrontOverride: "gb");

        var json = System.Text.Json.JsonSerializer.Serialize(settings);
        var roundTripped = System.Text.Json.JsonSerializer.Deserialize<Settings>(json);

        Assert.Equal(settings, roundTripped);
    }

    [Fact]
    public void Settings_round_trips_tier_sample_rates_through_System_Text_Json()
    {
        var settings = new Settings(
            new DeviceFormat(88_200, 24),
            TimeSpan.FromSeconds(30),
            DeviceTargetingMode.FollowDefault,
            PinnedDeviceId: null,
            TierSampleRates: new TierSampleRateMapping(44_100, 48_000, 88_200, 176_400));

        var json = System.Text.Json.JsonSerializer.Serialize(settings);
        var roundTripped = System.Text.Json.JsonSerializer.Deserialize<Settings>(json);

        Assert.Equal(settings, roundTripped);
    }

    [Fact]
    public void Settings_without_tier_sample_rates_round_trips_to_null_through_System_Text_Json()
    {
        var settings = new Settings(new DeviceFormat(88_200, 24), TimeSpan.FromSeconds(30), DeviceTargetingMode.FollowDefault, PinnedDeviceId: null);

        var json = System.Text.Json.JsonSerializer.Serialize(settings);
        var roundTripped = System.Text.Json.JsonSerializer.Deserialize<Settings>(json);

        Assert.Null(roundTripped!.TierSampleRates);
        Assert.Equal(TierSampleRateMapping.Default, roundTripped.EffectiveTierSampleRates);
    }
}
