namespace Samklang.ViewModels;

/// <summary>
/// One row of the dashboard's album track list (see <see cref="DashboardViewModel.AlbumTracks"/>):
/// the track's position in album order, its catalog title/artist/duration, and whether it is the
/// one currently playing (the view bolds that row and shows an artwork-backed card).
/// <paramref name="CatalogId"/> is the Apple Music catalog song id backing the row and
/// <paramref name="AlbumId"/> the album it belongs to — neither is shown, but
/// <see cref="DashboardViewModel.PlayAlbumTrackCommand"/> hands both to the track launcher to play
/// this exact song in its album context (so playback continues through the album), regardless of
/// the current play queue.
/// </summary>
public sealed record AlbumTrackEntry(int Number, string Title, string Artist, bool IsCurrent, string CatalogId = "", TimeSpan? Duration = null, string AlbumId = "")
{
    /// <summary>"m:ss" for display, or empty when the catalog didn't report a duration (e.g. older test fakes).</summary>
    public string DurationDisplay => Duration is { } duration
        ? $"{(int)duration.TotalMinutes}:{duration.Seconds:D2}"
        : string.Empty;
}
