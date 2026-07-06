using Samklang.Domain;

namespace Samklang.Devices;

/// <summary>
/// The switch-or-skip decision behind <see cref="IDeviceController"/>: compares the Target
/// Format against the effective device's actual current Device Format, skips entirely when they
/// already match, and otherwise mutes, writes the new format, and unmutes — unmuting even if the
/// write throws, so a failed switch never leaves the device silently muted.
///
/// Which device is "effective" is decided fresh on every call by
/// <see cref="Domain.DeviceTargetResolver"/>, from the targeting choice set via
/// <see cref="SetTargeting"/> plus a live snapshot of the default device and active device list
/// read from the endpoint — so Follow mode always tracks Windows' current default, and Pinned
/// mode falls back to the default (and recovers) as the pinned device disappears and reappears.
///
/// All actual hardware access is delegated to an <see cref="IAudioEndpoint"/>, which lets this
/// class be unit-tested with a fake endpoint instead of real Windows COM calls.
/// </summary>
public sealed class DeviceController(IAudioEndpoint endpoint) : IDeviceController
{
    private DeviceTargetingMode _mode = DeviceTargetingMode.FollowDefault;
    private string? _pinnedDeviceId;

    public void SetTargeting(DeviceTargetingMode mode, string? pinnedDeviceId)
    {
        _mode = mode;
        _pinnedDeviceId = pinnedDeviceId;
    }

    public IReadOnlyList<RenderDevice> GetActiveRenderDevices() => endpoint.GetActiveRenderDevices();

    public DeviceTargetStatus GetTargetStatus()
    {
        var activeDevices = endpoint.GetActiveRenderDevices();
        var resolved = ResolveTarget(activeDevices);
        var friendlyName = resolved.DeviceId is null
            ? null
            : activeDevices.FirstOrDefault(d => d.Id == resolved.DeviceId)?.FriendlyName;

        return new DeviceTargetStatus(resolved.DeviceId, friendlyName, resolved.IsFallback);
    }

    public DeviceFormat? GetCurrentFormat()
    {
        var deviceId = ResolveTarget().DeviceId;
        return deviceId is null ? null : endpoint.GetCurrentFormat(deviceId);
    }

    public bool ApplyTargetFormat(DeviceFormat target)
    {
        var deviceId = ResolveTarget().DeviceId;
        if (deviceId is null)
        {
            return false;
        }

        var current = endpoint.GetCurrentFormat(deviceId);
        if (current == target)
        {
            return false;
        }

        endpoint.SetMuted(deviceId, true);
        try
        {
            endpoint.SetFormat(deviceId, target);
        }
        finally
        {
            endpoint.SetMuted(deviceId, false);
        }

        return true;
    }

    public IReadOnlySet<int> GetSupportedSampleRates(int bitDepth)
    {
        var deviceId = ResolveTarget().DeviceId;
        return deviceId is null ? new HashSet<int>() : endpoint.GetSupportedSampleRates(deviceId, bitDepth);
    }

    private ResolvedDeviceTarget ResolveTarget() => ResolveTarget(endpoint.GetActiveRenderDevices());

    private ResolvedDeviceTarget ResolveTarget(IReadOnlyList<RenderDevice> activeDevices)
    {
        var defaultDeviceId = endpoint.GetDefaultRenderDeviceId();
        var activeDeviceIds = activeDevices.Select(d => d.Id).ToHashSet();
        return DeviceTargetResolver.Resolve(_mode, _pinnedDeviceId, defaultDeviceId, activeDeviceIds);
    }
}
