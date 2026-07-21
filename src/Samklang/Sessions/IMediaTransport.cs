namespace Samklang.Sessions;

/// <summary>
/// The Apple Music session's media-navigation surface: album artwork plus previous/play-pause/next
/// commands, layered over the same SMTC session <see cref="ITrackWatcher"/> already watches. Kept
/// separate from <see cref="ITrackWatcher"/> because the format-switching pipeline
/// (<see cref="TrackSyncCoordinator"/>) only observes tracks and must never depend on the ability
/// to drive playback — only the now-playing UI does.
/// </summary>
public interface IMediaTransport
{
    /// <summary>
    /// The current Track's album artwork as encoded image bytes (whatever format the media session
    /// hands out — typically PNG/JPEG), or null when there is no session, no track, or no artwork.
    /// </summary>
    byte[]? ArtworkBytes { get; }

    /// <summary>Raised whenever <see cref="ArtworkBytes"/> changes, including transitions to/from null.</summary>
    event EventHandler? ArtworkChanged;

    /// <summary>
    /// The current track's playback position, or null when there is no session or the session's
    /// timeline properties can't be read right now. Distinct from <see cref="Domain.PlaybackState"/>:
    /// a session can report itself <see cref="Domain.PlaybackState.Playing"/> while its audio stream
    /// hasn't actually resumed producing sound yet (see <see cref="PlaybackPausingDeviceController"/>'s
    /// use of this to confirm real recovery after a format switch), so only an actually-advancing
    /// position confirms audio is really flowing.
    /// </summary>
    TimeSpan? PlaybackPosition { get; }

    /// <summary>Asks the session to skip to the previous track. A no-op when no session is attached.</summary>
    Task SkipPreviousAsync();

    /// <summary>Asks the session to toggle between playing and paused. A no-op when no session is attached.</summary>
    Task TogglePlayPauseAsync();

    /// <summary>Asks the session to skip to the next track. A no-op when no session is attached.</summary>
    Task SkipNextAsync();
}
