using System.IO;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Samklang.Tests")]

namespace Samklang.Logging;

/// <summary>
/// Rolling file logger (2026-07-08 handoff: a user's "catalog isn't resolving" report was
/// completely undiagnosable because nothing the app did was ever written down anywhere). Appends
/// terse, timestamped lines to <c>%LOCALAPPDATA%\Samklang\logs\samklang-{yyyy-MM-dd}.log</c> —
/// the same root Velopack installs the app under, so it's easy to find alongside it.
///
/// <para>
/// Gated by <see cref="Enabled"/> (backed by <see cref="SettingsManagement.Settings.EnableDetailedLogging"/>,
/// off by default): a user only pays the (small) disk-write cost after opting in from the
/// Settings page, and every call site below stays a no-op until then. The composition root
/// (<see cref="MainWindow"/>) syncs <see cref="Enabled"/> from Settings once at startup and again
/// on every settings change, so flipping the toggle takes effect immediately without a restart.
/// The log directory and file are still not created until the first write that happens while
/// enabled — lazy by construction, not by a separate initialization step.
/// </para>
///
/// <para>
/// Deliberately a static, hand-rolled writer rather than a NuGet logging framework: this is a
/// tray utility producing a few dozen lines an hour at most, not a server, so Serilog/NLog would
/// be a lot of dependency for very little benefit. <see cref="Info"/>/<see cref="Warn"/>/
/// <see cref="Error"/> are safe to call from any thread (SMTC callbacks, background catalog
/// lookups, the UI thread) — writes are serialized on <see cref="Gate"/>, and any I/O failure
/// (locked file, disk full, permissions) is swallowed so logging itself can never be the reason
/// the app breaks; the failure is reported to <see cref="Console.Error"/> only (there is nowhere
/// else safe to put it once the log file itself is the thing that failed).
/// </para>
///
/// <para>
/// Never log secret values (tokens, credentials) — callers are responsible for that; this class
/// just writes whatever string it's given.
/// </para>
///
/// <para>
/// Real file I/O against a fixed, real OS path and not unit-tested directly — the same pattern as
/// <see cref="SettingsManagement.JsonFileSettingsStore"/> and
/// <see cref="Resolver.Catalog.HttpAppleMusicCatalogClient"/>. Everything that decides *what* to
/// log (the resolver/coordinator call sites) is ordinary code exercised by their own tests; this
/// class is deliberately just a thin, swallow-all-I/O-errors writer underneath that.
/// </para>
/// </summary>
public static class AppLog
{
    /// <summary>Per-file cap before rolling to a numbered backup — 10 MB, per the logging spec.</summary>
    private const long MaxFileBytes = 10 * 1024 * 1024;

    /// <summary>
    /// How many numbered backups (<c>samklang-{date}.log.1</c> .. <c>.5</c>) are kept once the
    /// active file rolls. Combined with the one active file, this bounds total disk usage to
    /// roughly <c>(MaxBackupFiles + 1) * MaxFileBytes</c> ≈ 60 MB.
    /// </summary>
    private const int MaxBackupFiles = 5;

    private static readonly object Gate = new();

    private static readonly string LogDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Samklang", "logs");

    /// <summary>
    /// Test-only kill switch, set once for the whole run by Samklang.Tests' <c>[ModuleInitializer]</c>
    /// (see <c>TestAssemblySetup</c>) before any test executes. CatalogFormatResolverLayer,
    /// FormatResolverChain, and TrackSyncCoordinator call <see cref="Info"/>/<see cref="Warn"/>/
    /// <see cref="Error"/> as an ordinary side effect of the production code paths their tests
    /// exercise; without this, <c>dotnet test</c> would append synthetic entries straight into the
    /// real user's <c>%LOCALAPPDATA%\Samklang\logs</c> file. Flipping a single bool once, rather
    /// than redirecting to a per-test temp directory, sidesteps any risk of tests racing each
    /// other over shared static log state when xunit runs them in parallel — with logging off,
    /// there is nothing left to race over. Internal rather than public: production code has no
    /// legitimate reason to ever disable logging.
    /// </summary>
    internal static bool DisabledForTests { get; set; }

    /// <summary>
    /// Whether file logging is currently turned on, per
    /// <see cref="SettingsManagement.Settings.EnableDetailedLogging"/>. Defaults to false —
    /// detailed logging is opt-in — so nothing is written (not even the log directory is created)
    /// until the composition root syncs this from a loaded/changed Settings value. Every
    /// <see cref="Write"/> call re-checks this, so toggling it off mid-run stops logging
    /// immediately without needing to close and reopen any file handle (none is held open between
    /// writes).
    /// </summary>
    public static bool Enabled { get; set; }

    public static void Info(string message, string category = "General") => Write("INFO", category, message);

    public static void Warn(string message, string category = "General") => Write("WARN", category, message);

    /// <summary>
    /// Logs an error. <paramref name="exception"/>'s message (never its full ToString/stack trace
    /// — this is a terse tray-utility log, not a crash dump) is appended when provided.
    /// </summary>
    public static void Error(string message, Exception? exception = null, string category = "General") =>
        Write("ERROR", category, exception is null ? message : $"{message}: {exception.GetType().Name}: {exception.Message}");

    private static void Write(string level, string category, string message)
    {
        if (DisabledForTests || !Enabled)
        {
            return;
        }

        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(LogDirectory);

                var path = CurrentLogPath();
                RollIfOversizedLocked(path);

                var line = $"[{DateTime.Now:HH:mm:ss.fff}] [{level}] {category}: {message}{Environment.NewLine}";
                File.AppendAllText(path, line);

                PruneOldFilesLocked();
            }
        }
        catch (Exception ex)
        {
            // Logging must never be the reason the app misbehaves — swallow any I/O failure here
            // (locked file, disk full, permissions) and report it only to stderr, since the log
            // file itself is the thing that just failed.
            TryWriteToStdErr(ex);
        }
    }

    private static void TryWriteToStdErr(Exception ex)
    {
        try
        {
            Console.Error.WriteLine($"Samklang: file logging failed ({ex.GetType().Name}: {ex.Message}).");
        }
        catch
        {
            // stderr itself can be unavailable (e.g. no console attached); nothing further to do.
        }
    }

    private static string CurrentLogPath() => Path.Combine(LogDirectory, $"samklang-{DateTime.Now:yyyy-MM-dd}.log");

    /// <summary>
    /// Caller must hold <see cref="Gate"/>. Classic numbered-backup rolling once today's file
    /// crosses <see cref="MaxFileBytes"/>: <c>.4</c> becomes <c>.5</c> (dropping any prior <c>.5</c>),
    /// <c>.3</c> becomes <c>.4</c>, and so on down to the active file itself becoming <c>.1</c>,
    /// after which a fresh, empty active file is written to on the next line below.
    /// </summary>
    private static void RollIfOversizedLocked(string path)
    {
        if (!File.Exists(path) || new FileInfo(path).Length < MaxFileBytes)
        {
            return;
        }

        var oldestBackup = $"{path}.{MaxBackupFiles}";
        if (File.Exists(oldestBackup))
        {
            File.Delete(oldestBackup);
        }

        for (var i = MaxBackupFiles - 1; i >= 1; i--)
        {
            var source = $"{path}.{i}";
            if (File.Exists(source))
            {
                File.Move(source, $"{path}.{i + 1}", overwrite: true);
            }
        }

        File.Move(path, $"{path}.1", overwrite: true);
    }

    /// <summary>
    /// Caller must hold <see cref="Gate"/>. Bounds total disk usage regardless of how many
    /// calendar days the app has been logging on: today's active file plus its numbered backups
    /// is <see cref="MaxBackupFiles"/> + 1 files, so anything beyond that many files total —
    /// including whole prior days' files once they age out — is pruned, oldest by last-write time
    /// first. No log line is precious enough to justify smarter cross-day bookkeeping here.
    /// </summary>
    private static void PruneOldFilesLocked()
    {
        const int maxRetainedFiles = MaxBackupFiles + 1;

        var files = new DirectoryInfo(LogDirectory).GetFiles("samklang-*.log*");
        if (files.Length <= maxRetainedFiles)
        {
            return;
        }

        foreach (var stale in files.OrderByDescending(f => f.LastWriteTimeUtc).Skip(maxRetainedFiles))
        {
            stale.Delete();
        }
    }
}
