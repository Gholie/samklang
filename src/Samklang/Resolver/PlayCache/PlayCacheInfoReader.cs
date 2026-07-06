using System.Globalization;
using System.IO;
using System.Xml;
using System.Xml.Linq;

namespace Samklang.Resolver.PlayCache;

/// <summary>The newest PlayCacheInfo.xml entry: when a cached item was last accessed, and the cloud-id that names its file.</summary>
public sealed record PlayCacheInfoEntry(DateTimeOffset AccessDateUtc, long CloudId);

/// <summary>
/// Reads the Apple Music PlayCache's own item-metadata plist (<c>PlayCacheInfo.xml</c>) for the
/// most recently accessed cached item's cloud-id — the "cloud-id correlation via the app's
/// preferences/cache metadata" signal called out in issue #7. This is an XML-format plist (not the
/// binary plist format some other Apple Music prefs use), so a plain <see cref="XDocument"/> parse
/// is enough; the external Apple DTD it declares must be ignored, not fetched, both because that
/// would require network access this layer must work without and because it would be slow.
/// </summary>
public static class PlayCacheInfoReader
{
    public static PlayCacheInfoEntry? TryReadNewestEntry(string playCacheInfoPath)
    {
        if (!File.Exists(playCacheInfoPath))
        {
            return null;
        }

        try
        {
            using var stream = new FileStream(playCacheInfoPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = XmlReader.Create(stream, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Ignore,
                XmlResolver = null,
            });
            var document = XDocument.Load(reader);

            PlayCacheInfoEntry? newest = null;
            foreach (var dict in document.Descendants("array").SelectMany(array => array.Elements("dict")))
            {
                var entry = ParseEntry(dict);
                if (entry is not null && (newest is null || entry.AccessDateUtc > newest.AccessDateUtc))
                {
                    newest = entry;
                }
            }

            return newest;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or XmlException)
        {
            // Missing/locked/malformed metadata file — this signal just isn't available right
            // now; the layer's other heuristics (locked/fresh file, download-folder name) still run.
            return null;
        }
    }

    /// <summary>
    /// A plist <c>&lt;dict&gt;</c> is a flat sequence of alternating <c>&lt;key&gt;</c> elements
    /// and value elements (e.g. <c>&lt;date&gt;</c>, <c>&lt;integer&gt;</c>) — not nested
    /// key/value pairs — so this walks the children two at a time.
    /// </summary>
    private static PlayCacheInfoEntry? ParseEntry(XElement dict)
    {
        DateTimeOffset? accessDate = null;
        long cloudId = 0;

        var children = dict.Elements().ToList();
        for (var i = 0; i + 1 < children.Count; i += 2)
        {
            var key = children[i].Value;
            var value = children[i + 1].Value;
            if (key == "access-date" &&
                DateTimeOffset.TryParse(
                    value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsedDate))
            {
                accessDate = parsedDate;
            }
            else if (key == "cloud-id" && long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedId))
            {
                cloudId = parsedId;
            }
        }

        return accessDate is not null && cloudId > 0 ? new PlayCacheInfoEntry(accessDate.Value, cloudId) : null;
    }
}
