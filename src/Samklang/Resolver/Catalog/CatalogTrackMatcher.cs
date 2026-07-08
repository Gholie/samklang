using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Samklang.Domain;

namespace Samklang.Resolver.Catalog;

/// <summary>
/// Decides which (if any) catalog search candidate is the same song as the Track reported by
/// SMTC. Deliberately conservative: Exact confidence is only as trustworthy as this match, so a
/// normalized title+artist mismatch returns null (falling through to a lower-confidence layer)
/// rather than risk applying a wrong track's sample rate. Pure string normalization/scoring — no
/// I/O — so it's unit-testable without any catalog candidates coming from a real search.
///
/// <para>
/// <b>Why album ranking exists</b> (2026-07-08 Sia "Alive" investigation). Apple's catalog
/// frequently carries the *same* recording under several releases of the *same* album — a
/// standard edition and a "Deluxe"/"Anniversary Edition" reissue are the common case — and those
/// releases are not guaranteed to share a master: for Sia's "Alive", the standard "This Is
/// Acting" catalog entry serves a 48 kHz asset while the "This Is Acting (Deluxe Version)" entry
/// serves a 96 kHz one for the very same song. Title+artist scoring alone can't tell them apart
/// (both score 2), so whichever one Apple's search ranks first wins — which live testing showed
/// is not reliably the higher-quality one. Once the album is known (SMTC's "Artist — Album" field,
/// recovered by <see cref="Sessions.SmtcTrackMetadataParser"/>), a candidate whose album matches
/// the track's beats one that doesn't, among candidates that already pass the title/artist gate —
/// see <see cref="FindBestMatch"/>. That alone still doesn't disambiguate *between* same-family
/// editions (SMTC reports the plain "This Is Acting" title even when Apple is actually streaming
/// the Deluxe edition's asset), so <see cref="FindAlbumSiblings"/> additionally exposes the other
/// gate-passing candidates that share the matched candidate's album family, so the caller
/// (<see cref="CatalogFormatResolverLayer"/>, which does the I/O this class deliberately avoids)
/// can fetch their manifests too and keep whichever edition's asset is actually the best.
/// </para>
/// </summary>
public static class CatalogTrackMatcher
{
    /// <summary>
    /// Dominates the 1-point gap between the title/artist gate's two passing scores (1 =
    /// collaborator-only artist match, 2 = exact artist), so an album match always outranks a
    /// looser artist match but never rescues a candidate that fails the gate outright (gate
    /// failures are skipped before this bonus is ever added — see <see cref="FindBestMatch"/>).
    /// </summary>
    private const int AlbumMatchBonus = 10;

    public static CatalogSearchCandidate? FindBestMatch(Track track, IReadOnlyList<CatalogSearchCandidate> candidates)
    {
        var normalizedTitle = Normalize(track.Title);
        var normalizedArtist = Normalize(track.Artist);

        if (normalizedTitle.Length == 0 || normalizedArtist.Length == 0)
        {
            return null;
        }

        // Empty when SMTC couldn't report an album (or hasn't been asked to) — every candidate's
        // album bonus is then unconditionally 0 below, which is exactly today's title/artist-only
        // behavior, preserved on purpose rather than by accident.
        var normalizedAlbum = NormalizeAlbum(track.Album);

        CatalogSearchCandidate? best = null;
        var bestRank = -1;

        foreach (var candidate in candidates)
        {
            var gateScore = Score(normalizedTitle, normalizedArtist, candidate);
            if (gateScore < 0)
            {
                continue; // fails the title/artist gate — no album bonus can rescue it
            }

            var rank = gateScore + (HasAlbumMatch(normalizedAlbum, candidate) ? AlbumMatchBonus : 0);
            if (rank > bestRank)
            {
                bestRank = rank;
                best = candidate;
            }
        }

        return best;
    }

    /// <summary>
    /// The other title/artist-gate-passing candidates that share <paramref name="matched"/>'s
    /// album family (see the class doc for why this matters) — alternate editions of the same
    /// release that might carry a different master for this song. Excludes <paramref name="matched"/>
    /// itself. Pure filtering, no I/O: the caller decides whether/how many of these are worth an
    /// extra manifest fetch to compare.
    /// </summary>
    public static IReadOnlyList<CatalogSearchCandidate> FindAlbumSiblings(
        Track track, IReadOnlyList<CatalogSearchCandidate> candidates, CatalogSearchCandidate matched)
    {
        var normalizedTitle = Normalize(track.Title);
        var normalizedArtist = Normalize(track.Artist);
        var matchedAlbumFamily = NormalizeAlbum(matched.Album);

        if (matchedAlbumFamily.Length == 0)
        {
            return [];
        }

        var siblings = new List<CatalogSearchCandidate>();
        foreach (var candidate in candidates)
        {
            if (candidate.Id == matched.Id)
            {
                continue;
            }

            if (Score(normalizedTitle, normalizedArtist, candidate) < 0)
            {
                continue;
            }

            if (NormalizeAlbum(candidate.Album) == matchedAlbumFamily)
            {
                siblings.Add(candidate);
            }
        }

        return siblings;
    }

    private static bool HasAlbumMatch(string normalizedTrackAlbum, CatalogSearchCandidate candidate) =>
        normalizedTrackAlbum.Length > 0 && NormalizeAlbum(candidate.Album) == normalizedTrackAlbum;

    private static int Score(string normalizedTitle, string normalizedArtist, CatalogSearchCandidate candidate)
    {
        var candidateTitle = Normalize(candidate.Title);
        if (candidateTitle != normalizedTitle)
        {
            return -1;
        }

        var candidateArtist = Normalize(candidate.Artist);
        if (candidateArtist == normalizedArtist)
        {
            return 2;
        }

        // SMTC sometimes reports only the primary artist while the catalog lists "A & B" or
        // "A, B"; accept when the SMTC artist appears as one of the catalog artist's parts. Split
        // the raw (pre-normalization) string on the separators, since Normalize strips them as
        // punctuation and would otherwise leave nothing to split on.
        var candidateArtistParts = candidate.Artist
            .Split(SeparatorChars, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Normalize);
        return candidateArtistParts.Contains(normalizedArtist) ? 1 : -1;
    }

    private static readonly char[] SeparatorChars = ['&', ',', '/'];

    /// <summary>
    /// Lower-cases, strips diacritics and punctuation, drops common "(feat. ...)"/"(remaster...)"
    /// suffixes, and collapses whitespace — enough normalization to match the same song reported
    /// with slightly different formatting by SMTC vs. the catalog, without being so fuzzy it
    /// matches a different song.
    /// </summary>
    internal static string Normalize(string value) => NormalizeCore(value, TitleDroppedSuffixMarkers);

    /// <summary>
    /// Same normalization as <see cref="Normalize"/>, but also drops edition-qualifier suffixes
    /// ("Deluxe Version", "Anniversary Edition", ...) that <see cref="Normalize"/> deliberately
    /// leaves alone for titles. Two releases of an album that differ only by such a qualifier are
    /// the *same* album for matching purposes — see the class doc's "Alive"/"This Is Acting
    /// (Deluxe Version)" example, where treating them as different families is exactly what let
    /// the wrong (lower sample rate) edition win. Unlike "live" on titles (see
    /// <see cref="TitleDroppedSuffixMarkers"/>), an edition qualifier doesn't name a genuinely
    /// different recording — it names a repackaging of the same one, which is why it's safe to
    /// fold away here even though the analogous choice for titles is not.
    ///
    /// <para>
    /// Also strips the same qualifiers when Apple appends them to the album title directly,
    /// without parentheses — live-verified against Michael Jackson's "Bad", whose 2012 remaster
    /// (96 kHz) sits under the catalog album literally named <c>"Bad 25th Anniversary"</c>, not
    /// <c>"Bad (25th Anniversary)"</c>. <see cref="StripTrailingEditionQualifier"/> handles this
    /// second, unparenthesized shape.
    /// </para>
    /// </summary>
    internal static string NormalizeAlbum(string value) =>
        NormalizeCore(StripTrailingEditionQualifier(value), AlbumDroppedSuffixMarkers);

    // Matches a trailing "<separator><ordinal? >qualifier<optional version/edition/tracks word>"
    // clause — e.g. " 25th Anniversary", " Deluxe Version", "-Expanded Edition" — so it can be cut
    // off the end of an otherwise-identical album title. Requires a leading separator (space/dash/
    // colon) so a one-word album genuinely *named* e.g. "Deluxe" is left alone: there's nothing
    // before it for the separator to match.
    private static readonly Regex TrailingEditionQualifierPattern = new(
        @"[\s\-–—:]+(?:\d+(?:st|nd|rd|th)\s+)?(?:deluxe|expanded|special|collector'?s?|bonus\s+tracks?|anniversary)(?:\s+(?:version|edition|tracks?))?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static string StripTrailingEditionQualifier(string value)
    {
        var result = value;
        while (TrailingEditionQualifierPattern.Match(result) is { Success: true } match)
        {
            result = result[..match.Index].TrimEnd();
        }

        return result;
    }

    private static string NormalizeCore(string value, string[] droppedSuffixMarkers)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var withoutParentheticals = StripParentheticalSuffixes(value, droppedSuffixMarkers);

        var formD = withoutParentheticals.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(formD.Length);
        foreach (var c in formD)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(c);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue; // drop combining diacritic marks left behind by FormD decomposition
            }

            if (char.IsLetterOrDigit(c))
            {
                builder.Append(char.ToLowerInvariant(c));
            }
            else if (char.IsWhiteSpace(c) && builder.Length > 0 && builder[^1] != ' ')
            {
                builder.Append(' ');
            }
        }

        return builder.ToString().Trim();
    }

    // "live" is deliberately absent: a live recording is a genuinely different variant whose
    // sample rate can differ from the studio version's, so equating "Song (Live)" with "Song"
    // risks applying the wrong variant's format at Exact confidence — the one failure mode this
    // class exists to avoid. Live titles still match when the catalog carries the same live
    // variant, since both sides then keep the suffix through normalization.
    private static readonly string[] TitleDroppedSuffixMarkers =
    [
        "feat.", "feat ", "featuring", "remaster", "mono version", "stereo version",
    ];

    // Album-only markers layered on top of the title list: edition qualifiers a reissue adds to
    // an otherwise-identical album title. "anniversary edition"/"expanded edition"/"special
    // edition" are checked before their bare "anniversary"/"expanded" forms only because both are
    // in the same array and word-start matching finds whichever appears first — either one strips
    // the whole parenthetical, so the order doesn't actually matter, but the longer forms are
    // listed first for readability.
    private static readonly string[] AlbumDroppedSuffixMarkers =
    [
        "feat.", "feat ", "featuring", "remaster", "mono version", "stereo version",
        "deluxe", "anniversary edition", "anniversary", "expanded edition", "expanded",
        "special edition", "collector's edition", "collectors edition", "bonus track",
    ];

    private static string StripParentheticalSuffixes(string value, string[] droppedSuffixMarkers)
    {
        var result = value;
        while (true)
        {
            var openParenIndex = result.LastIndexOfAny(['(', '[']);
            if (openParenIndex < 0)
            {
                return result;
            }

            var closeChar = result[openParenIndex] == '(' ? ')' : ']';
            var closeParenIndex = result.IndexOf(closeChar, openParenIndex);
            if (closeParenIndex < 0)
            {
                return result;
            }

            var inner = result[(openParenIndex + 1)..closeParenIndex].Trim().ToLowerInvariant();
            if (!droppedSuffixMarkers.Any(marker => ContainsMarkerAtWordStart(inner, marker)))
            {
                return result;
            }

            result = (result[..openParenIndex] + result[(closeParenIndex + 1)..]).Trim();
        }
    }

    /// <summary>
    /// Whether <paramref name="marker"/> occurs in <paramref name="inner"/> starting at a word
    /// boundary. A bare <c>Contains</c> would fire on markers buried inside unrelated words —
    /// e.g. "remaster" inside "(Unremastered)", which names a *different* variant that must not
    /// be stripped away. Markers matching as a word *prefix* is still intended ("remaster"
    /// matches "Remastered 2011").
    /// </summary>
    private static bool ContainsMarkerAtWordStart(string inner, string marker)
    {
        var searchFrom = 0;
        while (searchFrom <= inner.Length - marker.Length)
        {
            var index = inner.IndexOf(marker, searchFrom, StringComparison.Ordinal);
            if (index < 0)
            {
                return false;
            }

            if (index == 0 || !char.IsLetterOrDigit(inner[index - 1]))
            {
                return true;
            }

            searchFrom = index + 1;
        }

        return false;
    }
}
