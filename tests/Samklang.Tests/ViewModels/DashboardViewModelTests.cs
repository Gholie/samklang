using Samklang.Devices;
using Samklang.Domain;
using Samklang.Resolver;
using Samklang.Resolver.Catalog;
using Samklang.Sessions;
using Samklang.SettingsManagement;
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
    public void Bit_depth_pinning_alone_does_not_show_the_clamped_format_warning()
    {
        // A 16-bit source is applied at the pinned 24-bit depth by design — that intentional
        // pinning must not read as "device doesn't support 16-bit/44.1 kHz".
        var resolution = new FormatResolution(new DeviceFormat(44_100, 16), ResolutionConfidence.Exact, "PlayCache", IsLossless: true);
        var (viewModel, watcher, _, _) = CreateSut(resolution);

        watcher.Fire(new Track("Title", "Artist", "Album"));

        Assert.False(viewModel.HasClampedFormat);
        Assert.Equal(string.Empty, viewModel.ClampedFormatDisplay);
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

    [Fact]
    public void Audio_tier_badge_is_unknown_for_a_fallback_resolution()
    {
        var resolution = new FormatResolution(new DeviceFormat(44_100, 24), ResolutionConfidence.Fallback, "Tier fallback");
        var (viewModel, watcher, _, _) = CreateSut(resolution);

        watcher.Fire(new Track("Title", "Artist", "Album"));

        Assert.Equal("Unknown", viewModel.AudioTierDisplay);
    }

    [Fact]
    public void Audio_tier_badge_shows_lossless_for_a_resolution_a_layer_confirmed_is_lossless()
    {
        var resolution = new FormatResolution(new DeviceFormat(96_000, 24), ResolutionConfidence.Exact, "Catalog match", IsLossless: true);
        var (viewModel, watcher, _, _) = CreateSut(resolution);

        watcher.Fire(new Track("Title", "Artist", "Album"));

        Assert.Equal("Hi-Res Lossless", viewModel.AudioTierDisplay);
    }

    /// <summary>
    /// Regression test: PlayCacheFormatResolverLayer reports ResolutionConfidence.Exact for a
    /// plain lossy AAC/MP3 file just as confidently as for genuine ALAC — the dashboard must not
    /// badge that "Lossless" just because the confidence is Exact and the rate happens to sit in
    /// the Lossless range.
    /// </summary>
    [Fact]
    public void Audio_tier_badge_does_not_show_lossless_for_a_PlayCache_sourced_resolution_known_to_be_lossy()
    {
        var resolution = new FormatResolution(new DeviceFormat(44_100, 24), ResolutionConfidence.Exact, "PlayCache", IsLossless: false);
        var (viewModel, watcher, _, _) = CreateSut(resolution);

        watcher.Fire(new Track("Title", "Artist", "Album"));

        Assert.Equal("Lossy Stereo", viewModel.AudioTierDisplay);
        Assert.NotEqual("Lossless", viewModel.AudioTierDisplay);
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

    // --- Album track list ---

    private static readonly IReadOnlyList<CatalogSearchCandidate> Album =
    [
        new("id-1", "Track One", "Artist", "Album", AlbumId: "album-1"),
        new("id-2", "Track Two", "Artist", "Album", AlbumId: "album-1"),
        new("id-3", "Track Three", "Artist", "Album", AlbumId: "album-1"),
    ];

    [Fact]
    public void Album_tracks_populate_in_album_order_with_the_playing_track_flagged_current()
    {
        var (viewModel, watcher, _, _) = CreateSut();
        watcher.Fire(new Track("Track Two", "Artist", "Album"));

        viewModel.OnAlbumTracksAvailable(Album);

        Assert.True(viewModel.HasAlbumTracks);
        Assert.Equal("Album — Album", viewModel.AlbumTracksHeader);
        Assert.Equal([1, 2, 3], viewModel.AlbumTracks.Select(entry => entry.Number));
        Assert.Equal(["Track One", "Track Two", "Track Three"], viewModel.AlbumTracks.Select(entry => entry.Title));
        Assert.Equal([false, true, false], viewModel.AlbumTracks.Select(entry => entry.IsCurrent));
    }

    [Fact]
    public void Album_tracks_delivered_before_the_track_change_still_populate_once_the_track_arrives()
    {
        // The catalog event and the SMTC track change race; the album must show either way.
        var (viewModel, watcher, _, _) = CreateSut();

        viewModel.OnAlbumTracksAvailable(Album);
        Assert.False(viewModel.HasAlbumTracks);

        watcher.Fire(new Track("Track One", "Artist", "Album"));

        Assert.True(viewModel.HasAlbumTracks);
        Assert.Equal([true, false, false], viewModel.AlbumTracks.Select(entry => entry.IsCurrent));
    }

    [Fact]
    public void A_track_change_within_the_same_album_moves_the_current_flag()
    {
        var (viewModel, watcher, _, _) = CreateSut();
        watcher.Fire(new Track("Track One", "Artist", "Album"));
        viewModel.OnAlbumTracksAvailable(Album);

        watcher.Fire(new Track("Track Three", "Artist", "Album"));

        Assert.Equal([false, false, true], viewModel.AlbumTracks.Select(entry => entry.IsCurrent));
    }

    [Fact]
    public void A_track_change_to_a_song_outside_the_album_clears_the_list()
    {
        var (viewModel, watcher, _, _) = CreateSut();
        watcher.Fire(new Track("Track One", "Artist", "Album"));
        viewModel.OnAlbumTracksAvailable(Album);

        watcher.Fire(new Track("Somewhere Else", "Other Artist", "Other Album"));

        Assert.False(viewModel.HasAlbumTracks);
        Assert.Empty(viewModel.AlbumTracks);
        Assert.Equal("Album", viewModel.AlbumTracksHeader);
    }

    [Fact]
    public void A_null_track_keeps_the_album_list_for_when_playback_resumes()
    {
        var (viewModel, watcher, _, _) = CreateSut();
        watcher.Fire(new Track("Track One", "Artist", "Album"));
        viewModel.OnAlbumTracksAvailable(Album);

        watcher.Fire(null);

        Assert.True(viewModel.HasAlbumTracks);
        Assert.Equal(3, viewModel.AlbumTracks.Count);
    }

    /// <summary>
    /// A transient SMTC placeholder (empty artist) between tracks never becomes a Track change at
    /// the coordinator level (see TrackSyncCoordinator/TransientTrackDetector), so the
    /// coordinator never raises CurrentTrack's PropertyChanged for it. This locks in the
    /// consequence for the dashboard: unlike a real track change to a song outside the album
    /// (which clears the list), the brief "Connecting…"/empty-artist gap between two songs on the
    /// *same* album must not wipe the album track list out from under the user.
    /// </summary>
    [Fact]
    public void A_transient_placeholder_between_tracks_does_not_clear_the_album_list()
    {
        var (viewModel, watcher, _, _) = CreateSut();
        watcher.Fire(new Track("Track One", "Artist", "Album"));
        viewModel.OnAlbumTracksAvailable(Album);

        watcher.Fire(new Track("Connecting…", "", ""));

        Assert.True(viewModel.HasAlbumTracks);
        Assert.Equal([true, false, false], viewModel.AlbumTracks.Select(entry => entry.IsCurrent));
    }

    // --- Playing / queuing an album track (album-track commands) ---

    private sealed class FakePlaybackController : IAppleMusicPlaybackController
    {
        public List<(AlbumTrackTarget Target, QueuePlacement Placement)> Invocations { get; } = [];

        public Task PlayAlbumTrackAsync(AlbumTrackTarget target, QueuePlacement placement)
        {
            Invocations.Add((target, placement));
            return Task.CompletedTask;
        }
    }

    private static (DashboardViewModel ViewModel, FakeTrackWatcher Watcher, FakePlaybackController Controller) CreateSutWithController(
        bool appControlEnabled = true)
    {
        var watcher = new FakeTrackWatcher();
        var resolver = new FakeResolver(new FormatResolution(new DeviceFormat(44_100, 24), ResolutionConfidence.Fallback, "Tier fallback"));
        var coordinator = new TrackSyncCoordinator(watcher, resolver, new FakeDeviceController(), new FakeRestingFormatReverter());
        var controller = new FakePlaybackController();
        // The album-track play/queue actions drive the Apple Music app, which is opt-in
        // (Settings.ControlAppleMusicApp); default this to on so the play/queue tests exercise the
        // enabled path, and pass false to cover the opted-out behavior.
        var settingsManager = new SettingsManager(new FakeSettingsStore());
        settingsManager.LoadOrSeed(new DeviceFormat(44_100, 24));
        settingsManager.UpdateControlAppleMusicApp(appControlEnabled);
        var viewModel = new DashboardViewModel(coordinator, settingsManager: settingsManager, playbackController: controller);
        return (viewModel, watcher, controller);
    }

    [Fact]
    public void Clicking_a_track_plays_that_exact_song()
    {
        // FakePlaybackController completes synchronously, so PlayAlbumTrackCommand's fire-and-forget
        // call (see InvokeAlbumTrackAsync) runs to completion within Execute. The row's number,
        // title and ids are handed over so the controller can navigate to and play that exact song.
        var (viewModel, watcher, controller) = CreateSutWithController();
        watcher.Fire(new Track("Track One", "Artist", "Album"));
        viewModel.OnAlbumTracksAvailable(Album);

        var target = viewModel.AlbumTracks.Single(entry => entry.Title == "Track Three");
        viewModel.PlayAlbumTrackCommand.Execute(target);

        Assert.Equal(
            [(new AlbumTrackTarget(3, "Track Three", "id-3", "album-1"), QueuePlacement.PlayNow)],
            controller.Invocations);
    }

    [Fact]
    public void Clicking_a_track_plays_the_right_song_even_when_it_is_not_the_next_or_previous_row()
    {
        // The bug the deep-link approach fixed: the old walk-based navigation assumed the play queue
        // was this album in order, so on a discovery station or shuffled playlist it landed on an
        // unrelated song. Targeting the exact catalog track is queue-independent — the clicked song
        // plays regardless of how far it sits from the currently-flagged row.
        var (viewModel, watcher, controller) = CreateSutWithController();
        watcher.Fire(new Track("Track One", "Artist", "Album"));
        viewModel.OnAlbumTracksAvailable(Album);

        var target = viewModel.AlbumTracks.Single(entry => entry.Title == "Track Two");
        viewModel.PlayAlbumTrackCommand.Execute(target);

        Assert.Equal(
            [(new AlbumTrackTarget(2, "Track Two", "id-2", "album-1"), QueuePlacement.PlayNow)],
            controller.Invocations);
    }

    [Fact]
    public void Play_next_button_queues_the_track_at_the_top_of_the_queue()
    {
        var (viewModel, watcher, controller) = CreateSutWithController();
        watcher.Fire(new Track("Track One", "Artist", "Album"));
        viewModel.OnAlbumTracksAvailable(Album);

        var target = viewModel.AlbumTracks.Single(entry => entry.Title == "Track Three");
        viewModel.PlayTrackNextCommand.Execute(target);

        Assert.Equal(
            [(new AlbumTrackTarget(3, "Track Three", "id-3", "album-1"), QueuePlacement.PlayNext)],
            controller.Invocations);
    }

    [Fact]
    public void Play_last_button_appends_the_track_to_the_end_of_the_queue()
    {
        var (viewModel, watcher, controller) = CreateSutWithController();
        watcher.Fire(new Track("Track One", "Artist", "Album"));
        viewModel.OnAlbumTracksAvailable(Album);

        var target = viewModel.AlbumTracks.Single(entry => entry.Title == "Track Two");
        viewModel.PlayTrackLastCommand.Execute(target);

        Assert.Equal(
            [(new AlbumTrackTarget(2, "Track Two", "id-2", "album-1"), QueuePlacement.PlayLast)],
            controller.Invocations);
    }

    [Fact]
    public void Clicking_the_currently_playing_track_is_disabled()
    {
        var (viewModel, watcher, _) = CreateSutWithController();
        watcher.Fire(new Track("Track Two", "Artist", "Album"));
        viewModel.OnAlbumTracksAvailable(Album);

        var current = viewModel.AlbumTracks.Single(entry => entry.IsCurrent);

        Assert.False(viewModel.PlayAlbumTrackCommand.CanExecute(current));
    }

    [Fact]
    public void Queue_buttons_stay_enabled_for_the_currently_playing_row()
    {
        // Unlike the row-click Play, "Play Next"/"Play Last" on the current track are valid (queue
        // it up again), so they must not be disabled the way the row-click is.
        var (viewModel, watcher, _) = CreateSutWithController();
        watcher.Fire(new Track("Track Two", "Artist", "Album"));
        viewModel.OnAlbumTracksAvailable(Album);

        var current = viewModel.AlbumTracks.Single(entry => entry.IsCurrent);

        Assert.True(viewModel.PlayTrackNextCommand.CanExecute(current));
        Assert.True(viewModel.PlayTrackLastCommand.CanExecute(current));
    }

    [Fact]
    public void Playing_a_track_is_disabled_for_a_null_parameter()
    {
        // Guards against a stray CanExecute probe with no CommandParameter bound yet (e.g. WPF's
        // own CommandManager requery) rather than assuming a row is always supplied.
        var (viewModel, watcher, _) = CreateSutWithController();
        watcher.Fire(new Track("Track One", "Artist", "Album"));
        viewModel.OnAlbumTracksAvailable(Album);

        Assert.False(viewModel.PlayAlbumTrackCommand.CanExecute(null));
        Assert.False(viewModel.PlayTrackNextCommand.CanExecute(null));
        Assert.False(viewModel.PlayTrackLastCommand.CanExecute(null));
    }

    [Fact]
    public void Album_track_commands_are_disabled_without_a_controller()
    {
        var (viewModel, watcher, _, _) = CreateSut();
        watcher.Fire(new Track("Track One", "Artist", "Album"));
        viewModel.OnAlbumTracksAvailable(Album);

        var target = viewModel.AlbumTracks.Single(entry => entry.Title == "Track Two");

        Assert.False(viewModel.PlayAlbumTrackCommand.CanExecute(target));
        Assert.False(viewModel.PlayTrackNextCommand.CanExecute(target));
        Assert.False(viewModel.PlayTrackLastCommand.CanExecute(target));
    }

    [Fact]
    public void Queue_commands_are_disabled_when_app_control_is_off()
    {
        // The Play Next / Play Last actions only work by driving the app, so without the opt-in
        // they're disabled (and their buttons hidden) — but the row-click Play still works: with app
        // control off it falls back to just opening the album via the controller.
        var (viewModel, watcher, _) = CreateSutWithController(appControlEnabled: false);
        watcher.Fire(new Track("Track One", "Artist", "Album"));
        viewModel.OnAlbumTracksAvailable(Album);

        var target = viewModel.AlbumTracks.Single(entry => entry.Title == "Track Two");

        Assert.False(viewModel.IsAppControlEnabled);
        Assert.False(viewModel.PlayTrackNextCommand.CanExecute(target));
        Assert.False(viewModel.PlayTrackLastCommand.CanExecute(target));
        Assert.True(viewModel.PlayAlbumTrackCommand.CanExecute(target));
    }

    [Fact]
    public void Clicking_a_track_still_invokes_the_controller_when_app_control_is_off()
    {
        // With app control off the controller navigates only (opens the album); the view model still
        // hands the click to it — the navigate-or-drive decision lives in the controller, not here.
        var (viewModel, watcher, controller) = CreateSutWithController(appControlEnabled: false);
        watcher.Fire(new Track("Track One", "Artist", "Album"));
        viewModel.OnAlbumTracksAvailable(Album);

        var target = viewModel.AlbumTracks.Single(entry => entry.Title == "Track Three");
        viewModel.PlayAlbumTrackCommand.Execute(target);

        Assert.Equal(
            [(new AlbumTrackTarget(3, "Track Three", "id-3", "album-1"), QueuePlacement.PlayNow)],
            controller.Invocations);
    }

    [Fact]
    public void Toggling_app_control_on_in_settings_enables_the_queue_commands_live()
    {
        var watcher = new FakeTrackWatcher();
        var resolver = new FakeResolver(new FormatResolution(new DeviceFormat(44_100, 24), ResolutionConfidence.Fallback, "Tier fallback"));
        var coordinator = new TrackSyncCoordinator(watcher, resolver, new FakeDeviceController(), new FakeRestingFormatReverter());
        var settingsManager = new SettingsManager(new FakeSettingsStore());
        settingsManager.LoadOrSeed(new DeviceFormat(44_100, 24));
        settingsManager.UpdateControlAppleMusicApp(false);
        var viewModel = new DashboardViewModel(coordinator, settingsManager: settingsManager, playbackController: new FakePlaybackController());
        watcher.Fire(new Track("Track One", "Artist", "Album"));
        viewModel.OnAlbumTracksAvailable(Album);
        var target = viewModel.AlbumTracks.Single(entry => entry.Title == "Track Two");
        Assert.False(viewModel.PlayTrackNextCommand.CanExecute(target));

        settingsManager.UpdateControlAppleMusicApp(true);

        Assert.True(viewModel.IsAppControlEnabled);
        Assert.True(viewModel.PlayTrackNextCommand.CanExecute(target));
    }

    // --- Switch-log toggle ---

    private sealed class FakeSettingsStore : ISettingsStore
    {
        public Settings? Stored { get; set; }

        public Settings? Load() => Stored;

        public void Save(Settings settings) => Stored = settings;
    }

    [Fact]
    public void SelectedTabIndex_defaults_to_the_Playing_Next_tab()
    {
        var (viewModel, _, _, _) = CreateSut();

        Assert.Equal(0, viewModel.SelectedTabIndex);
    }

    [Fact]
    public void SelectedTabIndex_seeds_to_the_History_tab_when_show_switch_log_is_enabled_at_startup()
    {
        var watcher = new FakeTrackWatcher();
        var resolver = new FakeResolver(new FormatResolution(new DeviceFormat(44_100, 24), ResolutionConfidence.Fallback, "Tier fallback"));
        var coordinator = new TrackSyncCoordinator(watcher, resolver, new FakeDeviceController(), new FakeRestingFormatReverter());
        var settingsManager = new SettingsManager(new FakeSettingsStore());
        settingsManager.LoadOrSeed(new DeviceFormat(44_100, 24));
        settingsManager.UpdateShowSwitchLog(true);

        // ShowSwitchLog only seeds SelectedTabIndex once, in the constructor (see
        // DashboardViewModel's doc comment) — it is not tracked live, so the setting must already
        // be true before construction for this to have any effect.
        var viewModel = new DashboardViewModel(coordinator, settingsManager: settingsManager);

        Assert.Equal(1, viewModel.SelectedTabIndex);
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
