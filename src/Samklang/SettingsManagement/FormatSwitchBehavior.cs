namespace Samklang.SettingsManagement;

/// <summary>
/// What happens to Apple Music playback and the device's mute state around a device format
/// switch. Previously two independent booleans (<c>PauseDuringFormatSwitch</c> and
/// <c>KeepFeedingAudioDuringFormatSwitch</c>), which let both be enabled at once — pausing
/// playback while also asking not to mute is a no-op combination (nothing is being fed to the
/// device once playback is paused), which was confusing in the settings UI. A single choice
/// makes the three actually-distinct behaviors mutually exclusive.
/// </summary>
public enum FormatSwitchBehavior
{
    /// <summary>
    /// Mute the device immediately before a switch and unmute immediately after, masking the
    /// brief mute/rebuild hiccup Windows makes with a silent gap instead. The default.
    /// </summary>
    MuteThroughSwitch,

    /// <summary>
    /// Pause Apple Music (via SMTC) immediately before a switch and resume it immediately after,
    /// masking the same hiccup by not feeding the device any audio during that window rather than
    /// muting through it.
    /// </summary>
    PauseDuringSwitch,

    /// <summary>
    /// Apply neither mitigation: the device stays unmuted and Apple Music keeps playing right
    /// through the switch, so the Windows hiccup is audible.
    /// </summary>
    KeepFeedingAudioDuringSwitch,
}
