namespace Samklang.Domain;

/// <summary>
/// The pure "which device do we actually act on" policy behind device targeting: given the user's
/// choice (Follow Default / Pinned) and a snapshot of what Windows currently reports — the
/// default render device's ID, and the IDs of every currently-active render device — decides the
/// effective device ID, and whether that's a fallback away from a Pinned device that has gone
/// missing.
///
/// No I/O and no device access — enumerating devices and reading the Windows default lives in
/// <see cref="Samklang.Devices.IAudioEndpoint"/>; this class only makes the decision from data
/// already gathered, which is what makes it unit-testable without COM (mirrors
/// <see cref="RateFamilyClamp"/>'s split for the same reason).
/// </summary>
public static class DeviceTargetResolver
{
    /// <summary>
    /// Resolves the effective device ID for the given targeting choice.
    ///
    /// In Follow mode (or Pinned mode with no device chosen yet), the effective device is always
    /// the Windows default, and this is never flagged as a fallback — there is nothing to fall
    /// back from.
    ///
    /// In Pinned mode, the effective device is the pinned device when it's still active;
    /// otherwise it's the Windows default, flagged as a fallback so the caller can surface the
    /// situation (this issue's acceptance criteria: "unplugging the pinned device falls back
    /// gracefully with visible status"). If the pinned device later reappears in
    /// <paramref name="activeDeviceIds"/>, resolution goes back to the pinned device with
    /// <see cref="ResolvedDeviceTarget.IsFallback"/> false — the recovery this issue also asks
    /// for, achieved simply by re-resolving from the latest snapshot each time.
    /// </summary>
    public static ResolvedDeviceTarget Resolve(
        DeviceTargetingMode mode,
        string? pinnedDeviceId,
        string? defaultDeviceId,
        IReadOnlySet<string> activeDeviceIds)
    {
        if (mode == DeviceTargetingMode.FollowDefault || pinnedDeviceId is null)
        {
            return new ResolvedDeviceTarget(defaultDeviceId, IsFallback: false);
        }

        if (activeDeviceIds.Contains(pinnedDeviceId))
        {
            return new ResolvedDeviceTarget(pinnedDeviceId, IsFallback: false);
        }

        return new ResolvedDeviceTarget(defaultDeviceId, IsFallback: true);
    }
}
