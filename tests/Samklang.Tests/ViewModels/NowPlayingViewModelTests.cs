using Samklang.Domain;
using Samklang.Sessions;
using Samklang.SettingsManagement;
using Samklang.ViewModels;
using Xunit;

namespace Samklang.Tests.ViewModels;

public class NowPlayingViewModelTests
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

    private sealed class FakeMediaTransport : IMediaTransport
    {
        public byte[]? ArtworkBytes { get; private set; }
        public event EventHandler? ArtworkChanged;

        public int SkipPreviousCallCount { get; private set; }
        public int TogglePlayPauseCallCount { get; private set; }
        public int SkipNextCallCount { get; private set; }

        public Task SkipPreviousAsync()
        {
            SkipPreviousCallCount++;
            return Task.CompletedTask;
        }

        public Task TogglePlayPauseAsync()
        {
            TogglePlayPauseCallCount++;
            return Task.CompletedTask;
        }

        public Task SkipNextAsync()
        {
            SkipNextCallCount++;
            return Task.CompletedTask;
        }

        public void FireArtwork(byte[]? bytes)
        {
            ArtworkBytes = bytes;
            ArtworkChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed class FakeSettingsStore : ISettingsStore
    {
        public Settings? Stored { get; set; }

        public Settings? Load() => Stored;

        public void Save(Settings settings) => Stored = settings;
    }

    private static (NowPlayingViewModel ViewModel, FakeTrackWatcher Watcher, FakeMediaTransport Transport, SettingsManager SettingsManager) CreateSut()
    {
        var watcher = new FakeTrackWatcher();
        var transport = new FakeMediaTransport();
        var settingsManager = new SettingsManager(new FakeSettingsStore());
        settingsManager.LoadOrSeed(new DeviceFormat(44_100, 24));
        var viewModel = new NowPlayingViewModel(watcher, transport, settingsManager);
        return (viewModel, watcher, transport, settingsManager);
    }

    [Fact]
    public void Initial_state_shows_placeholders_with_no_artwork_and_not_playing()
    {
        var (viewModel, _, _, _) = CreateSut();

        Assert.False(viewModel.HasTrack);
        Assert.Equal("Nothing playing", viewModel.Title);
        Assert.Equal("Waiting for Apple Music…", viewModel.Artist);
        Assert.Equal(string.Empty, viewModel.Album);
        Assert.Null(viewModel.ArtworkBytes);
        Assert.False(viewModel.IsPlaying);
    }

    [Fact]
    public void Rich_view_is_on_by_default()
    {
        var (viewModel, _, _, _) = CreateSut();

        Assert.True(viewModel.IsRichView);
        Assert.False(viewModel.IsSimpleView);
    }

    [Fact]
    public void TrackChanged_updates_title_artist_and_album()
    {
        var (viewModel, watcher, _, _) = CreateSut();

        watcher.Fire(new Track("Title", "Artist", "Album"));

        Assert.True(viewModel.HasTrack);
        Assert.Equal("Title", viewModel.Title);
        Assert.Equal("Artist", viewModel.Artist);
        Assert.Equal("Album", viewModel.Album);
    }

    [Fact]
    public void TrackChanged_to_null_resets_to_placeholders()
    {
        var (viewModel, watcher, _, _) = CreateSut();

        watcher.Fire(new Track("Title", "Artist", "Album"));
        watcher.Fire(null);

        Assert.False(viewModel.HasTrack);
        Assert.Equal("Nothing playing", viewModel.Title);
        Assert.Equal("Waiting for Apple Music…", viewModel.Artist);
        Assert.Equal(string.Empty, viewModel.Album);
    }

    [Fact]
    public void A_track_with_a_blank_title_falls_back_to_the_placeholder_title()
    {
        var (viewModel, watcher, _, _) = CreateSut();

        watcher.Fire(new Track("  ", "Artist", "Album"));

        Assert.Equal("Nothing playing", viewModel.Title);
        Assert.Equal("Artist", viewModel.Artist);
    }

    [Fact]
    public void IsPlaying_follows_the_watchers_playback_state()
    {
        var (viewModel, watcher, _, _) = CreateSut();

        watcher.FirePlaybackState(PlaybackState.Playing);
        Assert.True(viewModel.IsPlaying);

        watcher.FirePlaybackState(PlaybackState.Paused);
        Assert.False(viewModel.IsPlaying);

        watcher.FirePlaybackState(null);
        Assert.False(viewModel.IsPlaying);
    }

    [Fact]
    public void ArtworkChanged_surfaces_the_transports_artwork_bytes()
    {
        var (viewModel, _, transport, _) = CreateSut();
        var artwork = new byte[] { 1, 2, 3 };

        transport.FireArtwork(artwork);
        Assert.Same(artwork, viewModel.ArtworkBytes);

        transport.FireArtwork(null);
        Assert.Null(viewModel.ArtworkBytes);
    }

    [Fact]
    public void Transport_commands_forward_to_the_media_transport()
    {
        var (viewModel, _, transport, _) = CreateSut();

        viewModel.PreviousCommand.Execute(null);
        viewModel.PlayPauseCommand.Execute(null);
        viewModel.PlayPauseCommand.Execute(null);
        viewModel.NextCommand.Execute(null);

        Assert.Equal(1, transport.SkipPreviousCallCount);
        Assert.Equal(2, transport.TogglePlayPauseCallCount);
        Assert.Equal(1, transport.SkipNextCallCount);
    }

    [Fact]
    public void Turning_the_rich_now_playing_setting_off_flips_to_the_simple_view_live()
    {
        var (viewModel, _, _, settingsManager) = CreateSut();

        settingsManager.UpdateRichNowPlaying(false);

        Assert.False(viewModel.IsRichView);
        Assert.True(viewModel.IsSimpleView);

        settingsManager.UpdateRichNowPlaying(true);

        Assert.True(viewModel.IsRichView);
        Assert.False(viewModel.IsSimpleView);
    }

    [Fact]
    public void Construction_reads_a_persisted_rich_now_playing_off_setting()
    {
        var watcher = new FakeTrackWatcher();
        var transport = new FakeMediaTransport();
        var settingsManager = new SettingsManager(new FakeSettingsStore());
        settingsManager.LoadOrSeed(new DeviceFormat(44_100, 24));
        settingsManager.UpdateRichNowPlaying(false);

        var viewModel = new NowPlayingViewModel(watcher, transport, settingsManager);

        Assert.False(viewModel.IsRichView);
        Assert.True(viewModel.IsSimpleView);
    }

    [Fact]
    public void Watcher_updates_are_routed_through_the_supplied_ui_thread_invoker()
    {
        var watcher = new FakeTrackWatcher();
        var transport = new FakeMediaTransport();
        var settingsManager = new SettingsManager(new FakeSettingsStore());
        settingsManager.LoadOrSeed(new DeviceFormat(44_100, 24));
        var invokedActions = new List<Action>();
        var viewModel = new NowPlayingViewModel(watcher, transport, settingsManager, uiThreadInvoker: invokedActions.Add);

        watcher.Fire(new Track("Title", "Artist", "Album"));

        // The update was captured by the invoker instead of applied immediately.
        Assert.Equal("Nothing playing", viewModel.Title);
        Assert.NotEmpty(invokedActions);

        foreach (var action in invokedActions)
        {
            action();
        }

        Assert.Equal("Title", viewModel.Title);
    }
}
