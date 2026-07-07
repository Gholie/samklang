using Samklang.Domain;
using Samklang.Sessions;
using Samklang.SettingsManagement;

namespace Samklang.ViewModels;

/// <summary>
/// The dashboard's now-playing hero card: track title/artist/album, album artwork, playing state,
/// and previous/play-pause/next commands, driven by <see cref="ITrackWatcher"/> and
/// <see cref="IMediaTransport"/> directly (not <see cref="TrackSyncCoordinator"/> — playback
/// navigation is a UI concern, deliberately outside the format-switching pipeline).
///
/// <para>
/// The whole rich card sits behind <see cref="Settings.RichNowPlaying"/> — some users just want a
/// very simple UI — so <see cref="IsRichView"/>/<see cref="IsSimpleView"/> follow
/// <see cref="SettingsManager"/> live and the view swaps between the hero card and a plain text
/// line without a restart.
/// </para>
///
/// <para>
/// <b>Thread marshaling.</b> Same contract as <see cref="DashboardViewModel"/>: the watcher's
/// events fire off the UI thread, so every reaction is wrapped in the caller-supplied
/// <paramref name="uiThreadInvoker"/> — production passes a Dispatcher invoke, tests pass nothing
/// and get synchronous invocation.
/// </para>
/// </summary>
public sealed class NowPlayingViewModel : ViewModelBase
{
    private const string NoTrackTitle = "Nothing playing";
    private const string NoTrackSubtitle = "Waiting for Apple Music…";

    private readonly ITrackWatcher _trackWatcher;
    private readonly IMediaTransport _transport;
    private readonly SettingsManager _settingsManager;
    private readonly Action<Action> _uiThreadInvoker;

    private bool _hasTrack;
    private string _title = NoTrackTitle;
    private string _artist = NoTrackSubtitle;
    private string _album = string.Empty;
    private byte[]? _artworkBytes;
    private bool _isPlaying;
    private bool _isRichView = true;

    public NowPlayingViewModel(
        ITrackWatcher trackWatcher,
        IMediaTransport transport,
        SettingsManager settingsManager,
        Action<Action>? uiThreadInvoker = null)
    {
        _trackWatcher = trackWatcher;
        _transport = transport;
        _settingsManager = settingsManager;
        _uiThreadInvoker = uiThreadInvoker ?? (action => action());

        _trackWatcher.TrackChanged += (_, _) => _uiThreadInvoker(RefreshTrack);
        _trackWatcher.PlaybackStateChanged += (_, _) => _uiThreadInvoker(RefreshPlaybackState);
        _transport.ArtworkChanged += (_, _) => _uiThreadInvoker(RefreshArtwork);
        _settingsManager.PropertyChanged += (_, _) => _uiThreadInvoker(RefreshViewMode);

        // Fire-and-forget by design: SMTC's Try* calls are best-effort (they no-op or fail softly
        // when the session is gone), and a button press has no meaningful continuation to await.
        PreviousCommand = new RelayCommand(() => _ = _transport.SkipPreviousAsync());
        PlayPauseCommand = new RelayCommand(() => _ = _transport.TogglePlayPauseAsync());
        NextCommand = new RelayCommand(() => _ = _transport.SkipNextAsync());

        RefreshTrack();
        RefreshPlaybackState();
        RefreshArtwork();
        RefreshViewMode();
    }

    public RelayCommand PreviousCommand { get; }

    public RelayCommand PlayPauseCommand { get; }

    public RelayCommand NextCommand { get; }

    /// <summary>True while Apple Music has a Track loaded — the transport buttons are only useful then.</summary>
    public bool HasTrack
    {
        get => _hasTrack;
        private set => SetField(ref _hasTrack, value);
    }

    /// <summary>The Track's title, or a "nothing playing" placeholder.</summary>
    public string Title
    {
        get => _title;
        private set => SetField(ref _title, value);
    }

    /// <summary>The Track's artist, or a "waiting for Apple Music" placeholder.</summary>
    public string Artist
    {
        get => _artist;
        private set => SetField(ref _artist, value);
    }

    /// <summary>The Track's album, or empty when there is no track (the line just collapses).</summary>
    public string Album
    {
        get => _album;
        private set => SetField(ref _album, value);
    }

    /// <summary>
    /// The album artwork as encoded image bytes, or null when unavailable. Kept as raw bytes here
    /// (not a WPF ImageSource) so this view model stays framework-free and unit-testable; the view
    /// converts via <c>ArtworkImageConverter</c>.
    /// </summary>
    public byte[]? ArtworkBytes
    {
        get => _artworkBytes;
        private set => SetField(ref _artworkBytes, value);
    }

    public bool IsPlaying
    {
        get => _isPlaying;
        private set => SetField(ref _isPlaying, value);
    }

    /// <summary>The rich now-playing card (artwork, controls, animation) — on when <see cref="Settings.RichNowPlaying"/> is.</summary>
    public bool IsRichView
    {
        get => _isRichView;
        private set
        {
            if (SetField(ref _isRichView, value))
            {
                OnPropertyChanged(nameof(IsSimpleView));
            }
        }
    }

    /// <summary>The inverse of <see cref="IsRichView"/>, exposed so plain BooleanToVisibility bindings work for both halves of the view.</summary>
    public bool IsSimpleView => !_isRichView;

    private void RefreshTrack()
    {
        var track = _trackWatcher.CurrentTrack;
        HasTrack = track is not null;
        Title = track is null || string.IsNullOrWhiteSpace(track.Title) ? NoTrackTitle : track.Title;
        Artist = track is null ? NoTrackSubtitle : track.Artist;
        Album = track?.Album ?? string.Empty;
    }

    private void RefreshPlaybackState() =>
        IsPlaying = _trackWatcher.PlaybackState == PlaybackState.Playing;

    private void RefreshArtwork() =>
        ArtworkBytes = _transport.ArtworkBytes;

    private void RefreshViewMode() =>
        IsRichView = _settingsManager.Current.RichNowPlaying;
}
