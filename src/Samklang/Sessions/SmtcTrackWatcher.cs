using Samklang.Domain;
using Windows.Media.Control;

namespace Samklang.Sessions;

/// <summary>
/// Concrete <see cref="ITrackWatcher"/> backed by the Windows System Media Transport Controls
/// (SMTC) session API (<c>GlobalSystemMediaTransportControlsSessionManager</c>). Watches every
/// media session Windows reports, but only ever surfaces Track changes from the one belonging
/// to Apple Music for Windows; every other session is discarded by
/// <see cref="AppleMusicSessionFilter"/>.
///
/// This class is a thin adapter over a live Windows Runtime API and cannot run outside a real
/// Windows session with SMTC available, so it is not unit-tested directly.
/// <see cref="AppleMusicSessionFilter"/> — the only decision logic here — is tested in isolation
/// instead.
/// </summary>
public sealed class SmtcTrackWatcher : ITrackWatcher, IDisposable
{
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _attachedSession;

    public Track? CurrentTrack { get; private set; }

    public event EventHandler<TrackChangedEventArgs>? TrackChanged;

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
            SetCurrentTrack(null);
            return;
        }

        if (ReferenceEquals(appleMusicSession, _attachedSession))
        {
            return;
        }

        DetachSession();
        _attachedSession = appleMusicSession;
        _attachedSession.MediaPropertiesChanged += OnMediaPropertiesChanged;
        await RefreshTrackAsync(_attachedSession);
    }

    private void DetachSession()
    {
        if (_attachedSession is not null)
        {
            _attachedSession.MediaPropertiesChanged -= OnMediaPropertiesChanged;
        }

        _attachedSession = null;
    }

    private async void OnMediaPropertiesChanged(
        GlobalSystemMediaTransportControlsSession sender,
        MediaPropertiesChangedEventArgs args) =>
        await RefreshTrackAsync(sender);

    private async Task RefreshTrackAsync(GlobalSystemMediaTransportControlsSession session)
    {
        try
        {
            var properties = await session.TryGetMediaPropertiesAsync();
            SetCurrentTrack(new Track(
                properties.Title ?? string.Empty,
                properties.Artist ?? string.Empty,
                properties.AlbumTitle ?? string.Empty));
        }
        catch
        {
            // The session can disappear mid-read (app closing, track skip mid-flight); treat it
            // as "no track" rather than propagating a transient COM failure into the UI.
            SetCurrentTrack(null);
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
}
