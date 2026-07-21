namespace Samklang.SettingsManagement;

/// <summary>
/// What happens to Apple Music playback and the device's mute state around a device format
/// switch. Modeled as one three-way choice rather than two independent booleans (mute vs.
/// don't, pause vs. don't) because two of the four combinations those booleans would allow are
/// degenerate: pausing playback while also asking not to mute is a no-op combination (nothing is
/// being fed to the device once playback is paused), which would be confusing to expose in the
/// settings UI. A single choice keeps the three actually-distinct behaviors mutually exclusive.
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
