using System.Diagnostics;
using Samklang.Logging;
using Samklang.Resolver.Catalog;

namespace Samklang.Sessions;

/// <summary>
/// Real <see cref="IAppleMusicTrackLauncher"/>: opens the catalog track's canonical
/// music.apple.com song link through the OS shell, which the Apple Music Windows app registers as
/// its handler at install time, so the hand-off lands in the app (whose SMTC session Samklang is
/// already watching) rather than a browser. The storefront comes from the same
/// <see cref="IStorefrontProvider"/> the catalog layer resolves formats against, so the link points
/// at the store the user actually browses.
///
/// <para>
/// Like <see cref="SmtcTrackWatcher"/> and <see cref="HttpAppleMusicCatalogClient"/>, this is a thin
/// adapter over an OS/network surface and is not unit-tested directly — only the pure
/// <see cref="BuildTrackUrl"/> link-building is (see the tests). Launching is best-effort: any
/// failure (no handler registered, shell error) is logged and swallowed, matching how every other
/// playback command already degrades.
/// </para>
/// </summary>
public sealed class AppleMusicUriLauncher(IStorefrontProvider storefrontProvider) : IAppleMusicTrackLauncher
{
    public Task PlayTrackAsync(string catalogTrackId)
    {
        if (string.IsNullOrWhiteSpace(catalogTrackId))
        {
            return Task.CompletedTask;
        }

        var url = BuildTrackUrl(storefrontProvider.GetStorefront(), catalogTrackId);
        try
        {
            // UseShellExecute routes the URL through the registered protocol handler (the Apple
            // Music app) instead of trying to exec it as a file.
            using var process = Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Failed to launch Apple Music for track {catalogTrackId}: {ex.GetType().Name}: {ex.Message}", category: "MediaTransport");
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Builds the canonical music.apple.com song URL for a storefront/catalog-id pair. Both parts
    /// are simple tokens (a two-letter storefront, a numeric id) but are escaped defensively since
    /// the id ultimately originates from a catalog API response, not our own code.
    /// </summary>
    internal static string BuildTrackUrl(string storefront, string catalogTrackId) =>
        $"https://music.apple.com/{Uri.EscapeDataString(storefront)}/song/{Uri.EscapeDataString(catalogTrackId)}";
}
