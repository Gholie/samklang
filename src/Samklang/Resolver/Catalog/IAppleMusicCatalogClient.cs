using Samklang.Domain;

namespace Samklang.Resolver.Catalog;

/// <summary>
/// Network seam for the catalog layer's three unofficial calls (docs/adr/0001): scraping the
/// anonymous web-player token, catalog search, and fetching the enhanced-HLS manifest behind a
/// matched track's extended asset URLs. Isolated behind this interface so
/// <see cref="CatalogFormatResolverLayer"/> can be unit-tested with fakes and never needs a live
/// network call in tests.
/// </summary>
public interface IAppleMusicCatalogClient
{
    /// <summary>
    /// Scrapes the current anonymous developer token embedded in the music.apple.com web
    /// player's JS bundle. Should throw if the page/bundle no longer contains a token in the
    /// expected shape (e.g. Apple reworked the web player) or the request otherwise fails — the
    /// catalog layer treats any failure here as reason to disable itself for the rest of the
    /// session rather than retry every track.
    /// </summary>
    Task<AppleMusicToken> FetchTokenAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Searches the catalog (<c>amp-api</c> song search) for candidates matching the given
    /// Track's metadata. Returns an empty list rather than throwing when there's simply no
    /// result; <see cref="CatalogTrackMatcher"/> decides which (if any) candidate counts as a
    /// match.
    /// </summary>
    Task<IReadOnlyList<CatalogSearchCandidate>> SearchTracksAsync(
        string storefront, string token, Track track, CancellationToken cancellationToken);

    /// <summary>
    /// Resolves the matched catalog track's extended asset URLs (<c>extend=extendedAssetUrls</c>)
    /// and downloads the resulting enhanced-HLS manifest, returning its raw text — or null if the
    /// track has no enhanced-HLS asset (e.g. lossy-only catalog entry).
    /// </summary>
    Task<string?> FetchEnhancedHlsManifestAsync(
        string storefront, string catalogTrackId, string token, CancellationToken cancellationToken);
}

/// <summary>One catalog search result: just enough metadata for <see cref="CatalogTrackMatcher"/> to score it.</summary>
public sealed record CatalogSearchCandidate(string Id, string Title, string Artist, string Album);
