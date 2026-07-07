using Samklang.Sessions;
using Xunit;

namespace Samklang.Tests.Sessions;

public class StaleRefreshGuardTests
{
    [Fact]
    public void A_single_refresh_applies_its_result()
    {
        var guard = new StaleRefreshGuard();
        var applied = false;

        var token = guard.Begin();
        var ran = guard.TryApply(token, () => applied = true);

        Assert.True(ran);
        Assert.True(applied);
    }

    [Fact]
    public void A_refresh_superseded_by_a_newer_one_is_discarded()
    {
        var guard = new StaleRefreshGuard();
        var olderApplied = false;

        var older = guard.Begin();
        guard.Begin();
        var ran = guard.TryApply(older, () => olderApplied = true);

        Assert.False(ran);
        Assert.False(olderApplied);
    }

    [Fact]
    public void The_newest_refresh_still_applies_after_older_ones_began()
    {
        var guard = new StaleRefreshGuard();
        var newestApplied = false;

        guard.Begin();
        guard.Begin();
        var newest = guard.Begin();
        var ran = guard.TryApply(newest, () => newestApplied = true);

        Assert.True(ran);
        Assert.True(newestApplied);
    }

    [Fact]
    public void A_token_can_apply_multiple_times_until_superseded()
    {
        // RefreshTrackAsync applies twice per refresh: the track right after the properties read,
        // and the artwork after the slower thumbnail read. Both must go through while the refresh
        // is still the newest.
        var guard = new StaleRefreshGuard();
        var applyCount = 0;

        var token = guard.Begin();
        guard.TryApply(token, () => applyCount++);
        guard.TryApply(token, () => applyCount++);

        Assert.Equal(2, applyCount);
    }

    [Fact]
    public void Issue_30_scenario_a_slow_stale_artwork_read_cannot_overwrite_the_current_tracks_artwork()
    {
        // Models the bug: MediaPropertiesChanged fires for the previous track's refresh (A), then
        // again for the current track (B). B's artwork read finishes first; A's slow read
        // completes afterwards and must be discarded, not applied over B's.
        var guard = new StaleRefreshGuard();
        string? artwork = null;

        var refreshA = guard.Begin();
        var refreshB = guard.Begin();

        Assert.True(guard.TryApply(refreshB, () => artwork = "current track"));
        Assert.False(guard.TryApply(refreshA, () => artwork = "previous track"));

        Assert.Equal("current track", artwork);
    }

    [Fact]
    public void Supersede_applies_immediately_and_discards_in_flight_refreshes()
    {
        // Models the session disappearing: the clear must take effect now, and a refresh still
        // awaiting its artwork read must not resurrect the dead session's state afterwards.
        var guard = new StaleRefreshGuard();
        string? state = "stale";

        var inFlight = guard.Begin();
        guard.Supersede(() => state = null);
        var ran = guard.TryApply(inFlight, () => state = "resurrected");

        Assert.False(ran);
        Assert.Null(state);
    }

    [Fact]
    public void A_refresh_begun_after_supersede_applies_normally()
    {
        var guard = new StaleRefreshGuard();
        string? state = null;

        guard.Supersede(() => state = null);
        var token = guard.Begin();
        var ran = guard.TryApply(token, () => state = "fresh");

        Assert.True(ran);
        Assert.Equal("fresh", state);
    }

    [Fact]
    public async Task Concurrent_refreshes_from_many_threads_leave_only_the_newest_applied()
    {
        var guard = new StaleRefreshGuard();
        var appliedTokens = new List<int>();

        await Task.WhenAll(Enumerable.Range(0, 32).Select(_ => Task.Run(() =>
        {
            var token = guard.Begin();
            guard.TryApply(token, () => appliedTokens.Add(token));
        })));

        // Applies may be sparse (any token superseded before its TryApply is skipped), but they
        // must be strictly increasing — a stale token can never apply after a newer one did —
        // and the very last Begin() must always have applied.
        Assert.Equal(appliedTokens, appliedTokens.OrderBy(token => token).ToList());
        Assert.Equal(32, appliedTokens[^1]);
    }
}
