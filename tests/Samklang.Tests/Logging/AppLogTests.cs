using Samklang.Logging;
using Xunit;

namespace Samklang.Tests.Logging;

/// <summary>
/// Regression guard for the "dotnet test polluted the real user's log file" incident (2026-07-08):
/// asserts that with <c>TestAssemblySetup</c>'s <c>[ModuleInitializer]</c> active, calling
/// <see cref="AppLog"/> never touches the real <c>%LOCALAPPDATA%\Samklang\logs</c> directory. This
/// only checks the *effect* of <see cref="AppLog.DisabledForTests"/> (already true for the whole
/// run) — it deliberately does not flip the flag itself, since doing so around a parallel test run
/// would race every other test's AppLog calls.
/// </summary>
public class AppLogTests
{
    [Fact]
    public void DisabledForTests_IsActiveForTheWholeAssembly()
    {
        // TestAssemblySetup's [ModuleInitializer] must have already flipped this before any test
        // — including this one — gets to run.
        Assert.True(AppLog.DisabledForTests);
    }

    [Fact]
    public void WhileDisabled_LoggingCallsDoNotTouchTheRealLogDirectory()
    {
        var logDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Samklang", "logs");

        // Snapshot by file name -> length rather than a directory-wide timestamp: the real
        // Samklang tray app may be running and legitimately appending to today's file at the same
        // time this test runs, so the assertion must survive that rather than racing it. A byte
        // count taken immediately before/after a handful of in-process, synchronous AppLog calls
        // is stable unless those very calls wrote something.
        var before = SnapshotLogFiles(logDirectory);

        AppLog.Info("AppLogTests regression guard — must never reach disk while disabled.");
        AppLog.Warn("AppLogTests regression guard — must never reach disk while disabled.");
        AppLog.Error("AppLogTests regression guard — must never reach disk while disabled.", new InvalidOperationException("guard"));

        var after = SnapshotLogFiles(logDirectory);

        Assert.Equal(before, after);
    }

    private static Dictionary<string, long> SnapshotLogFiles(string logDirectory)
    {
        if (!Directory.Exists(logDirectory))
        {
            return new Dictionary<string, long>();
        }

        return new DirectoryInfo(logDirectory)
            .GetFiles("samklang-*.log")
            .ToDictionary(f => f.Name, f => f.Length);
    }
}
