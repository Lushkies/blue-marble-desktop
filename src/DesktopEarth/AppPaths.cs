namespace DesktopEarth;

/// <summary>
/// Single source of truth for where the app keeps its data
/// (%AppData%\BlueMarbleDesktop). The BLUEMARBLE_DATA_DIR environment variable
/// overrides it so a test run can use a scratch folder without touching the
/// user's real settings, caches or wallpaper file.
/// </summary>
public static class AppPaths
{
    public static string DataDir { get; } = ResolveDataDir();

    public static string SettingsFile => Path.Combine(DataDir, "settings.json");
    public static string WallpaperFile => Path.Combine(DataDir, "wallpaper.bmp");
    public static string ImageCacheDir => Path.Combine(DataDir, "image_cache");
    public static string EpicCacheDir => Path.Combine(DataDir, "epic_images");
    public static string UserImagesDir => Path.Combine(DataDir, "user_images");
    public static string HdTexturesDir => Path.Combine(DataDir, "textures_hd");
    public static string CrashLog => Path.Combine(DataDir, "crash.log");

    private static string ResolveDataDir()
    {
        var overrideDir = Environment.GetEnvironmentVariable("BLUEMARBLE_DATA_DIR");
        if (!string.IsNullOrWhiteSpace(overrideDir))
            return Path.GetFullPath(overrideDir);

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "BlueMarbleDesktop");
    }
}
