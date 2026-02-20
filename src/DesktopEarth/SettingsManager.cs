using System.Text.Json;
using System.Text.Json.Serialization;

namespace DesktopEarth;

public class SettingsManager
{
    private static readonly string SettingsDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BlueMarbleDesktop");
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

                // Migrate old v4.0.0 DisplayMode enum values to the new combined StillImage mode.
                // We need to capture which source was selected BEFORE replacing the enum values,
                // so we can set StillImageSource correctly after deserialization.
                ImageSource? migratedSource = null;

                if (json.Contains("\"NasaEpic\"") && !json.Contains("\"StillImage\""))
                    migratedSource = ImageSource.NasaEpic;
                else if (json.Contains("\"NasaApod\"") && !json.Contains("\"StillImage\""))
                    migratedSource = ImageSource.NasaApod;
                else if (json.Contains("\"NationalParks\"") && !json.Contains("\"StillImage\""))
                    migratedSource = ImageSource.NationalParks;
                else if (json.Contains("\"Smithsonian\"") && !json.Contains("\"StillImage\""))
                    migratedSource = ImageSource.Smithsonian;

                // Replace removed/renamed enum values so JsonStringEnumConverter doesn't throw
                json = json.Replace("\"Unsplash\"", "\"Spherical\"");
                json = json.Replace("\"NasaEpic\"", "\"StillImage\"");
                json = json.Replace("\"NasaApod\"", "\"StillImage\"");
                json = json.Replace("\"NationalParks\"", "\"StillImage\"");
                json = json.Replace("\"Smithsonian\"", "\"StillImage\"");

                Settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();

                // Set StillImageSource from the migrated DisplayMode
                if (migratedSource != null && Settings.DisplayMode == DisplayMode.StillImage)
                {
                    Settings.StillImageSource = migratedSource.Value;
                }

                // Migrate old separate API keys to unified ApiDataGovKey
                MigrateApiKeys();

                // Clear extension data so old properties don't get re-serialized
                Settings.ExtensionData = null;
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

    /// <summary>
    /// Migrate old separate API keys (NasaApiKey, NpsApiKey, SmithsonianApiKey) to unified ApiDataGovKey.
    /// Old keys end up in ExtensionData since they're no longer properties on AppSettings.
    /// </summary>
    private void MigrateApiKeys()
    {
        if (Settings.ApiDataGovKey != "DEMO_KEY" || Settings.ExtensionData == null)
            return;

        // Try each old key name, use the first non-empty, non-default one
        string[] oldKeyNames = ["NasaApiKey", "NpsApiKey", "SmithsonianApiKey"];
        foreach (var keyName in oldKeyNames)
        {
            if (Settings.ExtensionData.TryGetValue(keyName, out var element))
            {
                var val = element.GetString();
                if (!string.IsNullOrEmpty(val) && val != "DEMO_KEY")
                {
                    Settings.ApiDataGovKey = val;
                    Save(); // Persist migrated key
                    return;
                }
            }
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(SettingsDir);
            // Clear extension data before saving to avoid writing stale old properties
            Settings.ExtensionData = null;
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
