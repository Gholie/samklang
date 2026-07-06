using Samklang.Domain;

namespace Samklang.Resolver;

/// <summary>
/// One layer of the Layered Resolver (CONTEXT.md: catalog match → local cache analysis →
/// tier fallback). A layer either produces a Format Resolution for the given Track, or
/// returns null to let the chain fall through to the next layer.
/// </summary>
public interface IFormatResolverLayer
{
    /// <summary>Short, stable name for this layer, surfaced as <see cref="FormatResolution.SourceLayer"/>.</summary>
    string Name { get; }

    /// <summary>Attempts to resolve the Track's Target Format. Returns null if this layer has nothing to say.</summary>
    FormatResolution? TryResolve(Track track);
}
