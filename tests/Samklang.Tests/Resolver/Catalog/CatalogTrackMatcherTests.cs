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
}
