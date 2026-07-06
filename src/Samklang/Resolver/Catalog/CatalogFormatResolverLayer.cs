using Samklang.Domain;

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
/// <item>if the lookup completes after the caller already gave up, the result is cached for the
/// next <c>TryResolve</c> call for that Track, <i>and</i> pushed out via
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
/// <b>Failure handling</b> (ADR-0001: "must degrade gracefully... never hard-fail"). Failing to
/// obtain a usable token at all is treated as session-fatal — if the anonymous scrape or amp-api
/// auth is broken, retrying it per-track wastes the resolve budget for nothing, so this layer
/// disables itself for the rest of the process (a restart retries). Failures further down the
/// pipeline for one specific Track (search miss, manifest fetch/parse failure, timeout) only fail
/// that Track; the layer stays enabled and tries again for the next one.
/// </para>
/// </summary>
public sealed class CatalogFormatResolverLayer : IFormatResolverLayer
{
    public static readonly TimeSpan DefaultResolveTimeout = TimeSpan.FromSeconds(2.5);
    public static readonly TimeSpan DefaultBackgroundCeiling = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan TokenRefreshBuffer = TimeSpan.FromMinutes(5);

    private readonly IAppleMusicCatalogClient _client;
    private readonly IStorefrontProvider _storefrontProvider;
    private readonly TimeSpan _resolveTimeout;
    private readonly TimeSpan _backgroundCeiling;
    private readonly Func<DateTimeOffset> _now;

    private readonly object _gate = new();
    private readonly Dictionary<Track, Task<FormatResolution?>> _inFlight = new();
    private readonly Dictionary<Track, FormatResolution?> _completed = new();
    private AppleMusicToken? _cachedToken;

    /// <summary>
    /// Set once acquiring a token fails; from then on <see cref="TryResolve"/> returns null
    /// immediately without touching the network again for the rest of the process.
    /// </summary>
    public bool IsDisabledForSession { get; private set; }

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

    public FormatResolution? TryResolve(Track track)
    {
        if (IsDisabledForSession)
        {
            return null;
        }

        Task<FormatResolution?> task;
        lock (_gate)
        {
            if (_completed.TryGetValue(track, out var cached))
            {
                return cached;
            }

            if (!_inFlight.TryGetValue(track, out task!))
            {
                task = ResolveCoreSafeAsync(track);
                _inFlight[track] = task;
            }
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

    private void Finalize(Track track, FormatResolution? result, bool alreadyWaitedOut)
    {
        bool alreadyDelivered;
        lock (_gate)
        {
            alreadyDelivered = _completed.ContainsKey(track);
            _completed[track] = result;
            _inFlight.Remove(track);
        }

        if (alreadyWaitedOut && !alreadyDelivered && result is not null)
        {
            LateResolutionAvailable?.Invoke(this, new LateResolutionEventArgs(track, result));
        }
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
            // Can't get a token at all — treat as Apple having broken the scrape/amp-api surface
            // for this session (ADR-0001) rather than retrying every track.
            IsDisabledForSession = true;
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

        var fetched = await _client.FetchTokenAsync(cancellationToken).ConfigureAwait(false);
        _cachedToken = fetched;
        return fetched;
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
            return null;
        }

        var manifest = await _client.FetchEnhancedHlsManifestAsync(storefront, match.Id, token.Value, cancellationToken)
            .ConfigureAwait(false);
        if (manifest is null)
        {
            return null;
        }

        var format = EnhancedHlsManifestParser.ParseBestLosslessFormat(manifest);

        // ParseBestLosslessFormat only ever matches ALAC variants (see its AUDIO-FORMAT filter),
        // so a result here is always genuinely lossless — IsLossless: true, not left null.
        return format is null ? null : new FormatResolution(format.Value, ResolutionConfidence.Exact, Name, IsLossless: true);
    }
}

/// <summary>A Format Resolution that arrived after the <see cref="CatalogFormatResolverLayer"/> caller had already given up waiting.</summary>
public sealed record LateResolutionEventArgs(Track Track, FormatResolution Resolution);
