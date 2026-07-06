namespace Samklang.Timing;

/// <summary>The real <see cref="IClock"/>, backed by the system clock.</summary>
public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
