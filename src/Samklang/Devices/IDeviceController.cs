using Samklang.Domain;

namespace Samklang.Devices;

/// <summary>
/// Applies a Format Resolution's Target Format to the default render device's shared-mode
/// Device Format. Per this issue's acceptance criteria: switching mutes the device immediately
/// before and unmutes immediately after, and is skipped entirely when the device is already at
/// the Target Format.
/// </summary>
public interface IDeviceController
{
    /// <summary>The default render device's current, actual Device Format, or null if it can't be read.</summary>
    DeviceFormat? GetCurrentFormat();

    /// <summary>
    /// Switches the default render device to <paramref name="target"/> unless it is already
    /// there. Returns true if a switch was performed, false if it was skipped because the
    /// device already matches.
    /// </summary>
    bool ApplyTargetFormat(DeviceFormat target);

    /// <summary>
    /// The sample rates (Hz) the default render device supports in shared mode at
    /// <paramref name="bitDepth"/>. Callers clamp a resolved Target Format against this — see
    /// <see cref="Samklang.Domain.RateFamilyClamp"/> — before calling
    /// <see cref="ApplyTargetFormat"/>, so a rate the device can't actually run at is never sent
    /// down to <see cref="ApplyTargetFormat"/> in the first place.
    /// </summary>
    IReadOnlySet<int> GetSupportedSampleRates(int bitDepth);
}
