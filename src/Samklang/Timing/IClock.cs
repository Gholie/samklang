namespace Samklang.Timing;

/// <summary>
/// The current time, abstracted so Grace Period revert timing can be unit-tested by advancing a
/// fake clock instead of waiting on real wall-clock time.
/// </summary>
public interface IClock
{
    /// <summary>The current instant, in UTC.</summary>
    DateTimeOffset UtcNow { get; }
}
