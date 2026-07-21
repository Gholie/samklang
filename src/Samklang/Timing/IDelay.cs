namespace Samklang.Timing;

/// <summary>
/// A single asynchronous wait, abstracted so background polling loops (e.g.
/// <see cref="PlaybackPausingDeviceController"/>'s post-switch recovery watch) can be
/// unit-tested without real <see cref="Task.Delay(TimeSpan)"/> waits making the test suite slow
/// or timing-flaky.
/// </summary>
public interface IDelay
{
    /// <summary>
    /// Completes after roughly <paramref name="duration"/> has elapsed, or throws
    /// <see cref="OperationCanceledException"/> as soon as <paramref name="cancellationToken"/> is
    /// canceled — whichever happens first. Callers that poll in a loop (e.g.
    /// <see cref="PlaybackPausingDeviceController"/>'s recovery watch) rely on the early-cancel
    /// behavior to stop waiting out a superseded poll instead of sitting through the rest of its
    /// interval.
    /// </summary>
    Task Wait(TimeSpan duration, CancellationToken cancellationToken);
}
