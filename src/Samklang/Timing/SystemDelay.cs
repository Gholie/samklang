namespace Samklang.Timing;

/// <summary>The real <see cref="IDelay"/>, backed by <see cref="Task.Delay(TimeSpan)"/>.</summary>
public sealed class SystemDelay : IDelay
{
    public Task Wait(TimeSpan duration, CancellationToken cancellationToken) => Task.Delay(duration, cancellationToken);
}
