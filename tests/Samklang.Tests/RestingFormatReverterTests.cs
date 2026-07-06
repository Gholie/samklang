using Samklang.Devices;
using Samklang.Domain;
using Samklang.SettingsManagement;
using Samklang.Timing;
using Xunit;

namespace Samklang.Tests;

public class RestingFormatReverterTests
{
    private sealed class FakeClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UnixEpoch;
    }

    private sealed class FakeSettingsStore(Settings settings) : ISettingsStore
    {
        public Settings? Load() => settings;
        public void Save(Settings newSettings) => settings = newSettings;
    }

    private sealed class FakeDeviceController : IDeviceController
    {
        public DeviceFormat? Current { get; set; }
        public int ApplyTargetFormatCallCount { get; private set; }
        public DeviceFormat? LastAppliedTarget { get; private set; }

        public DeviceFormat? GetCurrentFormat() => Current;

        public bool ApplyTargetFormat(DeviceFormat target)
        {
            ApplyTargetFormatCallCount++;
            LastAppliedTarget = target;
            Current = target;
            return true;
        }
    }

    private static readonly DeviceFormat RestingFormat = new(44_100, 24);
    private static readonly TimeSpan GracePeriod = TimeSpan.FromSeconds(30);

    private static (RestingFormatReverter Reverter, FakeClock Clock, FakeDeviceController DeviceController) CreateSut()
    {
        var clock = new FakeClock();
        var deviceController = new FakeDeviceController();
        var settingsManager = new SettingsManager(new FakeSettingsStore(new Settings(RestingFormat, GracePeriod)));
        settingsManager.LoadOrSeed(null);

        var reverter = new RestingFormatReverter(settingsManager, deviceController, clock);
        return (reverter, clock, deviceController);
    }

    [Fact]
    public void Tick_before_the_grace_period_elapses_does_not_revert()
    {
        var (reverter, clock, deviceController) = CreateSut();

        reverter.NotifyIdle();
        clock.UtcNow += GracePeriod - TimeSpan.FromSeconds(1);
        reverter.Tick();

        Assert.Equal(0, deviceController.ApplyTargetFormatCallCount);
    }

    [Fact]
    public void Tick_once_the_grace_period_elapses_reverts_to_the_resting_format()
    {
        var (reverter, clock, deviceController) = CreateSut();

        reverter.NotifyIdle();
        clock.UtcNow += GracePeriod;
        reverter.Tick();

        Assert.Equal(1, deviceController.ApplyTargetFormatCallCount);
        Assert.Equal(RestingFormat, deviceController.LastAppliedTarget);
    }

    [Fact]
    public void Tick_does_not_revert_twice_for_the_same_idle_period()
    {
        var (reverter, clock, deviceController) = CreateSut();

        reverter.NotifyIdle();
        clock.UtcNow += GracePeriod;
        reverter.Tick();
        clock.UtcNow += TimeSpan.FromMinutes(5);
        reverter.Tick();

        Assert.Equal(1, deviceController.ApplyTargetFormatCallCount);
    }

    [Fact]
    public void NotifyActive_before_the_grace_period_elapses_cancels_the_pending_revert()
    {
        var (reverter, clock, deviceController) = CreateSut();

        reverter.NotifyIdle();
        clock.UtcNow += GracePeriod - TimeSpan.FromSeconds(1);
        reverter.NotifyActive();
        clock.UtcNow += TimeSpan.FromSeconds(1);
        reverter.Tick();

        Assert.Equal(0, deviceController.ApplyTargetFormatCallCount);
    }

    [Fact]
    public void NotifyActive_resets_the_idle_clock_so_a_later_idle_period_needs_its_own_full_grace_period()
    {
        var (reverter, clock, deviceController) = CreateSut();

        reverter.NotifyIdle();
        clock.UtcNow += GracePeriod - TimeSpan.FromSeconds(1);
        reverter.NotifyActive();

        reverter.NotifyIdle();
        clock.UtcNow += GracePeriod - TimeSpan.FromSeconds(1);
        reverter.Tick();

        Assert.Equal(0, deviceController.ApplyTargetFormatCallCount);
    }

    [Fact]
    public void A_new_idle_period_after_a_previous_revert_can_revert_again()
    {
        var (reverter, clock, deviceController) = CreateSut();

        reverter.NotifyIdle();
        clock.UtcNow += GracePeriod;
        reverter.Tick();
        Assert.Equal(1, deviceController.ApplyTargetFormatCallCount);

        reverter.NotifyActive();
        reverter.NotifyIdle();
        clock.UtcNow += GracePeriod;
        reverter.Tick();

        Assert.Equal(2, deviceController.ApplyTargetFormatCallCount);
    }

    [Fact]
    public void Tick_without_ever_going_idle_never_reverts()
    {
        var (reverter, clock, deviceController) = CreateSut();

        clock.UtcNow += GracePeriod * 10;
        reverter.Tick();

        Assert.Equal(0, deviceController.ApplyTargetFormatCallCount);
    }
}
