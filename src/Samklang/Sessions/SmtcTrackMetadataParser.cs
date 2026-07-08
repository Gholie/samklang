using Samklang.Domain;

namespace Samklang.Sessions;

/// <summary>
/// Undoes Apple Music for Windows' habit of packing "Artist — Album" into SMTC's Artist field
/// (and, when AlbumTitle is left blank, being the *only* place the album title appears at all).
/// Extracted out of <see cref="SmtcTrackWatcher"/> so the split logic is unit-testable without a
/// real Windows media session, following the same pattern as <see cref="AppleMusicSessionFilter"/>
/// and <see cref="StaleRefreshGuard"/>.
///
/// <para>
/// Left unparsed, a downstream Track like <c>("Bird Set Free", "Sia — This Is Acting", "")</c>
/// makes every <see cref="Resolver.Catalog.CatalogTrackMatcher"/> comparison fail (the catalog's
/// artist is just "Sia"), so <see cref="Resolver.Catalog.CatalogFormatResolverLayer"/> never
/// produces an Exact-confidence match and the dashboard's album track list never populates — the
/// root cause behind the 2026-07-08 handoff.
/// </para>
///
/// <para>
/// Verified against a live Apple Music for Windows SMTC session on 2026-07-08 (Windows 11):
/// Title = "Bird Set Free", Artist = "Sia — This Is Acting", AlbumTitle = "" (empty — Apple Music
/// does not populate it for this session type). The separator is a single EM DASH (U+2014)
/// surrounded by one space on each side. An EN DASH (U+2013) variant, spaced the same way, is
/// accepted defensively too — it is not known to occur in practice, but costs nothing to also
/// recognize and a future Apple Music build (or a different session/locale) could plausibly use
/// it instead.
/// </para>
/// </summary>
public static class SmtcTrackMetadataParser
{
    private static readonly string[] Separators = [" — ", " – "]; // em dash, en dash

    /// <summary>
    /// Builds a <see cref="Track"/> from the raw SMTC properties, splitting an "Artist — Album"
    /// combined Artist field apart when it's safe to do so. Conservative by design — the catalog
    /// match this feeds is used at Exact confidence, so a wrong split (or one that discards a
    /// genuine artist name containing an em dash) is worse than leaving the field alone:
    /// <list type="bullet">
    /// <item>
    /// If AlbumTitle is non-empty, Artist is only touched when it ends with exactly
    /// "&lt;separator&gt;&lt;AlbumTitle&gt;" — that suffix is stripped and nothing else. If
    /// AlbumTitle is set but doesn't match (e.g. a stale/unrelated value), Artist is left as-is
    /// rather than guessing.
    /// </item>
    /// <item>
    /// If AlbumTitle is empty and Artist contains a separator, it is split at the *first*
    /// occurrence into artist + album. First, not last: a real artist name is very unlikely to
    /// contain " — ", but an album title (subtitle, series name, "Part 1 — Part 2") can, so
    /// splitting on the first occurrence keeps the artist side minimal and correct even if the
    /// album side still contains the separator.
    /// </item>
    /// <item>Otherwise, both fields are returned unchanged.</item>
    /// </list>
    /// Never produces an empty Artist from a non-empty one — a split or strip that would leave
    /// nothing on the artist side is discarded and the original string kept instead.
    /// </summary>
    public static Track ParseTrack(string? title, string? artist, string? album)
    {
        var normalizedTitle = title ?? string.Empty;
        var normalizedArtist = artist ?? string.Empty;
        var normalizedAlbum = album ?? string.Empty;

        if (normalizedArtist.Length == 0)
        {
            return new Track(normalizedTitle, normalizedArtist, normalizedAlbum);
        }

        if (normalizedAlbum.Length > 0)
        {
            var strippedArtist = TryStripAlbumSuffix(normalizedArtist, normalizedAlbum);
            return new Track(normalizedTitle, strippedArtist ?? normalizedArtist, normalizedAlbum);
        }

        var split = TrySplitAtFirstSeparator(normalizedArtist);
        return split is { } parts
            ? new Track(normalizedTitle, parts.Artist, parts.Album)
            : new Track(normalizedTitle, normalizedArtist, normalizedAlbum);
    }

    /// <summary>
    /// Returns Artist with a trailing "&lt;separator&gt;&lt;album&gt;" suffix removed, or null if
    /// no recognized separator produces that exact suffix (or removing it would leave nothing).
    /// </summary>
    private static string? TryStripAlbumSuffix(string artist, string album)
    {
        foreach (var separator in Separators)
        {
            var suffix = separator + album;
            if (artist.Length > suffix.Length && artist.EndsWith(suffix, StringComparison.Ordinal))
            {
                return artist[..^suffix.Length];
            }
        }

        return null;
    }

    /// <summary>
    /// Splits Artist at the earliest occurrence of any recognized separator, or returns null if
    /// none occurs (or the split would leave an empty artist).
    /// </summary>
    private static (string Artist, string Album)? TrySplitAtFirstSeparator(string artist)
    {
        var earliestIndex = -1;
        var matchedSeparatorLength = 0;
        foreach (var separator in Separators)
        {
            var index = artist.IndexOf(separator, StringComparison.Ordinal);
            if (index > 0 && (earliestIndex < 0 || index < earliestIndex))
            {
                earliestIndex = index;
                matchedSeparatorLength = separator.Length;
            }
        }

        if (earliestIndex < 0)
        {
            return null;
        }

        var splitArtist = artist[..earliestIndex];
        var splitAlbum = artist[(earliestIndex + matchedSeparatorLength)..].Trim();
        return splitArtist.Length == 0 ? null : (splitArtist, splitAlbum);
    }
}
