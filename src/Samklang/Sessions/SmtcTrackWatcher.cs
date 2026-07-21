using Samklang.Domain;
using Samklang.Logging;
using Windows.Media.Control;
using Windows.Storage.Streams;
using AppPlaybackState = Samklang.Domain.PlaybackState;

namespace Samklang.Sessions;

/// <summary>
/// Concrete <see cref="ITrackWatcher"/> backed by the Windows System Media Transport Controls
/// (SMTC) session API (<c>GlobalSystemMediaTransportControlsSessionManager</c>). Watches every
/// media session Windows reports, but only ever surfaces Track changes from the one belonging
/// to Apple Music for Windows; every other session is discarded by
/// <see cref="AppleMusicSessionFilter"/>.
///
/// Also implements <see cref="IMediaTransport"/> over the same attached session, since SMTC is
/// both the source of track metadata/artwork and the channel for previous/play-pause/next.
///
/// This class is a thin adapter over a live Windows Runtime API and cannot run outside a real
/// Windows session with SMTC available, so it is not unit-tested directly. Its decision logic
/// lives in classes tested in isolation instead: <see cref="AppleMusicSessionFilter"/> (which
/// session to attach), <see cref="StaleRefreshGuard"/> (which of several overlapping refreshes
/// may publish its results), and <see cref="SmtcTrackMetadataParser"/> (how the raw property
/// strings become a Track).
/// </summary>
public sealed class SmtcTrackWatcher : ITrackWatcher, IMediaTransport, IDisposable
{
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _attachedSession;

    // MediaPropertiesChanged fires several times per track change (Apple Music updates the title
    // first, the thumbnail a beat later), so RefreshTrackAsync calls overlap. This guard makes the
    // newest refresh win: an older refresh whose slow thumbnail read finishes late is discarded
    // instead of overwriting the current track's artwork with the previous track's (issue #30).
    private readonly StaleRefreshGuard _refreshGuard = new();

    public Track? CurrentTrack { get; private set; }

    public event EventHandler<TrackChangedEventArgs>? TrackChanged;

    public AppPlaybackState? PlaybackState { get; private set; }

    public event EventHandler<PlaybackStateChangedEventArgs>? PlaybackStateChanged;

    public byte[]? ArtworkBytes { get; private set; }

    public event EventHandler? ArtworkChanged;

    public TimeSpan? PlaybackPosition
    {
        get
        {
            if (_attachedSession is not { } session)
            {
                return null;
            }

            try
            {
                return session.GetTimelineProperties().Position;
            }
            catch (Exception ex)
            {
                // Mirrors RefreshPlaybackState's handling: a session that disappears mid-read
                // reads as "no position" rather than propagating a transient COM failure.
                AppLog.Warn($"Failed to read playback position from the SMTC session: {ex.GetType().Name}: {ex.Message}", category: "TrackWatcher");
                return null;
            }
        }
    }

    public async Task StartAsync()
    {
        _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        _manager.SessionsChanged += OnSessionsChanged;
        await RefreshAttachedSessionAsync();
    }

    public void Dispose()
    {
        if (_manager is not null)
        {
            _manager.SessionsChanged -= OnSessionsChanged;
        }

        DetachSession();
    }

    private async void OnSessionsChanged(
        GlobalSystemMediaTransportControlsSessionManager sender,
        SessionsChangedEventArgs args) =>
        await RefreshAttachedSessionAsync();

    private async Task RefreshAttachedSessionAsync()
    {
        if (_manager is null)
        {
            return;
        }

        var appleMusicSession = _manager.GetSessions()
            .FirstOrDefault(session => AppleMusicSessionFilter.IsAppleMusicSession(session.SourceAppUserModelId));

        if (appleMusicSession is null)
        {
            DetachSession();

            // Supersede (not just apply): an in-flight RefreshTrackAsync for the now-dead session
            // must not resurrect its track/artwork after this clear.
            _refreshGuard.Supersede(() =>
            {
                SetCurrentTrack(null);
                SetArtwork(null);
            });
            SetPlaybackState(null);
            return;
        }

        if (ReferenceEquals(appleMusicSession, _attachedSession))
        {
            return;
        }

        DetachSession();
        _attachedSession = appleMusicSession;
        _attachedSession.MediaPropertiesChanged += OnMediaPropertiesChanged;
        _attachedSession.PlaybackInfoChanged += OnPlaybackInfoChanged;
        await RefreshTrackAsync(_attachedSession);
        RefreshPlaybackState(_attachedSession);
    }

    private void DetachSession()
    {
        if (_attachedSession is not null)
        {
            _attachedSession.MediaPropertiesChanged -= OnMediaPropertiesChanged;
            _attachedSession.PlaybackInfoChanged -= OnPlaybackInfoChanged;
        }

        _attachedSession = null;
    }

    private async void OnMediaPropertiesChanged(
        GlobalSystemMediaTransportControlsSession sender,
        MediaPropertiesChangedEventArgs args) =>
        await RefreshTrackAsync(sender);

    private async Task RefreshTrackAsync(GlobalSystemMediaTransportControlsSession session)
    {
        var token = _refreshGuard.Begin();
        try
        {
            var properties = await session.TryGetMediaPropertiesAsync();

            // Apple Music reports Artist as "Artist — Album" (and usually leaves AlbumTitle
            // empty); the parser undoes that join so the catalog layer sees a clean artist.
            var track = SmtcTrackMetadataParser.Parse(
                properties.Title,
                properties.Artist,
                properties.AlbumTitle);

            // Apply the track before the (slower) artwork read so downstream consumers — the
            // format-switching pipeline in particular — react as early as before this guard
            // existed.
            _refreshGuard.TryApply(token, () => SetCurrentTrack(track));

            var artwork = await TryReadArtworkAsync(properties.Thumbnail);
            _refreshGuard.TryApply(token, () => SetArtwork(artwork));
        }
        catch (Exception ex)
        {
            // The session can disappear mid-read (app closing, track skip mid-flight); treat it
            // as "no track" rather than propagating a transient COM failure into the UI — unless
            // a newer refresh has already taken over, in which case its result stands.
            AppLog.Error("Failed to read track metadata from the SMTC session — treating as no track.", ex, category: "TrackWatcher");
            _refreshGuard.TryApply(token, () =>
            {
                SetCurrentTrack(null);
                SetArtwork(null);
            });
        }
    }

    /// <summary>
    /// Reads the session's thumbnail stream (the album artwork) into encoded image bytes, or null
    /// when there is no thumbnail or it disappears mid-read — artwork is decoration, so any
    /// failure here degrades to "no artwork" rather than surfacing an error.
    /// </summary>
    private static async Task<byte[]?> TryReadArtworkAsync(IRandomAccessStreamReference? thumbnail)
    {
        if (thumbnail is null)
        {
            return null;
        }

        try
        {
            using var stream = await thumbnail.OpenReadAsync();
            using var reader = new DataReader(stream.GetInputStreamAt(0));
            var size = (uint)stream.Size;
            await reader.LoadAsync(size);
            var bytes = new byte[size];
            reader.ReadBytes(bytes);
            return bytes;
        }
        catch
        {
            return null;
        }
    }

    public async Task SkipPreviousAsync()
    {
        if (_attachedSession is not { } session)
        {
            return;
        }

        try
        {
            await session.TrySkipPreviousAsync();
        }
        catch (Exception ex)
        {
            // Session gone mid-call (app closing); the button press just does nothing.
            AppLog.Warn($"SkipPrevious command failed: {ex.GetType().Name}: {ex.Message}", category: "MediaTransport");
        }
    }

    public async Task TogglePlayPauseAsync()
    {
        if (_attachedSession is not { } session)
        {
            return;
        }

        try
        {
            await session.TryTogglePlayPauseAsync();
        }
        catch (Exception ex)
        {
            // Session gone mid-call (app closing); the button press just does nothing.
            AppLog.Warn($"TogglePlayPause command failed: {ex.GetType().Name}: {ex.Message}", category: "MediaTransport");
        }
    }

    public async Task SkipNextAsync()
    {
        if (_attachedSession is not { } session)
        {
            return;
        }

        try
        {
            await session.TrySkipNextAsync();
        }
        catch (Exception ex)
        {
            // Session gone mid-call (app closing); the button press just does nothing.
            AppLog.Warn($"SkipNext command failed: {ex.GetType().Name}: {ex.Message}", category: "MediaTransport");
        }
    }

    private void OnPlaybackInfoChanged(
        GlobalSystemMediaTransportControlsSession sender,
        PlaybackInfoChangedEventArgs args) =>
        RefreshPlaybackState(sender);

    private void RefreshPlaybackState(GlobalSystemMediaTransportControlsSession session)
    {
        try
        {
            var status = session.GetPlaybackInfo()?.PlaybackStatus;
            SetPlaybackState(status switch
            {
                GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing => AppPlaybackState.Playing,
                GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused => AppPlaybackState.Paused,
                // Stopped/Closed/Opened/Changing are all treated as idle ("not actively
                // playing"); Grace Period revert only cares about Playing vs. everything else.
                _ => AppPlaybackState.Stopped,
            });
        }
        catch (Exception ex)
        {
            // Mirrors RefreshTrackAsync's handling: a session that disappears mid-read reads as
            // idle rather than propagating a transient COM failure.
            AppLog.Warn($"Failed to read playback state from the SMTC session — treating as stopped: {ex.GetType().Name}: {ex.Message}", category: "TrackWatcher");
            SetPlaybackState(AppPlaybackState.Stopped);
        }
    }

    private void SetCurrentTrack(Track? track)
    {
        // Track is a record, so this is a structural comparison: Apple Music re-firing
        // MediaPropertiesChanged with identical metadata (it does, occasionally) won't cause a
        // redundant TrackChanged/resolution/switch cycle downstream.
        if (CurrentTrack == track)
        {
            return;
        }

        CurrentTrack = track;
        TrackChanged?.Invoke(this, new TrackChangedEventArgs(track));
    }

    private void SetArtwork(byte[]? bytes)
    {
        // Structural comparison, mirroring SetCurrentTrack: Apple Music occasionally re-fires
        // MediaPropertiesChanged with identical metadata, and re-announcing identical artwork
        // would make the UI rebuild its bitmap for nothing. Artwork blobs are small (tens of KB),
        // so the sequence compare is cheap.
        if (ArtworkBytes is not null && bytes is not null && ArtworkBytes.AsSpan().SequenceEqual(bytes))
        {
            return;
        }

        if (ArtworkBytes is null && bytes is null)
        {
            return;
        }

        ArtworkBytes = bytes;
        ArtworkChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SetPlaybackState(AppPlaybackState? state)
    {
        if (PlaybackState == state)
        {
            return;
        }

        PlaybackState = state;
        PlaybackStateChanged?.Invoke(this, new PlaybackStateChangedEventArgs(state));
    }
}
