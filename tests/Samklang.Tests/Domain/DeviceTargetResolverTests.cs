using Samklang.Domain;
using Xunit;

namespace Samklang.Tests.Domain;

public class DeviceTargetResolverTests
{
    [Fact]
    public void Follow_mode_always_resolves_to_the_default_device_regardless_of_a_stale_pinned_id()
    {
        var result = DeviceTargetResolver.Resolve(
            DeviceTargetingMode.FollowDefault,
            pinnedDeviceId: "device-2",
            defaultDeviceId: "device-1",
            activeDeviceIds: new HashSet<string> { "device-1", "device-2" });

        Assert.Equal("device-1", result.DeviceId);
        Assert.False(result.IsFallback);
    }

    [Fact]
    public void Follow_mode_tracks_the_default_device_changing()
    {
        var result = DeviceTargetResolver.Resolve(
            DeviceTargetingMode.FollowDefault,
            pinnedDeviceId: null,
            defaultDeviceId: "device-2",
            activeDeviceIds: new HashSet<string> { "device-1", "device-2" });

        Assert.Equal("device-2", result.DeviceId);
        Assert.False(result.IsFallback);
    }

    [Fact]
    public void Pinned_mode_with_no_device_chosen_yet_falls_back_to_default_without_flagging_a_fallback()
    {
        var result = DeviceTargetResolver.Resolve(
            DeviceTargetingMode.Pinned,
            pinnedDeviceId: null,
            defaultDeviceId: "device-1",
            activeDeviceIds: new HashSet<string> { "device-1" });

        Assert.Equal("device-1", result.DeviceId);
        Assert.False(result.IsFallback);
    }

    [Fact]
    public void Pinned_mode_resolves_to_the_pinned_device_when_its_active_even_if_not_default()
    {
        var result = DeviceTargetResolver.Resolve(
            DeviceTargetingMode.Pinned,
            pinnedDeviceId: "device-2",
            defaultDeviceId: "device-1",
            activeDeviceIds: new HashSet<string> { "device-1", "device-2" });

        Assert.Equal("device-2", result.DeviceId);
        Assert.False(result.IsFallback);
    }

    [Fact]
    public void Pinned_mode_falls_back_to_default_and_flags_it_when_the_pinned_device_is_not_active()
    {
        var result = DeviceTargetResolver.Resolve(
            DeviceTargetingMode.Pinned,
            pinnedDeviceId: "device-2",
            defaultDeviceId: "device-1",
            activeDeviceIds: new HashSet<string> { "device-1" });

        Assert.Equal("device-1", result.DeviceId);
        Assert.True(result.IsFallback);
    }

    [Fact]
    public void Pinned_mode_recovers_once_the_pinned_device_becomes_active_again()
    {
        var stillMissing = DeviceTargetResolver.Resolve(
            DeviceTargetingMode.Pinned,
            pinnedDeviceId: "device-2",
            defaultDeviceId: "device-1",
            activeDeviceIds: new HashSet<string> { "device-1" });
        Assert.True(stillMissing.IsFallback);

        var recovered = DeviceTargetResolver.Resolve(
            DeviceTargetingMode.Pinned,
            pinnedDeviceId: "device-2",
            defaultDeviceId: "device-1",
            activeDeviceIds: new HashSet<string> { "device-1", "device-2" });

        Assert.Equal("device-2", recovered.DeviceId);
        Assert.False(recovered.IsFallback);
    }

    [Fact]
    public void Pinned_mode_falls_back_to_null_when_no_device_is_available_at_all()
    {
        var result = DeviceTargetResolver.Resolve(
            DeviceTargetingMode.Pinned,
            pinnedDeviceId: "device-2",
            defaultDeviceId: null,
            activeDeviceIds: new HashSet<string>());

        Assert.Null(result.DeviceId);
        Assert.True(result.IsFallback);
    }
}
