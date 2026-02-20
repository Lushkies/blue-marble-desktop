using System.Text.Json;
using System.Text.Json.Serialization;

namespace DesktopEarth;

public class SettingsManager
{
    private static readonly string SettingsDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BlueMarbleDesktop");
    private static readonly string SettingsPath = Path.Combine(SettingsDir, "settings.json");
    private static readonly string SettingsTempPath = Path.Combine(SettingsDir, "settings.json.tmp");
    private static readonly string SettingsBackupPath = Path.Combine(SettingsDir, "settings.json.bak");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public AppSettings Settings { get; private set; } = new();

    /// <summary>
    /// Runtime-only flag: true when the app has launched before (settings existed).
    /// Used by RenderScheduler to skip the first render on subsequent launches.
    /// </summary>
    public bool SkipFirstRender { get; private set; }

    public event Action? SettingsChanged;

    public void Load()
    {
        bool loaded = false;

        // Try primary settings file first
        if (File.Exists(SettingsPath))
        {
            loaded = TryLoadFromFile(SettingsPath);
        }

        // If primary failed, try backup
        if (!loaded && File.Exists(SettingsBackupPath))
        {
            Console.WriteLine("Settings: Primary file failed, recovering from backup...");
            loaded = TryLoadFromFile(SettingsBackupPath);
            if (loaded)
            {
                Console.WriteLine("Settings: Successfully recovered from backup.");
                Save(); // Re-write primary file from recovered backup
            }
        }

        // True first run (no settings files exist)
        if (!loaded)
        {
            Settings = new AppSettings();
            Settings.HasLaunchedBefore = true;
            Save();
            SkipFirstRender = false; // First run: render the default globe wallpaper
            return;
        }

        // Subsequent launch: skip the first render to preserve existing wallpaper
        if (Settings.HasLaunchedBefore)
        {
            SkipFirstRender = true;
        }
        else
        {
            // Settings existed but HasLaunchedBefore was false (upgrade from older version)
            Settings.HasLaunchedBefore = true;
            Save();
            SkipFirstRender = false;
        }
    }

    /// <summary>
    /// Try to load and parse settings from a specific file path.
    /// Returns true if successful, false if file is corrupted/invalid.
    /// </summary>
    private bool TryLoadFromFile(string path)
    {
        try
        {
            string json = File.ReadAllText(path);

            // Migrate old v4.0.0 DisplayMode enum values to the new combined StillImage mode.
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
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: Could not load settings from {Path.GetFileName(path)}. ({ex.Message})");
            return false;
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

            // Atomic save: write to temp file, backup existing, then rename
            File.WriteAllText(SettingsTempPath, json);

            // Backup current settings before replacing
            if (File.Exists(SettingsPath))
            {
                File.Copy(SettingsPath, SettingsBackupPath, overwrite: true);
            }

            // Atomic rename (on NTFS this replaces the target atomically)
            File.Move(SettingsTempPath, SettingsPath, overwrite: true);
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
