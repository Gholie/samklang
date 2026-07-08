using Samklang.Domain;

namespace Samklang.Resolver.Catalog;

/// <summary>
/// Parses an enhanced-HLS manifest (the playlist behind a catalog track's
/// <c>extendedAssetUrls</c>, per docs/adr/0001) for the highest-rate lossless (ALAC) variant's
/// literal <c>SAMPLE-RATE</c> and bit-depth attributes. Pure text parsing, no I/O — the manifest
/// text is fetched by <see cref="IAppleMusicCatalogClient"/> and handed in here so this logic is
/// unit-testable against fixture files with no network involved.
///
/// <para>
/// Two manifest shapes are supported, because Apple has served both. Live manifests (observed
/// 2026-07-08, see the <c>media-group-alac.m3u8</c> fixture) mark lossless variants with
/// <c>CODECS="alac"</c> only and carry <c>SAMPLE-RATE</c>/<c>BIT-DEPTH</c> on the
/// <c>#EXT-X-MEDIA</c> rendition the variant references via <c>AUDIO="GROUP-ID"</c>; the shape
/// this parser originally targeted puts an <c>AUDIO-FORMAT="alac"</c> attribute and the format
/// numbers inline on the <c>#EXT-X-STREAM-INF</c> line itself. Inline attributes win when both
/// are present.
/// </para>
/// </summary>
public static class EnhancedHlsManifestParser
{
    private const string StreamInfPrefix = "#EXT-X-STREAM-INF:";
    private const string MediaPrefix = "#EXT-X-MEDIA:";

    /// <summary>
    /// Returns the (sample rate, bit depth) of the best ALAC variant in the manifest — "best"
    /// meaning highest sample rate, then highest bit depth as a tie-break — or null if the
    /// manifest has no ALAC (lossless) variant at all (e.g. a lossy-only or Atmos-only track,
    /// which this layer has nothing Exact to say about).
    /// </summary>
    public static DeviceFormat? ParseBestLosslessFormat(string manifestText)
    {
        var lines = manifestText.Split('\n');

        // First pass: the audio rendition groups, keyed by GROUP-ID, so a variant that only says
        // CODECS="alac" can pull its SAMPLE-RATE/BIT-DEPTH from the group it references.
        var audioGroups = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (!line.StartsWith(MediaPrefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var attributes = ParseAttributes(line[MediaPrefix.Length..]);
            if (attributes.TryGetValue("TYPE", out var type) && type == "AUDIO" &&
                attributes.TryGetValue("GROUP-ID", out var groupId))
            {
                audioGroups[groupId] = attributes;
            }
        }

        DeviceFormat? best = null;
        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (!line.StartsWith(StreamInfPrefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var attributes = ParseAttributes(line[StreamInfPrefix.Length..]);

            var isAlac =
                (attributes.TryGetValue("AUDIO-FORMAT", out var audioFormat) && audioFormat.Contains("alac", StringComparison.OrdinalIgnoreCase)) ||
                (attributes.TryGetValue("CODECS", out var codecs) && codecs.Contains("alac", StringComparison.OrdinalIgnoreCase));
            if (!isAlac)
            {
                continue;
            }

            var group = attributes.TryGetValue("AUDIO", out var audioGroupId) && audioGroups.TryGetValue(audioGroupId, out var groupAttributes)
                ? groupAttributes
                : null;

            if (ReadInt(attributes, group, "SAMPLE-RATE") is not { } sampleRateHz)
            {
                continue;
            }

            var bitDepth = ReadInt(attributes, group, "BIT-DEPTH") ?? FallbackFormatResolverLayer.PinnedBitDepth;

            var candidate = new DeviceFormat(sampleRateHz, bitDepth);
            if (best is null || IsBetter(candidate, best.Value))
            {
                best = candidate;
            }
        }

        return best;
    }

    /// <summary>An integer attribute read from the variant's own line first, then from its referenced audio rendition group.</summary>
    private static int? ReadInt(
        Dictionary<string, string> streamAttributes, Dictionary<string, string>? groupAttributes, string key)
    {
        if (streamAttributes.TryGetValue(key, out var text) && int.TryParse(text, out var value))
        {
            return value;
        }

        if (groupAttributes is not null && groupAttributes.TryGetValue(key, out text) && int.TryParse(text, out value))
        {
            return value;
        }

        return null;
    }

    private static bool IsBetter(DeviceFormat candidate, DeviceFormat current) =>
        candidate.SampleRateHz > current.SampleRateHz ||
        (candidate.SampleRateHz == current.SampleRateHz && candidate.BitDepth > current.BitDepth);

    /// <summary>
    /// Parses an HLS attribute list (comma-separated <c>KEY=VALUE</c> pairs, values optionally
    /// quoted, quoted values may themselves contain commas) into a lookup, unquoting string
    /// values. Attribute names are matched case-sensitively per the HLS spec's own casing
    /// convention (all-caps), which is what real manifests use.
    /// </summary>
    private static Dictionary<string, string> ParseAttributes(string attributeList)
    {
        var attributes = new Dictionary<string, string>(StringComparer.Ordinal);
        var span = attributeList.AsSpan();
        var position = 0;

        while (position < span.Length)
        {
            var equalsIndex = span[position..].IndexOf('=');
            if (equalsIndex < 0)
            {
                break;
            }

            var key = span.Slice(position, equalsIndex).Trim().ToString();
            position += equalsIndex + 1;

            string value;
            if (position < span.Length && span[position] == '"')
            {
                position++; // skip opening quote
                var closingQuote = span[position..].IndexOf('"');
                if (closingQuote < 0)
                {
                    value = span[position..].ToString();
                    position = span.Length;
                }
                else
                {
                    value = span.Slice(position, closingQuote).ToString();
                    position += closingQuote + 1; // skip closing quote
                }
            }
            else
            {
                var nextComma = span[position..].IndexOf(',');
                value = (nextComma < 0 ? span[position..] : span.Slice(position, nextComma)).Trim().ToString();
                position += nextComma < 0 ? span.Length - position : nextComma;
            }

            attributes[key] = value;

            // Skip past the separating comma, if any.
            var commaIndex = span[position..].IndexOf(',');
            position += commaIndex < 0 ? span.Length - position : commaIndex + 1;
        }

        return attributes;
    }
}
