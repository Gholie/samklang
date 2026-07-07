using Samklang.Updates;
using Xunit;

namespace Samklang.Tests.Updates;

/// <summary>
/// Exercises the safety net issue #10 depends on: this test process is never a real Velopack
/// install (it's an xunit test host, not something `vpk pack` produced), so
/// <see cref="AppUpdateService"/> must treat that as "not installed" everywhere rather than
/// throwing — the same guarantee that lets MainWindow construct and call it unconditionally on
/// every startup, including plain `dotnet run`.
/// </summary>
public class AppUpdateServiceTests
{
    [Fact]
    public void Construction_DoesNotThrow_OutsideAnInstalledContext()
    {
        var exception = Record.Exception(() => new AppUpdateService());

        Assert.Null(exception);
    }

    [Fact]
    public void IsInstalled_IsFalse_OutsideAnInstalledContext()
    {
        var service = new AppUpdateService();

        Assert.False(service.IsInstalled);
    }

    [Fact]
    public async Task CheckAndApplyUpdateAsync_ReturnsNotInstalled_OutsideAnInstalledContext()
    {
        var service = new AppUpdateService();

        var result = await service.CheckAndApplyUpdateAsync();

        Assert.Equal(UpdateCheckResult.NotInstalled, result);
    }
}
