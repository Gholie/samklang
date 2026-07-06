using Samklang.Devices;
using Samklang.Domain;
using Samklang.Resolver;
using Samklang.Sessions;
using Samklang.Timing;
using Samklang.ViewModels;
using Xunit;

namespace Samklang.Tests.ViewModels;

public class DashboardViewModelTests
{
    private sealed class FakeTrackWatcher : ITrackWatcher
    {
        public Track? CurrentTrack { get; private set; }
        public event EventHandler<TrackChangedEventArgs>? TrackChanged;

        public PlaybackState? PlaybackState { get; private set; }
        public event EventHandler<PlaybackStateChangedEventArgs>? PlaybackStateChanged;

        public Task StartAsync() => Task.CompletedTask;

        public void Fire(Track? track)
        {
            CurrentTrack = track;
            TrackChanged?.Invoke(this, new TrackChangedEventArgs(track));
        }

        public void FirePlaybackState(PlaybackState? state)
        {
            PlaybackState = state;
            PlaybackStateChanged?.Invoke(this, new PlaybackStateChangedEventArgs(state));
        }
    }

    private sealed class FakeResolver(FormatResolution result) : IFormatResolver
    {
        public FormatResolution Resolve(Track track) => result;
    }

    private sealed class FakeDeviceController : IDeviceController
    {
        public DeviceFormat? Current { get; set; }
        public DeviceTargetStatus TargetStatusToReturn { get; set; } = new(null, null, false);
        public IReadOnlySet<int> SupportedSampleRates { get; set; } = new HashSet<int>();

        public DeviceFormat? GetCurrentFormat() => Current;

        public bool ApplyTargetFormat(DeviceFormat target)
        {
            Current = target;
            return true;
        }

        public IReadOnlySet<int> GetSupportedSampleRates(int bitDepth) => SupportedSampleRates;

        public void SetTargeting(DeviceTargetingMode mode, string? pinnedDeviceId)
        {
        }

        public IReadOnlyList<RenderDevice> GetActiveRenderDevices() => [];

        public DeviceTargetStatus GetTargetStatus() => TargetStatusToReturn;
    }

    private sealed class FakeRestingFormatReverter : IRestingFormatReverter
    {
        public void NotifyIdle()
        {
        }

        public void NotifyActive()
        {
        }

        public void Tick()
        {
        }
    }

    private sealed class FakeClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UnixEpoch;
    }

    private static (DashboardViewModel ViewModel, FakeTrackWatcher Watcher, FakeDeviceController DeviceController, FakeClock Clock) CreateSut(
        FormatResolution? resolutionToReturn = null)
    {
        var watcher = new FakeTrackWatcher();
        var resolver = new FakeResolver(resolutionToReturn ?? new FormatResolution(new DeviceFormat(44_100, 24), ResolutionConfidence.Fallback, "Tier fallback"));
        var deviceController = new FakeDeviceController();
        var coordinator = new TrackSyncCoordinator(watcher, resolver, deviceController, new FakeRestingFormatReverter());
        var clock = new FakeClock();
        var viewModel = new DashboardViewModel(coordinator, clock);
        return (viewModel, watcher, deviceController, clock);
    }

    [Fact]
    public void Initial_state_shows_placeholder_text_and_no_history()
    {
        var (viewModel, _, _, _) = CreateSut();

        Assert.Equal("(none — waiting for Apple Music)", viewModel.TrackDisplay);
        Assert.Equal("—", viewModel.TargetFormatDisplay);
        Assert.Equal("—", viewModel.ConfidenceDisplay);
        Assert.Equal("—", viewModel.SourceLayerDisplay);
        Assert.Equal("—", viewModel.DeviceFormatDisplay);
        Assert.False(viewModel.HasClampedFormat);
        Assert.False(viewModel.HasDeviceTargetWarning);
        Assert.Empty(viewModel.History);
    }

    [Fact]
    public void TrackChanged_updates_the_now_playing_display_properties()
    {
        var resolution = new FormatResolution(new DeviceFormat(96_000, 24), ResolutionConfidence.Exact, "Catalog match");
        var (viewModel, watcher, _, _) = CreateSut(resolution);

        watcher.Fire(new Track("Title", "Artist", "Album"));

        Assert.Equal("Title — Artist (Album)", viewModel.TrackDisplay);
        Assert.Equal(resolution.Target.ToString(), viewModel.TargetFormatDisplay);
        Assert.Equal("Exact", viewModel.ConfidenceDisplay);
        Assert.Equal("Catalog match", viewModel.SourceLayerDisplay);
        Assert.Equal(resolution.Target.ToString(), viewModel.DeviceFormatDisplay);
    }

    [Fact]
    public void TrackChanged_to_null_resets_the_now_playing_display_to_placeholders()
    {
        var (viewModel, watcher, _, _) = CreateSut();

        watcher.Fire(new Track("Title", "Artist", "Album"));
        watcher.Fire(null);

        Assert.Equal("(none — waiting for Apple Music)", viewModel.TrackDisplay);
        Assert.Equal("—", viewModel.TargetFormatDisplay);
        Assert.Equal("—", viewModel.ConfidenceDisplay);
    }

    [Fact]
    public void Clamped_target_format_is_surfaced_alongside_the_original_target()
    {
        var resolution = new FormatResolution(new DeviceFormat(192_000, 24), ResolutionConfidence.Exact, "Catalog match");
        var watcher = new FakeTrackWatcher();
        var resolver = new FakeResolver(resolution);
        var deviceController = new FakeDeviceController { SupportedSampleRates = new HashSet<int> { 44_100, 48_000 } };
        var coordinator = new TrackSyncCoordinator(watcher, resolver, deviceController, new FakeRestingFormatReverter());
        var viewModel = new DashboardViewModel(coordinator);

        watcher.Fire(new Track("Title", "Artist", "Album"));

        Assert.True(viewModel.HasClampedFormat);
        Assert.Contains("48", viewModel.ClampedFormatDisplay);
        Assert.Contains("192", viewModel.ClampedFormatDisplay);
    }

    [Fact]
    public void Device_target_fallback_shows_a_warning_message()
    {
        var watcher = new FakeTrackWatcher();
        var resolver = new FakeResolver(new FormatResolution(new DeviceFormat(44_100, 24), ResolutionConfidence.Fallback, "Tier fallback"));
        var deviceController = new FakeDeviceController
        {
            TargetStatusToReturn = new DeviceTargetStatus("device-1", "USB DAC", IsFallback: true),
        };
        var coordinator = new TrackSyncCoordinator(watcher, resolver, deviceController, new FakeRestingFormatReverter());
        var viewModel = new DashboardViewModel(coordinator);

        coordinator.RefreshDeviceFormat();

        Assert.True(viewModel.HasDeviceTargetWarning);
        Assert.Contains("USB DAC", viewModel.DeviceTargetWarningMessage);
    }

    [Theory]
    [InlineData(ResolutionConfidence.Fallback, 44_100, "Unknown")]
    [InlineData(ResolutionConfidence.Exact, 44_100, "Lossless")]
    [InlineData(ResolutionConfidence.Exact, 96_000, "Hi-Res Lossless")]
    [InlineData(ResolutionConfidence.TierDerived, 192_000, "Hi-Res Lossless")]
    public void Audio_tier_badge_is_derived_from_confidence_and_sample_rate(ResolutionConfidence confidence, int sampleRateHz, string expectedBadge)
    {
        var resolution = new FormatResolution(new DeviceFormat(sampleRateHz, 24), confidence, "Some layer");
        var (viewModel, watcher, _, _) = CreateSut(resolution);

        watcher.Fire(new Track("Title", "Artist", "Album"));

        Assert.Equal(expectedBadge, viewModel.AudioTierDisplay);
    }

    [Fact]
    public void A_resolved_track_appends_a_history_entry_with_the_current_timestamp()
    {
        var resolution = new FormatResolution(new DeviceFormat(96_000, 24), ResolutionConfidence.Exact, "Catalog match");
        var (viewModel, watcher, _, clock) = CreateSut(resolution);
        clock.UtcNow = DateTimeOffset.UnixEpoch + TimeSpan.FromHours(3);

        watcher.Fire(new Track("Title", "Artist", "Album"));

        var entry = Assert.Single(viewModel.History);
        Assert.Equal(clock.UtcNow, entry.TimestampUtc);
        Assert.Contains("Title", entry.TrackDisplay);
        Assert.Equal(resolution.Target.ToString(), entry.TargetFormatDisplay);
        Assert.Equal("Exact", entry.ConfidenceDisplay);
        Assert.Equal("Catalog match", entry.SourceLayer);
    }

    [Fact]
    public void History_keeps_newest_entries_first()
    {
        var watcher = new FakeTrackWatcher();
        var deviceController = new FakeDeviceController();
        var resolver = new SequencedResolver();
        var coordinator = new TrackSyncCoordinator(watcher, resolver, deviceController, new FakeRestingFormatReverter());
        var viewModel = new DashboardViewModel(coordinator);

        watcher.Fire(new Track("First", "Artist", "Album"));
        watcher.Fire(new Track("Second", "Artist", "Album"));

        Assert.Equal(2, viewModel.History.Count);
        Assert.Contains("Second", viewModel.History[0].TrackDisplay);
        Assert.Contains("First", viewModel.History[1].TrackDisplay);
    }

    [Fact]
    public void History_trims_to_the_maximum_entry_count()
    {
        var watcher = new FakeTrackWatcher();
        var deviceController = new FakeDeviceController();
        var resolver = new SequencedResolver();
        var coordinator = new TrackSyncCoordinator(watcher, resolver, deviceController, new FakeRestingFormatReverter());
        var viewModel = new DashboardViewModel(coordinator);

        for (var i = 0; i < DashboardViewModel.MaxHistoryEntries + 5; i++)
        {
            watcher.Fire(new Track($"Track {i}", "Artist", "Album"));
        }

        Assert.Equal(DashboardViewModel.MaxHistoryEntries, viewModel.History.Count);
        Assert.Contains("Track " + (DashboardViewModel.MaxHistoryEntries + 4), viewModel.History[0].TrackDisplay);
    }

    [Fact]
    public void ApplyLateResolution_appends_a_new_history_entry_for_the_correction()
    {
        var watcher = new FakeTrackWatcher();
        var resolver = new FakeResolver(new FormatResolution(new DeviceFormat(44_100, 24), ResolutionConfidence.Fallback, "Tier fallback"));
        var deviceController = new FakeDeviceController { SupportedSampleRates = new HashSet<int> { 44_100, 96_000 } };
        var coordinator = new TrackSyncCoordinator(watcher, resolver, deviceController, new FakeRestingFormatReverter());
        var viewModel = new DashboardViewModel(coordinator);
        var track = new Track("Title", "Artist", "Album");
        watcher.Fire(track);

        var lateResolution = new FormatResolution(new DeviceFormat(96_000, 24), ResolutionConfidence.Exact, "Catalog match");
        coordinator.ApplyLateResolution(track, lateResolution);

        Assert.Equal(2, viewModel.History.Count);
        Assert.Equal("Exact", viewModel.History[0].ConfidenceDisplay);
        Assert.Equal("Fallback", viewModel.History[1].ConfidenceDisplay);
    }

    [Fact]
    public void IsPaused_reflects_the_coordinators_paused_state()
    {
        var (viewModel, _, _, _) = CreateSut();

        Assert.False(viewModel.IsPaused);
    }

    [Fact]
    public void Coordinator_updates_are_routed_through_the_supplied_ui_thread_invoker()
    {
        var watcher = new FakeTrackWatcher();
        var resolver = new FakeResolver(new FormatResolution(new DeviceFormat(44_100, 24), ResolutionConfidence.Fallback, "Tier fallback"));
        var deviceController = new FakeDeviceController();
        var coordinator = new TrackSyncCoordinator(watcher, resolver, deviceController, new FakeRestingFormatReverter());
        var invokedActions = new List<Action>();
        var viewModel = new DashboardViewModel(coordinator, uiThreadInvoker: invokedActions.Add);

        watcher.Fire(new Track("Title", "Artist", "Album"));

        // The update was captured by the invoker instead of applied immediately.
        Assert.Equal("(none — waiting for Apple Music)", viewModel.TrackDisplay);
        Assert.NotEmpty(invokedActions);

        foreach (var action in invokedActions)
        {
            action();
        }

        Assert.Equal("Title — Artist (Album)", viewModel.TrackDisplay);
    }

    /// <summary>Resolves each distinct Track to a distinct Target Format, so history entries are distinguishable by more than just the track name.</summary>
    private sealed class SequencedResolver : IFormatResolver
    {
        private int _callCount;

        public FormatResolution Resolve(Track track)
        {
            _callCount++;
            return new FormatResolution(new DeviceFormat(44_100 + _callCount, 24), ResolutionConfidence.Fallback, "Tier fallback");
        }
    }
}
