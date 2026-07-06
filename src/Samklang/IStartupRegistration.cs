namespace Samklang;

/// <summary>
/// Registers/unregisters Samklang to launch at Windows sign-in for the current user, without
/// requiring elevation. The registration itself is the single source of truth for whether
/// Start-with-Windows is enabled — there is deliberately no separate persisted flag in
/// <see cref="SettingsManagement.Settings"/> that could drift out of sync with it; the settings
/// view reads <see cref="IsEnabled"/> live and drives <see cref="Enable"/>/<see cref="Disable"/>
/// directly.
/// </summary>
public interface IStartupRegistration
{
    /// <summary>Whether Samklang is currently registered to launch at sign-in.</summary>
    bool IsEnabled { get; }

    /// <summary>Registers Samklang to launch at sign-in for the current user. Idempotent.</summary>
    void Enable();

    /// <summary>Removes the launch-at-sign-in registration for the current user. Idempotent (no-op if not registered).</summary>
    void Disable();
}
