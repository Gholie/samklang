using Samklang.Sessions;
using Xunit;

namespace Samklang.Tests.Sessions;

public class AppleMusicUriLauncherTests
{
    [Fact]
    public void BuildTrackUrl_targets_the_song_in_its_album_context_so_playback_continues_through_the_album()
    {
        // Apple's canonical "song" link is the album URL with the song as the `i` query param;
        // opening it plays the song and keeps going through the album afterwards.
        Assert.Equal(
            "https://music.apple.com/us/album/1440817899?i=1440817932",
            AppleMusicUriLauncher.BuildTrackUrl("us", "1440817932", "1440817899"));
    }

    [Fact]
    public void BuildTrackUrl_uses_the_supplied_storefront_code()
    {
        Assert.Equal(
            "https://music.apple.com/no/album/1440817899?i=1440817932",
            AppleMusicUriLauncher.BuildTrackUrl("no", "1440817932", "1440817899"));
    }

    [Fact]
    public void BuildTrackUrl_falls_back_to_a_plain_song_link_when_no_album_id_is_known()
    {
        Assert.Equal(
            "https://music.apple.com/us/song/1440817932",
            AppleMusicUriLauncher.BuildTrackUrl("us", "1440817932", albumId: ""));
    }

    [Fact]
    public void BuildTrackUrl_escapes_the_ids_so_a_tampered_catalog_value_cannot_break_out_of_the_url()
    {
        // The ids originate from a catalog API response, not our own code; values carrying URL
        // metacharacters must stay inside their own url part rather than injecting path/query.
        var url = AppleMusicUriLauncher.BuildTrackUrl("us", "12&evil=1", "34/../x");

        Assert.Equal("https://music.apple.com/us/album/34%2F..%2Fx?i=12%26evil%3D1", url);
    }
}
