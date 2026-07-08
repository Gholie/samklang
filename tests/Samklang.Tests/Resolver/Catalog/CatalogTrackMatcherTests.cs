using Samklang.Domain;
using Samklang.Resolver.Catalog;
using Xunit;

namespace Samklang.Tests.Resolver.Catalog;

public class CatalogTrackMatcherTests
{
    [Fact]
    public void FindBestMatch_matches_an_exact_title_and_artist()
    {
        var track = new Track("Midnight City", "M83", "Hurry Up, We're Dreaming");
        var candidates = new[] { new CatalogSearchCandidate("1", "Midnight City", "M83", "Hurry Up, We're Dreaming") };

        var result = CatalogTrackMatcher.FindBestMatch(track, candidates);

        Assert.Equal("1", result?.Id);
    }

    [Fact]
    public void FindBestMatch_is_case_insensitive_and_ignores_punctuation_differences()
    {
        var track = new Track("Don't Stop Me Now", "Queen", "Jazz");
        var candidates = new[] { new CatalogSearchCandidate("2", "DONT STOP ME NOW", "queen", "Jazz") };

        var result = CatalogTrackMatcher.FindBestMatch(track, candidates);

        Assert.Equal("2", result?.Id);
    }

    [Fact]
    public void FindBestMatch_ignores_a_feat_suffix_on_the_catalog_title()
    {
        var track = new Track("Blinding Lights", "The Weeknd", "After Hours");
        var candidates = new[] { new CatalogSearchCandidate("3", "Blinding Lights (feat. Someone)", "The Weeknd", "After Hours") };

        var result = CatalogTrackMatcher.FindBestMatch(track, candidates);

        Assert.Equal("3", result?.Id);
    }

    [Fact]
    public void FindBestMatch_accepts_when_smtc_reports_only_the_primary_artist_of_a_collaboration()
    {
        var track = new Track("Some Song", "Artist A", "Album");
        var candidates = new[] { new CatalogSearchCandidate("4", "Some Song", "Artist A & Artist B", "Album") };

        var result = CatalogTrackMatcher.FindBestMatch(track, candidates);

        Assert.Equal("4", result?.Id);
    }

    [Fact]
    public void FindBestMatch_ignores_a_remaster_suffix_on_the_catalog_title()
    {
        var track = new Track("Some Song", "Artist A", "Album");
        var candidates = new[] { new CatalogSearchCandidate("10", "Some Song (2011 Remastered Version)", "Artist A", "Album") };

        var result = CatalogTrackMatcher.FindBestMatch(track, candidates);

        Assert.Equal("10", result?.Id);
    }

    [Fact]
    public void FindBestMatch_does_not_equate_a_live_version_with_the_studio_version()
    {
        // A live recording is a different variant whose sample rate can genuinely differ — an
        // Exact-confidence match against it would apply the wrong variant's format.
        var track = new Track("Some Song", "Artist A", "Album");
        var candidates = new[] { new CatalogSearchCandidate("11", "Some Song (Live)", "Artist A", "Live Album") };

        var result = CatalogTrackMatcher.FindBestMatch(track, candidates);

        Assert.Null(result);
    }

    [Fact]
    public void FindBestMatch_matches_a_live_version_to_the_same_live_version()
    {
        var track = new Track("Some Song (Live)", "Artist A", "Live Album");
        var candidates = new[] { new CatalogSearchCandidate("12", "Some Song (Live)", "Artist A", "Live Album") };

        var result = CatalogTrackMatcher.FindBestMatch(track, candidates);

        Assert.Equal("12", result?.Id);
    }

    [Fact]
    public void FindBestMatch_does_not_strip_a_parenthetical_that_only_contains_a_marker_inside_another_word()
    {
        // "Unremastered" contains "remaster" but names a different variant; stripping it would
        // wrongly equate the two titles.
        var track = new Track("Some Song", "Artist A", "Album");
        var candidates = new[] { new CatalogSearchCandidate("13", "Some Song (Unremastered)", "Artist A", "Album") };

        var result = CatalogTrackMatcher.FindBestMatch(track, candidates);

        Assert.Null(result);
    }

    [Fact]
    public void FindBestMatch_returns_null_when_no_candidate_title_matches()
    {
        var track = new Track("Some Song", "Artist A", "Album");
        var candidates = new[] { new CatalogSearchCandidate("5", "A Completely Different Song", "Artist A", "Album") };

        var result = CatalogTrackMatcher.FindBestMatch(track, candidates);

        Assert.Null(result);
    }

    [Fact]
    public void FindBestMatch_returns_null_when_title_matches_but_artist_is_unrelated()
    {
        var track = new Track("Some Song", "Artist A", "Album");
        var candidates = new[] { new CatalogSearchCandidate("6", "Some Song", "A Cover Band", "Album") };

        var result = CatalogTrackMatcher.FindBestMatch(track, candidates);

        Assert.Null(result);
    }

    [Fact]
    public void FindBestMatch_returns_null_for_an_empty_candidate_list()
    {
        var track = new Track("Some Song", "Artist A", "Album");

        var result = CatalogTrackMatcher.FindBestMatch(track, Array.Empty<CatalogSearchCandidate>());

        Assert.Null(result);
    }

    [Fact]
    public void FindBestMatch_prefers_the_exact_artist_match_over_a_looser_collaborator_match()
    {
        var track = new Track("Some Song", "Artist A", "Album");
        var candidates = new[]
        {
            new CatalogSearchCandidate("7", "Some Song", "Artist A & Artist B", "Album"),
            new CatalogSearchCandidate("8", "Some Song", "Artist A", "Album"),
        };

        var result = CatalogTrackMatcher.FindBestMatch(track, candidates);

        Assert.Equal("8", result?.Id);
    }

    // --- Album-aware ranking (2026-07-08 Sia "Alive" investigation: Apple's catalog can carry the
    // same song under several editions of the same album with different masters, and title+artist
    // scoring alone can't tell those editions apart) ---

    [Fact]
    public void FindBestMatch_prefers_an_album_matched_candidate_over_an_earlier_ranked_candidate_with_a_different_album()
    {
        var track = new Track("Alive", "Sia", "This Is Acting");
        var candidates = new[]
        {
            // Apple's search relevance ranks this first even though it's a different release.
            new CatalogSearchCandidate("single", "Alive", "Sia", "Alive - Single"),
            new CatalogSearchCandidate("album", "Alive", "Sia", "This Is Acting"),
        };

        var result = CatalogTrackMatcher.FindBestMatch(track, candidates);

        Assert.Equal("album", result?.Id);
    }

    [Fact]
    public void FindBestMatch_album_bonus_outranks_a_looser_collaborator_artist_match()
    {
        // An album match is "ABOVE the existing score": a candidate that only clears the gate via
        // the looser collaborator rule but matches the album beats one with an exact artist match
        // and a wrong album.
        var track = new Track("Some Song", "Artist A", "Right Album");
        var candidates = new[]
        {
            new CatalogSearchCandidate("exact-artist-wrong-album", "Some Song", "Artist A", "Wrong Album"),
            new CatalogSearchCandidate("collaborator-right-album", "Some Song", "Artist A & Artist B", "Right Album"),
        };

        var result = CatalogTrackMatcher.FindBestMatch(track, candidates);

        Assert.Equal("collaborator-right-album", result?.Id);
    }

    [Fact]
    public void FindBestMatch_falls_back_to_todays_behavior_when_no_candidate_album_matches()
    {
        // No candidate's album matches the track's — the album layer has nothing to reorder, so
        // the first gate-passing candidate (today's behavior) still wins.
        var track = new Track("Some Song", "Artist A", "This Is Acting");
        var candidates = new[]
        {
            new CatalogSearchCandidate("9", "Some Song", "Artist A", "Some Other Album"),
            new CatalogSearchCandidate("10", "Some Song", "Artist A", "Yet Another Album"),
        };

        var result = CatalogTrackMatcher.FindBestMatch(track, candidates);

        Assert.Equal("9", result?.Id);
    }

    [Fact]
    public void FindBestMatch_with_an_empty_track_album_behaves_exactly_like_the_title_artist_only_gate()
    {
        // SMTC sometimes yields no album at all; the album layer must be a no-op rather than
        // treating "empty" as a value every candidate's own album fails to match against — which
        // it already wouldn't (HasAlbumMatch requires a non-empty normalized track album), but this
        // pins the observable behavior: the first gate-passing candidate wins, same as before this
        // feature existed.
        var track = new Track("Some Song", "Artist A", "");
        var candidates = new[]
        {
            new CatalogSearchCandidate("11", "Some Song", "Artist A", "Some Album"),
            new CatalogSearchCandidate("12", "Some Song", "Artist A", "A Totally Different Album"),
        };

        var result = CatalogTrackMatcher.FindBestMatch(track, candidates);

        Assert.Equal("11", result?.Id);
    }

    [Fact]
    public void FindBestMatch_treats_a_deluxe_edition_album_as_the_same_family_as_the_standard_edition()
    {
        // The decision this test pins: "This Is Acting (Deluxe Version)" and "This Is Acting" are
        // the same album family for matching purposes (an edition qualifier names a repackaging of
        // the same release, not a different one) — see NormalizeAlbum's doc comment. Both
        // candidates here tie on the album bonus, so the first one encountered (today's tie-break)
        // wins; CatalogFormatResolverLayer is what actually picks the better-quality edition
        // between ties, via FindAlbumSiblings.
        var track = new Track("Alive", "Sia", "This Is Acting");
        var candidates = new[]
        {
            new CatalogSearchCandidate("plain", "Alive", "Sia", "This Is Acting"),
            new CatalogSearchCandidate("deluxe", "Alive", "Sia", "This Is Acting (Deluxe Version)"),
        };

        var result = CatalogTrackMatcher.FindBestMatch(track, candidates);

        Assert.Equal("plain", result?.Id);
    }

    [Fact]
    public void FindAlbumSiblings_treats_an_unparenthesized_trailing_edition_qualifier_as_the_same_family()
    {
        // Live-verified against Michael Jackson's "Bad": the 96 kHz remaster sits under the
        // catalog album literally named "Bad 25th Anniversary" — no parentheses — while SMTC
        // reports the plain "Bad". Unlike "This Is Acting (Deluxe Version)", there's no bracket
        // for the existing parenthetical stripper to find, so this needs its own handling.
        var track = new Track("Bad", "Michael Jackson", "Bad");
        var matched = new CatalogSearchCandidate("plain", "Bad", "Michael Jackson", "Bad");
        var anniversary = new CatalogSearchCandidate("anniv", "Bad (2012 Remaster)", "Michael Jackson", "Bad 25th Anniversary");
        var candidates = new[] { matched, anniversary };

        var siblings = CatalogTrackMatcher.FindAlbumSiblings(track, candidates, matched);

        Assert.Equal(["anniv"], siblings.Select(s => s.Id));
    }

    [Fact]
    public void FindAlbumSiblings_does_not_strip_a_one_word_album_that_is_only_named_after_an_edition_qualifier()
    {
        // The trailing-qualifier stripper requires a leading separator before the qualifier word,
        // so an album genuinely titled just "Deluxe" isn't reduced to an empty family key that
        // would spuriously tie it to every other album.
        var track = new Track("Some Song", "Artist A", "Deluxe");
        var matched = new CatalogSearchCandidate("a", "Some Song", "Artist A", "Deluxe");
        var unrelated = new CatalogSearchCandidate("b", "Some Song", "Artist A", "Some Other Album");
        var candidates = new[] { matched, unrelated };

        var siblings = CatalogTrackMatcher.FindAlbumSiblings(track, candidates, matched);

        Assert.Empty(siblings);
    }

    // --- FindAlbumSiblings ---

    [Fact]
    public void FindAlbumSiblings_returns_other_gate_passing_candidates_in_the_matched_albums_family()
    {
        var track = new Track("Alive", "Sia", "This Is Acting");
        var matched = new CatalogSearchCandidate("plain", "Alive", "Sia", "This Is Acting");
        var deluxe = new CatalogSearchCandidate("deluxe", "Alive", "Sia", "This Is Acting (Deluxe Version)");
        var anniversary = new CatalogSearchCandidate("anniv", "Alive", "Sia", "This Is Acting (10th Anniversary Edition)");
        var candidates = new[] { matched, deluxe, anniversary };

        var siblings = CatalogTrackMatcher.FindAlbumSiblings(track, candidates, matched);

        Assert.Equal(new[] { "deluxe", "anniv" }, siblings.Select(s => s.Id));
    }

    [Fact]
    public void FindAlbumSiblings_excludes_the_matched_candidate_itself()
    {
        var track = new Track("Alive", "Sia", "This Is Acting");
        var matched = new CatalogSearchCandidate("plain", "Alive", "Sia", "This Is Acting");
        var candidates = new[] { matched };

        var siblings = CatalogTrackMatcher.FindAlbumSiblings(track, candidates, matched);

        Assert.Empty(siblings);
    }

    [Fact]
    public void FindAlbumSiblings_excludes_candidates_that_fail_the_title_artist_gate_even_if_the_album_matches()
    {
        var track = new Track("Alive", "Sia", "This Is Acting");
        var matched = new CatalogSearchCandidate("plain", "Alive", "Sia", "This Is Acting");
        var wrongSong = new CatalogSearchCandidate("wrong-song", "Unstoppable", "Sia", "This Is Acting (Deluxe Version)");
        var candidates = new[] { matched, wrongSong };

        var siblings = CatalogTrackMatcher.FindAlbumSiblings(track, candidates, matched);

        Assert.Empty(siblings);
    }

    [Fact]
    public void FindAlbumSiblings_excludes_candidates_from_a_different_album_family()
    {
        var track = new Track("Alive", "Sia", "This Is Acting");
        var matched = new CatalogSearchCandidate("plain", "Alive", "Sia", "This Is Acting");
        var differentAlbum = new CatalogSearchCandidate("single", "Alive", "Sia", "Alive - Single");
        var candidates = new[] { matched, differentAlbum };

        var siblings = CatalogTrackMatcher.FindAlbumSiblings(track, candidates, matched);

        Assert.Empty(siblings);
    }

    [Fact]
    public void FindAlbumSiblings_returns_empty_when_the_matched_candidates_own_album_is_empty()
    {
        var track = new Track("Alive", "Sia", "");
        var matched = new CatalogSearchCandidate("plain", "Alive", "Sia", "");
        var other = new CatalogSearchCandidate("other", "Alive", "Sia", "");
        var candidates = new[] { matched, other };

        var siblings = CatalogTrackMatcher.FindAlbumSiblings(track, candidates, matched);

        Assert.Empty(siblings);
    }
}
