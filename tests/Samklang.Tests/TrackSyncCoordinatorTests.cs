using Samklang.Devices;
using Samklang.Domain;
using Samklang.Resolver;
using Samklang.Sessions;
using Xunit;

namespace Samklang.Tests;

public class TrackSyncCoordinatorTests
{
    private sealed class FakeTrackWatcher : ITrackWatcher
    {
        public Track? CurrentTrack { get; private set; }
        public event EventHandler<TrackChangedEventArgs>? TrackChanged;
        public Task StartAsync() => Task.CompletedTask;

        public void Fire(Track? track)
        {
            CurrentTrack = track;
            TrackChanged?.Invoke(this, new TrackChangedEventArgs(track));
        }
    }

    private sealed class FakeResolver(FormatResolution result) : IFormatResolver
    {
        public int CallCount { get; private set; }

        public FormatResolution Resolve(Track track)
        {
            CallCount++;
            return result;
        }
    }

    private sealed class FakeDeviceController : IDeviceController
    {
        public DeviceFormat? Current { get; set; }
        public DeviceFormat? LastAppliedTarget { get; private set; }

        // Empty by default: RateFamilyClamp treats an empty supported set as "nothing known" and
        // passes the requested rate through unchanged, so existing tests that don't care about
        // clamping keep working without setting this up.
        public IReadOnlySet<int> SupportedSampleRates { get; set; } = new HashSet<int>();

        public DeviceFormat? GetCurrentFormat() => Current;

        public bool ApplyTargetFormat(DeviceFormat target)
        {
            LastAppliedTarget = target;
            Current = target;
            return true;
        }

        public IReadOnlySet<int> GetSupportedSampleRates(int bitDepth) => SupportedSampleRates;
    }

    [Fact]
    public void TrackChanged_runs_the_resolver_and_applies_its_target_to_the_device()
    {
        var watcher = new FakeTrackWatcher();
        var resolution = new FormatResolution(new DeviceFormat(44_100, 24), ResolutionConfidence.Fallback, "Tier fallback");
        var resolver = new FakeResolver(resolution);
        var deviceController = new FakeDeviceController();
        var coordinator = new TrackSyncCoordinator(watcher, resolver, deviceController);

        watcher.Fire(new Track("Title", "Artist", "Album"));

        Assert.Equal(1, resolver.CallCount);
        Assert.Equal(resolution.Target, deviceController.LastAppliedTarget);
        Assert.Equal(resolution, coordinator.Resolution);
        Assert.Equal(resolution.Target, coordinator.DeviceFormat);
    }

    [Fact]
    public void TrackChanged_applies_the_resolvers_target_unchanged_when_the_device_supports_it()
    {
        var watcher = new FakeTrackWatcher();
        var resolution = new FormatResolution(new DeviceFormat(96_000, 24), ResolutionConfidence.Exact, "Catalog match");
        var resolver = new FakeResolver(resolution);
        var deviceController = new FakeDeviceController { SupportedSampleRates = new HashSet<int> { 44_100, 96_000 } };
        var coordinator = new TrackSyncCoordinator(watcher, resolver, deviceController);

        watcher.Fire(new Track("Title", "Artist", "Album"));

        Assert.Equal(resolution.Target, coordinator.AppliedFormat);
        Assert.Equal(resolution.Target, deviceController.LastAppliedTarget);
    }

    [Fact]
    public void TrackChanged_clamps_the_resolvers_target_to_the_devices_best_supported_rate_before_applying_it()
    {
        var watcher = new FakeTrackWatcher();
        var resolution = new FormatResolution(new DeviceFormat(192_000, 24), ResolutionConfidence.Exact, "Catalog match");
        var resolver = new FakeResolver(resolution);
        var deviceController = new FakeDeviceController { SupportedSampleRates = new HashSet<int> { 44_100, 48_000 } };
        var coordinator = new TrackSyncCoordinator(watcher, resolver, deviceController);

        watcher.Fire(new Track("Title", "Artist", "Album"));

        var expectedApplied = new DeviceFormat(48_000, 24);
        Assert.Equal(expectedApplied, coordinator.AppliedFormat);
        Assert.Equal(expectedApplied, deviceController.LastAppliedTarget);
        Assert.NotEqual(coordinator.Resolution!.Target, coordinator.AppliedFormat);
    }

    [Fact]
    public void TrackChanged_to_null_clears_the_applied_format_alongside_the_resolution()
    {
        var watcher = new FakeTrackWatcher();
        var resolver = new FakeResolver(new FormatResolution(new DeviceFormat(44_100, 24), ResolutionConfidence.Fallback, "Tier fallback"));
        var coordinator = new TrackSyncCoordinator(watcher, resolver, new FakeDeviceController());

        watcher.Fire(new Track("Title", "Artist", "Album"));
        watcher.Fire(null);

        Assert.Null(coordinator.AppliedFormat);
    }

    [Fact]
    public void TrackChanged_to_null_clears_the_resolution_without_calling_the_resolver()
    {
        var watcher = new FakeTrackWatcher();
        var resolver = new FakeResolver(new FormatResolution(new DeviceFormat(44_100, 24), ResolutionConfidence.Fallback, "Tier fallback"));
        var coordinator = new TrackSyncCoordinator(watcher, resolver, new FakeDeviceController());

        watcher.Fire(new Track("Title", "Artist", "Album"));
        watcher.Fire(null);

        Assert.Equal(1, resolver.CallCount);
        Assert.Null(coordinator.CurrentTrack);
        Assert.Null(coordinator.Resolution);
    }

    [Fact]
    public void RefreshDeviceFormat_reads_the_device_controllers_current_format()
    {
        var watcher = new FakeTrackWatcher();
        var deviceController = new FakeDeviceController { Current = new DeviceFormat(96_000, 24) };
        var coordinator = new TrackSyncCoordinator(
            watcher,
            new FakeResolver(new FormatResolution(new DeviceFormat(44_100, 24), ResolutionConfidence.Fallback, "Tier fallback")),
            deviceController);

        coordinator.RefreshDeviceFormat();

        Assert.Equal(new DeviceFormat(96_000, 24), coordinator.DeviceFormat);
    }
}
