using Samklang.Sessions;
using Xunit;

namespace Samklang.Tests.Sessions;

public class AppleMusicUiMatcherTests
{
    // Row names are the Apple Music album view's real accessibility strings, e.g.
    // "Track 9 Ticket to Ride 3 minutes, 10 seconds".
    private const string TicketToRideRow = "Track 9 Ticket to Ride 3 minutes, 10 seconds";

    [Fact]
    public void IsTrackRow_matches_the_row_with_the_right_number_and_title()
    {
        Assert.True(AppleMusicUiMatcher.IsTrackRow(TicketToRideRow, 9, "Ticket to Ride"));
    }

    [Fact]
    public void IsTrackRow_rejects_the_right_title_at_the_wrong_track_number()
    {
        // A same-titled reprise elsewhere on the album must not match this row.
        Assert.False(AppleMusicUiMatcher.IsTrackRow(TicketToRideRow, 3, "Ticket to Ride"));
    }

    [Fact]
    public void IsTrackRow_rejects_a_different_title_at_the_same_number()
    {
        Assert.False(AppleMusicUiMatcher.IsTrackRow(TicketToRideRow, 9, "Yesterday"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void IsTrackRow_is_false_for_blank_row_names(string? rowName)
    {
        Assert.False(AppleMusicUiMatcher.IsTrackRow(rowName, 1, "Anything"));
    }

    [Fact]
    public void RowMentionsTitle_is_the_title_only_fallback()
    {
        Assert.True(AppleMusicUiMatcher.RowMentionsTitle(TicketToRideRow, "Ticket to Ride"));
        Assert.False(AppleMusicUiMatcher.RowMentionsTitle(TicketToRideRow, "Hey Jude"));
        Assert.False(AppleMusicUiMatcher.RowMentionsTitle(TicketToRideRow, ""));
    }

    [Fact]
    public void IsMenuItem_matches_the_title_echoing_play_now_item_regardless_of_curly_quotes()
    {
        // The "play now" item echoes the song title (Apple wraps it in curly quotes), which the
        // matcher keys off so it works independent of the menu's language.
        Assert.True(AppleMusicUiMatcher.IsMenuItem("Play “Yesterday”", QueuePlacement.PlayNow, "Yesterday"));
        Assert.True(AppleMusicUiMatcher.IsMenuItem("Play \"Yesterday\"", QueuePlacement.PlayNow, "Yesterday"));
    }

    [Fact]
    public void IsMenuItem_does_not_treat_play_next_or_last_as_the_play_now_item()
    {
        Assert.False(AppleMusicUiMatcher.IsMenuItem("Play Next", QueuePlacement.PlayNow, "Yesterday"));
        Assert.False(AppleMusicUiMatcher.IsMenuItem("Play Last", QueuePlacement.PlayNow, "Yesterday"));
    }

    [Fact]
    public void IsMenuItem_matches_the_exact_queue_labels()
    {
        Assert.True(AppleMusicUiMatcher.IsMenuItem("Play Next", QueuePlacement.PlayNext, "Yesterday"));
        Assert.True(AppleMusicUiMatcher.IsMenuItem("Play Last", QueuePlacement.PlayLast, "Yesterday"));

        // and don't cross-match
        Assert.False(AppleMusicUiMatcher.IsMenuItem("Play Last", QueuePlacement.PlayNext, "Yesterday"));
        Assert.False(AppleMusicUiMatcher.IsMenuItem("Play Next", QueuePlacement.PlayLast, "Yesterday"));
        Assert.False(AppleMusicUiMatcher.IsMenuItem("Play “Yesterday”", QueuePlacement.PlayNext, "Yesterday"));
    }
}
