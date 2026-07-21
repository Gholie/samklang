namespace Samklang.Timing;

/// <summary>
/// A single asynchronous wait, abstracted so background polling loops (e.g.
/// <see cref="PlaybackPausingDeviceController"/>'s post-switch recovery watch) can be
/// unit-tested without real <see cref="Task.Delay(TimeSpan)"/> waits making the test suite slow
/// or timing-flaky.
/// </summary>
public interface IDelay
{
    /// <summary>Completes after roughly <paramref name="duration"/> has elapsed.</summary>
    Task Wait(TimeSpan duration);
}
