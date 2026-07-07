using Microsoft.Win32;

namespace Samklang;

/// <summary>
/// <see cref="IStartupRegistration"/> via the per-user Run key
/// (HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run), which launches the
/// registered command at sign-in with no admin/UAC prompt since it lives entirely under the
/// current user's registry hive. The per-machine Run key (HKLM) and a Startup-folder shortcut
/// were both considered instead: HKLM needs elevation to write, and a Startup-folder shortcut
/// needs its own file to be kept in sync with the exe's install path — the per-user Run key needs
/// neither, so it's the simplest mechanism that satisfies "no admin prompt".
///
/// This class is real registry I/O and is not unit-tested directly, the same way
/// <see cref="SettingsManagement.JsonFileSettingsStore"/>'s real file I/O isn't — there is no
/// decision logic here beyond a direct passthrough of user intent to the registry.
/// </summary>
public sealed class RegistryStartupRegistration : IStartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Samklang";

    public bool IsEnabled
    {
        get
        {
            // Presence of the value is what "enabled" means — deliberately NOT an exact match
            // against the current exe path. After an update or reinstall moves the exe, an exact
            // comparison would report "disabled" while a stale Run entry still exists (and still
            // launches, or fails to launch, the old path); reporting it as enabled keeps the
            // Settings checkbox truthful, and the user toggling it re-runs Enable(), which
            // rewrites the entry to the current path.
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(ValueName) is string;
        }
    }

    public void Enable()
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
        key.SetValue(ValueName, CommandLine);
    }

    public void Disable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    /// <summary>
    /// The command Windows runs at sign-in: the running process's own executable path, quoted so
    /// paths containing spaces (the common case under Program Files) survive. Read from the
    /// running process via <see cref="Environment.ProcessPath"/> rather than
    /// <c>Assembly.Location</c> so this keeps working whether Samklang runs as a self-contained
    /// single-file EXE or a framework-dependent apphost.
    /// </summary>
    private static string CommandLine => $"\"{Environment.ProcessPath}\"";
}
