using System.IO;

namespace Samklang.Resolver.PlayCache;

/// <summary>
/// Locates the Apple Music Windows app's PlayCache directory under its packaged app-data folder.
/// The package folder name carries a per-install publisher hash suffix (e.g.
/// <c>AppleInc.AppleMusicWin_nzyj5cx40ttqa</c>; see
/// <see cref="Sessions.AppleMusicSessionFilter.PackageFamilyNamePrefix"/>), so this looks up the
/// first folder under <c>Packages</c> whose name starts with that prefix rather than hard-coding
/// one install's hash.
/// </summary>
public sealed class PlayCachePaths : IPlayCacheLocator
{
    private readonly Func<string> _packagesRootAccessor;

    /// <param name="packagesRootAccessor">
    /// Reads the root <c>Packages</c> folder to search under. Defaults to the real
    /// <c>%LOCALAPPDATA%\Packages</c>; overridable for tests so this class never depends on the
    /// real OS/install layout in unit tests.
    /// </param>
    public PlayCachePaths(Func<string>? packagesRootAccessor = null)
    {
        _packagesRootAccessor = packagesRootAccessor ?? (() =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages"));
    }

    /// <summary>
    /// The Apple Music package's PlayCache directory, or null if no matching package folder is
    /// installed, or the package has no <c>AMPLibraryAgent</c> folder, or none of its
    /// subdirectories look like a PlayCache. The returned path is not guaranteed to exist beyond
    /// that last check — callers still handle races/removal themselves.
    /// </summary>
    /// <remarks>
    /// A real Windows 11 install (issue #20) named this directory <c>SubscriptionPlayCache</c>,
    /// not the originally assumed <c>PlayCache</c> — and other app/install versions may use yet
    /// another name — so this matches any subdirectory of <c>AMPLibraryAgent</c> whose name
    /// contains "PlayCache" (case-insensitive) rather than hard-coding one exact name.
    /// </remarks>
    public string? PlayCacheDirectory => TryFindPackageRoot() is { } packageRoot
        ? TryFindPlayCacheDirectory(Path.Combine(packageRoot, "LocalCache", "Local", "Apple", "AMPLibraryAgent"))
        : null;

    /// <summary>Path to the PlayCache directory's item-metadata plist (access-date/cloud-id per cached item).</summary>
    public string? PlayCacheInfoPath => PlayCacheDirectory is { } directory ? Path.Combine(directory, "PlayCacheInfo.xml") : null;

    public string? GetPlayCacheDirectory() => PlayCacheDirectory;

    private string? TryFindPackageRoot()
    {
        try
        {
            var packagesRoot = _packagesRootAccessor();
            if (!Directory.Exists(packagesRoot))
            {
                return null;
            }

            return Directory.EnumerateDirectories(packagesRoot)
                .FirstOrDefault(directory => Path.GetFileName(directory)
                    .StartsWith(Sessions.AppleMusicSessionFilter.PackageFamilyNamePrefix, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Packages folder unreadable for any reason — no PlayCache path to offer; the layer
            // treats this the same as "not installed."
            return null;
        }
    }

    /// <summary>
    /// Finds the first subdirectory of <paramref name="amplibraryAgentDirectory"/> whose name
    /// contains "PlayCache" (e.g. <c>PlayCache</c> or the real-world <c>SubscriptionPlayCache</c>),
    /// or null if that folder doesn't exist or none of its children match.
    /// </summary>
    private static string? TryFindPlayCacheDirectory(string amplibraryAgentDirectory)
    {
        try
        {
            if (!Directory.Exists(amplibraryAgentDirectory))
            {
                return null;
            }

            return Directory.EnumerateDirectories(amplibraryAgentDirectory)
                .FirstOrDefault(directory =>
                    Path.GetFileName(directory).Contains("PlayCache", StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
