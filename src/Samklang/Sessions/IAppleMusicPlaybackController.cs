namespace Samklang.Sessions;

/// <summary>Where a chosen album track should land relative to the play queue.</summary>
public enum QueuePlacement
{
    /// <summary>Play the track now (the album row's own "Play …" action).</summary>
    PlayNow,

    /// <summary>Insert the track at the top of the queue, right after the current one ("Play Next").</summary>
    PlayNext,

    /// <summary>Append the track to the end of the queue ("Play Last").</summary>
    PlayLast,
}

/// <summary>
/// Identifies one album track well enough for the controller to both navigate to it (a deep link
/// needs the catalog <paramref name="CatalogId"/> and <paramref name="AlbumId"/>) and pick its row
/// out of the Apple Music app's album view (the UI row is identified by
/// <paramref name="TrackNumber"/> + <paramref name="Title"/>). Built from the dashboard's
/// <see cref="ViewModels.AlbumTrackEntry"/>.
/// </summary>
public sealed record AlbumTrackTarget(int TrackNumber, string Title, string CatalogId, string AlbumId);

/// <summary>
/// Plays or queues a specific album track in the Apple Music Windows app. Unlike a bare deep link
/// (which only *navigates* the app to the track — the app never autoplays from a link, verified on
/// the shipping build), this drives the app's own UI through Windows UI Automation: it opens the
/// album, finds the track's row, and invokes its "Play …" / "Play Next" / "Play Last" menu item —
/// the only interface the app exposes for choosing a track or touching the queue (SMTC offers only
/// relative previous/next and no queue verb; there is no play/queue URL parameter).
///
/// <para>
/// This is deliberately separate from <see cref="IAppleMusicTrackLauncher"/> (the deep-link
/// navigator it builds on): the launcher gets the album on screen and is also the graceful fallback
/// — if the UI-automation step can't complete (app updated its tree, wrong locale, timing), the
/// user is left on the open album, i.e. exactly the pre-automation behavior, never worse.
/// </para>
/// </summary>
public interface IAppleMusicPlaybackController
{
    /// <summary>
    /// Navigates the Apple Music app to <paramref name="target"/>'s album, then plays it or adds it
    /// to the queue per <paramref name="placement"/>. Best-effort — the UI-automation step is
    /// logged and swallowed on failure, leaving the album open (the deep-link-only fallback).
    /// </summary>
    Task PlayAlbumTrackAsync(AlbumTrackTarget target, QueuePlacement placement);
}
