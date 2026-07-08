using System.Text.Json;
using System.Text.RegularExpressions;

namespace Samklang.Resolver.Catalog;

/// <summary>
/// The pure string-scraping half of the anonymous developer-token fetch (issue #31): given the
/// web player's index HTML or a JS bundle's text, finds the bundle to scan and the token inside
/// it. Split out of <see cref="HttpAppleMusicCatalogClient"/> so this logic is unit-testable
/// against fixture text while the network adapter stays deliberately untested.
///
/// <para>
/// Token candidates are matched with a generic JWT-literal pattern and then <b>validated by
/// decoding</b> — never by hardcoding a header prefix. Apple changed the token's JWT header once
/// already (adding <c>"typ":"JWT"</c>, 2026 — fixed in PR #40), which silently disabled the
/// catalog layer for a whole release; matching on decoded content instead of encoded bytes
/// survives that class of change. The bundle carries more than one real ES256
/// JWT (a metrics token alongside the developer token), so candidates with the MusicKit issuer
/// <c>AMPWebPlay</c> are preferred over merely-plausible ones.
/// </para>
/// </summary>
public static partial class DeveloperTokenExtractor
{
    /// <summary>The <c>iss</c> claim of the real MusicKit developer token, as opposed to the other ES256 JWTs (e.g. the metrics token) embedded in the same bundle.</summary>
    private const string MusicKitIssuer = "AMPWebPlay";

    /// <summary>
    /// Paths of the web player's index JS bundles referenced by <paramref name="indexHtml"/>,
    /// the modern bundle ordered before the <c>index-legacy</c> transpilation of it (both embed
    /// a token, but the modern one is the smaller, canonical scan target — and relying on
    /// document order picked whichever Apple happened to emit first).
    /// </summary>
    public static IReadOnlyList<string> FindBundlePaths(string indexHtml) =>
        IndexBundleScriptRegex().Matches(indexHtml)
            .Select(match => match.Groups["src"].Value)
            .Distinct()
            .OrderBy(path => path.Contains("-legacy", StringComparison.OrdinalIgnoreCase))
            .ToList();

    /// <summary>
    /// Extracts the developer token from a JS bundle's text, or null when the bundle contains no
    /// convincing candidate. A candidate must decode as an ES256 JWT with an <c>exp</c> claim
    /// (the expiry drives the token-refresh cycle, so a token without one is unusable anyway);
    /// among those, the <see cref="MusicKitIssuer"/> one wins.
    /// </summary>
    public static AppleMusicToken? TryExtractToken(string bundleJs)
    {
        var candidates = JwtLiteralRegex().Matches(bundleJs)
            .Select(match => match.Value)
            .Distinct()
            .Select(TryDescribe)
            .Where(candidate => candidate is not null)
            .ToList();

        var chosen = candidates.FirstOrDefault(candidate => candidate!.Issuer == MusicKitIssuer)
            ?? candidates.FirstOrDefault();
        return chosen is null ? null : new AppleMusicToken(chosen.Value, chosen.ExpiresAtUtc);
    }

    /// <summary>
    /// Same-origin <c>assets/*.js</c> chunk paths referenced inside a bundle's text — the sweep
    /// targets for finding the token again should Apple move it out of the index bundle.
    /// </summary>
    public static IReadOnlyList<string> FindChunkPaths(string bundleJs) =>
        ChunkReferenceRegex().Matches(bundleJs)
            .Select(match => match.Value)
            .Distinct()
            .ToList();

    /// <summary>Decodes one JWT-shaped literal into a validated candidate, or null for the near-misses a bundle is full of (base64 blobs that aren't JSON, non-ES256 JWTs, tokens without an expiry).</summary>
    private static TokenCandidate? TryDescribe(string jwt)
    {
        var parts = jwt.Split('.');
        try
        {
            using var header = JsonDocument.Parse(Base64UrlDecode(parts[0]));
            if (!header.RootElement.TryGetProperty("alg", out var alg) || alg.ValueKind != JsonValueKind.String || alg.GetString() != "ES256")
            {
                return null;
            }

            using var payload = JsonDocument.Parse(Base64UrlDecode(parts[1]));
            if (!payload.RootElement.TryGetProperty("exp", out var exp) || exp.ValueKind != JsonValueKind.Number)
            {
                return null;
            }

            var issuer = payload.RootElement.TryGetProperty("iss", out var iss) && iss.ValueKind == JsonValueKind.String
                ? iss.GetString()
                : null;
            return new TokenCandidate(jwt, issuer, DateTimeOffset.FromUnixTimeSeconds(exp.GetInt64()));
        }
        catch (Exception e) when (e is FormatException or JsonException)
        {
            return null;
        }
    }

    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded += new string('=', (4 - padded.Length % 4) % 4);
        return Convert.FromBase64String(padded);
    }

    private sealed record TokenCandidate(string Value, string? Issuer, DateTimeOffset ExpiresAtUtc);

    [GeneratedRegex("""<script[^>]+src="(?<src>[^"]*assets/index[^"]*\.js)"[^>]*>""", RegexOptions.IgnoreCase)]
    private static partial Regex IndexBundleScriptRegex();

    [GeneratedRegex(@"eyJ[\w-]+\.[\w-]+\.[\w-]+")]
    private static partial Regex JwtLiteralRegex();

    [GeneratedRegex(@"assets/[\w~.-]+\.js")]
    private static partial Regex ChunkReferenceRegex();
}
