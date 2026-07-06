namespace Samklang.Domain;

/// <summary>
/// Coarse playback state of the Apple Music media session, used to tell active playback apart
/// from idle (paused or stopped) per the Grace Period revert's idle definition in CONTEXT.md.
/// </summary>
public enum PlaybackState
{
    /// <summary>Actively playing.</summary>
    Playing,

    /// <summary>Paused, with a track loaded.</summary>
    Paused,

    /// <summary>Stopped, with no track actively loaded (or a transient/unknown session state).</summary>
    Stopped,
}
