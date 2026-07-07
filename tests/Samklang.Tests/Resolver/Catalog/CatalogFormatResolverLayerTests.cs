using System.Net.Http;
using Samklang.Domain;
using Samklang.Resolver.Catalog;
using Xunit;

namespace Samklang.Tests.Resolver.Catalog;

public class CatalogFormatResolverLayerTests
{
    private const string DefaultManifest = """
        #EXTM3U
        #EXT-X-STREAM-INF:AUDIO-FORMAT="alac",SAMPLE-RATE=96000,BIT-DEPTH=24,CODECS="alac"
        audio.m3u8
        """;

    private sealed class FakeCatalogClient : IAppleMusicCatalogClient
    {
        public int FetchTokenCallCount { get; private set; }
        public int SearchCallCount { get; private set; }
        public int ManifestCallCount { get; private set; }
        public string? LastSearchStorefront { get; private set; }
        public string? LastManifestStorefront { get; private set; }

        public Func<CancellationToken, Task<AppleMusicToken>> FetchTokenImpl { get; set; } =
            _ => Task.FromResult(new AppleMusicToken("token", DateTimeOffset.UtcNow.AddHours(1)));

        public Func<string, string, Track, CancellationToken, Task<IReadOnlyList<CatalogSearchCandidate>>> SearchImpl { get; set; } =
            (_, _, track, _) => Task.FromResult<IReadOnlyList<CatalogSearchCandidate>>(
                [new CatalogSearchCandidate("id-1", track.Title, track.Artist, track.Album)]);

        public Func<string, string, string, CancellationToken, Task<string?>> ManifestImpl { get; set; } =
            (_, _, _, _) => Task.FromResult<string?>(DefaultManifest);

        public Task<AppleMusicToken> FetchTokenAsync(CancellationToken cancellationToken)
        {
            FetchTokenCallCount++;
            return FetchTokenImpl(cancellationToken);
        }

        public Task<IReadOnlyList<CatalogSearchCandidate>> SearchTracksAsync(
            string storefront, string token, Track track, CancellationToken cancellationToken)
        {
            SearchCallCount++;
            LastSearchStorefront = storefront;
            return SearchImpl(storefront, token, track, cancellationToken);
        }

        public Task<string?> FetchEnhancedHlsManifestAsync(
            string storefront, string catalogTrackId, string token, CancellationToken cancellationToken)
        {
            ManifestCallCount++;
            LastManifestStorefront = storefront;
            return ManifestImpl(storefront, catalogTrackId, token, cancellationToken);
        }
    }

    private sealed class FakeStorefrontProvider(string storefront) : IStorefrontProvider
    {
        public string GetStorefront() => storefront;
    }

    private static Track SampleTrack(string title = "Title") => new(title, "Artist", "Album");

    [Fact]
    public void TryResolve_returns_an_exact_resolution_when_the_token_search_and_manifest_all_succeed_within_the_budget()
    {
        var layer = new CatalogFormatResolverLayer(new FakeCatalogClient(), new FakeStorefrontProvider("us"));

        var result = layer.TryResolve(SampleTrack());

        Assert.NotNull(result);
        Assert.Equal(new DeviceFormat(96_000, 24), result!.Target);
        Assert.Equal(ResolutionConfidence.Exact, result.Confidence);
        Assert.Equal(layer.Name, result.SourceLayer);
        // ParseBestLosslessFormat only ever matches ALAC variants, so every resolution this layer
        // produces is genuinely lossless — a dashboard consumer must be able to trust this flag
        // rather than inferring losslessness from Confidence (see AudioTierClassifier).
        Assert.True(result.IsLossless);
    }

    [Fact]
    public void TryResolve_passes_the_storefront_providers_value_through_to_search_and_manifest_calls()
    {
        var client = new FakeCatalogClient();
        var layer = new CatalogFormatResolverLayer(client, new FakeStorefrontProvider("no"));

        layer.TryResolve(SampleTrack());

        Assert.Equal("no", client.LastSearchStorefront);
        Assert.Equal("no", client.LastManifestStorefront);
    }

    [Fact]
    public void TryResolve_fetches_the_token_once_and_reuses_it_across_tracks_while_still_fresh()
    {
        var client = new FakeCatalogClient();
        var layer = new CatalogFormatResolverLayer(client, new FakeStorefrontProvider("us"));

        layer.TryResolve(SampleTrack("Title A"));
        layer.TryResolve(SampleTrack("Title B"));

        Assert.Equal(1, client.FetchTokenCallCount);
    }

    [Fact]
    public void TryResolve_caches_the_result_per_track_so_a_repeat_lookup_does_not_hit_the_network_again()
    {
        var client = new FakeCatalogClient();
        var layer = new CatalogFormatResolverLayer(client, new FakeStorefrontProvider("us"));
        var track = SampleTrack();

        layer.TryResolve(track);
        var searchCallsAfterFirst = client.SearchCallCount;
        var second = layer.TryResolve(track);

        Assert.Equal(searchCallsAfterFirst, client.SearchCallCount);
        Assert.Equal(ResolutionConfidence.Exact, second?.Confidence);
    }

    [Fact]
    public void TryResolve_refetches_the_token_once_the_cached_one_is_within_the_refresh_buffer_of_expiring()
    {
        var fetchCount = 0;
        var currentTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var client = new FakeCatalogClient
        {
            FetchTokenImpl = _ =>
            {
                fetchCount++;
                return Task.FromResult(new AppleMusicToken($"token-{fetchCount}", currentTime.AddMinutes(10)));
            },
        };
        var layer = new CatalogFormatResolverLayer(client, new FakeStorefrontProvider("us"), now: () => currentTime);

        layer.TryResolve(SampleTrack("Title A"));
        Assert.Equal(1, fetchCount);

        // 6 minutes later, the first token (10-minute life) is within the 5-minute refresh buffer.
        currentTime = currentTime.AddMinutes(6);
        layer.TryResolve(SampleTrack("Title B"));

        Assert.Equal(2, fetchCount);
    }

    [Fact]
    public void TryResolve_disables_the_layer_for_the_session_after_a_token_fetch_failure_and_never_retries_it()
    {
        var client = new FakeCatalogClient
        {
            FetchTokenImpl = _ => throw new HttpRequestException("scrape failed — Apple likely changed the web player"),
        };
        var layer = new CatalogFormatResolverLayer(client, new FakeStorefrontProvider("us"));

        var first = layer.TryResolve(SampleTrack("Title A"));
        Assert.Null(first);
        Assert.True(layer.IsDisabledForSession);

        var second = layer.TryResolve(SampleTrack("Title B"));

        Assert.Null(second);
        Assert.Equal(1, client.FetchTokenCallCount);
    }

    [Fact]
    public void TryResolve_only_fails_the_one_track_when_search_throws_and_still_resolves_the_next_track()
    {
        var client = new FakeCatalogClient
        {
            SearchImpl = (_, _, track, _) => track.Title == "Bad Track"
                ? throw new HttpRequestException("transient network error")
                : Task.FromResult<IReadOnlyList<CatalogSearchCandidate>>([new CatalogSearchCandidate("id-1", track.Title, track.Artist, track.Album)]),
        };
        var layer = new CatalogFormatResolverLayer(client, new FakeStorefrontProvider("us"));

        var badResult = layer.TryResolve(SampleTrack("Bad Track"));
        Assert.Null(badResult);
        Assert.False(layer.IsDisabledForSession);

        var goodResult = layer.TryResolve(SampleTrack("Good Track"));
        Assert.Equal(ResolutionConfidence.Exact, goodResult?.Confidence);
    }

    [Fact]
    public void TryResolve_retries_a_track_whose_earlier_lookup_failed_instead_of_caching_the_failure()
    {
        var attempts = 0;
        var client = new FakeCatalogClient
        {
            SearchImpl = (_, _, track, _) => ++attempts == 1
                ? throw new HttpRequestException("transient network error")
                : Task.FromResult<IReadOnlyList<CatalogSearchCandidate>>(
                    [new CatalogSearchCandidate("id-1", track.Title, track.Artist, track.Album)]),
        };
        var layer = new CatalogFormatResolverLayer(client, new FakeStorefrontProvider("us"));
        var track = SampleTrack();

        // One network blip must not turn this track into a permanent miss for the session —
        // replaying it later should hit the catalog again and succeed.
        Assert.Null(layer.TryResolve(track));
        var second = layer.TryResolve(track);

        Assert.Equal(2, attempts);
        Assert.Equal(ResolutionConfidence.Exact, second?.Confidence);
    }

    [Fact]
    public void TryResolve_returns_null_when_no_catalog_candidate_matches_the_track()
    {
        var client = new FakeCatalogClient
        {
            SearchImpl = (_, _, _, _) => Task.FromResult<IReadOnlyList<CatalogSearchCandidate>>(
                [new CatalogSearchCandidate("id-1", "Completely Different Song", "Nobody", "Nothing")]),
        };
        var layer = new CatalogFormatResolverLayer(client, new FakeStorefrontProvider("us"));

        var result = layer.TryResolve(SampleTrack());

        Assert.Null(result);
        Assert.False(layer.IsDisabledForSession);
    }

    [Fact]
    public void TryResolve_returns_null_when_the_manifest_has_no_lossless_variant()
    {
        var client = new FakeCatalogClient
        {
            ManifestImpl = (_, _, _, _) => Task.FromResult<string?>("""
                #EXTM3U
                #EXT-X-STREAM-INF:AUDIO-FORMAT="he-aac",CODECS="mp4a.40.2"
                audio.m3u8
                """),
        };
        var layer = new CatalogFormatResolverLayer(client, new FakeStorefrontProvider("us"));

        var result = layer.TryResolve(SampleTrack());

        Assert.Null(result);
    }

    [Fact]
    public void TryResolve_returns_null_when_the_manifest_fetch_yields_no_asset_url()
    {
        var client = new FakeCatalogClient { ManifestImpl = (_, _, _, _) => Task.FromResult<string?>(null) };
        var layer = new CatalogFormatResolverLayer(client, new FakeStorefrontProvider("us"));

        var result = layer.TryResolve(SampleTrack());

        Assert.Null(result);
    }

    [Fact]
    public void TryResolve_gives_up_within_the_bounded_timeout_and_later_raises_a_late_resolution_once_the_slow_lookup_completes()
    {
        var searchTcs = new TaskCompletionSource<IReadOnlyList<CatalogSearchCandidate>>();
        var client = new FakeCatalogClient { SearchImpl = (_, _, _, _) => searchTcs.Task };
        var layer = new CatalogFormatResolverLayer(
            client,
            new FakeStorefrontProvider("us"),
            resolveTimeout: TimeSpan.FromMilliseconds(20),
            backgroundCeiling: TimeSpan.FromSeconds(5));

        LateResolutionEventArgs? received = null;
        using var signal = new ManualResetEventSlim(false);
        layer.LateResolutionAvailable += (_, args) =>
        {
            received = args;
            signal.Set();
        };

        var track = SampleTrack();
        var promptResult = layer.TryResolve(track);

        Assert.Null(promptResult); // gave up within the 20ms budget; catalog lookup is still running

        searchTcs.SetResult([new CatalogSearchCandidate("id-1", track.Title, track.Artist, track.Album)]);

        Assert.True(signal.Wait(TimeSpan.FromSeconds(5)), "expected LateResolutionAvailable to fire once the slow lookup finished");
        Assert.Equal(track, received!.Track);
        Assert.Equal(new DeviceFormat(96_000, 24), received.Resolution.Target);
        Assert.Equal(ResolutionConfidence.Exact, received.Resolution.Confidence);

        // The now-completed lookup is cached, so a subsequent call for the same Track is instant and needs no further network call.
        var searchCallsSoFar = client.SearchCallCount;
        var cachedResult = layer.TryResolve(track);
        Assert.Equal(ResolutionConfidence.Exact, cachedResult?.Confidence);
        Assert.Equal(searchCallsSoFar, client.SearchCallCount);
    }

    [Fact]
    public void TryResolve_reuses_the_in_flight_lookup_for_a_second_call_to_the_same_track_instead_of_starting_a_new_one()
    {
        var searchTcs = new TaskCompletionSource<IReadOnlyList<CatalogSearchCandidate>>();
        var client = new FakeCatalogClient { SearchImpl = (_, _, _, _) => searchTcs.Task };
        var layer = new CatalogFormatResolverLayer(
            client, new FakeStorefrontProvider("us"), resolveTimeout: TimeSpan.FromMilliseconds(10));
        var track = SampleTrack();

        layer.TryResolve(track);
        layer.TryResolve(track);

        Assert.Equal(1, client.SearchCallCount);

        searchTcs.SetResult([new CatalogSearchCandidate("id-1", track.Title, track.Artist, track.Album)]);
    }
}
