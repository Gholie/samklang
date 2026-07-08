using Samklang.Domain;

namespace Samklang.Sessions;

/// <summary>
/// Builds a <see cref="Track"/> from the raw SMTC media property strings, undoing Apple Music
/// for Windows's habit of reporting the artist field as <c>"Artist — Album"</c> (an em dash,
/// U+2014, with a space on either side — verified live against the running app on 2026-07-08,
/// where <c>Artist</c> was <c>"Sia — This Is Acting"</c> and <c>AlbumTitle</c> was empty).
/// Left as-is, that combined string breaks the whole Exact-confidence pipeline: the catalog
/// search term is polluted by the album and <see cref="Resolver.Catalog.CatalogTrackMatcher"/>
/// (which requires normalized artist equality) rejects every candidate, so every track falls
/// through to the tier fallback.
///
/// Deliberately conservative, mirroring the matcher's philosophy: a wrongly-split artist could
/// *match the wrong song* at Exact confidence, while an unsplit one merely fails open to a
/// lower-confidence layer. So when the album is known we only strip an exact
/// <c>"&lt;sep&gt;Album"</c> suffix (never blind-split, since legitimate artist names can contain
/// dashes), and when it isn't we split only on the first spaced em/en dash (album names can
/// contain the separator too, so everything after the first one is the album). Plain hyphens are
/// never treated as separators — "Jay-Z" and "T-Pain" must survive intact, and Apple Music uses
/// the em dash. Pure string policy with no I/O or WinRT types, so it's unit-testable even though
/// its only caller (<see cref="SmtcTrackWatcher"/>) is a thin adapter over a live Windows API.
/// </summary>
public static class SmtcTrackMetadataParser
{
    // What Apple Music actually emits (em dash, U+2014, verified live) first; the en dash
    // (U+2013) variant is tolerated because it's visually identical in logs and would be
    // indistinguishable if Apple ever swaps it in. Both require the surrounding spaces —
    // an unspaced dash is far more likely to be part of a name than a separator.
    private static readonly string[] Separators = [" — ", " – "];

    public static Track Parse(string? title, string? artist, string? albumTitle)
    {
        var safeTitle = title ?? string.Empty;
        var safeArtist = artist ?? string.Empty;
        var safeAlbum = albumTitle ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(safeAlbum))
        {
            // The album is independently known, so the only trustworthy repair is stripping an
            // exact "<sep>Album" suffix. Anything less exact (the artist merely *containing* a
            // separator) could be a legitimate dash in the artist's name — leave it intact.
            foreach (var separator in Separators)
            {
                var suffix = separator + safeAlbum;
                if (safeArtist.Length > suffix.Length && safeArtist.EndsWith(suffix, StringComparison.Ordinal))
                {
                    return new Track(safeTitle, safeArtist[..^suffix.Length], safeAlbum);
                }
            }

            return new Track(safeTitle, safeArtist, safeAlbum);
        }

        // No album reported (the live-verified shape): recover it from the artist field. Split at
        // the *first* separator so an album whose own name contains one stays whole; an artist
        // name containing a spaced em dash would lose its tail here, but that shape is
        // vanishingly rare compared to the every-track "Artist — Album" join being undone.
        var splitIndex = -1;
        var splitSeparator = string.Empty;
        foreach (var separator in Separators)
        {
            var index = safeArtist.IndexOf(separator, StringComparison.Ordinal);
            if (index >= 0 && (splitIndex < 0 || index < splitIndex))
            {
                splitIndex = index;
                splitSeparator = separator;
            }
        }

        if (splitIndex >= 0)
        {
            var artistPart = safeArtist[..splitIndex];
            var albumPart = safeArtist[(splitIndex + splitSeparator.Length)..];
            if (!string.IsNullOrWhiteSpace(artistPart) && !string.IsNullOrWhiteSpace(albumPart))
            {
                return new Track(safeTitle, artistPart, albumPart);
            }
        }

        return new Track(safeTitle, safeArtist, safeAlbum);
    }
}
