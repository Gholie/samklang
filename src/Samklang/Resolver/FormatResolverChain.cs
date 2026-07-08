using Samklang.Domain;
using Samklang.Logging;

namespace Samklang.Resolver;

/// <summary>
/// The Layered Resolver itself: tries each <see cref="IFormatResolverLayer"/> in order and
/// returns the first Format Resolution produced. Later issues plug in the catalog-match and
/// PlayCache-analysis layers ahead of the tier-fallback layer this issue ships; the chain
/// doesn't change shape when they arrive, only the layer list passed to the constructor does.
/// </summary>
public sealed class FormatResolverChain : IFormatResolver
{
    private readonly IReadOnlyList<IFormatResolverLayer> _layers;

    public FormatResolverChain(IReadOnlyList<IFormatResolverLayer> layers)
    {
        if (layers.Count == 0)
        {
            throw new ArgumentException("A resolver chain needs at least one layer.", nameof(layers));
        }

        _layers = layers;
    }

    public FormatResolution Resolve(Track track)
    {
        foreach (var layer in _layers)
        {
            var resolution = layer.TryResolve(track);
            if (resolution is not null)
            {
                AppLog.Info(
                    $"Resolved \"{track.Title}\" — {track.Artist} via {layer.Name}: " +
                    $"{resolution.Target.SampleRateHz} Hz / {resolution.Target.BitDepth}-bit ({resolution.Confidence}).");
                return resolution;
            }
        }

        throw new InvalidOperationException(
            "No resolver layer produced a Format Resolution. The chain must end in a layer " +
            "that always resolves (the tier-fallback layer never returns null).");
    }
}
