using Samklang.Domain;
using Xunit;

namespace Samklang.Tests.Domain;

public class RateFamilyClampTests
{
    [Fact]
    public void Clamp_returns_the_requested_rate_when_the_device_supports_it_outright()
    {
        var requested = new DeviceFormat(96_000, 24);
        var supported = new HashSet<int> { 44_100, 48_000, 96_000 };

        var result = RateFamilyClamp.Clamp(requested, supported);

        Assert.Equal(requested, result);
    }

    [Fact]
    public void Clamp_falls_back_to_the_highest_supported_rate_in_the_same_forty_four_family()
    {
        // Requested rate (176.4k) is in the 44.1k family; the device only supports 44.1k and
        // 88.2k in that family (plus an unrelated 48k-family rate) — expect the highest of the two.
        var requested = new DeviceFormat(176_400, 24);
        var supported = new HashSet<int> { 44_100, 88_200, 48_000 };

        var result = RateFamilyClamp.Clamp(requested, supported);

        Assert.Equal(new DeviceFormat(88_200, 24), result);
    }

    [Fact]
    public void Clamp_falls_back_to_the_highest_supported_rate_in_the_same_forty_eight_family()
    {
        // Requested rate (192k) is in the 48k family; the device only supports 48k and 96k in
        // that family (plus an unrelated 44.1k-family rate) — expect the highest of the two.
        var requested = new DeviceFormat(192_000, 24);
        var supported = new HashSet<int> { 48_000, 96_000, 44_100 };

        var result = RateFamilyClamp.Clamp(requested, supported);

        Assert.Equal(new DeviceFormat(96_000, 24), result);
    }

    [Fact]
    public void Clamp_crosses_families_only_when_the_requested_family_has_no_supported_rate_at_all()
    {
        // Requested rate (44.1k family) has nothing supported in its own family; the device only
        // supports 48k-family rates, so we cross families and take the highest of those.
        var requested = new DeviceFormat(44_100, 24);
        var supported = new HashSet<int> { 48_000, 96_000 };

        var result = RateFamilyClamp.Clamp(requested, supported);

        Assert.Equal(new DeviceFormat(96_000, 24), result);
    }

    [Fact]
    public void Clamp_leaves_bit_depth_untouched()
    {
        var requested = new DeviceFormat(44_100, 16);
        var supported = new HashSet<int> { 48_000 };

        var result = RateFamilyClamp.Clamp(requested, supported);

        Assert.Equal(16, result.BitDepth);
    }

    [Fact]
    public void Clamp_returns_the_requested_format_unchanged_when_nothing_is_known_to_be_supported()
    {
        var requested = new DeviceFormat(96_000, 24);

        var result = RateFamilyClamp.Clamp(requested, new HashSet<int>());

        Assert.Equal(requested, result);
    }
}
