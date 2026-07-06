using System.IO;

namespace Samklang.Resolver.PlayCache;

/// <summary>
/// Pure string-matching heuristics for correlating a PlayCache file to the current Track — no I/O,
/// so unit-testable without a real cache directory. Adapted from WindowsLosslessSwitcher's
/// equivalent (GPL-3.0) per docs/PLAN.md's "mine prior art for ideas" decision.
/// </summary>
public static class PlayCacheFileMatcher
{
    // Observed Apple Music truncation length for `Downloads\<folderName>.tmp` folder names — the
    // folder name becomes a cut-off prefix of "<Title> _ <Album>" at or beyond this length.
    private const int TruncatedFolderNameLength = 30;

    /// <summary>
    /// Whether an in-progress download's <c>Downloads\&lt;folderName&gt;.tmp</c> folder belongs to
    /// this Track. Apple Music names the folder "&lt;Title&gt; _ &lt;Album&gt;" (or just
    /// "&lt;Title&gt;" with no album) and truncates it past
    /// <see cref="TruncatedFolderNameLength"/> characters, so a long-enough folder name only needs
    /// to be a prefix of the expected combined name rather than an exact match.
    /// </summary>
    public static bool TitleMatchesDownloadFolder(string? title, string? album, string folderName)
    {
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(folderName))
        {
            return false;
        }

        var sanitizedTitle = SanitizeForFileName(title.Trim());
        if (folderName.Equals(sanitizedTitle, StringComparison.OrdinalIgnoreCase) ||
            folderName.StartsWith(sanitizedTitle + " _ ", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Below the truncation length, a folder name that didn't match above is simply a
        // different (possibly shorter) title — requiring the *full* folder name as a prefix of
        // the combined name keeps this strict and avoids e.g. "Clean" matching "Cleaner".
        if (folderName.Length < TruncatedFolderNameLength)
        {
            return false;
        }

        var combined = string.IsNullOrWhiteSpace(album)
            ? sanitizedTitle
            : $"{sanitizedTitle} _ {SanitizeForFileName(album.Trim())}";
        return combined.StartsWith(folderName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The completed-download filename suffix a PlayCacheInfo.xml cloud-id correlates to.</summary>
    public static string CloudIdSuffix(long cloudId) => $"-{cloudId:X16}";

    private static string SanitizeForFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(value.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
    }
}
