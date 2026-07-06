using Samklang.Resolver.Catalog;
using Xunit;

namespace Samklang.Tests.Resolver.Catalog;

public class WindowsRegionStorefrontProviderTests
{
    [Fact]
    public void GetStorefront_uses_the_settings_override_when_present()
    {
        var provider = new WindowsRegionStorefrontProvider(() => "gb", () => "US");

        Assert.Equal("gb", provider.GetStorefront());
    }

    [Fact]
    public void GetStorefront_lower_cases_and_trims_the_override()
    {
        var provider = new WindowsRegionStorefrontProvider(() => "  NO ", () => "US");

        Assert.Equal("no", provider.GetStorefront());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void GetStorefront_falls_back_to_the_detected_region_when_no_override_is_set(string? overrideValue)
    {
        var provider = new WindowsRegionStorefrontProvider(() => overrideValue, () => "NO");

        Assert.Equal("no", provider.GetStorefront());
    }

    [Fact]
    public void GetStorefront_falls_back_to_the_default_storefront_when_region_detection_throws()
    {
        var provider = new WindowsRegionStorefrontProvider(() => null, () => throw new InvalidOperationException("no region configured"));

        Assert.Equal(WindowsRegionStorefrontProvider.DefaultStorefront, provider.GetStorefront());
    }

    [Fact]
    public void GetStorefront_falls_back_to_the_default_storefront_when_the_region_accessor_returns_blank()
    {
        var provider = new WindowsRegionStorefrontProvider(() => null, () => string.Empty);

        Assert.Equal(WindowsRegionStorefrontProvider.DefaultStorefront, provider.GetStorefront());
    }
}
