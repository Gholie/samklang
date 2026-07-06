using System.IO;
using System.Text.Json;

namespace Samklang.SettingsManagement;

/// <summary>
/// Reads/writes <see cref="Settings"/> as JSON under %APPDATA%\Samklang\settings.json. This
/// class is real file I/O and is not unit-tested directly — <see cref="SettingsManager"/>'s
/// loading/seeding decisions are tested instead, against a fake <see cref="ISettingsStore"/>,
/// the same way <see cref="Sessions.SmtcTrackWatcher"/>'s COM calls are left to its caller's
/// tests.
/// </summary>
public sealed class JsonFileSettingsStore : ISettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    private readonly string _filePath;

    public JsonFileSettingsStore()
        : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Samklang", "settings.json"))
    {
    }

    /// <summary>Exposed for tools that need a non-default path; production code always uses the parameterless constructor.</summary>
    internal JsonFileSettingsStore(string filePath) => _filePath = filePath;

    public Settings? Load()
    {
        if (!File.Exists(_filePath))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<Settings>(json, SerializerOptions);
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            // A corrupt or unreadable settings file shouldn't crash the app; treat it like first
            // run so SettingsManager reseeds and overwrites it with something valid.
            return null;
        }
    }

    public void Save(Settings settings)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(_filePath, JsonSerializer.Serialize(settings, SerializerOptions));
    }
}
