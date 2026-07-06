using System.IO;
using Samklang.Domain;

namespace Samklang.Resolver.PlayCache;

/// <summary>
/// The middle layer of the Layered Resolver (CONTEXT.md): offline Exact-confidence resolution by
/// probing the Apple Music Windows app's local PlayCache for the file backing the currently
/// playing Track and reading its real sample rate/bit depth straight out of the container header
/// (<see cref="PlayCacheAudioFormatProbe"/> — never decodes audio). Needs no network access, so it
/// acts as a tie-breaker/fallback while the catalog layer (issue #6) is unavailable or ambiguous,
/// per docs/PLAN.md's M3.
///
/// <para>
/// File&#8596;Track correlation is heuristic — PlayCache carries no track id GSMTC could hand
/// back — using three signals in order, each one falling through to the next
/// (adapted from WindowsLosslessSwitcher, GPL-3.0, per docs/PLAN.md's "mine for ideas" decision):
/// <list type="number">
/// <item>an in-progress download's <c>Downloads\&lt;Title&gt; _ &lt;Album&gt;.tmp\download.*</c>
/// folder name matches the Track (<see cref="PlayCacheFileMatcher.TitleMatchesDownloadFolder"/>);</item>
/// <item>a cached media file the Apple Music process currently holds open — or, failing that, one
/// written within <see cref="FreshWriteSlack"/> of this call — is the Track now (re)loading or
/// (re)downloading. There's no per-Track detection timestamp in this codebase's
/// <see cref="Track"/> (unlike prior art's), but <c>TryResolve</c> is always called synchronously
/// right when <c>TrackSyncCoordinator.OnTrackChanged</c> fires, so the injected clock evaluated at
/// call time is already a good proxy for "when this Track was detected";</item>
/// <item><c>PlayCacheInfo.xml</c>'s most recently accessed entry, if fresh enough
/// (<see cref="AccessDateSlack"/>), names the current file by its cloud-id suffix.</item>
/// </list>
/// </para>
///
/// <para>
/// Failure handling (issue #7 acceptance criteria: a missing or restructured cache must not break
/// anything user-facing). A missing PlayCache directory, or any unexpected on-disk layout
/// encountered while walking it, makes this layer return null exactly as if it had nothing to say
/// — the chain falls through to a lower-confidence layer. A single locked/corrupt candidate file
/// only skips that candidate; the other two heuristics (and the next Track) still get a fair try.
/// </para>
/// </summary>
public sealed class PlayCacheFormatResolverLayer : IFormatResolverLayer
{
    private static readonly string[] CandidateExtensions = [".mp3", ".m4a"];

    /// <summary>A cache file written within this window of a <see cref="TryResolve"/> call counts as the current Track's own (re)download completing.</summary>
    public static readonly TimeSpan FreshWriteSlack = TimeSpan.FromSeconds(15);

    /// <summary>A PlayCacheInfo.xml entry is only trusted when its access-date is no older than this window before the call.</summary>
    public static readonly TimeSpan AccessDateSlack = TimeSpan.FromMinutes(2);

    private readonly Func<string?> _playCacheDirectoryAccessor;
    private readonly IAudioFileFormatProbe _probe;
    private readonly Func<DateTimeOffset> _now;

    public PlayCacheFormatResolverLayer(PlayCachePaths? paths = null, IAudioFileFormatProbe? probe = null)
        : this(() => (paths ?? new PlayCachePaths()).PlayCacheDirectory, probe ?? new PlayCacheAudioFormatProbe(), now: null)
    {
    }

    /// <param name="playCacheDirectoryAccessor">Reads the current PlayCache directory path (or null if unresolvable). Re-invoked on every call so tests can point it at a fixture directory.</param>
    /// <param name="probe">Reads sample rate/bit depth from a candidate file's container header.</param>
    /// <param name="now">Clock used for freshness comparisons; defaults to the real UTC clock.</param>
    internal PlayCacheFormatResolverLayer(
        Func<string?> playCacheDirectoryAccessor,
        IAudioFileFormatProbe probe,
        Func<DateTimeOffset>? now = null)
    {
        _playCacheDirectoryAccessor = playCacheDirectoryAccessor;
        _probe = probe;
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    public string Name => "PlayCache";

    public FormatResolution? TryResolve(Track track)
    {
        if (string.IsNullOrWhiteSpace(track.Title))
        {
            return null;
        }

        try
        {
            var directory = _playCacheDirectoryAccessor();
            if (directory is null || !Directory.Exists(directory))
            {
                return null;
            }

            var now = _now();
            var format =
                MatchDownloadTemp(directory, track) ??
                MatchInUseOrFreshlyWritten(directory, now) ??
                MatchRecentCacheInfoEntry(directory, now);

            return format is null ? null : ToResolution(format);
        }
        catch
        {
            // An unexpected/restructured PlayCache layout (or any other unpredictable I/O
            // problem) — this layer just has nothing to say for this Track; the chain falls
            // through to a lower-confidence layer rather than the app breaking over it.
            return null;
        }
    }

    private FormatResolution ToResolution(AudioFileFormat format) =>
        new(
            new DeviceFormat(format.SampleRateHz, format.BitDepth ?? FallbackFormatResolverLayer.PinnedBitDepth),
            ResolutionConfidence.Exact,
            Name);

    private AudioFileFormat? MatchDownloadTemp(string directory, Track track)
    {
        var candidates = EnumerateCandidateFiles(directory, "download.*")
            .Where(file => file.Directory?.Name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) == true)
            .OrderByDescending(file => file.LastWriteTimeUtc);

        foreach (var file in candidates)
        {
            var folderName = file.Directory!.Name[..^".tmp".Length];
            if (!PlayCacheFileMatcher.TitleMatchesDownloadFolder(track.Title, track.Album, folderName))
            {
                continue;
            }

            if (TryProbe(file) is { } format)
            {
                return format;
            }
        }

        return null;
    }

    private AudioFileFormat? MatchInUseOrFreshlyWritten(string directory, DateTimeOffset now)
    {
        var files = EnumerateCandidateFiles(directory, "*").OrderByDescending(file => file.LastWriteTimeUtc).ToList();

        foreach (var file in files.Where(IsHeldOpenByAnotherProcess))
        {
            if (TryProbe(file) is { } format)
            {
                return format;
            }
        }

        var freshCutoff = (now - FreshWriteSlack).UtcDateTime;
        foreach (var file in files.Where(file => file.LastWriteTimeUtc >= freshCutoff))
        {
            if (TryProbe(file) is { } format)
            {
                return format;
            }
        }

        return null;
    }

    private AudioFileFormat? MatchRecentCacheInfoEntry(string directory, DateTimeOffset now)
    {
        var entry = PlayCacheInfoReader.TryReadNewestEntry(Path.Combine(directory, "PlayCacheInfo.xml"));
        if (entry is null || entry.AccessDateUtc < now - AccessDateSlack)
        {
            return null;
        }

        var suffix = PlayCacheFileMatcher.CloudIdSuffix(entry.CloudId);
        var file = EnumerateCandidateFiles(directory, "*")
            .FirstOrDefault(f => Path.GetFileNameWithoutExtension(f.Name).EndsWith(suffix, StringComparison.OrdinalIgnoreCase));

        return file is null ? null : TryProbe(file);
    }

    private static IEnumerable<FileInfo> EnumerateCandidateFiles(string directory, string searchPattern) =>
        Directory.EnumerateFiles(directory, searchPattern, SearchOption.AllDirectories)
            .Where(path => CandidateExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            .Select(path => new FileInfo(path));

    /// <summary>Whether another process (Apple Music, playing or loading it) currently has this file open at all.</summary>
    private static bool IsHeldOpenByAnotherProcess(FileInfo file)
    {
        try
        {
            using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.None);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }

    private AudioFileFormat? TryProbe(FileInfo file)
    {
        try
        {
            var format = _probe.Probe(file.FullName);
            return format is { SampleRateHz: > 0 } ? format : null;
        }
        catch (IOException)
        {
            // Locked right now — try a different candidate, or the same file again on the next
            // Track's lookup; the failure is deliberately not cached.
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}
