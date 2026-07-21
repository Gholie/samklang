using Samklang.Domain;

namespace Samklang.Devices;

/// <summary>
/// Applies a Format Resolution's Target Format to the effective render device's shared-mode
/// Device Format, skipped entirely when the device is already at the Target Format's sample
/// rate — a bit-depth-only difference never triggers a switch, since bit depth is pinned to
/// 24-bit upstream and 24-bit playback of 16-bit content is bit-perfect. <see cref="DeviceController"/>,
/// the canonical implementation, mutes the device immediately before a real switch and unmutes
/// immediately after by default, but that mute step is itself opt-out-able (see its
/// <c>muteDuringSwitch</c> constructor parameter and <see cref="SettingsManagement.FormatSwitchBehavior.KeepFeedingAudioDuringSwitch"/>)
/// — the interface makes no muting guarantee, only that the format write happens.
///
/// "The effective render device" is either the Windows default (Follow mode) or a specific
/// pinned device (Pinned mode), as set via <see cref="SetTargeting"/> and resolved fresh on every
/// call via <see cref="Domain.DeviceTargetResolver"/> — including falling back to the default
/// when a pinned device isn't currently active, and recovering automatically once it is again.
/// </summary>
public interface IDeviceController
{
    /// <summary>The effective render device's current, actual Device Format, or null if it can't be read.</summary>
    DeviceFormat? GetCurrentFormat();

    /// <summary>
    /// Switches the effective render device to <paramref name="target"/> unless it already runs
    /// at the target's sample rate (bit depth alone is never worth a switch). Returns true if a
    /// switch was performed, false if it was skipped because the device's rate already matches
    /// (or because there is no effective device to switch right now).
    /// </summary>
    bool ApplyTargetFormat(DeviceFormat target);

    /// <summary>
    /// The sample rates (Hz) the effective render device supports in shared mode at
    /// <paramref name="bitDepth"/>. Callers clamp a resolved Target Format against this — see
    /// <see cref="Samklang.Domain.RateFamilyClamp"/> — before calling
    /// <see cref="ApplyTargetFormat"/>, so a rate the device can't actually run at is never sent
    /// down to <see cref="ApplyTargetFormat"/> in the first place.
    /// </summary>
    IReadOnlySet<int> GetSupportedSampleRates(int bitDepth);

    /// <summary>
    /// Sets the device-targeting choice: Follow the Windows default, or Pin a specific device by
    /// ID. Takes effect on the next call to any of the members above.
    /// </summary>
    void SetTargeting(DeviceTargetingMode mode, string? pinnedDeviceId);

    /// <summary>Every render device Windows currently reports as active, for the Settings device picker.</summary>
    IReadOnlyList<RenderDevice> GetActiveRenderDevices();

    /// <summary>
    /// The device-targeting status right now: which device is actually in effect, its friendly
    /// name, and whether that's a fallback away from a Pinned device that's currently missing.
    /// </summary>
    DeviceTargetStatus GetTargetStatus();
}
