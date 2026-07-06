namespace Samklang.Domain;

/// <summary>
/// The render device currently in effect for switching and Resting Format handling, as decided by
/// <see cref="DeviceTargetResolver"/>.
/// </summary>
/// <param name="DeviceId">
/// The effective device's ID, or null when no render device is available at all (nothing to
/// target).
/// </param>
/// <param name="IsFallback">
/// True when Pinned mode was requested with a device that isn't currently active (unplugged,
/// disabled), so this is actually the Windows default standing in for it — the situation this
/// issue's acceptance criteria says must be surfaced to the user.
/// </param>
public sealed record ResolvedDeviceTarget(string? DeviceId, bool IsFallback);
