using Samklang.Domain;
using Samklang.Sessions;
using Xunit;

namespace Samklang.Tests.Sessions;

public class TransientTrackDetectorTests
{
    [Theory]
    [InlineData("Connecting…", "")]
    [InlineData("REVNOIR & Similar Artists", "")]
    [InlineData("Relax", "")] // a real-looking title can still be a placeholder if the artist is empty
    [InlineData("", "")]
    [InlineData("Title", "   ")] // whitespace-only artist is just as empty as ""
    [InlineData("   ", "Artist")] // whitespace-only title likewise
    public void IsTransient_is_true_when_title_or_artist_is_empty_or_whitespace(string title, string artist)
    {
        Assert.True(TransientTrackDetector.IsTransient(new Track(title, artist, "Album")));
    }

    [Fact]
    public void IsTransient_is_false_for_a_complete_track()
    {
        Assert.False(TransientTrackDetector.IsTransient(new Track("Alive", "Sia", "This Is Acting")));
    }

    [Fact]
    public void IsTransient_is_false_when_only_the_album_is_empty()
    {
        // Album is frequently empty on legitimate live tracks (Apple Music often reports it via
        // the artist-join that SmtcTrackMetadataParser undoes); only Title/Artist gate transience.
        Assert.False(TransientTrackDetector.IsTransient(new Track("Alive", "Sia", "")));
    }
}
