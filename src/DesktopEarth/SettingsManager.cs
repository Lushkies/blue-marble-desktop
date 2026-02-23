using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;

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

    private static readonly object SaveLock = new();

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
            Settings.DarkModeEnabled = IsWindowsDarkTheme();
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
    /// Detect whether Windows is using a dark app theme.
    /// Reads HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize\AppsUseLightTheme.
    /// Returns true if dark, false only if explicitly set to light mode.
    /// Defaults to dark if the registry key is missing or unreadable.
    /// </summary>
    private static bool IsWindowsDarkTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (key?.GetValue("AppsUseLightTheme") is int value)
                return value == 0; // 0 = dark, 1 = light
        }
        catch { }
        return true; // Default to dark mode if detection fails
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
            // Old format had DisplayMode = NasaEpic/NasaApod/NationalParks/Smithsonian/Unsplash.
            // New format uses DisplayMode = StillImage with a separate StillImageSource property.
            //
            // IMPORTANT: Only replace DisplayMode values, NOT ImageSource values!
            // A global string replace would corrupt Favorites[].Source, StillImageSource, etc.
            ImageSource? migratedSource = null;

            // Detect old format: DisplayMode has an image source name instead of StillImage
            var displayModeMatch = System.Text.RegularExpressions.Regex.Match(
                json, @"""DisplayMode""\s*:\s*""(\w+)""");
            if (displayModeMatch.Success)
            {
                var oldMode = displayModeMatch.Groups[1].Value;
                switch (oldMode)
                {
                    case "NasaEpic":
                        migratedSource = ImageSource.NasaEpic;
                        json = json.Replace(displayModeMatch.Value,
                            "\"DisplayMode\": \"StillImage\"");
                        break;
                    case "NasaApod":
                        migratedSource = ImageSource.NasaApod;
                        json = json.Replace(displayModeMatch.Value,
                            "\"DisplayMode\": \"StillImage\"");
                        break;
                    case "NationalParks":
                        migratedSource = ImageSource.NationalParks;
                        json = json.Replace(displayModeMatch.Value,
                            "\"DisplayMode\": \"StillImage\"");
                        break;
                    case "Smithsonian":
                        migratedSource = ImageSource.Smithsonian;
                        json = json.Replace(displayModeMatch.Value,
                            "\"DisplayMode\": \"StillImage\"");
                        break;
                    case "Unsplash":
                        // Unsplash was removed — fall back to Spherical (globe)
                        json = json.Replace(displayModeMatch.Value,
                            "\"DisplayMode\": \"Spherical\"");
                        break;
                }
            }

            Settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();

            // Set StillImageSource from the migrated DisplayMode
            if (migratedSource != null && Settings.DisplayMode == DisplayMode.StillImage)
            {
                Settings.StillImageSource = migratedSource.Value;
            }

            // Migrate old separate API keys to unified ApiDataGovKey
            MigrateApiKeys();

            // Migrate old RandomFromFavoritesOnly to new RotationSource
            MigrateRotationSettings();

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

    /// <summary>
    /// Migrate old RandomFromFavoritesOnly boolean to new RotationSource enum.
    /// Old property ends up in ExtensionData since it's no longer on AppSettings.
    /// </summary>
    private void MigrateRotationSettings()
    {
        if (Settings.ExtensionData == null) return;

        if (Settings.ExtensionData.TryGetValue("RandomFromFavoritesOnly", out var favOnlyEl))
        {
            if (favOnlyEl.ValueKind == System.Text.Json.JsonValueKind.True)
            {
                Settings.RandomRotationSource = RotationSource.Favorites;
            }
            else if (favOnlyEl.ValueKind == System.Text.Json.JsonValueKind.False && Settings.RandomRotationEnabled)
            {
                // Old "rotate but not favorites only" = rotate current source
                Settings.RandomRotationSource = Settings.StillImageSource switch
                {
                    ImageSource.NasaEpic => RotationSource.NasaEpic,
                    ImageSource.NasaApod => RotationSource.NasaApod,
                    ImageSource.NationalParks => RotationSource.NationalParks,
                    ImageSource.Smithsonian => RotationSource.Smithsonian,
                    ImageSource.UserImages => RotationSource.UserImages,
                    _ => RotationSource.Favorites
                };
            }
        }
    }

    public void Save()
    {
        lock (SaveLock)
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
    }

    public void ApplyAndSave(Action<AppSettings> modify)
    {
        modify(Settings);
        Save();
        SettingsChanged?.Invoke();
    }
}
