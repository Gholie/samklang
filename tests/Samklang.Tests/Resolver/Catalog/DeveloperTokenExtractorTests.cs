using System.Text;
using Samklang.Resolver.Catalog;
using Xunit;

namespace Samklang.Tests.Resolver.Catalog;

public class DeveloperTokenExtractorTests
{
    private const long Expiry = 1_784_689_460;

    // The two header shapes the token has actually shipped with: the pre-2026 one the old code
    // hardcoded a prefix for, and the current one (observed live 2026-07-07) whose added
    // "typ":"JWT" member broke that prefix and disabled the catalog layer (issue #31).
    private const string OldHeader = """{"alg":"ES256","kid":"ABC123DEF4"}""";
    private const string CurrentHeader = """{"typ":"JWT","alg":"ES256","kid":"WebPlayKid"}""";

    private static readonly string DeveloperTokenPayload =
        $$"""{"iss":"AMPWebPlay","iat":1781665460,"exp":{{Expiry}},"root_https_origin":["apple.com"]}""";

    // The other real ES256 JWT living in the same bundle (a metrics token) — the reason
    // extraction must prefer by issuer instead of taking the first decodable candidate.
    private static readonly string MetricsTokenPayload =
        $$"""{"iss":"M62YD85FTQ","iat":1781665460,"exp":{{Expiry}},"origin":"*.apple.com"}""";

    private static string Jwt(string headerJson, string payloadJson) =>
        $"{Base64Url(headerJson)}.{Base64Url(payloadJson)}.fake-signature_bytes";

    private static string Base64Url(string json) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(json)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    [Fact]
    public void TryExtractToken_extracts_the_current_token_format_and_its_expiry()
    {
        var token = Jwt(CurrentHeader, DeveloperTokenPayload);
        var bundle = $$"""const go="2626.2.0-external",qc="{{token}}";e.configure({developerToken:qc})""";

        var result = DeveloperTokenExtractor.TryExtractToken(bundle);

        Assert.NotNull(result);
        Assert.Equal(token, result.Value);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(Expiry), result.ExpiresAtUtc);
    }

    [Fact]
    public void TryExtractToken_still_extracts_the_pre_2026_header_format()
    {
        var token = Jwt(OldHeader, DeveloperTokenPayload);

        var result = DeveloperTokenExtractor.TryExtractToken($"""var t="{token}";""");

        Assert.Equal(token, result?.Value);
    }

    [Fact]
    public void TryExtractToken_prefers_the_musickit_issuer_over_another_valid_es256_token_appearing_earlier()
    {
        var metricsToken = Jwt(CurrentHeader, MetricsTokenPayload);
        var developerToken = Jwt(CurrentHeader, DeveloperTokenPayload);
        var bundle = $"""metrics="{metricsToken}";developer="{developerToken}";""";

        var result = DeveloperTokenExtractor.TryExtractToken(bundle);

        Assert.Equal(developerToken, result?.Value);
    }

    [Fact]
    public void TryExtractToken_falls_back_to_any_valid_es256_token_when_no_musickit_issuer_is_present()
    {
        // If Apple renames the issuer, a decodable ES256 token with an expiry is still the best
        // available guess — better than disabling the layer outright.
        var token = Jwt(CurrentHeader, MetricsTokenPayload);

        var result = DeveloperTokenExtractor.TryExtractToken($"\"{token}\"");

        Assert.Equal(token, result?.Value);
    }

    [Fact]
    public void TryExtractToken_ignores_near_miss_candidates_that_are_not_es256_tokens_with_an_expiry()
    {
        var notJson = "eyJQQQQ.eyJRRRR.sig"; // JWT-shaped but decodes to garbage
        var wrongAlg = Jwt("""{"alg":"HS256"}""", DeveloperTokenPayload);
        var noExpiry = Jwt(CurrentHeader, """{"iss":"AMPWebPlay","iat":1781665460}""");
        var bundle = $"""a="{notJson}";b="{wrongAlg}";c="{noExpiry}";""";

        Assert.Null(DeveloperTokenExtractor.TryExtractToken(bundle));
    }

    [Fact]
    public void TryExtractToken_returns_null_for_a_bundle_with_no_token_at_all()
    {
        Assert.Null(DeveloperTokenExtractor.TryExtractToken("var config={};"));
    }

    [Fact]
    public void FindBundlePaths_prefers_the_modern_bundle_over_the_legacy_one_regardless_of_document_order()
    {
        const string indexHtml = """
            <script type="module" src="/assets/index-legacy~a89d293dac.js"></script>
            <script type="module" src="/assets/polyfills-legacy~be5a3b69f2.js"></script>
            <script type="module" src="/assets/index~299c09aac6.js"></script>
            """;

        var paths = DeveloperTokenExtractor.FindBundlePaths(indexHtml);

        Assert.Equal(["/assets/index~299c09aac6.js", "/assets/index-legacy~a89d293dac.js"], paths);
    }

    [Fact]
    public void FindBundlePaths_returns_empty_when_the_page_references_no_index_bundle()
    {
        Assert.Empty(DeveloperTokenExtractor.FindBundlePaths("<html><body>empty 200</body></html>"));
    }

    [Fact]
    public void FindChunkPaths_finds_the_same_origin_script_assets_referenced_by_a_bundle()
    {
        const string bundle = """import("./x");u="assets/mt-client-logger-core.esm~62f4bb1ef7.js";v="assets/GetLibraryPinsIntentController~acada1ef92.js";""";

        var paths = DeveloperTokenExtractor.FindChunkPaths(bundle);

        Assert.Equal(
            ["assets/mt-client-logger-core.esm~62f4bb1ef7.js", "assets/GetLibraryPinsIntentController~acada1ef92.js"],
            paths);
    }
}
