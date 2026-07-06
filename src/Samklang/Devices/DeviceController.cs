using Samklang.Domain;

namespace Samklang.Devices;

/// <summary>
/// The switch-or-skip decision behind <see cref="IDeviceController"/>: compares the Target
/// Format against the device's actual current Device Format, skips entirely when they already
/// match, and otherwise mutes, writes the new format, and unmutes — unmuting even if the write
/// throws, so a failed switch never leaves the device silently muted.
///
/// All actual hardware access is delegated to an <see cref="IAudioEndpoint"/>, which lets this
/// class be unit-tested with a fake endpoint instead of real Windows COM calls.
/// </summary>
public sealed class DeviceController(IAudioEndpoint endpoint) : IDeviceController
{
    public DeviceFormat? GetCurrentFormat() => endpoint.GetCurrentFormat();

    public bool ApplyTargetFormat(DeviceFormat target)
    {
        var current = endpoint.GetCurrentFormat();
        if (current == target)
        {
            return false;
        }

        endpoint.SetMuted(true);
        try
        {
            endpoint.SetFormat(target);
        }
        finally
        {
            endpoint.SetMuted(false);
        }

        return true;
    }
}
