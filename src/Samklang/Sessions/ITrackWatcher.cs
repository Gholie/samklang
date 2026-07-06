using Samklang.Domain;

namespace Samklang.Sessions;

/// <summary>
/// Watches Windows media sessions and reports the Track currently playing in Apple Music for
/// Windows — per docs/PLAN.md's "session filtering: Apple Music package identity only"
/// decision, every other app's session (Spotify, browsers, etc.) is ignored entirely.
/// </summary>
public interface ITrackWatcher
{
    /// <summary>The Track currently playing in Apple Music, or null if it isn't running or isn't playing.</summary>
    Track? CurrentTrack { get; }

    /// <summary>Raised whenever <see cref="CurrentTrack"/> changes, including transitions to/from null.</summary>
    event EventHandler<TrackChangedEventArgs>? TrackChanged;

    /// <summary>Begins watching for media sessions. Safe to await before the first Track arrives.</summary>
    Task StartAsync();
}

public sealed class TrackChangedEventArgs(Track? track) : EventArgs
{
    public Track? Track { get; } = track;
}
