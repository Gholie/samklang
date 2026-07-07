using Velopack;
using Velopack.Sources;

namespace Samklang.Updates;

/// <summary>Outcome of <see cref="AppUpdateService.CheckAndApplyUpdateAsync"/>, for callers that want to surface it (e.g. the tray's "Check for Updates" item).</summary>
public enum UpdateCheckResult
{
    /// <summary>Not a real Velopack install (dev run, unit test, CI) — nothing to check.</summary>
    NotInstalled,

    /// <summary>Checked GitHub Releases; already on the latest version.</summary>
    UpToDate,

    /// <summary>The check, download, or apply step failed (e.g. no network, GitHub unreachable).</summary>
    CheckFailed,

    /// <summary>
    /// A newer release was downloaded and <see cref="UpdateManager.ApplyUpdatesAndRestart"/> was
    /// invoked. In practice that call exits and relaunches the process before returning, so
    /// callers should not expect to observe this value — it exists for testability.
    /// </summary>
    UpdateApplied,
}

/// <summary>
/// Wraps Velopack's <see cref="UpdateManager"/> to check the project's GitHub Releases for a
/// newer version and self-update (issue #10's "detects and applies an update from a newer
/// release" acceptance criterion).
///
/// <para>
/// Every entry point is guarded so this can be constructed and called unconditionally from
/// <c>MainWindow</c> without the caller needing its own installed-check: this app only becomes a
/// real Velopack install once distributed via <c>.github/workflows/release-please.yml</c>'s packaging
/// step, so plain <c>dotnet run</c>, unit tests, and CI builds must all take the "not installed,
/// nothing to do" path rather than throwing.
/// </para>
/// </summary>
public sealed class AppUpdateService
{
    // Must match .github/workflows/release-please.yml's --repoUrl / GithubSource's expected format —
    // this is where GithubSource looks for release assets published by `vpk upload github`.
    private const string RepositoryUrl = "https://github.com/Gholie/samklang";

    private readonly UpdateManager? _updateManager;

    public AppUpdateService()
    {
        _updateManager = TryCreateUpdateManager();
    }

    /// <summary>True when this process is a real Velopack install capable of checking/applying updates.</summary>
    public bool IsInstalled => _updateManager?.IsInstalled ?? false;

    /// <summary>
    /// Checks GitHub Releases for a newer version; if one exists, downloads and applies it
    /// (restarting the app). No-ops to <see cref="UpdateCheckResult.NotInstalled"/> outside a real
    /// install, and to <see cref="UpdateCheckResult.CheckFailed"/> on any error — an optional
    /// background update check should never crash or block the app over a network hiccup.
    /// </summary>
    public async Task<UpdateCheckResult> CheckAndApplyUpdateAsync(CancellationToken cancellationToken = default)
    {
        if (_updateManager is null || !_updateManager.IsInstalled)
        {
            return UpdateCheckResult.NotInstalled;
        }

        try
        {
            var updateInfo = await _updateManager.CheckForUpdatesAsync();
            if (updateInfo is null)
            {
                return UpdateCheckResult.UpToDate;
            }

            await _updateManager.DownloadUpdatesAsync(updateInfo, cancelToken: cancellationToken);

            // Exits and relaunches the process on the new version; the return below is only
            // reached if that somehow doesn't happen (kept for testability, see UpdateApplied's
            // doc comment).
            _updateManager.ApplyUpdatesAndRestart(updateInfo.TargetFullRelease);
            return UpdateCheckResult.UpdateApplied;
        }
        catch
        {
            return UpdateCheckResult.CheckFailed;
        }
    }

    private static UpdateManager? TryCreateUpdateManager()
    {
        try
        {
            return new UpdateManager(new GithubSource(RepositoryUrl, accessToken: null, prerelease: false));
        }
        catch
        {
            // UpdateManager's locator does environment probing at construction time; how hard it
            // fails outside of a real Velopack install isn't guaranteed stable across versions, so
            // this treats any construction failure the same as "not installed" rather than
            // crashing app startup over an optional feature.
            return null;
        }
    }
}
