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
        public int AlbumTracksCallCount { get; private set; }
        public string? LastSearchStorefront { get; private set; }
        public string? LastManifestStorefront { get; private set; }
        public string? LastAlbumTracksTrackId { get; private set; }

        public Func<CancellationToken, Task<AppleMusicToken>> FetchTokenImpl { get; set; } =
            _ => Task.FromResult(new AppleMusicToken("token", DateTimeOffset.UtcNow.AddHours(1)));

        public Func<string, string, Track, CancellationToken, Task<IReadOnlyList<CatalogSearchCandidate>>> SearchImpl { get; set; } =
            (_, _, track, _) => Task.FromResult<IReadOnlyList<CatalogSearchCandidate>>(
                [new CatalogSearchCandidate("id-1", track.Title, track.Artist, track.Album)]);

        public Func<string, string, string, CancellationToken, Task<string?>> ManifestImpl { get; set; } =
            (_, _, _, _) => Task.FromResult<string?>(DefaultManifest);

        // Empty by default: with no album to predict from, prefetching quietly does nothing and
        // the pre-existing tests are unaffected.
        public Func<string, string, string, CancellationToken, Task<IReadOnlyList<CatalogSearchCandidate>>> AlbumTracksImpl { get; set; } =
            (_, _, _, _) => Task.FromResult<IReadOnlyList<CatalogSearchCandidate>>([]);

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

        public Task<IReadOnlyList<CatalogSearchCandidate>> FetchAlbumTracksAsync(
            string storefront, string catalogTrackId, string token, CancellationToken cancellationToken)
        {
            AlbumTracksCallCount++;
            LastAlbumTracksTrackId = catalogTrackId;
            return AlbumTracksImpl(storefront, catalogTrackId, token, cancellationToken);
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

    // SMTC transient states (session attaching, placeholder titles like "Connecting…") report an
    // empty artist; the matcher can never accept such a Track, so the layer must not spend a
    // network round trip (or the resolve budget) on it.
    [Theory]
    [InlineData("Connecting…", "")] // placeholder title, no artist
    [InlineData("Focus", "  ")] // whitespace-only artist
    [InlineData("", "Sia")] // no title
    public void TryResolve_short_circuits_without_any_network_call_when_title_or_artist_is_empty(
        string title, string artist)
    {
        var client = new FakeCatalogClient();
        var layer = new CatalogFormatResolverLayer(client, new FakeStorefrontProvider("us"));

        var result = layer.TryResolve(new Track(title, artist, ""));

        Assert.Null(result);
        Assert.Equal(0, client.FetchTokenCallCount);
        Assert.Equal(0, client.SearchCallCount);
    }

    [Fact]
    public void TryResolve_still_resolves_the_next_complete_track_after_short_circuiting_a_transient_one()
    {
        var client = new FakeCatalogClient();
        var layer = new CatalogFormatResolverLayer(client, new FakeStorefrontProvider("us"));

        Assert.Null(layer.TryResolve(new Track("Connecting…", "", "")));
        var result = layer.TryResolve(SampleTrack());

        Assert.Equal(ResolutionConfidence.Exact, result?.Confidence);
        Assert.Equal(1, client.SearchCallCount);
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
    public void TryResolve_enters_a_cooldown_after_a_token_fetch_failure_and_skips_the_network_while_it_is_active()
    {
        var client = new FakeCatalogClient
        {
            FetchTokenImpl = _ => throw new HttpRequestException("scrape failed — Apple likely changed the web player"),
        };
        var currentTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var layer = new CatalogFormatResolverLayer(client, new FakeStorefrontProvider("us"), now: () => currentTime);

        var first = layer.TryResolve(SampleTrack("Title A"));
        Assert.Null(first);
        Assert.True(layer.IsDisabledForSession);

        // Still well within the first backoff step: a replay must not touch the network again.
        currentTime += TimeSpan.FromSeconds(5);
        var second = layer.TryResolve(SampleTrack("Title B"));

        Assert.Null(second);
        Assert.True(layer.IsDisabledForSession);
        Assert.Equal(1, client.FetchTokenCallCount);
    }

    [Fact]
    public void TryResolve_retries_the_token_fetch_once_the_cooldown_elapses_and_resolves_normally_on_success()
    {
        var attempts = 0;
        var client = new FakeCatalogClient
        {
            FetchTokenImpl = _ => ++attempts == 1
                ? throw new HttpRequestException("transient scrape failure")
                : Task.FromResult(new AppleMusicToken("token", DateTimeOffset.UtcNow.AddHours(1))),
        };
        var currentTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var layer = new CatalogFormatResolverLayer(client, new FakeStorefrontProvider("us"), now: () => currentTime);

        Assert.Null(layer.TryResolve(SampleTrack("Title A")));
        Assert.True(layer.IsDisabledForSession);

        // Past the first backoff step (30s): the next call must try the network again.
        currentTime += CatalogFormatResolverLayer.TokenFailureBackoffSchedule[0] + TimeSpan.FromSeconds(1);
        var result = layer.TryResolve(SampleTrack("Title B"));

        Assert.Equal(2, client.FetchTokenCallCount);
        Assert.False(layer.IsDisabledForSession);
        Assert.Equal(ResolutionConfidence.Exact, result?.Confidence);
    }

    [Fact]
    public void Consecutive_token_failures_escalate_the_cooldown_along_the_backoff_schedule()
    {
        var client = new FakeCatalogClient
        {
            FetchTokenImpl = _ => throw new HttpRequestException("scrape stays broken"),
        };
        var currentTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var layer = new CatalogFormatResolverLayer(client, new FakeStorefrontProvider("us"), now: () => currentTime);

        // First failure: cooldown is the schedule's first step.
        layer.TryResolve(SampleTrack("Title A"));
        currentTime += CatalogFormatResolverLayer.TokenFailureBackoffSchedule[0] + TimeSpan.FromSeconds(1);
        Assert.False(layer.IsDisabledForSession);

        // Second failure: cooldown escalates to the schedule's second step — advancing by only
        // the *first* step's duration again must still leave it disabled.
        layer.TryResolve(SampleTrack("Title B"));
        Assert.True(layer.IsDisabledForSession);
        currentTime += CatalogFormatResolverLayer.TokenFailureBackoffSchedule[0] + TimeSpan.FromSeconds(1);
        Assert.True(layer.IsDisabledForSession);

        currentTime += CatalogFormatResolverLayer.TokenFailureBackoffSchedule[1];
        Assert.False(layer.IsDisabledForSession);

        Assert.Equal(2, client.FetchTokenCallCount);
    }

    [Fact]
    public void A_successful_token_fetch_resets_the_failure_streak_so_a_later_failure_uses_the_short_first_step_again()
    {
        var shouldFail = true;
        var currentTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var client = new FakeCatalogClient
        {
            // Ties expiry to the fake clock (not real wall-clock time), same as the other
            // token-refresh tests below — otherwise a real-time expiry never lines up with the
            // fake time this test advances.
            FetchTokenImpl = _ => shouldFail
                ? throw new HttpRequestException("transient")
                : Task.FromResult(new AppleMusicToken("token", currentTime.AddHours(1))),
        };
        var layer = new CatalogFormatResolverLayer(client, new FakeStorefrontProvider("us"), now: () => currentTime);

        // One failure, then let it recover.
        layer.TryResolve(SampleTrack("Title A"));
        currentTime += CatalogFormatResolverLayer.TokenFailureBackoffSchedule[0] + TimeSpan.FromSeconds(1);
        shouldFail = false;
        var recovered = layer.TryResolve(SampleTrack("Title B"));
        Assert.Equal(ResolutionConfidence.Exact, recovered?.Confidence);
        Assert.False(layer.IsDisabledForSession);

        // A fresh failure after the reset must use the schedule's first (short) step again, not
        // an escalated one — the token is due for refresh again because the fetched token in this
        // test always expires an hour out, so force a refresh by advancing past that.
        shouldFail = true;
        currentTime += TimeSpan.FromHours(1);
        layer.TryResolve(SampleTrack("Title C"));
        Assert.True(layer.IsDisabledForSession);

        currentTime += CatalogFormatResolverLayer.TokenFailureBackoffSchedule[0] + TimeSpan.FromSeconds(1);
        Assert.False(layer.IsDisabledForSession);
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

    // --- Next-track prefetch buffer ---

    private const string HiResManifest = """
        #EXTM3U
        #EXT-X-STREAM-INF:AUDIO-FORMAT="alac",SAMPLE-RATE=192000,BIT-DEPTH=24,CODECS="alac"
        audio.m3u8
        """;

    /// <summary>An album whose first track ("id-1") is what the default SearchImpl matches, so resolving "Track One" anchors the prefetch prediction at "id-2".</summary>
    private static readonly IReadOnlyList<CatalogSearchCandidate> AlbumTracks =
    [
        new("id-1", "Track One", "Artist", "Album"),
        new("id-2", "Track Two", "Artist", "Album"),
        new("id-3", "Track Three", "Artist", "Album"),
    ];

    private static FakeCatalogClient AlbumPlaybackClient() => new()
    {
        AlbumTracksImpl = (_, _, _, _) => Task.FromResult(AlbumTracks),
        // Distinct rates per track so a buffer hit is distinguishable from a fresh lookup of the
        // anchoring track: id-2 is hi-res, everything else uses the 96k default manifest.
        ManifestImpl = (_, catalogTrackId, _, _) =>
            Task.FromResult<string?>(catalogTrackId == "id-2" ? HiResManifest : DefaultManifest),
    };

    private static Track AlbumTrack(string title) => new(title, "Artist", "Album");

    [Fact]
    public void A_successful_resolve_prefetches_the_next_album_tracks_format_and_a_matching_track_change_is_answered_from_the_buffer()
    {
        var client = AlbumPlaybackClient();
        var layer = new CatalogFormatResolverLayer(client, new FakeStorefrontProvider("us"));

        NextTrackPrefetchedEventArgs? prefetched = null;
        using var signal = new ManualResetEventSlim(false);
        layer.NextTrackPrefetched += (_, args) =>
        {
            prefetched = args;
            signal.Set();
        };

        var first = layer.TryResolve(AlbumTrack("Track One"));
        Assert.Equal(new DeviceFormat(96_000, 24), first!.Target);

        Assert.True(signal.Wait(TimeSpan.FromSeconds(5)), "expected the next album track's format to be prefetched");
        Assert.Equal("id-1", client.LastAlbumTracksTrackId); // predicted from the *matched* track's album
        Assert.Equal("id-2", prefetched!.NextTrack.Id);
        Assert.Equal(new DeviceFormat(192_000, 24), prefetched.Resolution.Target);

        // The predicted track arrives: the buffer answers instantly, with no new search round trip
        // (and therefore none of the bounded wait a fresh lookup could burn).
        var searchCallsSoFar = client.SearchCallCount;
        var second = layer.TryResolve(AlbumTrack("Track Two"));

        Assert.Equal(new DeviceFormat(192_000, 24), second!.Target);
        Assert.Equal(ResolutionConfidence.Exact, second.Confidence);
        Assert.True(second.IsLossless);
        Assert.Equal(searchCallsSoFar, client.SearchCallCount);
    }

    [Fact]
    public void A_buffer_hit_chains_the_prefetch_to_the_following_track_reusing_the_already_fetched_album_list()
    {
        var client = AlbumPlaybackClient();
        var layer = new CatalogFormatResolverLayer(client, new FakeStorefrontProvider("us"));

        var prefetchedIds = new List<string>();
        using var prefetchSignal = new SemaphoreSlim(0);
        layer.NextTrackPrefetched += (_, args) =>
        {
            lock (prefetchedIds)
            {
                prefetchedIds.Add(args.NextTrack.Id);
            }

            prefetchSignal.Release();
        };

        layer.TryResolve(AlbumTrack("Track One"));
        Assert.True(prefetchSignal.Wait(TimeSpan.FromSeconds(5)), "expected the first prefetch (Track Two)");

        layer.TryResolve(AlbumTrack("Track Two")); // buffer hit — should chain a prefetch of Track Three
        Assert.True(prefetchSignal.Wait(TimeSpan.FromSeconds(5)), "expected the chained prefetch (Track Three)");

        lock (prefetchedIds)
        {
            Assert.Equal(["id-2", "id-3"], prefetchedIds);
        }

        // The album list came along with the buffered entry, so the chained prefetch needed no
        // second album lookup — and the now-buffered third track resolves without a search.
        Assert.Equal(1, client.AlbumTracksCallCount);
        var searchCallsSoFar = client.SearchCallCount;
        var third = layer.TryResolve(AlbumTrack("Track Three"));

        Assert.Equal(ResolutionConfidence.Exact, third?.Confidence);
        Assert.Equal(searchCallsSoFar, client.SearchCallCount);
    }

    [Fact]
    public void The_buffered_prediction_is_ignored_for_a_track_it_does_not_match_which_resolves_via_the_normal_search_path()
    {
        var client = AlbumPlaybackClient();
        var layer = new CatalogFormatResolverLayer(client, new FakeStorefrontProvider("us"));
        using var signal = new ManualResetEventSlim(false);
        layer.NextTrackPrefetched += (_, _) => signal.Set();

        layer.TryResolve(AlbumTrack("Track One"));
        Assert.True(signal.Wait(TimeSpan.FromSeconds(5)));
        var searchCallsSoFar = client.SearchCallCount;

        // The user shuffled/jumped: this is not the predicted "Track Two", so the conservative
        // matcher must reject the buffer and a fresh search must run instead.
        var result = layer.TryResolve(new Track("Somewhere Else Entirely", "Artist", "Album"));

        Assert.Equal(searchCallsSoFar + 1, client.SearchCallCount);
        Assert.Equal(new DeviceFormat(96_000, 24), result?.Target);
    }

    [Fact]
    public void The_buffer_matches_through_the_same_normalization_as_the_search_path()
    {
        var client = AlbumPlaybackClient();
        // The catalog titles the next track with a feat. suffix that SMTC won't report.
        client.AlbumTracksImpl = (_, _, _, _) => Task.FromResult<IReadOnlyList<CatalogSearchCandidate>>(
        [
            new("id-1", "Track One", "Artist", "Album"),
            new("id-2", "Track Two (feat. Guest)", "Artist", "Album"),
        ]);
        var layer = new CatalogFormatResolverLayer(client, new FakeStorefrontProvider("us"));
        using var signal = new ManualResetEventSlim(false);
        layer.NextTrackPrefetched += (_, _) => signal.Set();

        layer.TryResolve(AlbumTrack("Track One"));
        Assert.True(signal.Wait(TimeSpan.FromSeconds(5)));
        var searchCallsSoFar = client.SearchCallCount;

        var second = layer.TryResolve(AlbumTrack("Track Two"));

        Assert.Equal(new DeviceFormat(192_000, 24), second?.Target);
        Assert.Equal(searchCallsSoFar, client.SearchCallCount);
    }

    [Fact]
    public void A_failing_prefetch_is_swallowed_and_the_next_track_just_takes_the_normal_lookup_path()
    {
        var client = new FakeCatalogClient
        {
            AlbumTracksImpl = (_, _, _, _) => throw new HttpRequestException("album lookup broke"),
        };
        var layer = new CatalogFormatResolverLayer(client, new FakeStorefrontProvider("us"));

        var first = layer.TryResolve(SampleTrack("Track One"));
        var second = layer.TryResolve(SampleTrack("Track Two"));

        Assert.Equal(ResolutionConfidence.Exact, first?.Confidence);
        Assert.Equal(ResolutionConfidence.Exact, second?.Confidence);
        Assert.False(layer.IsDisabledForSession);
    }

    // --- Album track list for the dashboard ---

    [Fact]
    public void A_successful_resolve_raises_AlbumTracksAvailable_with_the_whole_album_list()
    {
        var client = AlbumPlaybackClient();
        var layer = new CatalogFormatResolverLayer(client, new FakeStorefrontProvider("us"));

        AlbumTracksAvailableEventArgs? albumArgs = null;
        using var signal = new ManualResetEventSlim(false);
        layer.AlbumTracksAvailable += (_, args) =>
        {
            albumArgs = args;
            signal.Set();
        };

        layer.TryResolve(AlbumTrack("Track One"));

        Assert.True(signal.Wait(TimeSpan.FromSeconds(5)), "expected the album track list to be published");
        Assert.Equal(["id-1", "id-2", "id-3"], albumArgs!.AlbumTracks.Select(track => track.Id));
    }

    [Fact]
    public void AlbumTracksAvailable_is_raised_for_the_albums_last_track_even_though_there_is_nothing_left_to_prefetch()
    {
        var client = AlbumPlaybackClient();
        client.SearchImpl = (_, _, track, _) => Task.FromResult<IReadOnlyList<CatalogSearchCandidate>>(
            [new CatalogSearchCandidate("id-3", track.Title, track.Artist, track.Album)]);
        var layer = new CatalogFormatResolverLayer(client, new FakeStorefrontProvider("us"));

        using var signal = new ManualResetEventSlim(false);
        layer.AlbumTracksAvailable += (_, _) => signal.Set();

        layer.TryResolve(AlbumTrack("Track Three"));

        Assert.True(signal.Wait(TimeSpan.FromSeconds(5)), "expected the album track list even at the album's end");
    }

    [Fact]
    public void AlbumTracksAvailable_is_not_raised_when_the_album_holds_no_other_songs()
    {
        var client = AlbumPlaybackClient();
        client.AlbumTracksImpl = (_, _, _, _) => Task.FromResult<IReadOnlyList<CatalogSearchCandidate>>(
            [new CatalogSearchCandidate("id-1", "Track One", "Artist", "Album")]);
        var layer = new CatalogFormatResolverLayer(client, new FakeStorefrontProvider("us"));

        using var signal = new ManualResetEventSlim(false);
        layer.AlbumTracksAvailable += (_, _) => signal.Set();

        layer.TryResolve(AlbumTrack("Track One"));

        // A single-track "list" has nothing to show; the event staying silent is the contract.
        Assert.False(signal.Wait(TimeSpan.FromMilliseconds(750)));
    }
}
