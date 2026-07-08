using Samklang.Domain;
using Samklang.Sessions;
using Xunit;

namespace Samklang.Tests.Sessions;

public class SmtcTrackMetadataParserTests
{
    // The exact shape observed live from Apple Music for Windows on 2026-07-08: Artist carries
    // "Artist — Album" (em dash U+2014 with surrounding spaces) and AlbumTitle is empty.
    [Fact]
    public void Parse_splits_artist_on_em_dash_when_album_is_empty()
    {
        var track = SmtcTrackMetadataParser.Parse("Bird Set Free", "Sia — This Is Acting", "");

        Assert.Equal(new Track("Bird Set Free", "Sia", "This Is Acting"), track);
    }

    [Fact]
    public void Parse_strips_the_album_suffix_from_artist_when_album_is_reported_separately()
    {
        var track = SmtcTrackMetadataParser.Parse(
            "Bird Set Free", "Sia — This Is Acting", "This Is Acting");

        Assert.Equal(new Track("Bird Set Free", "Sia", "This Is Acting"), track);
    }

    [Fact]
    public void Parse_tolerates_an_en_dash_separator()
    {
        var track = SmtcTrackMetadataParser.Parse("Hexagons", "Muse – The Wow! Signal", "");

        Assert.Equal(new Track("Hexagons", "Muse", "The Wow! Signal"), track);
    }

    // Plain hyphens are part of many legitimate artist names and must never be treated as the
    // separator — only the spaced em/en dash Apple Music actually emits is.
    [Theory]
    [InlineData("Jay-Z")]
    [InlineData("Twenty One Pilots - Special Edition Band")] // spaced hyphen, still not a separator
    [InlineData("t.A.T.u.")]
    public void Parse_leaves_artists_with_legitimate_dashes_intact(string artist)
    {
        var track = SmtcTrackMetadataParser.Parse("Song", artist, "");

        Assert.Equal(new Track("Song", artist, ""), track);
    }

    // An album whose own name contains the separator: the split happens at the FIRST separator,
    // so the album keeps its dash.
    [Fact]
    public void Parse_splits_at_the_first_separator_so_a_separator_inside_the_album_name_survives()
    {
        var track = SmtcTrackMetadataParser.Parse(
            "Song", "Artist — Album — The Sequel", "");

        Assert.Equal(new Track("Song", "Artist", "Album — The Sequel"), track);
    }

    // When the album is independently known, suffix-stripping (not blind splitting) repairs an
    // artist name that itself contains the separator.
    [Fact]
    public void Parse_prefers_suffix_match_over_blind_splitting_when_album_is_known()
    {
        var track = SmtcTrackMetadataParser.Parse(
            "Song", "Some — Band — The Album", "The Album");

        Assert.Equal(new Track("Song", "Some — Band", "The Album"), track);
    }

    // Album known but the artist doesn't end with "<sep>Album": could be a legitimate dash in
    // the artist's name, so nothing is stripped.
    [Fact]
    public void Parse_leaves_artist_intact_when_album_is_known_but_is_not_a_suffix()
    {
        var track = SmtcTrackMetadataParser.Parse(
            "Song", "Duo — Trio", "Completely Different Album");

        Assert.Equal(new Track("Song", "Duo — Trio", "Completely Different Album"), track);
    }

    [Fact]
    public void Parse_leaves_a_clean_artist_and_album_untouched()
    {
        var track = SmtcTrackMetadataParser.Parse("Bird Set Free", "Sia", "This Is Acting");

        Assert.Equal(new Track("Bird Set Free", "Sia", "This Is Acting"), track);
    }

    // SMTC transient states report empty strings (and TryGetMediaPropertiesAsync can hand back
    // nulls); they pass through as empty rather than throwing or inventing fields.
    [Theory]
    [InlineData("", "", "")]
    [InlineData(null, null, null)]
    [InlineData("Connecting…", "", "")]
    public void Parse_passes_empty_or_null_fields_through_as_empty(string? title, string? artist, string? album)
    {
        var track = SmtcTrackMetadataParser.Parse(title, artist, album);

        Assert.Equal(new Track(title ?? "", "", ""), track);
    }

    // A separator with an empty side ("<sep>Album" alone, or "Artist<sep> ") is too suspicious
    // to act on — better an unsplit string (matcher fails open) than an invented artist/album.
    [Theory]
    [InlineData("— This Is Acting")]
    [InlineData(" — This Is Acting")]
    [InlineData("Sia — ")]
    public void Parse_does_not_split_when_either_side_of_the_separator_would_be_empty(string artist)
    {
        var track = SmtcTrackMetadataParser.Parse("Song", artist, "");

        Assert.Equal(new Track("Song", artist, ""), track);
    }

    // Stripping the suffix must never leave an empty artist: "<sep>Album" alone stays intact.
    [Fact]
    public void Parse_does_not_strip_the_suffix_when_it_would_leave_an_empty_artist()
    {
        var track = SmtcTrackMetadataParser.Parse("Song", " — This Is Acting", "This Is Acting");

        Assert.Equal(new Track("Song", " — This Is Acting", "This Is Acting"), track);
    }
}
