using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace DesktopEarth;

public class SettingsManager
{
    private static readonly string SettingsDir =
        AppPaths.DataDir;
    private static readonly string SettingsPath = Path.Combine(SettingsDir, "settings.json");
    private static readonly string SettingsTempPath = Path.Combine(SettingsDir, "settings.json.tmp");
    private static readonly string SettingsBackupPath = Path.Combine(SettingsDir, "settings.json.bak");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>Bumped when a settings change needs a one-time migration.</summary>
    public const int CurrentSettingsVersion = 2;

    public AppSettings Settings { get; private set; } = new();

    /// <summary>
    /// Runtime-only flag: true when the app has launched before (settings existed).
    /// Used by RenderScheduler to skip the first render on subsequent launches.
    /// </summary>
    public bool SkipFirstRender { get; private set; }

    /// <summary>
    /// Runtime-only flag: true when existing settings files could not be read and
    /// settings were reset to defaults. Program.cs warns the user when this is set.
    /// </summary>
    public bool SettingsWereReset { get; private set; }

    // True once the primary settings file is known to parse cleanly this session.
    // Guards the backup step in Save() so a corrupt primary is never copied over
    // the last good backup.
    private bool _primaryKnownGood;

    private static readonly object SaveLock = new();

    public event Action? SettingsChanged;

    public void Load()
    {
        bool loaded = false;

        // Try primary settings file first
        if (File.Exists(SettingsPath))
        {
            loaded = TryLoadFromFile(SettingsPath);
            _primaryKnownGood = loaded;
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
            // If files existed but could not be read, keep a copy for recovery
            // and let Program.cs warn the user instead of resetting silently.
            if (File.Exists(SettingsPath) || File.Exists(SettingsBackupPath))
            {
                QuarantineCorruptSettings();
                SettingsWereReset = true;
            }

            Settings = new AppSettings { SettingsVersion = CurrentSettingsVersion };
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

            // Migrate old v4.0.0 DisplayMode enum values to the new combined
            // StillImage mode. Done on the parsed JSON tree so ONLY DisplayMode
            // properties are touched — a text replace also hits identical text
            // in other fields (Favorites[].Source, nested configs, etc.).
            json = MigrateLegacyDisplayModes(json);

            Settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();

            // Both migrations read ExtensionData, so nothing may save (and clear
            // it) until both have run
            // Keep the untouched pre-upgrade file once, so a migration problem
            // can always be recovered from by hand
            if (Settings.SettingsVersion < CurrentSettingsVersion)
            {
                try
                {
                    string preUpgrade = Path.Combine(SettingsDir, $"settings.pre-v{CurrentSettingsVersion}.json");
                    if (!File.Exists(preUpgrade)) File.Copy(path, preUpgrade);
                }
                catch { /* Best effort — never block startup on the safety copy */ }
            }

            bool migrated = MigrateApiKeys();
            migrated |= MigrateRotationSettings();
            migrated |= MigrateSchemaVersion();

            // Clear extension data so old properties don't get re-serialized
            Settings.ExtensionData = null;
            if (migrated) Save();
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: Could not load settings from {Path.GetFileName(path)}. ({ex.Message})");
            return false;
        }
    }

    /// <summary>
    /// Rewrite pre-v4.1 DisplayMode values (source names like "NasaApod") to the
    /// combined StillImage mode with the matching StillImageSource. Applies to
    /// the top level and to nested DisplayConfigs and Presets entries.
    /// Returns the (possibly rewritten) JSON text; parse errors propagate to the
    /// caller's catch, which triggers backup recovery/quarantine.
    /// </summary>
    private static string MigrateLegacyDisplayModes(string json)
    {
        if (System.Text.Json.Nodes.JsonNode.Parse(json) is not System.Text.Json.Nodes.JsonObject root)
            return json;

        bool changed = MigrateDisplayModeObject(root);

        foreach (var listName in new[] { "DisplayConfigs", "Presets" })
        {
            if (root[listName] is System.Text.Json.Nodes.JsonArray array)
            {
                foreach (var item in array)
                {
                    if (item is System.Text.Json.Nodes.JsonObject obj)
                        changed |= MigrateDisplayModeObject(obj);
                }
            }
        }

        return changed ? root.ToJsonString() : json;
    }

    private static bool MigrateDisplayModeObject(System.Text.Json.Nodes.JsonObject obj)
    {
        if (obj["DisplayMode"] is not System.Text.Json.Nodes.JsonValue value ||
            !value.TryGetValue<string>(out var mode))
            return false;

        switch (mode)
        {
            case "NasaEpic":
            case "NasaApod":
            case "NationalParks":
            case "Smithsonian":
                obj["DisplayMode"] = "StillImage";
                obj["StillImageSource"] = mode;
                return true;
            case "Unsplash":
                // Unsplash was removed — fall back to Spherical (globe)
                obj["DisplayMode"] = "Spherical";
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Migrate old separate API keys (NasaApiKey, NpsApiKey, SmithsonianApiKey) to unified ApiDataGovKey.
    /// Old keys end up in ExtensionData since they're no longer properties on AppSettings.
    /// Returns true if a key was migrated; the caller saves once after all migrations.
    /// </summary>
    private bool MigrateApiKeys()
    {
        if (Settings.ApiDataGovKey != "DEMO_KEY" || Settings.ExtensionData == null)
            return false;

        // Try each old key name, use the first non-empty, non-default one
        string[] oldKeyNames = ["NasaApiKey", "NpsApiKey", "SmithsonianApiKey"];
        foreach (var keyName in oldKeyNames)
        {
            if (Settings.ExtensionData.TryGetValue(keyName, out var element) &&
                element.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                var val = element.GetString();
                if (!string.IsNullOrEmpty(val) && val != "DEMO_KEY")
                {
                    Settings.ApiDataGovKey = val;
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>
    /// Give per-display configs saved before v2.2 (keyed only by \\.\DISPLAYn)
    /// their stable monitor id now, and save it — so a later renumbering by
    /// Windows can't attach them to a different monitor.
    /// Call AFTER Application.SetHighDpiMode: it reads Screen.AllScreens, which
    /// WinForms caches for the process — read while DPI-unaware, all later
    /// monitor bounds would be the scaled-down ones.
    /// </summary>
    public void AdoptLegacyDisplayConfigs()
    {
        bool hasLegacy;
        lock (Settings.DisplayConfigsLock)
            hasLegacy = Settings.DisplayConfigs.Any(c => c.MonitorId.Length == 0);
        if (!hasLegacy) return;

        try
        {
            bool adopted = false;
            foreach (var display in MonitorManager.GetDisplays())
            {
                bool wasLegacy;
                lock (Settings.DisplayConfigsLock)
                    wasLegacy = Settings.DisplayConfigs.Any(c => c.MonitorId.Length == 0 && c.DeviceName == display.DeviceName);
                if (wasLegacy && Settings.FindDisplayConfig(display) != null) adopted = true;
            }
            if (adopted) Save();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Settings: display config adoption skipped ({ex.Message})");
        }
    }

    /// <summary>
    /// Version-gated migrations, each run exactly once per settings file.
    /// Returns true if anything changed.
    /// </summary>
    private bool MigrateSchemaVersion()
    {
        if (Settings.SettingsVersion >= CurrentSettingsVersion) return false;

        if (Settings.SettingsVersion < 2)
        {
            // v2.2: image offsets became a true screen-space shift where positive
            // means right/up. The old camera shift moved the picture the
            // opposite way, so flip saved values to keep existing views in place.
            static float Flip(float v) => v == 0 ? 0 : -v;
            Settings.ImageOffsetX = Flip(Settings.ImageOffsetX);
            Settings.ImageOffsetY = Flip(Settings.ImageOffsetY);
            foreach (var config in Settings.DisplayConfigs)
            {
                config.ImageOffsetX = Flip(config.ImageOffsetX);
                config.ImageOffsetY = Flip(config.ImageOffsetY);
            }
            foreach (var preset in Settings.Presets)
            {
                preset.ImageOffsetX = Flip(preset.ImageOffsetX);
                preset.ImageOffsetY = Flip(preset.ImageOffsetY);
            }
        }

        Settings.SettingsVersion = CurrentSettingsVersion;
        return true;
    }

    /// <summary>
    /// Migrate old RandomFromFavoritesOnly boolean to new RotationSource enum.
    /// Old property ends up in ExtensionData since it's no longer on AppSettings.
    /// Returns true if anything was migrated.
    /// </summary>
    private bool MigrateRotationSettings()
    {
        if (Settings.ExtensionData == null) return false;

        if (Settings.ExtensionData.TryGetValue("RandomFromFavoritesOnly", out var favOnlyEl))
        {
            if (favOnlyEl.ValueKind == System.Text.Json.JsonValueKind.True)
            {
                Settings.RandomRotationSource = RotationSource.Favorites;
                return true;
            }
            if (favOnlyEl.ValueKind == System.Text.Json.JsonValueKind.False && Settings.RandomRotationEnabled)
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
                return true;
            }
        }
        return false;
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

                // Backup current settings before replacing — but only when the
                // primary is known to have parsed cleanly this session, so a
                // corrupt file is never copied over the last good backup.
                if (_primaryKnownGood && File.Exists(SettingsPath))
                {
                    File.Copy(SettingsPath, SettingsBackupPath, overwrite: true);
                }

                // Atomic rename (on NTFS this replaces the target atomically)
                File.Move(SettingsTempPath, SettingsPath, overwrite: true);
                _primaryKnownGood = true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Could not save settings. ({ex.Message})");
            }
        }
    }

    /// <summary>
    /// Preserve unreadable settings files as settings.json.corrupt so the user
    /// (or a future version) can recover favorites and presets from them.
    /// </summary>
    private static void QuarantineCorruptSettings()
    {
        try
        {
            if (File.Exists(SettingsPath))
                File.Copy(SettingsPath, SettingsPath + ".corrupt", overwrite: true);
            else if (File.Exists(SettingsBackupPath))
                File.Copy(SettingsBackupPath, SettingsPath + ".corrupt", overwrite: true);
        }
        catch { /* Best effort — never block startup on quarantine */ }
    }

    public void ApplyAndSave(Action<AppSettings> modify)
    {
        modify(Settings);
        Save();
        SettingsChanged?.Invoke();
    }
}
