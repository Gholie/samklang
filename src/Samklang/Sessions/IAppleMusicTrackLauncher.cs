namespace Samklang.Sessions;

/// <summary>
/// Plays a specific Apple Music catalog track by handing the Apple Music Windows app a deep link
/// to it, independent of the current play queue. This is deliberately separate from
/// <see cref="IMediaTransport"/>: SMTC only exposes relative previous/next verbs, which can reach
/// an arbitrary track *only* when the queue happens to be that track's album in album order — the
/// album picker's original walk-based navigation broke exactly here (a discovery station or
/// shuffled playlist lands on an unrelated song). Launching the catalog track directly jumps
/// straight to it regardless of what the queue is.
/// </summary>
public interface IAppleMusicTrackLauncher
{
    /// <summary>
    /// Asks Apple Music to play the catalog track with the given id, in the context of
    /// <paramref name="albumId"/> when supplied so playback continues into the rest of that album
    /// rather than the track in isolation (an empty <paramref name="albumId"/> falls back to a plain
    /// song link). Best-effort — a no-op (logged, never thrown) when the track id is empty or the
    /// launch can't be started.
    /// </summary>
    Task PlayTrackAsync(string catalogTrackId, string albumId);
}
