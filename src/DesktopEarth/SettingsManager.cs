using System.Text.Json;
using System.Text.Json.Serialization;

namespace DesktopEarth;

public class SettingsManager
{
    private static readonly string SettingsDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DesktopEarth");
    private static readonly string SettingsPath = Path.Combine(SettingsDir, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public AppSettings Settings { get; private set; } = new();

    public event Action? SettingsChanged;

    public void Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                string json = File.ReadAllText(SettingsPath);
                Settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            }
            else
            {
                Settings = new AppSettings();
                Save();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: Could not load settings, using defaults. ({ex.Message})");
            Settings = new AppSettings();
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(SettingsDir);
            string json = JsonSerializer.Serialize(Settings, JsonOptions);
            File.WriteAllText(SettingsPath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: Could not save settings. ({ex.Message})");
        }
    }

    public void ApplyAndSave(Action<AppSettings> modify)
    {
        modify(Settings);
        Save();
        SettingsChanged?.Invoke();
    }
}
