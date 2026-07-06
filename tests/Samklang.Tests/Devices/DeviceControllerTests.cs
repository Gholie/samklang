using Samklang.Devices;
using Samklang.Domain;
using Xunit;

namespace Samklang.Tests.Devices;

public class DeviceControllerTests
{
    private sealed class FakeAudioEndpoint : IAudioEndpoint
    {
        public DeviceFormat? Current { get; set; }
        public List<string> Calls { get; } = [];

        public DeviceFormat? GetCurrentFormat()
        {
            Calls.Add("get");
            return Current;
        }

        public void SetFormat(DeviceFormat format)
        {
            Calls.Add($"set:{format}");
            Current = format;
        }

        public void SetMuted(bool muted) => Calls.Add(muted ? "mute" : "unmute");

        public IReadOnlySet<int> GetSupportedSampleRates(int bitDepth) => new HashSet<int>();
    }

    private sealed class ThrowingFakeAudioEndpoint : IAudioEndpoint
    {
        public DeviceFormat? Current { get; set; }
        public List<string> Calls { get; } = [];

        public DeviceFormat? GetCurrentFormat() => Current;

        public void SetFormat(DeviceFormat format)
        {
            Calls.Add("set-attempted");
            throw new InvalidOperationException("switch failed");
        }

        public void SetMuted(bool muted) => Calls.Add(muted ? "mute" : "unmute");

        public IReadOnlySet<int> GetSupportedSampleRates(int bitDepth) => new HashSet<int>();
    }

    [Fact]
    public void ApplyTargetFormat_skips_the_switch_when_already_at_the_target_format()
    {
        var target = new DeviceFormat(48_000, 24);
        var endpoint = new FakeAudioEndpoint { Current = target };
        var controller = new DeviceController(endpoint);

        var switched = controller.ApplyTargetFormat(target);

        Assert.False(switched);
        Assert.DoesNotContain(endpoint.Calls, call => call is "mute" or "unmute" || call.StartsWith("set:"));
    }

    [Fact]
    public void ApplyTargetFormat_mutes_switches_and_unmutes_in_order_when_the_format_differs()
    {
        var endpoint = new FakeAudioEndpoint { Current = new DeviceFormat(48_000, 16) };
        var controller = new DeviceController(endpoint);
        var target = new DeviceFormat(44_100, 24);

        var switched = controller.ApplyTargetFormat(target);

        Assert.True(switched);
        Assert.Equal(["get", "mute", $"set:{target}", "unmute"], endpoint.Calls);
        Assert.Equal(target, endpoint.Current);
    }

    [Fact]
    public void ApplyTargetFormat_still_unmutes_when_the_switch_throws()
    {
        var endpoint = new ThrowingFakeAudioEndpoint { Current = new DeviceFormat(48_000, 16) };
        var controller = new DeviceController(endpoint);

        Assert.Throws<InvalidOperationException>(() => controller.ApplyTargetFormat(new DeviceFormat(44_100, 24)));
        Assert.Equal(["mute", "set-attempted", "unmute"], endpoint.Calls);
    }

    [Fact]
    public void GetCurrentFormat_delegates_to_the_endpoint()
    {
        var endpoint = new FakeAudioEndpoint { Current = new DeviceFormat(96_000, 24) };
        var controller = new DeviceController(endpoint);

        Assert.Equal(endpoint.Current, controller.GetCurrentFormat());
    }

    [Fact]
    public void GetSupportedSampleRates_delegates_to_the_endpoint()
    {
        var endpoint = new RecordingAudioEndpoint(new HashSet<int> { 44_100, 88_200 });
        var controller = new DeviceController(endpoint);

        var result = controller.GetSupportedSampleRates(24);

        Assert.Equal(new HashSet<int> { 44_100, 88_200 }, result);
        Assert.Equal(24, endpoint.LastRequestedBitDepth);
    }

    private sealed class RecordingAudioEndpoint(IReadOnlySet<int> supportedSampleRates) : IAudioEndpoint
    {
        public int? LastRequestedBitDepth { get; private set; }

        public DeviceFormat? GetCurrentFormat() => null;

        public void SetFormat(DeviceFormat format)
        {
        }

        public void SetMuted(bool muted)
        {
        }

        public IReadOnlySet<int> GetSupportedSampleRates(int bitDepth)
        {
            LastRequestedBitDepth = bitDepth;
            return supportedSampleRates;
        }
    }
}
