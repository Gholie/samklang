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
        public string? LastRequestedDeviceId { get; private set; }

        public string? DefaultDeviceId { get; set; } = "device-1";
        public IReadOnlyList<RenderDevice> ActiveDevices { get; set; } =
            [new RenderDevice("device-1", "Fake Default Device")];

        public DeviceFormat? GetCurrentFormat(string deviceId)
        {
            LastRequestedDeviceId = deviceId;
            Calls.Add("get");
            return Current;
        }

        public void SetFormat(string deviceId, DeviceFormat format)
        {
            LastRequestedDeviceId = deviceId;
            Calls.Add($"set:{format}");
            Current = format;
        }

        public void SetMuted(string deviceId, bool muted)
        {
            LastRequestedDeviceId = deviceId;
            Calls.Add(muted ? "mute" : "unmute");
        }

        public IReadOnlySet<int> GetSupportedSampleRates(string deviceId, int bitDepth)
        {
            LastRequestedDeviceId = deviceId;
            return new HashSet<int>();
        }

        public IReadOnlyList<RenderDevice> GetActiveRenderDevices() => ActiveDevices;

        public string? GetDefaultRenderDeviceId() => DefaultDeviceId;
    }

    private sealed class ThrowingFakeAudioEndpoint : IAudioEndpoint
    {
        public DeviceFormat? Current { get; set; }
        public List<string> Calls { get; } = [];

        public DeviceFormat? GetCurrentFormat(string deviceId) => Current;

        public void SetFormat(string deviceId, DeviceFormat format)
        {
            Calls.Add("set-attempted");
            throw new InvalidOperationException("switch failed");
        }

        public void SetMuted(string deviceId, bool muted) => Calls.Add(muted ? "mute" : "unmute");

        public IReadOnlySet<int> GetSupportedSampleRates(string deviceId, int bitDepth) => new HashSet<int>();

        public IReadOnlyList<RenderDevice> GetActiveRenderDevices() => [new RenderDevice("device-1", "Fake Default Device")];

        public string? GetDefaultRenderDeviceId() => "device-1";
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
    public void ApplyTargetFormat_is_a_noop_when_there_is_no_effective_device()
    {
        var endpoint = new FakeAudioEndpoint { DefaultDeviceId = null, ActiveDevices = [] };
        var controller = new DeviceController(endpoint);

        var switched = controller.ApplyTargetFormat(new DeviceFormat(44_100, 24));

        Assert.False(switched);
        Assert.Empty(endpoint.Calls);
    }

    [Fact]
    public void GetCurrentFormat_delegates_to_the_endpoint()
    {
        var endpoint = new FakeAudioEndpoint { Current = new DeviceFormat(96_000, 24) };
        var controller = new DeviceController(endpoint);

        Assert.Equal(endpoint.Current, controller.GetCurrentFormat());
    }

    [Fact]
    public void GetCurrentFormat_returns_null_when_there_is_no_effective_device()
    {
        var endpoint = new FakeAudioEndpoint { DefaultDeviceId = null, ActiveDevices = [], Current = new DeviceFormat(96_000, 24) };
        var controller = new DeviceController(endpoint);

        Assert.Null(controller.GetCurrentFormat());
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

    [Fact]
    public void By_default_the_controller_follows_the_windows_default_device()
    {
        var endpoint = new FakeAudioEndpoint { DefaultDeviceId = "device-1", Current = new DeviceFormat(48_000, 24) };
        var controller = new DeviceController(endpoint);

        controller.GetCurrentFormat();

        Assert.Equal("device-1", endpoint.LastRequestedDeviceId);
    }

    [Fact]
    public void SetTargeting_to_pinned_routes_operations_to_the_pinned_device_even_when_its_not_default()
    {
        var endpoint = new FakeAudioEndpoint
        {
            DefaultDeviceId = "device-1",
            ActiveDevices = [new RenderDevice("device-1", "Default Device"), new RenderDevice("device-2", "USB DAC")],
        };
        var controller = new DeviceController(endpoint);

        controller.SetTargeting(DeviceTargetingMode.Pinned, "device-2");
        controller.ApplyTargetFormat(new DeviceFormat(96_000, 24));

        Assert.Equal("device-2", endpoint.LastRequestedDeviceId);
    }

    [Fact]
    public void SetTargeting_to_pinned_falls_back_to_the_default_device_when_the_pinned_device_is_gone()
    {
        var endpoint = new FakeAudioEndpoint
        {
            DefaultDeviceId = "device-1",
            ActiveDevices = [new RenderDevice("device-1", "Default Device")],
        };
        var controller = new DeviceController(endpoint);

        controller.SetTargeting(DeviceTargetingMode.Pinned, "device-2");
        controller.ApplyTargetFormat(new DeviceFormat(96_000, 24));

        Assert.Equal("device-1", endpoint.LastRequestedDeviceId);
    }

    [Fact]
    public void GetTargetStatus_in_follow_mode_reports_the_default_device_and_no_fallback()
    {
        var endpoint = new FakeAudioEndpoint
        {
            DefaultDeviceId = "device-1",
            ActiveDevices = [new RenderDevice("device-1", "Default Device")],
        };
        var controller = new DeviceController(endpoint);

        var status = controller.GetTargetStatus();

        Assert.Equal("device-1", status.DeviceId);
        Assert.Equal("Default Device", status.FriendlyName);
        Assert.False(status.IsFallback);
    }

    [Fact]
    public void GetTargetStatus_in_pinned_mode_with_the_device_present_reports_no_fallback()
    {
        var endpoint = new FakeAudioEndpoint
        {
            DefaultDeviceId = "device-1",
            ActiveDevices = [new RenderDevice("device-1", "Default Device"), new RenderDevice("device-2", "USB DAC")],
        };
        var controller = new DeviceController(endpoint);
        controller.SetTargeting(DeviceTargetingMode.Pinned, "device-2");

        var status = controller.GetTargetStatus();

        Assert.Equal("device-2", status.DeviceId);
        Assert.Equal("USB DAC", status.FriendlyName);
        Assert.False(status.IsFallback);
    }

    [Fact]
    public void GetTargetStatus_flags_a_fallback_when_the_pinned_device_disappears_and_clears_it_on_reconnect()
    {
        var endpoint = new FakeAudioEndpoint
        {
            DefaultDeviceId = "device-1",
            ActiveDevices = [new RenderDevice("device-1", "Default Device"), new RenderDevice("device-2", "USB DAC")],
        };
        var controller = new DeviceController(endpoint);
        controller.SetTargeting(DeviceTargetingMode.Pinned, "device-2");

        // Unplug the pinned device.
        endpoint.ActiveDevices = [new RenderDevice("device-1", "Default Device")];
        var fallbackStatus = controller.GetTargetStatus();

        Assert.Equal("device-1", fallbackStatus.DeviceId);
        Assert.True(fallbackStatus.IsFallback);

        // Plug it back in — resolution should recover automatically without any further calls.
        endpoint.ActiveDevices = [new RenderDevice("device-1", "Default Device"), new RenderDevice("device-2", "USB DAC")];
        var recoveredStatus = controller.GetTargetStatus();

        Assert.Equal("device-2", recoveredStatus.DeviceId);
        Assert.False(recoveredStatus.IsFallback);
    }

    [Fact]
    public void GetActiveRenderDevices_delegates_to_the_endpoint()
    {
        var devices = new List<RenderDevice> { new("device-1", "Speakers"), new("device-2", "Headphones") };
        var endpoint = new FakeAudioEndpoint { ActiveDevices = devices };
        var controller = new DeviceController(endpoint);

        Assert.Equal(devices, controller.GetActiveRenderDevices());
    }

    private sealed class RecordingAudioEndpoint(IReadOnlySet<int> supportedSampleRates) : IAudioEndpoint
    {
        public int? LastRequestedBitDepth { get; private set; }

        public DeviceFormat? GetCurrentFormat(string deviceId) => null;

        public void SetFormat(string deviceId, DeviceFormat format)
        {
        }

        public void SetMuted(string deviceId, bool muted)
        {
        }

        public IReadOnlySet<int> GetSupportedSampleRates(string deviceId, int bitDepth)
        {
            LastRequestedBitDepth = bitDepth;
            return supportedSampleRates;
        }

        public IReadOnlyList<RenderDevice> GetActiveRenderDevices() => [new RenderDevice("device-1", "Fake Device")];

        public string? GetDefaultRenderDeviceId() => "device-1";
    }
}
