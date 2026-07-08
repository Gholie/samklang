using Samklang.Sessions;
using Xunit;

namespace Samklang.Tests.Sessions;

public class SmtcTrackMetadataParserTests
{
    [Fact]
    public void ParseTrack_strips_an_album_suffix_from_artist_when_it_matches_a_non_empty_AlbumTitle()
    {
        var track = SmtcTrackMetadataParser.ParseTrack(
            "Hexagons", "Muse — The Wow! Signal", "The Wow! Signal");

        Assert.Equal("Muse", track.Artist);
        Assert.Equal("The Wow! Signal", track.Album);
        Assert.Equal("Hexagons", track.Title);
    }

    [Fact]
    public void ParseTrack_splits_at_the_first_separator_when_AlbumTitle_is_empty()
    {
        var track = SmtcTrackMetadataParser.ParseTrack(
            "Bird Set Free", "Sia — This Is Acting", "");

        Assert.Equal("Sia", track.Artist);
        Assert.Equal("This Is Acting", track.Album);
    }

    [Fact]
    public void ParseTrack_leaves_artist_unchanged_when_it_has_no_separator()
    {
        var track = SmtcTrackMetadataParser.ParseTrack("Song", "Kasbo", "");

        Assert.Equal("Kasbo", track.Artist);
        Assert.Equal("", track.Album);
    }

    [Fact]
    public void ParseTrack_leaves_artist_unchanged_when_AlbumTitle_is_set_but_does_not_match_the_artist_suffix()
    {
        // AlbumTitle is populated but doesn't correspond to the em-dash suffix in Artist — this is
        // ambiguous (a stale/unrelated AlbumTitle value, not necessarily "no album leaked into
        // Artist"), so the conservative choice is to leave Artist untouched rather than guess by
        // falling back to a first-separator split.
        var track = SmtcTrackMetadataParser.ParseTrack(
            "Song", "Sia — This Is Acting", "A Completely Different Album");

        Assert.Equal("Sia — This Is Acting", track.Artist);
        Assert.Equal("A Completely Different Album", track.Album);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void ParseTrack_never_produces_an_empty_artist_from_a_non_empty_one(string? album)
    {
        // Degenerate input: Artist is *only* the separator, with nothing on the artist side. The
        // split guard must refuse to leave Artist empty and keep the original string instead.
        var track = SmtcTrackMetadataParser.ParseTrack("Song", " — Some Album", album);

        Assert.Equal(" — Some Album", track.Artist);
    }

    [Fact]
    public void ParseTrack_returns_empty_artist_unchanged_when_the_source_artist_is_already_empty()
    {
        var track = SmtcTrackMetadataParser.ParseTrack("Song", "", "Album");

        Assert.Equal("", track.Artist);
        Assert.Equal("Album", track.Album);
    }

    [Fact]
    public void ParseTrack_treats_null_fields_as_empty_strings()
    {
        var track = SmtcTrackMetadataParser.ParseTrack(null, null, null);

        Assert.Equal("", track.Title);
        Assert.Equal("", track.Artist);
        Assert.Equal("", track.Album);
    }

    [Fact]
    public void ParseTrack_splits_at_the_first_separator_even_when_the_album_side_also_contains_one()
    {
        // The album side legitimately containing " — " (e.g. a two-part title) must not confuse
        // the split — splitting at the *first* occurrence keeps the artist side correct.
        var track = SmtcTrackMetadataParser.ParseTrack(
            "Song", "Muse — Origin of Symmetry — Deluxe", "");

        Assert.Equal("Muse", track.Artist);
        Assert.Equal("Origin of Symmetry — Deluxe", track.Album);
    }

    [Fact]
    public void ParseTrack_accepts_the_en_dash_variant_defensively()
    {
        var track = SmtcTrackMetadataParser.ParseTrack("Song", "Kasbo – Places We Don't Know", "");

        Assert.Equal("Kasbo", track.Artist);
        Assert.Equal("Places We Don't Know", track.Album);
    }

    [Fact]
    public void ParseTrack_strips_the_suffix_with_the_en_dash_variant_too()
    {
        var track = SmtcTrackMetadataParser.ParseTrack("Song", "Kasbo – Places We Don't Know", "Places We Don't Know");

        Assert.Equal("Kasbo", track.Artist);
    }
}
