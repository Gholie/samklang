namespace Samklang.ViewModels;

/// <summary>
/// One row of the dashboard's album track list (see <see cref="DashboardViewModel.AlbumTracks"/>):
/// the track's position in album order, its catalog title/artist, and whether it is the one
/// currently playing (the view bolds that row).
/// </summary>
public sealed record AlbumTrackEntry(int Number, string Title, string Artist, bool IsCurrent);
