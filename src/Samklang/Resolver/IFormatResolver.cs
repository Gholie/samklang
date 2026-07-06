using Samklang.Domain;

namespace Samklang.Resolver;

/// <summary>
/// The Layered Resolver's public contract: performs Format Resolution for a Track, producing
/// a Target Format and the Confidence behind it. Implemented by <see cref="FormatResolverChain"/>.
/// </summary>
public interface IFormatResolver
{
    FormatResolution Resolve(Track track);
}
