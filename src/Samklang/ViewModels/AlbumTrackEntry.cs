namespace Samklang.ViewModels;

/// <summary>
/// One row of the dashboard's album track list (see <see cref="DashboardViewModel.AlbumTracks"/>):
/// the track's position in album order, its catalog title/artist/duration, and whether it is the
/// one currently playing (the view bolds that row and shows an artwork-backed card).
/// <paramref name="CatalogId"/> is the Apple Music catalog song id backing the row — not shown, but
/// what <see cref="DashboardViewModel.PlayAlbumTrackCommand"/> hands the track launcher to play this
/// exact song regardless of the current play queue.
/// </summary>
public sealed record AlbumTrackEntry(int Number, string Title, string Artist, bool IsCurrent, string CatalogId = "", TimeSpan? Duration = null)
{
    /// <summary>"m:ss" for display, or empty when the catalog didn't report a duration (e.g. older test fakes).</summary>
    public string DurationDisplay => Duration is { } duration
        ? $"{(int)duration.TotalMinutes}:{duration.Seconds:D2}"
        : string.Empty;
}
