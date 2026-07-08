using Samklang.Domain;
using Samklang.Logging;

namespace Samklang.Resolver.Catalog;

/// <summary>
/// The top layer of the Layered Resolver (docs/adr/0001): resolves a Track's true sample
/// rate/bit depth at <see cref="ResolutionConfidence.Exact"/> via the anonymous Apple Music
/// web-player token, a catalog search match, and enhanced-HLS manifest parsing.
///
/// <para>
/// <b>Bounded-latency strategy</b> (issue #6 acceptance criteria: "a slow catalog never delays
/// switching beyond a bounded budget"). <see cref="IFormatResolverLayer.TryResolve"/> is a
/// synchronous, chain-of-responsibility contract shared with every other layer, so this layer
/// runs its network pipeline asynchronously underneath but only *waits* on it synchronously for
/// up to <see cref="_resolveTimeout"/> (default 2.5s) before giving up for that call and
/// returning null — the chain then falls through to a lower-confidence layer immediately, so a
/// stalled network never blocks a track switch beyond that bound. Crucially, the in-flight lookup
/// is not abandoned when the caller stops waiting: it keeps running (capped at
/// <see cref="_backgroundCeiling"/>, default 10s, so it can't run forever either) against a
/// per-Track result cache, so
/// <list type="bullet">
/// <item>duplicate/concurrent calls for the same Track collapse onto one network round trip, and</item>
/// <item>if the lookup completes after the caller already gave up, a successful result is cached
/// for the next <c>TryResolve</c> call for that Track (failures are deliberately not cached —
/// see <see cref="Finalize"/> — so replaying a track that hit a transient error retries the
/// catalog instead of being a permanent miss for the session), <i>and</i> pushed out via
/// <see cref="LateResolutionAvailable"/> so a subscriber (see
/// <c>TrackSyncCoordinator.ApplyLateResolution</c>) can correct an already-applied
/// lower-confidence switch to Exact — but only while the same Track is still current.</item>
/// </list>
/// This is the "lower layer applies first, corrected when Exact arrives later" strategy the issue
/// calls out explicitly, implemented without making the shared <see cref="IFormatResolverLayer"/>
/// contract asynchronous.
/// </para>
///
/// <para>
/// <b>Next-track prefetch buffer.</b> SMTC exposes no play queue, so the literal next track can't
/// be read anywhere — but it can be predicted: albums usually play in order. After every
/// successful lookup this layer fetches the matched song's album track list in the background
/// (<see cref="IAppleMusicCatalogClient.FetchAlbumTracksAsync"/>), resolves the manifest of the
/// track that follows the current one, and parks the result in a one-slot buffer. When the next
/// <see cref="TryResolve"/> call's Track matches the prediction (scored by
/// <see cref="CatalogTrackMatcher"/>, the same conservative gate the search path uses), the
/// buffered resolution is returned instantly — no network round trip and no bounded wait, so the
/// device switch lands right at the track boundary — and the track *after* that one is prefetched
/// in turn, reusing the already-fetched album list. A miss (shuffle, a different album, a
/// playlist jump) simply falls through to the normal lookup path, and any prefetch failure is
/// swallowed: prefetching is purely opportunistic and must never make anything worse.
/// </para>
///
/// <para>
/// <b>Failure handling</b> (ADR-0001: "must degrade gracefully... never hard-fail"). Failing to
/// obtain a usable token at all — the anonymous scrape or amp-api auth is broken, or (very
/// plausibly, per the 2026-07-08 handoff) the app autostarted before the network was up, or
/// music.apple.com hiccuped once — puts the layer into a cooldown per
/// <see cref="TokenFailureBackoffSchedule"/> instead of disabling it for the rest of the process:
/// retrying every single track would waste the resolve budget for nothing, but a fixed, bounded
/// wait means a transient blip recovers within the session instead of requiring a restart. Each
/// further consecutive failure grows the cooldown (capped at the schedule's last entry); any
/// success resets it. Failures further down the pipeline for one specific Track (search miss,
/// manifest fetch/parse failure, timeout) only fail that Track; the layer stays enabled and tries
/// again for the next one.
/// </para>
/// </summary>
public sealed class CatalogFormatResolverLayer : IFormatResolverLayer
{
    public static readonly TimeSpan DefaultResolveTimeout = TimeSpan.FromSeconds(2.5);
    public static readonly TimeSpan DefaultBackgroundCeiling = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan TokenRefreshBuffer = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Cooldown applied after a token-fetch failure, indexed by consecutive-failure count (the
    /// 1st failure uses index 0, the 2nd index 1, ...) and clamped to the last entry for any
    /// failure beyond the schedule's length. Short at first, since the most likely real-world
    /// cause is transient (the app autostarting before the network is up, one bad response from
    /// Apple's edge) and should clear within a minute; the last step is long enough that a
    /// genuinely broken scrape (Apple reworked the web player) doesn't hammer music.apple.com
    /// every track, without ever requiring a restart to recover once the underlying problem
    /// clears.
    /// </summary>
    public static readonly IReadOnlyList<TimeSpan> TokenFailureBackoffSchedule =
    [
        TimeSpan.FromSeconds(30),
        TimeSpan.FromMinutes(2),
        TimeSpan.FromMinutes(10),
        TimeSpan.FromMinutes(30),
    ];

    /// <summary>
    /// Ceiling on <see cref="_completed"/> so a long-running tray session playing thousands of
    /// distinct Tracks can't grow the cache forever. Hitting it just clears the cache: entries
    /// only save one bounded network round trip each, so wholesale eviction is cheap, and anything
    /// smarter (LRU) isn't worth the bookkeeping here.
    /// </summary>
    private const int CompletedCacheCapacity = 512;

    private readonly IAppleMusicCatalogClient _client;
    private readonly IStorefrontProvider _storefrontProvider;
    private readonly TimeSpan _resolveTimeout;
    private readonly TimeSpan _backgroundCeiling;
    private readonly Func<DateTimeOffset> _now;

    private readonly object _gate = new();
    private readonly Dictionary<Track, Task<FormatResolution?>> _inFlight = new();
    private readonly Dictionary<Track, FormatResolution> _completed = new();
    private AppleMusicToken? _cachedToken;

    /// <summary>The one-slot next-track buffer: the predicted next Track's already-resolved format, or null.</summary>
    private PrefetchedNextTrack? _prefetched;

    /// <summary>
    /// The catalog id whose "next track" the buffer currently holds or is being filled for —
    /// dedupes prefetch kicks (SMTC can re-fire for the same Track) and lets a stale, slow
    /// prefetch detect it has been superseded before overwriting a newer buffer.
    /// </summary>
    private string? _prefetchAnchorId;

    // Guarded by _gate; written from a background lookup thread, read by every TryResolve caller.
    private int _consecutiveTokenFailures;
    private DateTimeOffset? _cooldownUntilUtc;

    /// <summary>
    /// True while a token-fetch failure's cooldown (see <see cref="TokenFailureBackoffSchedule"/>)
    /// is still in effect — <see cref="TryResolve"/> returns null immediately without touching the
    /// network until it elapses. The name predates bounded retry (when this really was a
    /// permanent, whole-process disable) and is kept for callers/tests already written against
    /// it, but this is no longer one-way: once the cooldown passes, the next <see cref="TryResolve"/>
    /// call tries the network again.
    /// </summary>
    public bool IsDisabledForSession
    {
        get
        {
            lock (_gate)
            {
                return _cooldownUntilUtc is { } cooldown && _now() < cooldown;
            }
        }
    }

    public CatalogFormatResolverLayer(
        IAppleMusicCatalogClient client,
        IStorefrontProvider storefrontProvider,
        TimeSpan? resolveTimeout = null,
        TimeSpan? backgroundCeiling = null,
        Func<DateTimeOffset>? now = null)
    {
        _client = client;
        _storefrontProvider = storefrontProvider;
        _resolveTimeout = resolveTimeout ?? DefaultResolveTimeout;
        _backgroundCeiling = backgroundCeiling ?? DefaultBackgroundCeiling;
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    public string Name => "Catalog match";

    /// <summary>
    /// Raised when a lookup completes *after* the <see cref="TryResolve"/> call that started it
    /// already gave up on the bounded wait. Subscribers should check the Track is still current
    /// before applying the correction.
    /// </summary>
    public event EventHandler<LateResolutionEventArgs>? LateResolutionAvailable;

    /// <summary>
    /// Raised when the predicted next Track's format has been prefetched into the buffer, mainly
    /// so tests (and, later, a dashboard "up next" hint) can observe the otherwise-background
    /// prefetch completing.
    /// </summary>
    public event EventHandler<NextTrackPrefetchedEventArgs>? NextTrackPrefetched;

    /// <summary>
    /// Raised when the current Track's album track list is in hand (fetched — or reused from the
    /// prefetch buffer — for next-track prediction anyway) and it holds more than just the
    /// current track. Lets the dashboard show the album's other songs without any lookup of its
    /// own. Fires from the background prefetch thread; subscribers marshal to the UI themselves.
    /// </summary>
    public event EventHandler<AlbumTracksAvailableEventArgs>? AlbumTracksAvailable;

    public FormatResolution? TryResolve(Track track)
    {
        // SMTC transient states (session attaching, placeholder titles like "Connecting…") carry
        // an empty artist or title. CatalogTrackMatcher rejects such Tracks unconditionally, so
        // a search could never produce a match — skip the network round trip entirely rather than
        // burn the resolve budget on a guaranteed miss.
        if (string.IsNullOrWhiteSpace(track.Title) || string.IsNullOrWhiteSpace(track.Artist))
        {
            LogTransientMetadataSkip(track);
            return null;
        }

        if (IsDisabledForSession)
        {
            return null;
        }

        Task<FormatResolution?> task = null!;
        PrefetchedNextTrack? bufferHit;
        lock (_gate)
        {
            if (_completed.TryGetValue(track, out var cached))
            {
                return cached;
            }

            bufferHit = TakeMatchingPrefetchLocked(track);
            if (bufferHit is not null)
            {
                // Promote to the per-Track cache under the *actual* SMTC key, so replays of this
                // track hit the ordinary cache path from now on.
                CacheCompletedLocked(track, bufferHit.Resolution);
            }
            else if (!_inFlight.TryGetValue(track, out task!))
            {
                task = ResolveCoreSafeAsync(track);
                _inFlight[track] = task;
            }
        }

        if (bufferHit is not null)
        {
            // The prediction landed: answer instantly, and keep the buffer one step ahead by
            // prefetching the track after this one — the album list is already in hand.
            StartNextTrackPrefetch(bufferHit.AlbumTracks[bufferHit.Index].Id, bufferHit.AlbumTracks);
            return bufferHit.Resolution;
        }

        if (task.Wait(_resolveTimeout))
        {
            // ResolveCoreSafeAsync never throws, so Result is always safe to read here.
            var result = task.Result;
            Finalize(track, result, alreadyWaitedOut: false);
            return result;
        }

        // Gave up for this call — the chain falls through to a lower layer right away. The task
        // keeps running (bounded by _backgroundCeiling); when it finishes, cache the result and
        // tell subscribers in case the same Track is still current.
        _ = task.ContinueWith(
            completed => Finalize(track, completed.Result, alreadyWaitedOut: true),
            TaskScheduler.Default);

        return null;
    }

    /// <summary>
    /// The last Track whose empty-metadata skip was logged, so SMTC re-firing the same transient
    /// state (it does, several times per track change) writes one log line instead of spamming.
    /// </summary>
    private Track? _lastTransientMetadataSkip;

    private void LogTransientMetadataSkip(Track track)
    {
        lock (_gate)
        {
            if (_lastTransientMetadataSkip == track)
            {
                return;
            }

            _lastTransientMetadataSkip = track;
        }

        AppLog.Info($"Catalog: skipping lookup for \"{track.Title}\" — {track.Artist} (empty title/artist is transient SMTC metadata; no search could match).");
    }

    private void Finalize(Track track, FormatResolution? result, bool alreadyWaitedOut)
    {
        bool alreadyDelivered;
        lock (_gate)
        {
            alreadyDelivered = _completed.ContainsKey(track);

            // Only successes are cached. A null result can be a transient failure (network blip,
            // timeout) just as easily as a genuine catalog miss, and caching it would make one
            // blip a permanent miss for that Track for the whole session — replaying the track
            // later should get a fresh try. The retry is bounded the same way every lookup is
            // (one per TryResolve call, capped at _backgroundCeiling), so this can't run away.
            if (result is not null)
            {
                CacheCompletedLocked(track, result);
            }

            _inFlight.Remove(track);
        }

        if (alreadyWaitedOut && !alreadyDelivered && result is not null)
        {
            LateResolutionAvailable?.Invoke(this, new LateResolutionEventArgs(track, result));
        }
    }

    /// <summary>Caller must hold <see cref="_gate"/>. See <see cref="CompletedCacheCapacity"/> for the eviction rationale.</summary>
    private void CacheCompletedLocked(Track track, FormatResolution result)
    {
        if (_completed.Count >= CompletedCacheCapacity && !_completed.ContainsKey(track))
        {
            _completed.Clear();
        }

        _completed[track] = result;
    }

    /// <summary>
    /// Caller must hold <see cref="_gate"/>. Consumes and returns the buffered prefetch when
    /// <paramref name="track"/> is the Track it predicted — scored by
    /// <see cref="CatalogTrackMatcher"/>, the same conservative normalized title+artist gate the
    /// search path trusts for Exact confidence — or returns null (buffer untouched) otherwise.
    /// </summary>
    private PrefetchedNextTrack? TakeMatchingPrefetchLocked(Track track)
    {
        if (_prefetched is not { } buffered ||
            CatalogTrackMatcher.FindBestMatch(track, [buffered.AlbumTracks[buffered.Index]]) is null)
        {
            return null;
        }

        _prefetched = null;
        return buffered;
    }

    private async Task<FormatResolution?> ResolveCoreSafeAsync(Track track)
    {
        using var cts = new CancellationTokenSource(_backgroundCeiling);

        AppleMusicToken token;
        try
        {
            token = await GetOrRefreshTokenAsync(cts.Token).ConfigureAwait(false);
        }
        catch
        {
            // GetOrRefreshTokenAsync already registered the failure and started the cooldown
            // (RegisterTokenFailure) before rethrowing — this call just needs to fail gracefully
            // so the chain falls through to a lower layer for this Track.
            return null;
        }

        try
        {
            return await ResolveWithTokenAsync(track, token, cts.Token).ConfigureAwait(false);
        }
        catch
        {
            // Per-track failure only (search miss, manifest fetch/parse issue, transient network
            // error); the layer stays enabled and will try again for the next track.
            return null;
        }
    }

    private async Task<AppleMusicToken> GetOrRefreshTokenAsync(CancellationToken cancellationToken)
    {
        var current = _cachedToken;
        if (current is not null && current.IsFreshAt(_now(), TokenRefreshBuffer))
        {
            return current;
        }

        AppLog.Info("Catalog: fetching developer token.");
        try
        {
            var fetched = await _client.FetchTokenAsync(cancellationToken).ConfigureAwait(false);
            _cachedToken = fetched;
            RegisterTokenSuccess();
            AppLog.Info($"Catalog: token fetch succeeded (expires {fetched.ExpiresAtUtc:u}).");
            return fetched;
        }
        catch (Exception ex)
        {
            RegisterTokenFailure(ex);
            throw;
        }
    }

    /// <summary>Caller must not hold <see cref="_gate"/> (acquires it itself). Resets the failure streak and clears any active cooldown.</summary>
    private void RegisterTokenSuccess()
    {
        int priorFailures;
        lock (_gate)
        {
            priorFailures = _consecutiveTokenFailures;
            _consecutiveTokenFailures = 0;
            _cooldownUntilUtc = null;
        }

        if (priorFailures > 0)
        {
            AppLog.Info($"Catalog: token fetch recovered after {priorFailures} consecutive failure(s) — cooldown cleared.");
        }
    }

    /// <summary>Caller must not hold <see cref="_gate"/> (acquires it itself). Grows the failure streak and sets a cooldown per <see cref="TokenFailureBackoffSchedule"/>.</summary>
    private void RegisterTokenFailure(Exception exception)
    {
        int failureCount;
        TimeSpan backoff;
        lock (_gate)
        {
            _consecutiveTokenFailures++;
            failureCount = _consecutiveTokenFailures;
            var stepIndex = Math.Min(failureCount, TokenFailureBackoffSchedule.Count) - 1;
            backoff = TokenFailureBackoffSchedule[stepIndex];
            _cooldownUntilUtc = _now() + backoff;
        }

        AppLog.Warn($"Catalog: token fetch failed ({failureCount} consecutive) — cooling down for {backoff}. {exception.GetType().Name}: {exception.Message}");
    }

    private async Task<FormatResolution?> ResolveWithTokenAsync(
        Track track, AppleMusicToken token, CancellationToken cancellationToken)
    {
        var storefront = _storefrontProvider.GetStorefront();

        var candidates = await _client.SearchTracksAsync(storefront, token.Value, track, cancellationToken)
            .ConfigureAwait(false);
        var match = CatalogTrackMatcher.FindBestMatch(track, candidates);
        if (match is null)
        {
            // Include the top few candidates so a mismatch is diagnosable from the log alone —
            // the 2026-07-08 "Artist — Album" SMTC bug was only findable because the raw strings
            // happened to appear in this line.
            var topCandidates = string.Join("; ", candidates.Take(3).Select(c => $"\"{c.Title}\" by {c.Artist}"));
            var candidatesSuffix = topCandidates.Length > 0 ? $"; top: {topCandidates}" : string.Empty;
            AppLog.Info($"Catalog: no confident match for \"{track.Title}\" — {track.Artist} ({candidates.Count} search candidate(s){candidatesSuffix}).");
            return null;
        }

        // The matched candidate isn't necessarily the best-quality one: Apple often carries the
        // same song across several editions of the same album (standard vs. "Deluxe"/"Anniversary
        // Edition") with different masters — see CatalogTrackMatcher's class doc for the "Alive"
        // case that motivated this. Check the matched candidate's manifest first (the common case,
        // and the only fetch when there are no siblings), then any same-album-family siblings, and
        // keep whichever asset is actually the highest quality. Bounded by how many same-family
        // editions Apple's own search surfaced in the top 10 — typically zero or one extra.
        var winner = match;
        var winningFormat = await FetchLosslessFormatAsync(storefront, match, token, cancellationToken).ConfigureAwait(false);

        foreach (var sibling in CatalogTrackMatcher.FindAlbumSiblings(track, candidates, match))
        {
            var siblingFormat = await FetchLosslessFormatAsync(storefront, sibling, token, cancellationToken).ConfigureAwait(false);
            if (siblingFormat is { } candidateFormat && (winningFormat is not { } currentBest || IsHigherQuality(candidateFormat, currentBest)))
            {
                winner = sibling;
                winningFormat = candidateFormat;
            }
        }

        if (winningFormat is not { } format)
        {
            AppLog.Info($"Catalog: matched \"{track.Title}\" — {track.Artist} (catalog id {match.Id}) but it (and any same-album-family sibling) has no lossless (ALAC) enhanced-HLS asset.");
            return null;
        }

        AppLog.Info($"Catalog: matched \"{track.Title}\" — {track.Artist} → id {winner.Id} \"{winner.Album}\" ({format.SampleRateHz} Hz / {format.BitDepth}-bit).");

        // This Track resolved, so it's the best available anchor for predicting the next one —
        // fill the buffer in the background. Runs for prompt and late successes alike (both end
        // up here), which is exactly right: even a late-arriving current-track resolution makes
        // the *next* track's prediction worth having ready. Anchored on the winning edition, not
        // necessarily the initially-ranked match, since that's the album Apple is actually
        // streaming from and therefore the one the next track is likely to come from too.
        StartNextTrackPrefetch(winner.Id, knownAlbumTracks: null);

        // ParseBestLosslessFormat only ever matches ALAC variants (see its AUDIO-FORMAT filter),
        // so a result here is always genuinely lossless — IsLossless: true, not left null.
        return new FormatResolution(format, ResolutionConfidence.Exact, Name, IsLossless: true);
    }

    private async Task<DeviceFormat?> FetchLosslessFormatAsync(
        string storefront, CatalogSearchCandidate candidate, AppleMusicToken token, CancellationToken cancellationToken)
    {
        var manifest = await _client.FetchEnhancedHlsManifestAsync(storefront, candidate.Id, token.Value, cancellationToken)
            .ConfigureAwait(false);
        return manifest is null ? null : EnhancedHlsManifestParser.ParseBestLosslessFormat(manifest);
    }

    private static bool IsHigherQuality(DeviceFormat candidate, DeviceFormat current) =>
        candidate.SampleRateHz > current.SampleRateHz ||
        (candidate.SampleRateHz == current.SampleRateHz && candidate.BitDepth > current.BitDepth);

    /// <summary>
    /// Kicks off the background fill of the next-track buffer, predicting "the album track after
    /// <paramref name="currentCatalogTrackId"/>". Deduped per anchor id, so SMTC re-firing the
    /// same Track doesn't spawn duplicate lookups. Fire-and-forget by design:
    /// <see cref="PrefetchNextTrackAsync"/> swallows every failure and is bounded by
    /// <see cref="_backgroundCeiling"/>, mirroring how background lookups are already handled.
    /// </summary>
    private void StartNextTrackPrefetch(string currentCatalogTrackId, IReadOnlyList<CatalogSearchCandidate>? knownAlbumTracks)
    {
        lock (_gate)
        {
            if (_prefetchAnchorId == currentCatalogTrackId)
            {
                return;
            }

            _prefetchAnchorId = currentCatalogTrackId;
        }

        _ = PrefetchNextTrackAsync(currentCatalogTrackId, knownAlbumTracks);
    }

    private async Task PrefetchNextTrackAsync(string currentCatalogTrackId, IReadOnlyList<CatalogSearchCandidate>? albumTracks)
    {
        try
        {
            using var cts = new CancellationTokenSource(_backgroundCeiling);
            var token = await GetOrRefreshTokenAsync(cts.Token).ConfigureAwait(false);
            var storefront = _storefrontProvider.GetStorefront();

            albumTracks ??= await _client.FetchAlbumTracksAsync(storefront, currentCatalogTrackId, token.Value, cts.Token)
                .ConfigureAwait(false);

            var currentIndex = IndexOfTrackId(albumTracks, currentCatalogTrackId);

            // The album list is a dashboard-worthy byproduct even when there's nothing left to
            // predict from it (e.g. the album's last track) — but only when the anchor is
            // actually in it (a list without it is suspect) and it has other songs to show.
            if (currentIndex >= 0 && albumTracks.Count > 1)
            {
                AlbumTracksAvailable?.Invoke(this, new AlbumTracksAvailableEventArgs(albumTracks));
            }

            if (currentIndex < 0 || currentIndex + 1 >= albumTracks.Count)
            {
                return; // last album track (or not found at all) — nothing to predict
            }

            var next = albumTracks[currentIndex + 1];
            var manifest = await _client.FetchEnhancedHlsManifestAsync(storefront, next.Id, token.Value, cts.Token)
                .ConfigureAwait(false);
            if (manifest is null)
            {
                return;
            }

            var format = EnhancedHlsManifestParser.ParseBestLosslessFormat(manifest);
            if (format is null)
            {
                return;
            }

            var resolution = new FormatResolution(format.Value, ResolutionConfidence.Exact, Name, IsLossless: true);
            lock (_gate)
            {
                if (_prefetchAnchorId != currentCatalogTrackId)
                {
                    return; // a newer track's prefetch superseded this one while it was in flight
                }

                _prefetched = new PrefetchedNextTrack(albumTracks, currentIndex + 1, resolution);
            }

            NextTrackPrefetched?.Invoke(this, new NextTrackPrefetchedEventArgs(next, resolution));
        }
        catch
        {
            // Prefetching is purely opportunistic — any failure (album lookup, manifest, parse,
            // timeout) just means the next track takes the normal lookup path.
        }
    }

    private static int IndexOfTrackId(IReadOnlyList<CatalogSearchCandidate> tracks, string id)
    {
        for (var i = 0; i < tracks.Count; i++)
        {
            if (tracks[i].Id == id)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// The one-slot next-track buffer's contents: the album's ordered track list, the index of
    /// the predicted (already-resolved) next track within it, and that track's Format Resolution.
    /// Carrying the whole list lets a buffer hit chain straight into prefetching the *following*
    /// track without re-fetching the album.
    /// </summary>
    private sealed record PrefetchedNextTrack(
        IReadOnlyList<CatalogSearchCandidate> AlbumTracks,
        int Index,
        FormatResolution Resolution);
}

/// <summary>A Format Resolution that arrived after the <see cref="CatalogFormatResolverLayer"/> caller had already given up waiting.</summary>
public sealed record LateResolutionEventArgs(Track Track, FormatResolution Resolution);

/// <summary>The predicted next track (an album-order prediction, since SMTC exposes no play queue) whose Format Resolution is now buffered and ready.</summary>
public sealed record NextTrackPrefetchedEventArgs(CatalogSearchCandidate NextTrack, FormatResolution Resolution);

/// <summary>The current Track's album track list, in album order, as fetched for next-track prediction — the dashboard's album view feeds off this.</summary>
public sealed record AlbumTracksAvailableEventArgs(IReadOnlyList<CatalogSearchCandidate> AlbumTracks);
