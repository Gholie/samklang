using System.Globalization;
using System.Text;
using Samklang.Domain;

namespace Samklang.Resolver.Catalog;

/// <summary>
/// Decides which (if any) catalog search candidate is the same song as the Track reported by
/// SMTC. Deliberately conservative: Exact confidence is only as trustworthy as this match, so a
/// normalized title+artist mismatch returns null (falling through to a lower-confidence layer)
/// rather than risk applying a wrong track's sample rate. Pure string normalization/scoring — no
/// I/O — so it's unit-testable without any catalog candidates coming from a real search.
/// </summary>
public static class CatalogTrackMatcher
{
    public static CatalogSearchCandidate? FindBestMatch(Track track, IReadOnlyList<CatalogSearchCandidate> candidates)
    {
        var normalizedTitle = Normalize(track.Title);
        var normalizedArtist = Normalize(track.Artist);

        if (normalizedTitle.Length == 0 || normalizedArtist.Length == 0)
        {
            return null;
        }

        CatalogSearchCandidate? best = null;
        var bestScore = -1;

        foreach (var candidate in candidates)
        {
            var score = Score(normalizedTitle, normalizedArtist, candidate);
            if (score > bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }

        // 2 = exact title + exact artist; 1 = exact title + artist appears among multiple
        // collaborators. Anything looser (title-only, fuzzy) isn't trustworthy enough for Exact
        // confidence.
        return bestScore >= 1 ? best : null;
    }

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
    internal static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var withoutParentheticals = StripParentheticalSuffixes(value);

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

    private static readonly string[] DroppedSuffixMarkers =
    [
        "feat.", "feat ", "featuring", "remaster", "live", "mono version", "stereo version",
    ];

    private static string StripParentheticalSuffixes(string value)
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
            if (!DroppedSuffixMarkers.Any(marker => inner.Contains(marker, StringComparison.Ordinal)))
            {
                return result;
            }

            result = (result[..openParenIndex] + result[(closeParenIndex + 1)..]).Trim();
        }
    }
}
