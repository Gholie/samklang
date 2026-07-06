using Samklang.Domain;
using Samklang.Resolver;
using Samklang.Resolver.Catalog;
using Xunit;

namespace Samklang.Tests.Resolver.Catalog;

public class EnhancedHlsManifestParserTests
{
    private static string ReadFixture(string fileName) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Resolver", "Catalog", "Fixtures", fileName));

    [Fact]
    public void ParseBestLosslessFormat_picks_the_highest_sample_rate_alac_variant_over_lower_ones_and_non_alac_variants()
    {
        var manifest = ReadFixture("hi-res-alac.m3u8");

        var result = EnhancedHlsManifestParser.ParseBestLosslessFormat(manifest);

        Assert.Equal(new DeviceFormat(192_000, 24), result);
    }

    [Fact]
    public void ParseBestLosslessFormat_returns_the_only_alac_variant_when_the_rest_are_lossy()
    {
        var manifest = ReadFixture("cd-quality-alac.m3u8");

        var result = EnhancedHlsManifestParser.ParseBestLosslessFormat(manifest);

        Assert.Equal(new DeviceFormat(44_100, 16), result);
    }

    [Fact]
    public void ParseBestLosslessFormat_returns_null_when_the_manifest_has_no_alac_variant()
    {
        var manifest = ReadFixture("no-lossless-variant.m3u8");

        var result = EnhancedHlsManifestParser.ParseBestLosslessFormat(manifest);

        Assert.Null(result);
    }

    [Fact]
    public void ParseBestLosslessFormat_falls_back_to_the_pinned_bit_depth_when_the_variant_has_no_explicit_bit_depth_attribute()
    {
        var manifest = ReadFixture("alac-without-bit-depth-attribute.m3u8");

        var result = EnhancedHlsManifestParser.ParseBestLosslessFormat(manifest);

        Assert.Equal(new DeviceFormat(48_000, FallbackFormatResolverLayer.PinnedBitDepth), result);
    }

    [Fact]
    public void ParseBestLosslessFormat_returns_null_for_an_empty_manifest()
    {
        var result = EnhancedHlsManifestParser.ParseBestLosslessFormat(string.Empty);

        Assert.Null(result);
    }

    [Fact]
    public void ParseBestLosslessFormat_ignores_malformed_stream_inf_lines_missing_a_sample_rate()
    {
        const string manifest = """
            #EXTM3U
            #EXT-X-STREAM-INF:AUDIO-FORMAT="alac",BIT-DEPTH=24,CODECS="alac"
            audio-broken.m3u8
            #EXT-X-STREAM-INF:AUDIO-FORMAT="alac",SAMPLE-RATE=48000,BIT-DEPTH=24,CODECS="alac"
            audio-good.m3u8
            """;

        var result = EnhancedHlsManifestParser.ParseBestLosslessFormat(manifest);

        Assert.Equal(new DeviceFormat(48_000, 24), result);
    }
}
