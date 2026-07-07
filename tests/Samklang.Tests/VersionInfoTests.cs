using Xunit;

namespace Samklang.Tests;

public class VersionInfoTests
{
    [Theory]
    [InlineData(0, 1, 0, "v0.1.0")]
    [InlineData(1, 2, 3, "v1.2.3")]
    [InlineData(10, 0, 4, "v10.0.4")]
    public void Format_RendersMajorMinorBuildWithVPrefix(int major, int minor, int build, string expected)
    {
        var version = new Version(major, minor, build);

        Assert.Equal(expected, VersionInfo.Format(version));
    }

    [Fact]
    public void Format_DropsRevisionField()
    {
        // .NET's default assembly versioning always populates all four fields; end users expect
        // the three-part version they'd type as a release tag (e.g. "v0.1.0"), not the fourth
        // (Revision) build-tooling detail.
        var version = new Version(0, 1, 0, 42);

        Assert.Equal("v0.1.0", VersionInfo.Format(version));
    }

    [Fact]
    public void Format_NullVersion_FallsBackToZero()
    {
        Assert.Equal("v0.0.0", VersionInfo.Format(null));
    }

    [Fact]
    public void CurrentDisplay_IsAWellFormedVersionString()
    {
        // Exercises the real assembly-reading path (issue #10) rather than re-asserting a
        // hard-coded version number, which would just go stale the next time <Version> in the
        // csproj is bumped for a release.
        Assert.Matches(@"^v\d+\.\d+\.\d+$", VersionInfo.CurrentDisplay);
    }
}
