using Samklang.Domain;
using Samklang.Resolver;
using Samklang.Resolver.PlayCache;
using Xunit;

namespace Samklang.Tests.Resolver.PlayCache;

/// <summary>
/// Exercises <see cref="PlayCacheFormatResolverLayer"/> against a real, synthetic cache-directory
/// fixture built under a temp folder (per issue #7's acceptance criteria — no real Apple Music
/// installation required). The audio probe itself is stubbed, since these tests are about the
/// file&lt;-&gt;Track matching heuristics, not container parsing (that's
/// <see cref="PlayCacheAudioFormatProbeTests"/>'s job).
/// </summary>
public sealed class PlayCacheFormatResolverLayerTests : IDisposable
{
    private const string LibraryId = "000000000C719DA9";

    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "samklang-playcache-layer-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
        }
    }

    private sealed class StubProbe : IAudioFileFormatProbe
    {
        private readonly Dictionary<string, AudioFileFormat?> _results = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, int> ProbeCounts { get; } = new(StringComparer.OrdinalIgnoreCase);

        public bool ThrowIoExceptionOnce { get; set; }

        public AudioFileFormat? this[string path]
        {
            set => _results[path] = value;
        }

        public AudioFileFormat? Probe(string filePath)
        {
            if (ThrowIoExceptionOnce)
            {
                ThrowIoExceptionOnce = false;
                throw new IOException("file locked");
            }

            ProbeCounts[filePath] = ProbeCounts.GetValueOrDefault(filePath) + 1;
            return _results.GetValueOrDefault(filePath);
        }
    }

    private sealed class FakeLocator(string? directory) : IPlayCacheLocator
    {
        public string? GetPlayCacheDirectory() => directory;
    }

    private PlayCacheFormatResolverLayer CreateLayer(StubProbe probe, DateTimeOffset? now = null, string? directoryOverride = null) =>
        new(new FakeLocator(directoryOverride ?? _directory), probe, now is null ? null : () => now.Value);

    private static Track SampleTrack(string title, string? album = null) => new(title, "Artist", album ?? string.Empty);

    private string CreateCacheFile(string relativePath, int ageMinutes = 0, DateTimeOffset? relativeToNow = null)
    {
        var path = Path.Combine(_directory, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, [0x00]);
        if (ageMinutes > 0)
        {
            // Old enough to defeat the freshly-written match — models a replayed cache file. Aged
            // relative to the same clock the layer is given (real wall-clock by default), so tests
            // injecting a fictional `now` don't accidentally look "fresh" against it.
            File.SetLastWriteTimeUtc(path, (relativeToNow ?? DateTimeOffset.UtcNow).AddMinutes(-ageMinutes).UtcDateTime);
        }

        return path;
    }

    private void WriteCacheInfo(params (DateTimeOffset AccessDateUtc, long CloudId)[] items)
    {
        var entries = string.Join(string.Empty, items.Select(item => $"""
                <dict>
                    <key>access-date</key>
                    <date>{item.AccessDateUtc:yyyy-MM-ddTHH:mm:ssZ}</date>
                    <key>cloud-id</key>
                    <integer>{item.CloudId}</integer>
                </dict>
        """));

        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "PlayCacheInfo.xml"), $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <plist version="1.0">
            <dict>
                <key>items</key>
                <array>
            {entries}
                </array>
            </dict>
            </plist>
            """);
    }

    [Fact]
    public void TryResolve_matches_a_download_temp_folder_and_resolves_at_exact_confidence()
    {
        var path = CreateCacheFile($"{LibraryId}\\Downloads\\My Song _ My Album.tmp\\download.mp3");
        var probe = new StubProbe { [path] = new AudioFileFormat(44_100, 24) };
        var layer = CreateLayer(probe);

        var result = layer.TryResolve(SampleTrack("My Song", "My Album"));

        Assert.NotNull(result);
        Assert.Equal(new DeviceFormat(44_100, 24), result!.Target);
        Assert.Equal(ResolutionConfidence.Exact, result.Confidence);
        Assert.Equal("PlayCache", result.SourceLayer);
        Assert.Equal(layer.Name, result.SourceLayer);
    }

    [Fact]
    public void TryResolve_matches_a_truncated_download_folder_name()
    {
        var folder = "Runner [V1] (feat. Lil Uzi Ver"; // truncated by Apple Music at ~30 chars
        var path = CreateCacheFile($"{LibraryId}\\Downloads\\{folder}.tmp\\download.m4a");
        var probe = new StubProbe { [path] = new AudioFileFormat(48_000, 24) };
        var layer = CreateLayer(probe);

        var result = layer.TryResolve(SampleTrack("Runner [V1] (feat. Lil Uzi Vert)", "Some Album"));

        Assert.NotNull(result);
        Assert.Equal(48_000, result!.Target.SampleRateHz);
    }

    [Fact]
    public void TryResolve_prefers_the_most_recently_written_matching_download_candidate()
    {
        var older = CreateCacheFile($"{LibraryId}\\Downloads\\My Song _ Old.tmp\\download.mp3");
        var newer = CreateCacheFile($"{LibraryId}\\Downloads\\My Song _ New.tmp\\download.m4a");
        File.SetLastWriteTimeUtc(older, DateTime.UtcNow.AddDays(-7));
        File.SetLastWriteTimeUtc(newer, DateTime.UtcNow);
        var probe = new StubProbe
        {
            [older] = new AudioFileFormat(44_100, 16),
            [newer] = new AudioFileFormat(96_000, 24),
        };
        var layer = CreateLayer(probe);

        var result = layer.TryResolve(SampleTrack("My Song"));

        Assert.NotNull(result);
        Assert.Equal(new DeviceFormat(96_000, 24), result!.Target);
    }

    [Fact]
    public void TryResolve_matches_a_freshly_written_file_when_no_download_folder_exists()
    {
        // Replayed/cached local tracks leave no Downloads folder — but the track's own
        // (re)download completing right at track start is a just-written media file.
        var stale = CreateCacheFile($"{LibraryId}\\02\\00\\00\\{LibraryId}-0000000000000457.mp3", ageMinutes: 10);
        var fresh = CreateCacheFile($"{LibraryId}\\02\\00\\01\\{LibraryId}-00000000000004D2.mp3");
        var probe = new StubProbe
        {
            [stale] = new AudioFileFormat(44_100, 16),
            [fresh] = new AudioFileFormat(48_000, 24),
        };
        var layer = CreateLayer(probe);

        var result = layer.TryResolve(SampleTrack("Some Replayed Song"));

        Assert.NotNull(result);
        Assert.Equal(48_000, result!.Target.SampleRateHz);
    }

    [Fact]
    public void TryResolve_prefers_a_file_currently_held_open_by_another_process_even_if_written_long_ago()
    {
        var path = CreateCacheFile($"{LibraryId}\\02\\00\\00\\{LibraryId}-0000000000000457.mp3", ageMinutes: 10);
        var probe = new StubProbe { [path] = new AudioFileFormat(44_100, 24) };
        var layer = CreateLayer(probe);

        using var holder = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var result = layer.TryResolve(SampleTrack("Some Replayed Song"));

        Assert.NotNull(result);
        Assert.Equal(44_100, result!.Target.SampleRateHz);
    }

    [Fact]
    public void TryResolve_skips_a_candidate_that_throws_during_probing_and_is_not_permanently_disabled_by_it()
    {
        var path = CreateCacheFile($"{LibraryId}\\Downloads\\My Song _ My Album.tmp\\download.mp3", ageMinutes: 10);
        var probe = new StubProbe
        {
            [path] = new AudioFileFormat(44_100, 24),
            ThrowIoExceptionOnce = true,
        };
        var layer = CreateLayer(probe);
        var track = SampleTrack("My Song", "My Album");

        // First lookup hits the (simulated) lock and has nothing else to fall back to.
        Assert.Null(layer.TryResolve(track));
        // The failure isn't cached — a later lookup for the same track succeeds.
        Assert.NotNull(layer.TryResolve(track));
    }

    [Fact]
    public void TryResolve_resolves_by_cloud_id_suffix_when_the_playcacheinfo_entry_is_fresh()
    {
        var now = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        // cloud-id 304158 = 0x4A41E -> file suffix 000000000004A41E. Aged well past the
        // fresh-write slack (relative to the injected `now`) so this only resolves via the
        // cloud-id heuristic, not the freshly-written one.
        var path = CreateCacheFile($"{LibraryId}\\02\\00\\00\\{LibraryId}-000000000004A41E.mp3", ageMinutes: 10, relativeToNow: now);
        WriteCacheInfo((now, 304158));
        var probe = new StubProbe { [path] = new AudioFileFormat(48_000, 24) };
        var layer = CreateLayer(probe, now);

        var result = layer.TryResolve(SampleTrack("My Song"));

        Assert.NotNull(result);
        Assert.Equal(48_000, result!.Target.SampleRateHz);
    }

    [Fact]
    public void TryResolve_ignores_a_stale_playcacheinfo_entry()
    {
        var now = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var path = CreateCacheFile($"{LibraryId}\\02\\00\\00\\{LibraryId}-000000000004A41E.mp3", ageMinutes: 10, relativeToNow: now);
        WriteCacheInfo((now.AddHours(-1), 304158));
        var probe = new StubProbe { [path] = new AudioFileFormat(48_000, 24) };
        var layer = CreateLayer(probe, now);

        Assert.Null(layer.TryResolve(SampleTrack("My Song")));
    }

    [Fact]
    public void TryResolve_falls_back_to_the_pinned_bit_depth_when_the_probe_cannot_determine_one()
    {
        var path = CreateCacheFile($"{LibraryId}\\Downloads\\My Song.tmp\\download.m4a");
        var probe = new StubProbe { [path] = new AudioFileFormat(44_100, null) }; // e.g. lossy AAC
        var layer = CreateLayer(probe);

        var result = layer.TryResolve(SampleTrack("My Song"));

        Assert.NotNull(result);
        Assert.Equal(FallbackFormatResolverLayer.PinnedBitDepth, result!.Target.BitDepth);
    }

    [Fact]
    public void TryResolve_returns_null_when_the_playcache_directory_does_not_exist()
    {
        var layer = CreateLayer(new StubProbe(), directoryOverride: Path.Combine(_directory, "does-not-exist"));

        Assert.Null(layer.TryResolve(SampleTrack("My Song")));
    }

    private sealed class ThrowingLocator : IPlayCacheLocator
    {
        public string? GetPlayCacheDirectory() => throw new InvalidOperationException("package lookup failed");
    }

    [Fact]
    public void TryResolve_returns_null_when_the_locator_throws()
    {
        var layer = new PlayCacheFormatResolverLayer(new ThrowingLocator(), new StubProbe());

        Assert.Null(layer.TryResolve(SampleTrack("My Song")));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryResolve_returns_null_without_scanning_when_the_track_title_is_blank(string? title)
    {
        var path = CreateCacheFile($"{LibraryId}\\Downloads\\My Song.tmp\\download.mp3");
        var probe = new StubProbe { [path] = new AudioFileFormat(44_100, 24) };
        var layer = CreateLayer(probe);

        Assert.Null(layer.TryResolve(new Track(title!, "Artist", string.Empty)));
        Assert.Empty(probe.ProbeCounts);
    }

    [Fact]
    public void TryResolve_returns_null_when_no_heuristic_matches_anything_in_an_unrelated_directory_layout()
    {
        // An empty (or unrecognized-layout) PlayCache directory must not throw — it just has
        // nothing to say for this Track.
        Directory.CreateDirectory(_directory);
        File.WriteAllBytes(Path.Combine(_directory, "unrelated.txt"), [0x00]);
        var layer = CreateLayer(new StubProbe());

        Assert.Null(layer.TryResolve(SampleTrack("My Song")));
    }

    [Fact]
    public void Name_is_PlayCache()
    {
        Assert.Equal("PlayCache", new PlayCacheFormatResolverLayer().Name);
    }
}
