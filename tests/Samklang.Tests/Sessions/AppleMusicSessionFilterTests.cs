using Samklang.Sessions;
using Xunit;

namespace Samklang.Tests.Sessions;

public class AppleMusicSessionFilterTests
{
    [Theory]
    [InlineData("AppleInc.AppleMusicWin_nzyj5cx40ttqa!App", true)]
    [InlineData("appleinc.applemusicwin_nzyj5cx40ttqa!app", true)] // case-insensitive
    [InlineData("AppleMusic.exe", true)] // Windows 10's bare-executable session identity
    [InlineData("APPLEMUSIC.EXE", true)] // case-insensitive
    [InlineData("Spotify.exe", false)]
    [InlineData("Spotify.com", false)]
    [InlineData("Microsoft.MicrosoftEdge_8wekyb3d8bbwe!MicrosoftEdge", false)]
    [InlineData("chrome.exe", false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    public void IsAppleMusicSession_matches_only_apple_music_identities(string? sourceAppUserModelId, bool expected) =>
        Assert.Equal(expected, AppleMusicSessionFilter.IsAppleMusicSession(sourceAppUserModelId));
}
