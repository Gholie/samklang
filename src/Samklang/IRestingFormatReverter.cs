namespace Samklang;

/// <summary>
/// Reverts the device to the Resting Format once playback has been idle (paused, stopped, or
/// Apple Music closed) for the Grace Period. Extracted as an interface so
/// <see cref="TrackSyncCoordinator"/> can be unit-tested with a fake instead of a real
/// <see cref="RestingFormatReverter"/> and its Settings/device/clock dependencies.
/// </summary>
public interface IRestingFormatReverter
{
    /// <summary>Call whenever playback becomes idle (paused, stopped, or the app closes). Idempotent while already idle.</summary>
    void NotifyIdle();

    /// <summary>Call whenever playback (re)starts. Cancels any pending revert without switching anything.</summary>
    void NotifyActive();

    /// <summary>
    /// Checks whether the Grace Period has elapsed since playback went idle and, if so, reverts
    /// the device to the Resting Format. Safe to call repeatedly/on a poll timer — a no-op
    /// while active, before the Grace Period has elapsed, or after it has already reverted once
    /// for the current idle period.
    /// </summary>
    void Tick();
}
