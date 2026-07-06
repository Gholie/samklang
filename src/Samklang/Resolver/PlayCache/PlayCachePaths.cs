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
    /// installed. The returned path is not guaranteed to exist — callers check that themselves.
    /// </summary>
    public string? PlayCacheDirectory => TryFindPackageRoot() is { } packageRoot
        ? Path.Combine(packageRoot, "LocalCache", "Local", "Apple", "AMPLibraryAgent", "PlayCache")
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
}
