namespace Samklang.Sessions;

/// <summary>
/// Pure filtering logic deciding whether a Windows media session belongs to Apple Music for
/// Windows, keyed off the session's <c>SourceAppUserModelId</c> (AUMID). Extracted out of the
/// SMTC watcher so it's unit-testable without a real Windows media session.
/// </summary>
public static class AppleMusicSessionFilter
{
    /// <summary>
    /// Prefix of Apple Music for Windows' package family name. The AUMID Windows 11 reports
    /// for the app's session is this prefix plus a per-install publisher hash and app id
    /// suffix (e.g. "AppleInc.AppleMusicWin_nzyj5cx40ttqa!App"), so we match on the prefix
    /// rather than a single hard-coded full AUMID.
    /// </summary>
    private const string PackageFamilyNamePrefix = "AppleInc.AppleMusicWin";

    /// <summary>
    /// Some Windows versions report the session under the bare executable name instead of the
    /// packaged AUMID above; matching it too keeps filtering correct across OS builds.
    /// </summary>
    private const string ExecutableSessionId = "AppleMusic.exe";

    public static bool IsAppleMusicSession(string? sourceAppUserModelId) =>
        !string.IsNullOrEmpty(sourceAppUserModelId) &&
        (sourceAppUserModelId.Contains(PackageFamilyNamePrefix, StringComparison.OrdinalIgnoreCase) ||
         sourceAppUserModelId.Equals(ExecutableSessionId, StringComparison.OrdinalIgnoreCase));
}
