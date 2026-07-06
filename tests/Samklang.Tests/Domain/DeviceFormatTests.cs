using Samklang.Domain;
using Xunit;

namespace Samklang.Tests.Domain;

public class DeviceFormatTests
{
    [Theory]
    [InlineData(44100, 16, "16-bit/44.1 kHz")]
    [InlineData(96000, 24, "24-bit/96 kHz")]
    [InlineData(192000, 24, "24-bit/192 kHz")]
    public void ToString_reads_like_the_sound_control_panel(int rate, int depth, string expected) =>
        Assert.Equal(expected, new DeviceFormat(rate, depth).ToString());

    [Theory]
    [InlineData(44100, 176400, true)]   // both 44.1k family
    [InlineData(48000, 192000, true)]   // both 48k family
    [InlineData(44100, 48000, false)]
    [InlineData(176400, 192000, false)]
    public void Rate_family_groups_integer_multiples(int a, int b, bool same) =>
        Assert.Equal(same, new DeviceFormat(a, 24).SharesRateFamilyWith(new DeviceFormat(b, 24)));
}
