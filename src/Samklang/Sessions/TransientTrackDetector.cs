using Samklang.Domain;

namespace Samklang.Sessions;

/// <summary>
/// Flags SMTC updates that are placeholder noise rather than a real Track change. Apple Music
/// for Windows briefly reports transitional states between tracks (and at app/station
/// transitions) — a "Connecting…" title with no artist, a station name like "REVNOIR & Similar
/// Artists" with no artist, or even a real-looking title with an empty artist (e.g. "Relax") —
/// before the real metadata lands a moment later.
///
/// Left untreated, each of these briefly became a genuine Track change: the resolver chain fell
/// through every real layer (an empty artist can't be catalog-matched — see
/// <see cref="Resolver.Catalog.CatalogFormatResolverLayer.TryResolve"/>) down to the Tier
/// fallback layer, which resolved a generic 44.1&nbsp;kHz/24-bit target and switched the device —
/// only to switch again a second later once the real track's metadata arrived. That produced an
/// audible extra DAC relock on most track changes, switch-log noise, and (via
/// <c>DashboardViewModel</c> re-matching on every Track change) a wiped album track list between
/// songs.
///
/// Deliberately keyed on empty/whitespace title or artist rather than matching the literal
/// "Connecting…" string: that string is localized, while every observed placeholder shape shares
/// the empty-artist property. Pure string policy with no I/O, mirroring
/// <see cref="SmtcTrackMetadataParser"/> — evaluated after that parser has already run, so it
/// sees the post-split Title/Artist/Album.
/// </summary>
public static class TransientTrackDetector
{
    public static bool IsTransient(Track track) =>
        string.IsNullOrWhiteSpace(track.Title) || string.IsNullOrWhiteSpace(track.Artist);
}
