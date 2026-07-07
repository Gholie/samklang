using Samklang.Resolver.PlayCache;
using Xunit;

namespace Samklang.Tests.Resolver.PlayCache;

/// <summary>
/// Exercises <see cref="PlayCachePaths"/>'s directory-discovery logic against a synthetic
/// <c>Packages</c> folder built under a temp directory — regression coverage for issue #20, which
/// found the leaf directory name was hard-coded to "PlayCache" when a real Windows 11 install
/// actually names it "SubscriptionPlayCache".
/// </summary>
public sealed class PlayCachePathsTests : IDisposable
{
    private const string PackageFolderName = "AppleInc.AppleMusicWin_nzyj5cx40ttqa";

    private readonly string _packagesRoot =
        Path.Combine(Path.GetTempPath(), "samklang-playcache-paths-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_packagesRoot, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
        }
    }

    private PlayCachePaths CreatePaths() => new(() => _packagesRoot);

    private string CreateAmpLibraryAgentDirectory()
    {
        var directory = Path.Combine(_packagesRoot, PackageFolderName, "LocalCache", "Local", "Apple", "AMPLibraryAgent");
        Directory.CreateDirectory(directory);
        return directory;
    }

    [Fact]
    public void PlayCacheDirectory_finds_a_directory_literally_named_PlayCache()
    {
        var amplibraryAgent = CreateAmpLibraryAgentDirectory();
        var expected = Path.Combine(amplibraryAgent, "PlayCache");
        Directory.CreateDirectory(expected);

        var paths = CreatePaths();

        Assert.Equal(expected, paths.PlayCacheDirectory);
    }

    [Fact]
    public void PlayCacheDirectory_finds_the_real_world_SubscriptionPlayCache_directory()
    {
        // The actual leaf name found on a real Windows 11 install per issue #20 — there was no
        // "PlayCache" directory at all on that machine, only this one.
        var amplibraryAgent = CreateAmpLibraryAgentDirectory();
        Directory.CreateDirectory(Path.Combine(amplibraryAgent, "Radio_2"));
        Directory.CreateDirectory(Path.Combine(amplibraryAgent, "Store_1"));
        var expected = Path.Combine(amplibraryAgent, "SubscriptionPlayCache");
        Directory.CreateDirectory(expected);

        var paths = CreatePaths();

        Assert.Equal(expected, paths.PlayCacheDirectory);
    }

    [Fact]
    public void PlayCacheDirectory_matches_case_insensitively()
    {
        var amplibraryAgent = CreateAmpLibraryAgentDirectory();
        var expected = Path.Combine(amplibraryAgent, "subscriptionplaycache");
        Directory.CreateDirectory(expected);

        var paths = CreatePaths();

        Assert.Equal(expected, paths.PlayCacheDirectory);
    }

    [Fact]
    public void PlayCacheDirectory_is_null_when_AMPLibraryAgent_does_not_exist()
    {
        Directory.CreateDirectory(Path.Combine(_packagesRoot, PackageFolderName));

        var paths = CreatePaths();

        Assert.Null(paths.PlayCacheDirectory);
    }

    [Fact]
    public void PlayCacheDirectory_is_null_when_no_subdirectory_matches()
    {
        var amplibraryAgent = CreateAmpLibraryAgentDirectory();
        Directory.CreateDirectory(Path.Combine(amplibraryAgent, "Radio_2"));
        Directory.CreateDirectory(Path.Combine(amplibraryAgent, "Store_1"));

        var paths = CreatePaths();

        Assert.Null(paths.PlayCacheDirectory);
    }

    [Fact]
    public void PlayCacheDirectory_is_null_when_no_package_folder_matches_the_prefix()
    {
        Directory.CreateDirectory(Path.Combine(_packagesRoot, "SomeOtherPublisher.SomeApp_abc123"));

        var paths = CreatePaths();

        Assert.Null(paths.PlayCacheDirectory);
    }

    [Fact]
    public void PlayCacheInfoPath_is_derived_from_whichever_directory_was_found()
    {
        var amplibraryAgent = CreateAmpLibraryAgentDirectory();
        var playCacheDir = Path.Combine(amplibraryAgent, "SubscriptionPlayCache");
        Directory.CreateDirectory(playCacheDir);

        var paths = CreatePaths();

        Assert.Equal(Path.Combine(playCacheDir, "PlayCacheInfo.xml"), paths.PlayCacheInfoPath);
    }

    [Fact]
    public void GetPlayCacheDirectory_matches_the_PlayCacheDirectory_property()
    {
        var amplibraryAgent = CreateAmpLibraryAgentDirectory();
        var expected = Path.Combine(amplibraryAgent, "PlayCache");
        Directory.CreateDirectory(expected);

        var paths = CreatePaths();

        Assert.Equal(paths.PlayCacheDirectory, paths.GetPlayCacheDirectory());
    }
}
