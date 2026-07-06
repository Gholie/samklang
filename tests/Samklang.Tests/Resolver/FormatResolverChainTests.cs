using Samklang.Domain;
using Samklang.Resolver;
using Xunit;

namespace Samklang.Tests.Resolver;

public class FormatResolverChainTests
{
    private static readonly Track SomeTrack = new("Title", "Artist", "Album");

    private sealed class FakeLayer(string name, FormatResolution? result) : IFormatResolverLayer
    {
        public int CallCount { get; private set; }
        public string Name => name;

        public FormatResolution? TryResolve(Track track)
        {
            CallCount++;
            return result;
        }
    }

    [Fact]
    public void Resolve_returns_the_first_layers_result_without_consulting_later_layers()
    {
        var expected = new FormatResolution(new DeviceFormat(96_000, 24), ResolutionConfidence.Exact, "Catalog match");
        var first = new FakeLayer("Catalog match", expected);
        var second = new FakeLayer("Tier fallback", new FormatResolution(new DeviceFormat(44_100, 24), ResolutionConfidence.Fallback, "Tier fallback"));
        var chain = new FormatResolverChain([first, second]);

        var result = chain.Resolve(SomeTrack);

        Assert.Same(expected, result);
        Assert.Equal(1, first.CallCount);
        Assert.Equal(0, second.CallCount);
    }

    [Fact]
    public void Resolve_falls_through_to_the_next_layer_when_the_first_returns_null()
    {
        var first = new FakeLayer("Catalog match", null);
        var expected = new FormatResolution(new DeviceFormat(44_100, 24), ResolutionConfidence.Fallback, "Tier fallback");
        var second = new FakeLayer("Tier fallback", expected);
        var chain = new FormatResolverChain([first, second]);

        var result = chain.Resolve(SomeTrack);

        Assert.Same(expected, result);
        Assert.Equal(1, first.CallCount);
        Assert.Equal(1, second.CallCount);
    }

    [Fact]
    public void Resolve_throws_when_no_layer_in_the_chain_resolves()
    {
        var chain = new FormatResolverChain([new FakeLayer("Catalog match", null)]);

        Assert.Throws<InvalidOperationException>(() => chain.Resolve(SomeTrack));
    }

    [Fact]
    public void Constructor_rejects_an_empty_chain()
    {
        Assert.Throws<ArgumentException>(() => new FormatResolverChain([]));
    }
}
