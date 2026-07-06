using Samklang.Devices;
using Samklang.Domain;
using Samklang.SettingsManagement;
using Samklang.ViewModels;
using Xunit;

namespace Samklang.Tests.ViewModels;

public class SettingsViewModelTests
{
    private sealed class FakeSettingsStore(Settings? settings = null) : ISettingsStore
    {
        public Settings? Stored { get; private set; } = settings;

        public Settings? Load() => Stored;

        public void Save(Settings newSettings) => Stored = newSettings;
    }

    private sealed class FakeDeviceController : IDeviceController
    {
        public DeviceTargetingMode? LastTargetingMode { get; private set; }
        public string? LastPinnedDeviceId { get; private set; }
        public int SetTargetingCallCount { get; private set; }
        public IReadOnlyList<RenderDevice> DevicesToReturn { get; set; } = [];

        public DeviceFormat? GetCurrentFormat() => new(44_100, 24);

        public bool ApplyTargetFormat(DeviceFormat target) => true;

        public IReadOnlySet<int> GetSupportedSampleRates(int bitDepth) => new HashSet<int>();

        public void SetTargeting(DeviceTargetingMode mode, string? pinnedDeviceId)
        {
            SetTargetingCallCount++;
            LastTargetingMode = mode;
            LastPinnedDeviceId = pinnedDeviceId;
        }

        public IReadOnlyList<RenderDevice> GetActiveRenderDevices() => DevicesToReturn;

        public DeviceTargetStatus GetTargetStatus() => new(null, null, false);
    }

    private sealed class FakeStartupRegistration : IStartupRegistration
    {
        public bool IsEnabled { get; private set; }
        public int EnableCallCount { get; private set; }
        public int DisableCallCount { get; private set; }

        public void Enable()
        {
            EnableCallCount++;
            IsEnabled = true;
        }

        public void Disable()
        {
            DisableCallCount++;
            IsEnabled = false;
        }
    }

    private static SettingsManager CreateSettingsManager(Settings? seeded = null)
    {
        var manager = new SettingsManager(new FakeSettingsStore(seeded));
        manager.LoadOrSeed(seeded?.RestingFormat ?? new DeviceFormat(44_100, 24));
        return manager;
    }

    [Fact]
    public void Construction_loads_fields_from_the_current_settings()
    {
        var settings = new Settings(
            new DeviceFormat(96_000, 24),
            TimeSpan.FromSeconds(45),
            DeviceTargetingMode.Pinned,
            "device-2",
            TierSampleRates: new TierSampleRateMapping(44_100, 44_100, 96_000, 48_000));
        var settingsManager = CreateSettingsManager(settings);
        var deviceController = new FakeDeviceController
        {
            DevicesToReturn = [new RenderDevice("device-2", "USB DAC")],
        };

        var viewModel = new SettingsViewModel(settingsManager, deviceController, new FakeStartupRegistration());

        Assert.Equal("96000", viewModel.RestingSampleRateHzText);
        Assert.Equal("24", viewModel.RestingBitDepthText);
        Assert.Equal("45", viewModel.GracePeriodSecondsText);
        Assert.True(viewModel.IsPinnedMode);
        Assert.False(viewModel.IsFollowDefaultMode);
        Assert.Equal("device-2", viewModel.SelectedDeviceId);
        Assert.Equal("44100", viewModel.LossyStereoHzText);
        Assert.Equal("96000", viewModel.HiResLosslessHzText);
    }

    [Fact]
    public void Construction_reads_start_with_windows_from_the_live_registration_not_settings()
    {
        var settingsManager = CreateSettingsManager();
        var startupRegistration = new FakeStartupRegistration();
        startupRegistration.Enable();

        var viewModel = new SettingsViewModel(settingsManager, new FakeDeviceController(), startupRegistration);

        Assert.True(viewModel.StartWithWindows);
        // Loading shouldn't itself toggle the registration.
        Assert.Equal(1, startupRegistration.EnableCallCount);
        Assert.Equal(0, startupRegistration.DisableCallCount);
    }

    [Fact]
    public void Save_with_valid_values_persists_everything_and_applies_device_targeting()
    {
        var settingsManager = CreateSettingsManager();
        var deviceController = new FakeDeviceController
        {
            DevicesToReturn = [new RenderDevice("device-1", "Speakers")],
        };
        var viewModel = new SettingsViewModel(settingsManager, deviceController, new FakeStartupRegistration())
        {
            RestingSampleRateHzText = "192000",
            RestingBitDepthText = "24",
            GracePeriodSecondsText = "60",
            IsPinnedMode = true,
            IsFollowDefaultMode = false,
            SelectedDeviceId = "device-1",
            LossyStereoHzText = "44100",
            LosslessHzText = "48000",
            HiResLosslessHzText = "88200",
            DolbyAtmosHzText = "48000",
        };

        viewModel.SaveCommand.Execute(null);

        Assert.Equal(new DeviceFormat(192_000, 24), settingsManager.Current.RestingFormat);
        Assert.Equal(TimeSpan.FromSeconds(60), settingsManager.Current.GracePeriod);
        Assert.Equal(DeviceTargetingMode.Pinned, settingsManager.Current.DeviceTargetingMode);
        Assert.Equal("device-1", settingsManager.Current.PinnedDeviceId);
        Assert.Equal(new TierSampleRateMapping(44_100, 48_000, 88_200, 48_000), settingsManager.Current.TierSampleRates);
        Assert.Equal(1, deviceController.SetTargetingCallCount);
        Assert.Equal(DeviceTargetingMode.Pinned, deviceController.LastTargetingMode);
        Assert.Equal("device-1", deviceController.LastPinnedDeviceId);
        Assert.Equal("Saved.", viewModel.StatusMessage);
    }

    [Fact]
    public void Save_with_invalid_numeric_values_does_not_persist_and_sets_a_status_message()
    {
        var settingsManager = CreateSettingsManager();
        var originalRestingFormat = settingsManager.Current.RestingFormat;
        var viewModel = new SettingsViewModel(settingsManager, new FakeDeviceController(), new FakeStartupRegistration())
        {
            RestingSampleRateHzText = "not a number",
        };

        viewModel.SaveCommand.Execute(null);

        Assert.Equal(originalRestingFormat, settingsManager.Current.RestingFormat);
        Assert.Equal("Invalid values — not saved.", viewModel.StatusMessage);
    }

    [Fact]
    public void Save_with_invalid_tier_sample_rates_does_not_persist_and_sets_a_status_message()
    {
        var settingsManager = CreateSettingsManager();
        var viewModel = new SettingsViewModel(settingsManager, new FakeDeviceController(), new FakeStartupRegistration())
        {
            LossyStereoHzText = "not a number",
        };

        viewModel.SaveCommand.Execute(null);

        Assert.Equal("Invalid tier sample rates — not saved.", viewModel.StatusMessage);
    }

    [Fact]
    public void Save_in_pinned_mode_with_no_device_selected_does_not_persist()
    {
        var settingsManager = CreateSettingsManager();
        var originalMode = settingsManager.Current.DeviceTargetingMode;
        var viewModel = new SettingsViewModel(settingsManager, new FakeDeviceController(), new FakeStartupRegistration())
        {
            IsPinnedMode = true,
            IsFollowDefaultMode = false,
            SelectedDeviceId = null,
        };

        viewModel.SaveCommand.Execute(null);

        Assert.Equal(originalMode, settingsManager.Current.DeviceTargetingMode);
        Assert.Equal("Pick a device to pin — not saved.", viewModel.StatusMessage);
    }

    [Fact]
    public void Setting_StartWithWindows_to_true_enables_the_registration()
    {
        var settingsManager = CreateSettingsManager();
        var startupRegistration = new FakeStartupRegistration();
        var viewModel = new SettingsViewModel(settingsManager, new FakeDeviceController(), startupRegistration);

        viewModel.StartWithWindows = true;

        Assert.Equal(1, startupRegistration.EnableCallCount);
    }

    [Fact]
    public void Setting_StartWithWindows_to_false_disables_the_registration()
    {
        var settingsManager = CreateSettingsManager();
        var startupRegistration = new FakeStartupRegistration();
        startupRegistration.Enable();
        var viewModel = new SettingsViewModel(settingsManager, new FakeDeviceController(), startupRegistration);

        viewModel.StartWithWindows = false;

        Assert.Equal(1, startupRegistration.DisableCallCount);
    }

    [Fact]
    public void RefreshDevices_repopulates_available_devices_and_preserves_the_current_selection()
    {
        var settingsManager = CreateSettingsManager();
        var deviceController = new FakeDeviceController
        {
            DevicesToReturn = [new RenderDevice("device-1", "Speakers")],
        };
        var viewModel = new SettingsViewModel(settingsManager, deviceController, new FakeStartupRegistration())
        {
            SelectedDeviceId = "device-1",
        };
        deviceController.DevicesToReturn = [new RenderDevice("device-1", "Speakers"), new RenderDevice("device-2", "Headphones")];

        viewModel.RefreshDevicesCommand.Execute(null);

        Assert.Equal(2, viewModel.AvailableDevices.Count);
        Assert.Equal("device-1", viewModel.SelectedDeviceId);
    }
}
