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
    public void ParseBestLosslessFormat_reads_the_format_from_the_media_rendition_group_a_codecs_alac_variant_references()
    {
        // The shape live manifests actually have (trimmed from a real one fetched 2026-07-08,
        // issue #31): no AUDIO-FORMAT or SAMPLE-RATE on the STREAM-INF line at all — only
        // CODECS="alac" plus an AUDIO="GROUP-ID" reference to the EXT-X-MEDIA line carrying them.
        var manifest = ReadFixture("media-group-alac.m3u8");

        var result = EnhancedHlsManifestParser.ParseBestLosslessFormat(manifest);

        Assert.Equal(new DeviceFormat(48_000, 24), result);
    }

    [Fact]
    public void ParseBestLosslessFormat_picks_the_best_across_media_rendition_groups()
    {
        const string manifest = """
            #EXTM3U
            #EXT-X-MEDIA:TYPE=AUDIO,GROUP-ID="audio-alac-stereo-48000-24",CHANNELS="2",SAMPLE-RATE=48000,BIT-DEPTH=24
            #EXT-X-MEDIA:TYPE=AUDIO,GROUP-ID="audio-alac-stereo-192000-24",CHANNELS="2",SAMPLE-RATE=192000,BIT-DEPTH=24
            #EXT-X-STREAM-INF:BANDWIDTH=1964174,CODECS="alac",AUDIO="audio-alac-stereo-48000-24"
            audio-alac-48.m3u8
            #EXT-X-STREAM-INF:BANDWIDTH=5964174,CODECS="alac",AUDIO="audio-alac-stereo-192000-24"
            audio-alac-192.m3u8
            """;

        var result = EnhancedHlsManifestParser.ParseBestLosslessFormat(manifest);

        Assert.Equal(new DeviceFormat(192_000, 24), result);
    }

    [Fact]
    public void ParseBestLosslessFormat_skips_an_alac_variant_whose_rendition_group_carries_no_sample_rate()
    {
        const string manifest = """
            #EXTM3U
            #EXT-X-MEDIA:TYPE=AUDIO,GROUP-ID="audio-alac-stereo",CHANNELS="2"
            #EXT-X-STREAM-INF:BANDWIDTH=1964174,CODECS="alac",AUDIO="audio-alac-stereo"
            audio-alac.m3u8
            """;

        Assert.Null(EnhancedHlsManifestParser.ParseBestLosslessFormat(manifest));
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
