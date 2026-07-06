namespace Samklang.SettingsManagement;

/// <summary>
/// Persistence boundary for <see cref="Settings"/>. Isolated behind this interface so
/// <see cref="SettingsManager"/>'s loading/seeding decisions can be unit-tested against an
/// in-memory fake instead of the real filesystem.
/// </summary>
public interface ISettingsStore
{
    /// <summary>Loads the persisted Settings, or null if none exist yet (first run) or the file is unreadable.</summary>
    Settings? Load();

    /// <summary>Persists Settings, overwriting whatever was stored before.</summary>
    void Save(Settings settings);
}
