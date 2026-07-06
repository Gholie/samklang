using Samklang.Domain;

namespace Samklang.Resolver.Catalog;

/// <summary>
/// Parses an enhanced-HLS manifest (the playlist behind a catalog track's
/// <c>extendedAssetUrls</c>, per docs/adr/0001) for the highest-rate lossless (ALAC) variant's
/// literal <c>SAMPLE-RATE</c> and bit-depth attributes. Pure text parsing, no I/O — the manifest
/// text is fetched by <see cref="IAppleMusicCatalogClient"/> and handed in here so this logic is
/// unit-testable against fixture files with no network involved.
/// </summary>
public static class EnhancedHlsManifestParser
{
    private const string StreamInfPrefix = "#EXT-X-STREAM-INF:";

    /// <summary>
    /// Returns the (sample rate, bit depth) of the best ALAC variant in the manifest — "best"
    /// meaning highest sample rate, then highest bit depth as a tie-break — or null if the
    /// manifest has no ALAC (lossless) variant at all (e.g. a lossy-only or Atmos-only track,
    /// which this layer has nothing Exact to say about).
    /// </summary>
    public static DeviceFormat? ParseBestLosslessFormat(string manifestText)
    {
        DeviceFormat? best = null;

        foreach (var rawLine in manifestText.Split('\n'))
        {
            var line = rawLine.Trim().TrimEnd('\r');
            if (!line.StartsWith(StreamInfPrefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var attributes = ParseAttributes(line[StreamInfPrefix.Length..]);

            if (!attributes.TryGetValue("AUDIO-FORMAT", out var audioFormat) ||
                !audioFormat.Contains("alac", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!attributes.TryGetValue("SAMPLE-RATE", out var sampleRateText) ||
                !int.TryParse(sampleRateText, out var sampleRateHz))
            {
                continue;
            }

            var bitDepth = attributes.TryGetValue("BIT-DEPTH", out var bitDepthText) && int.TryParse(bitDepthText, out var parsedBitDepth)
                ? parsedBitDepth
                : FallbackFormatResolverLayer.PinnedBitDepth;

            var candidate = new DeviceFormat(sampleRateHz, bitDepth);
            if (best is null || IsBetter(candidate, best.Value))
            {
                best = candidate;
            }
        }

        return best;
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
