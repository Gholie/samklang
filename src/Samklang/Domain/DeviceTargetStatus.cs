namespace Samklang.Domain;

/// <summary>
/// The device-targeting status surfaced to the UI: which device is actually in effect right now,
/// its friendly name (for display), and whether that's a fallback away from a Pinned device that
/// has gone missing.
/// </summary>
public sealed record DeviceTargetStatus(string? DeviceId, string? FriendlyName, bool IsFallback);
