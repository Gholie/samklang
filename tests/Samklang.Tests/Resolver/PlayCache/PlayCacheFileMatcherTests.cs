using Samklang.Resolver.PlayCache;
using Xunit;

namespace Samklang.Tests.Resolver.PlayCache;

public class PlayCacheFileMatcherTests
{
    [Fact]
    public void TitleMatchesDownloadFolder_matches_the_exact_title_when_there_is_no_album()
    {
        Assert.True(PlayCacheFileMatcher.TitleMatchesDownloadFolder("My Song", null, "My Song"));
    }

    [Fact]
    public void TitleMatchesDownloadFolder_matches_title_underscore_album_join()
    {
        Assert.True(PlayCacheFileMatcher.TitleMatchesDownloadFolder("My Song", "My Album", "My Song _ My Album"));
    }

    [Fact]
    public void TitleMatchesDownloadFolder_matches_a_truncated_folder_name_that_is_a_prefix_of_title_and_album()
    {
        // Apple Music truncates the folder name at ~30 characters.
        var folder = "Runner [V1] (feat. Lil Uzi Ver";
        Assert.True(PlayCacheFileMatcher.TitleMatchesDownloadFolder("Runner [V1] (feat. Lil Uzi Vert)", "Some Album", folder));
    }

    [Fact]
    public void TitleMatchesDownloadFolder_sanitizes_invalid_filename_characters_before_matching()
    {
        Assert.True(PlayCacheFileMatcher.TitleMatchesDownloadFolder("Clean / Money Can't Buy Dreams", null, "Clean _ Money Can't Buy Dreams"));
    }

    [Fact]
    public void TitleMatchesDownloadFolder_does_not_match_a_short_folder_name_that_is_only_a_prefix_of_a_longer_title()
    {
        // "Clean" must not match the folder for the track "Cleaner" — below the truncation
        // length, a folder name that isn't an exact (or "_ album"-joined) match is just wrong.
        Assert.False(PlayCacheFileMatcher.TitleMatchesDownloadFolder("Cleaner", "Album", "Clean"));
    }

    [Fact]
    public void TitleMatchesDownloadFolder_does_not_match_an_unrelated_folder_name()
    {
        Assert.False(PlayCacheFileMatcher.TitleMatchesDownloadFolder("My Song", "My Album", "Some Other Song"));
    }

    [Fact]
    public void TitleMatchesDownloadFolder_is_case_insensitive()
    {
        Assert.True(PlayCacheFileMatcher.TitleMatchesDownloadFolder("my song", "my album", "MY SONG _ MY ALBUM"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TitleMatchesDownloadFolder_returns_false_for_a_blank_title(string? title)
    {
        Assert.False(PlayCacheFileMatcher.TitleMatchesDownloadFolder(title, "Album", "My Song"));
    }

    [Fact]
    public void TitleMatchesDownloadFolder_returns_false_for_a_blank_folder_name()
    {
        Assert.False(PlayCacheFileMatcher.TitleMatchesDownloadFolder("My Song", "Album", ""));
    }

    [Fact]
    public void CloudIdSuffix_formats_as_16_uppercase_hex_digits_with_a_leading_dash()
    {
        Assert.Equal("-000000000004A41E", PlayCacheFileMatcher.CloudIdSuffix(304158));
    }
}
