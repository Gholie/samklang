using Samklang.Resolver.PlayCache;
using Xunit;

namespace Samklang.Tests.Resolver.PlayCache;

public sealed class PlayCacheInfoReaderTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "samklang-playcacheinfo-tests", Guid.NewGuid().ToString("N"));

    public PlayCacheInfoReaderTests() => Directory.CreateDirectory(_directory);

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

    private string InfoPath => Path.Combine(_directory, "PlayCacheInfo.xml");

    private void WriteEntries(params (DateTimeOffset AccessDateUtc, long CloudId)[] items)
    {
        var entries = string.Join(string.Empty, items.Select(item => $"""
                <dict>
                    <key>access-date</key>
                    <date>{item.AccessDateUtc:yyyy-MM-ddTHH:mm:ssZ}</date>
                    <key>cloud-id</key>
                    <integer>{item.CloudId}</integer>
                    <key>file-size</key>
                    <integer>1</integer>
                </dict>
        """));

        File.WriteAllText(InfoPath, $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
            <plist version="1.0">
            <dict>
                <key>cache-size</key>
                <integer>624090552</integer>
                <key>items</key>
                <array>
            {entries}
                </array>
            </dict>
            </plist>
            """);
    }

    [Fact]
    public void TryReadNewestEntry_returns_the_single_entry()
    {
        var accessDate = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        WriteEntries((accessDate, 304158));

        var result = PlayCacheInfoReader.TryReadNewestEntry(InfoPath);

        Assert.NotNull(result);
        Assert.Equal(accessDate, result!.AccessDateUtc);
        Assert.Equal(304158, result.CloudId);
    }

    [Fact]
    public void TryReadNewestEntry_picks_the_entry_with_the_latest_access_date_regardless_of_array_order()
    {
        var older = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var newer = new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero);
        WriteEntries((older, 1111), (newer, 2222));

        var result = PlayCacheInfoReader.TryReadNewestEntry(InfoPath);

        Assert.NotNull(result);
        Assert.Equal(2222, result!.CloudId);
        Assert.Equal(newer, result.AccessDateUtc);
    }

    [Fact]
    public void TryReadNewestEntry_returns_null_when_the_file_does_not_exist()
    {
        Assert.Null(PlayCacheInfoReader.TryReadNewestEntry(InfoPath));
    }

    [Fact]
    public void TryReadNewestEntry_returns_null_for_malformed_xml_instead_of_throwing()
    {
        File.WriteAllText(InfoPath, "<not-even-close-to-a-plist");

        Assert.Null(PlayCacheInfoReader.TryReadNewestEntry(InfoPath));
    }

    [Fact]
    public void TryReadNewestEntry_ignores_an_entry_missing_a_cloud_id()
    {
        File.WriteAllText(InfoPath, """
            <?xml version="1.0" encoding="UTF-8"?>
            <plist version="1.0">
            <dict>
                <key>items</key>
                <array>
                    <dict>
                        <key>access-date</key>
                        <date>2026-01-01T00:00:00Z</date>
                    </dict>
                </array>
            </dict>
            </plist>
            """);

        Assert.Null(PlayCacheInfoReader.TryReadNewestEntry(InfoPath));
    }

    [Fact]
    public void TryReadNewestEntry_does_not_fetch_the_declared_external_dtd()
    {
        // A regression guard: if XmlResolver isn't disabled, XDocument.Load would try to fetch
        // http://www.apple.com/DTDs/PropertyList-1.0.dtd over the network — this layer must work
        // fully offline. Success here (rather than a network-related exception) is the assertion.
        var accessDate = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        WriteEntries((accessDate, 42));

        var result = PlayCacheInfoReader.TryReadNewestEntry(InfoPath);

        Assert.NotNull(result);
    }
}
