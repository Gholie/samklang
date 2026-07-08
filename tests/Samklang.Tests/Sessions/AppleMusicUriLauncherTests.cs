using Samklang.Sessions;
using Xunit;

namespace Samklang.Tests.Sessions;

public class AppleMusicUriLauncherTests
{
    [Fact]
    public void BuildTrackUrl_targets_the_catalog_song_in_the_given_storefront()
    {
        Assert.Equal(
            "https://music.apple.com/us/song/1440817932",
            AppleMusicUriLauncher.BuildTrackUrl("us", "1440817932"));
    }

    [Fact]
    public void BuildTrackUrl_uses_the_supplied_storefront_code()
    {
        Assert.Equal(
            "https://music.apple.com/no/song/1440817932",
            AppleMusicUriLauncher.BuildTrackUrl("no", "1440817932"));
    }

    [Fact]
    public void BuildTrackUrl_escapes_the_id_so_a_tampered_catalog_value_cannot_break_out_of_the_path()
    {
        // The id originates from a catalog API response, not our own code; a value carrying URL
        // metacharacters must stay inside the song path segment rather than injecting query/path.
        var url = AppleMusicUriLauncher.BuildTrackUrl("us", "12/../evil?x=1");

        Assert.StartsWith("https://music.apple.com/us/song/", url);
        Assert.DoesNotContain("?", url["https://music.apple.com/us/song/".Length..]);
        Assert.DoesNotContain("/", url["https://music.apple.com/us/song/".Length..]);
    }
}
