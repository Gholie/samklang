using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
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
/// fakes of this interface instead.
/// </summary>
public sealed partial class HttpAppleMusicCatalogClient(HttpClient httpClient) : IAppleMusicCatalogClient
{
    private const string WebPlayerOrigin = "https://music.apple.com";
    private const string ApiBase = "https://amp-api.music.apple.com/v1/catalog";

    public async Task<AppleMusicToken> FetchTokenAsync(CancellationToken cancellationToken)
    {
        var indexHtml = await httpClient.GetStringAsync(WebPlayerOrigin, cancellationToken).ConfigureAwait(false);

        var bundleMatch = IndexBundleScriptRegex().Match(indexHtml);
        if (!bundleMatch.Success)
        {
            throw new InvalidOperationException("Could not locate the web player's JS bundle reference — Apple may have reworked the page.");
        }

        var bundleUrl = new Uri(new Uri(WebPlayerOrigin), bundleMatch.Groups["src"].Value);
        var bundleJs = await httpClient.GetStringAsync(bundleUrl, cancellationToken).ConfigureAwait(false);

        var tokenMatch = JwtLiteralRegex().Match(bundleJs);
        if (!tokenMatch.Success)
        {
            throw new InvalidOperationException("Could not locate an embedded developer token in the web player's JS bundle.");
        }

        var token = tokenMatch.Value;
        var expiresAtUtc = DecodeJwtExpiry(token);
        return new AppleMusicToken(token, expiresAtUtc);
    }

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

        return await httpClient.GetStringAsync(manifestUrl, cancellationToken).ConfigureAwait(false);
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, string url, string token)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Referrer = new Uri(WebPlayerOrigin);
        request.Headers.Add("Origin", WebPlayerOrigin);
        return request;
    }

    /// <summary>Decodes a JWT's unvalidated payload to read its <c>exp</c> claim (seconds since epoch). Signature is not verified — this is a public, anonymous token; we only need its stated expiry.</summary>
    private static DateTimeOffset DecodeJwtExpiry(string jwt)
    {
        var parts = jwt.Split('.');
        if (parts.Length < 2)
        {
            throw new InvalidOperationException("Embedded developer token is not a well-formed JWT.");
        }

        var payloadJson = Base64UrlDecode(parts[1]);
        using var document = JsonDocument.Parse(payloadJson);
        if (!document.RootElement.TryGetProperty("exp", out var expElement))
        {
            throw new InvalidOperationException("JWT payload has no 'exp' claim.");
        }

        return DateTimeOffset.FromUnixTimeSeconds(expElement.GetInt64());
    }

    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded += new string('=', (4 - padded.Length % 4) % 4);
        return Convert.FromBase64String(padded);
    }

    [GeneratedRegex("""<script[^>]+src="(?<src>[^"]*assets/index[^"]*\.js)"[^>]*>""", RegexOptions.IgnoreCase)]
    private static partial Regex IndexBundleScriptRegex();

    [GeneratedRegex(@"eyJhbGciOiJFUzI1NiIsImtpZCI6Ij[\w-]+\.[\w-]+\.[\w-]+")]
    private static partial Regex JwtLiteralRegex();
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

    [JsonPropertyName("attributes")]
    public CatalogSongAttributes Attributes { get; set; } = new();
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
internal partial class CatalogJsonContext : JsonSerializerContext
{
}
