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
    public Task PlayTrackAsync(string catalogTrackId, string albumId)
    {
        if (string.IsNullOrWhiteSpace(catalogTrackId))
        {
            return Task.CompletedTask;
        }

        var url = BuildTrackUrl(storefrontProvider.GetStorefront(), catalogTrackId, albumId);
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
    /// Builds the music.apple.com play URL for a track. With an album id, this is the album-context
    /// form (<c>album/{albumId}?i={songId}</c>) — Apple's own canonical "song" link — so the app
    /// plays the track and keeps going through the album afterwards. Without one, it falls back to a
    /// plain <c>song/{songId}</c> link that just plays the track. All parts are simple tokens (a
    /// two-letter storefront, numeric ids) but are escaped defensively since the ids ultimately
    /// originate from a catalog API response, not our own code.
    /// </summary>
    internal static string BuildTrackUrl(string storefront, string catalogTrackId, string albumId)
    {
        var storefrontSegment = Uri.EscapeDataString(storefront);
        return string.IsNullOrWhiteSpace(albumId)
            ? $"https://music.apple.com/{storefrontSegment}/song/{Uri.EscapeDataString(catalogTrackId)}"
            : $"https://music.apple.com/{storefrontSegment}/album/{Uri.EscapeDataString(albumId)}?i={Uri.EscapeDataString(catalogTrackId)}";
    }
}
