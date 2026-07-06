using Samklang.Domain;
using Samklang.Resolver;
using Xunit;

namespace Samklang.Tests.Resolver;

public class FallbackFormatResolverLayerTests
{
    [Fact]
    public void TryResolve_always_returns_the_24_bit_pinned_fallback_at_fallback_confidence()
    {
        var layer = new FallbackFormatResolverLayer();
        var track = new Track("Title", "Artist", "Album");

        var result = layer.TryResolve(track);

        Assert.NotNull(result);
        Assert.Equal(new DeviceFormat(FallbackFormatResolverLayer.FallbackSampleRateHz, FallbackFormatResolverLayer.PinnedBitDepth), result!.Target);
        Assert.Equal(24, result.Target.BitDepth);
        Assert.Equal(ResolutionConfidence.Fallback, result.Confidence);
        Assert.Equal(layer.Name, result.SourceLayer);
    }
}
