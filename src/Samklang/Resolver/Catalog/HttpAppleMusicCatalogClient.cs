using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Samklang.Domain;

namespace Samklang.Resolver.Catalog;

/// <summary>
/// Real, network-backed <see cref="IAppleMusicCatalogClient"/>: scrapes the anonymous
/// developer token out of the music.apple.com web player's JS bundle, then calls
/// <c>amp-api.music.apple.com</c> for search and extended asset URLs (docs/adr/0001).
///
/// This is a thin adapter over an unofficial, scrapeable surface and is not unit-tested directly
/// — the same pattern as <see cref="Sessions.SmtcTrackWatcher"/> for the real SMTC API and
/// <see cref="SettingsManagement.JsonFileSettingsStore"/> for real file I/O. Everything it
/// produces (matching, manifest parsing, timeout/caching/disable behavior) is tested against
/// fakes of this interface instead, and the token-scraping string logic lives in the pure,
/// tested <see cref="DeveloperTokenExtractor"/> — only the HTTP plumbing is untested here.
/// </summary>
public sealed class HttpAppleMusicCatalogClient(HttpClient httpClient) : IAppleMusicCatalogClient
{
    private const string WebPlayerOrigin = "https://music.apple.com";
    private const string ApiBase = "https://amp-api.music.apple.com/v1/catalog";

    public async Task<AppleMusicToken> FetchTokenAsync(CancellationToken cancellationToken)
    {
        var indexHtml = await httpClient.GetStringAsync(WebPlayerOrigin, cancellationToken).ConfigureAwait(false);

        var bundlePaths = DeveloperTokenExtractor.FindBundlePaths(indexHtml);
        if (bundlePaths.Count == 0)
        {
            throw new InvalidOperationException("Could not locate the web player's JS bundle reference — Apple may have reworked the page.");
        }

        var bundleJs = await FetchAssetAsync(bundlePaths[0], cancellationToken).ConfigureAwait(false);
        if (DeveloperTokenExtractor.TryExtractToken(bundleJs) is { } token)
        {
            return token;
        }

        // The index bundle had no valid token. Before giving up (which disables the catalog
        // layer for the whole session), sweep the other same-origin script assets it references
        // plus any remaining index bundles — cheap resilience against Apple repackaging the
        // token into another chunk. Bounded by the caller's cancellation token like every fetch.
        var fallbackPaths = DeveloperTokenExtractor.FindChunkPaths(bundleJs)
            .Concat(bundlePaths.Skip(1))
            .Distinct();
        foreach (var path in fallbackPaths)
        {
            var chunkJs = await FetchAssetAsync(path, cancellationToken).ConfigureAwait(false);
            if (DeveloperTokenExtractor.TryExtractToken(chunkJs) is { } fallbackToken)
            {
                return fallbackToken;
            }
        }

        throw new InvalidOperationException("Could not locate an embedded developer token in any of the web player's JS assets.");
    }

    private Task<string> FetchAssetAsync(string path, CancellationToken cancellationToken) =>
        httpClient.GetStringAsync(new Uri(new Uri(WebPlayerOrigin), path), cancellationToken);

    public async Task<IReadOnlyList<CatalogSearchCandidate>> SearchTracksAsync(
        string storefront, string token, Track track, CancellationToken cancellationToken)
    {
        var term = Uri.EscapeDataString($"{track.Title} {track.Artist}");
        using var request = CreateRequest(HttpMethod.Get, $"{ApiBase}/{storefront}/search?term={term}&types=songs&limit=10", token);

        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync(CatalogJsonContext.Default.SearchResponse, cancellationToken)
            .ConfigureAwait(false);

        var songs = payload?.Results?.Songs?.Data;
        if (songs is null)
        {
            return [];
        }

        return songs
            .Select(song => new CatalogSearchCandidate(song.Id, song.Attributes.Name, song.Attributes.ArtistName, song.Attributes.AlbumName))
            .ToList();
    }

    public async Task<string?> FetchEnhancedHlsManifestAsync(
        string storefront, string catalogTrackId, string token, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(
            HttpMethod.Get, $"{ApiBase}/{storefront}/songs/{catalogTrackId}?extend=extendedAssetUrls", token);

        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync(CatalogJsonContext.Default.SongLookupResponse, cancellationToken)
            .ConfigureAwait(false);

        var manifestUrl = payload?.Data?.FirstOrDefault()?.Attributes?.ExtendedAssetUrls?.EnhancedHls;
        if (string.IsNullOrEmpty(manifestUrl))
        {
            return null;
        }

        // The URL comes out of an API response, not our own code — only follow it over HTTPS
        // (real asset URLs always are); anything else reads as a malformed/tampered payload.
        if (!Uri.TryCreate(manifestUrl, UriKind.Absolute, out var manifestUri) || manifestUri.Scheme != Uri.UriSchemeHttps)
        {
            return null;
        }

        return await httpClient.GetStringAsync(manifestUri, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<CatalogSearchCandidate>> FetchAlbumTracksAsync(
        string storefront, string catalogTrackId, string token, CancellationToken cancellationToken)
    {
        // Two amp-api hops: the song's albums relationship (ids only), then the first album's
        // ordered track list. limit=300 is the relationship endpoint's maximum and comfortably
        // covers any real album.
        using var albumsRequest = CreateRequest(
            HttpMethod.Get, $"{ApiBase}/{storefront}/songs/{catalogTrackId}/albums", token);
        using var albumsResponse = await httpClient.SendAsync(albumsRequest, cancellationToken).ConfigureAwait(false);
        albumsResponse.EnsureSuccessStatusCode();

        var albumsPayload = await albumsResponse.Content
            .ReadFromJsonAsync(CatalogJsonContext.Default.AlbumsLookupResponse, cancellationToken)
            .ConfigureAwait(false);
        var albumId = albumsPayload?.Data?.FirstOrDefault()?.Id;
        if (string.IsNullOrEmpty(albumId))
        {
            return [];
        }

        using var tracksRequest = CreateRequest(
            HttpMethod.Get, $"{ApiBase}/{storefront}/albums/{albumId}/tracks?limit=300", token);
        using var tracksResponse = await httpClient.SendAsync(tracksRequest, cancellationToken).ConfigureAwait(false);
        tracksResponse.EnsureSuccessStatusCode();

        // Same { data: [song resources] } shape as a song lookup, so the response type is reused.
        var tracksPayload = await tracksResponse.Content
            .ReadFromJsonAsync(CatalogJsonContext.Default.SongLookupResponse, cancellationToken)
            .ConfigureAwait(false);
        var tracks = tracksPayload?.Data;
        if (tracks is null)
        {
            return [];
        }

        return tracks
            // An album's track list can also carry music-video resources; only songs are
            // meaningful next-track predictions.
            .Where(track => track.Type == "songs")
            .Select(track => new CatalogSearchCandidate(track.Id, track.Attributes.Name, track.Attributes.ArtistName, track.Attributes.AlbumName))
            .ToList();
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, string url, string token)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Referrer = new Uri(WebPlayerOrigin);
        request.Headers.Add("Origin", WebPlayerOrigin);
        return request;
    }

}

internal sealed class SearchResponse
{
    [JsonPropertyName("results")]
    public SearchResults? Results { get; set; }
}

internal sealed class SearchResults
{
    [JsonPropertyName("songs")]
    public SongsBlock? Songs { get; set; }
}

internal sealed class SongsBlock
{
    [JsonPropertyName("data")]
    public List<CatalogSong>? Data { get; set; }
}

internal sealed class SongLookupResponse
{
    [JsonPropertyName("data")]
    public List<CatalogSong>? Data { get; set; }
}

internal sealed class CatalogSong
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    // Defaults to "songs" because search (types=songs) and song-lookup responses are always
    // songs; only an album's track list mixes in other resource types worth filtering out.
    [JsonPropertyName("type")]
    public string Type { get; set; } = "songs";

    [JsonPropertyName("attributes")]
    public CatalogSongAttributes Attributes { get; set; } = new();
}

internal sealed class AlbumsLookupResponse
{
    [JsonPropertyName("data")]
    public List<AlbumRef>? Data { get; set; }
}

internal sealed class AlbumRef
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;
}

internal sealed class CatalogSongAttributes
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("artistName")]
    public string ArtistName { get; set; } = string.Empty;

    [JsonPropertyName("albumName")]
    public string AlbumName { get; set; } = string.Empty;

    [JsonPropertyName("extendedAssetUrls")]
    public ExtendedAssetUrls? ExtendedAssetUrls { get; set; }
}

internal sealed class ExtendedAssetUrls
{
    [JsonPropertyName("enhancedHls")]
    public string? EnhancedHls { get; set; }
}

[JsonSerializable(typeof(SearchResponse))]
[JsonSerializable(typeof(SongLookupResponse))]
[JsonSerializable(typeof(AlbumsLookupResponse))]
internal partial class CatalogJsonContext : JsonSerializerContext
{
}
