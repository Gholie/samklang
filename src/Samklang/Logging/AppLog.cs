using System.IO;

namespace Samklang.Logging;

/// <summary>
/// Minimal file logger (2026-07-08 handoff: a user's "catalog isn't resolving" report was
/// completely undiagnosable because nothing the app did was ever written down anywhere). Appends
/// terse, timestamped lines to <c>%LOCALAPPDATA%\Samklang\logs\samklang-{yyyy-MM-dd}.log</c> —
/// the same root Velopack installs the app under, so it's easy to find alongside it.
///
/// <para>
/// Deliberately a static, hand-rolled writer rather than a NuGet logging framework: this is a
/// tray utility producing a few dozen lines an hour at most, not a server, so Serilog/NLog would
/// be a lot of dependency for very little benefit. <see cref="Info"/>/<see cref="Warn"/>/
/// <see cref="Error"/> are safe to call from any thread (SMTC callbacks, background catalog
/// lookups, the UI thread) — writes are serialized on <see cref="Gate"/>, and any I/O failure
/// (locked file, disk full, permissions) is swallowed so logging itself can never be the reason
/// the app breaks.
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
    /// <summary>Per-file cap before rolling to a new, timestamped file — keeps one bad day from growing a single file without bound.</summary>
    private const long MaxFileBytes = 2 * 1024 * 1024;

    /// <summary>How many log files (across rolled-over same-day files and prior days) are kept; oldest by last-write time are pruned beyond this.</summary>
    private const int MaxRetainedFiles = 14;

    private static readonly object Gate = new();

    private static readonly string LogDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Samklang", "logs");

    public static void Info(string message) => Write("INFO", message);

    public static void Warn(string message) => Write("WARN", message);

    /// <summary>
    /// Logs an error. <paramref name="exception"/>'s message (never its full ToString/stack trace
    /// — this is a terse tray-utility log, not a crash dump) is appended when provided.
    /// </summary>
    public static void Error(string message, Exception? exception = null) =>
        Write("ERROR", exception is null ? message : $"{message}: {exception.GetType().Name}: {exception.Message}");

    private static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(LogDirectory);

                var path = CurrentLogPath();
                RollIfOversizedLocked(path);

                var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} [{level}] {message}{Environment.NewLine}";
                File.AppendAllText(path, line);

                PruneOldFilesLocked();
            }
        }
        catch
        {
            // Logging must never be the reason the app misbehaves — swallow any I/O failure here.
        }
    }

    private static string CurrentLogPath() => Path.Combine(LogDirectory, $"samklang-{DateTime.Now:yyyy-MM-dd}.log");

    /// <summary>Caller must hold <see cref="Gate"/>. Renames today's file out of the way once it crosses <see cref="MaxFileBytes"/>, so the next write starts a fresh one under the same day's name.</summary>
    private static void RollIfOversizedLocked(string path)
    {
        if (!File.Exists(path) || new FileInfo(path).Length < MaxFileBytes)
        {
            return;
        }

        var rolledPath = Path.Combine(LogDirectory, $"samklang-{DateTime.Now:yyyy-MM-dd-HHmmss}.log");
        File.Move(path, rolledPath, overwrite: true);
    }

    /// <summary>Caller must hold <see cref="Gate"/>. Simple retention: oldest files by last-write time beyond <see cref="MaxRetainedFiles"/> are deleted. No log line is precious enough to justify smarter bookkeeping here.</summary>
    private static void PruneOldFilesLocked()
    {
        var files = new DirectoryInfo(LogDirectory).GetFiles("samklang-*.log");
        if (files.Length <= MaxRetainedFiles)
        {
            return;
        }

        foreach (var stale in files.OrderByDescending(f => f.LastWriteTimeUtc).Skip(MaxRetainedFiles))
        {
            stale.Delete();
        }
    }
}
