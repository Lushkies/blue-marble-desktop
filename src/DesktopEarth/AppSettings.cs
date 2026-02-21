using System.Text.Json;
using System.Text.Json.Serialization;

namespace DesktopEarth;

public class AppSettings
{
    // Update frequency in seconds
    public int UpdateIntervalSeconds { get; set; } = 600; // 10 minutes

    // Display mode (simplified: Globe, FlatMap, Moon, StillImage)
    public DisplayMode DisplayMode { get; set; } = DisplayMode.Spherical;

    // Still image source (which service to use when DisplayMode == StillImage)
    public ImageSource StillImageSource { get; set; } = ImageSource.NasaEpic;

    // View controls — Zoom combines old ZoomLevel + FieldOfView into a single control
    // Default: zoom slider 25 (full globe visible, nicely framed)
    public float ZoomLevel { get; set; } = 3.94f;
    public float FieldOfView { get; set; } = 50.3f;

    // Latitude (degrees north/south). Was "CameraTilt" — kept same JSON key for back-compat.
    public float CameraTilt { get; set; } = 42.0f;

    // Longitude offset (degrees). Default 88 = Chicago (negated geographic convention)
    public float LongitudeOffset { get; set; } = 88.0f;

    // Image offset (percentage of screen, -25 to +25). Moves rendered image without affecting view.
    public float ImageOffsetX { get; set; } = 0.0f;
    public float ImageOffsetY { get; set; } = 0.0f;

    // Night lights
    public bool NightLightsEnabled { get; set; } = true;
    public float NightLightsBrightness { get; set; } = 1.5f;

    // Image style
    public ImageStyle ImageStyle { get; set; } = ImageStyle.TopoBathy;

    // Multi-monitor
    public MultiMonitorMode MultiMonitorMode { get; set; } = MultiMonitorMode.SameForAll;

    // Startup
    public bool RunOnStartup { get; set; } = false;

    // Rendering (0 = auto-detect from primary monitor)
    public int RenderWidth { get; set; } = 0;
    public int RenderHeight { get; set; } = 0;

    // Ambient light level
    public float AmbientLight { get; set; } = 0.40f;

    // NASA EPIC settings
    public EpicImageType EpicImageType { get; set; } = EpicImageType.Natural;
    public bool EpicUseLatest { get; set; } = true;
    public string EpicSelectedDate { get; set; } = "";
    public string EpicSelectedImage { get; set; } = "";

    // NASA APOD settings
    public bool ApodUseLatest { get; set; } = true;
    public string ApodSelectedDate { get; set; } = "";
    public string ApodSelectedImageId { get; set; } = "";
    public string ApodSelectedImageUrl { get; set; } = "";

    // National Park Service settings
    public string NpsSearchQuery { get; set; } = "";
    public string NpsSelectedParkCode { get; set; } = "";
    public string NpsSelectedImageId { get; set; } = "";
    public string NpsSelectedImageUrl { get; set; } = "";

    // Smithsonian settings
    public string SmithsonianSearchQuery { get; set; } = "landscape painting";
    public string SmithsonianSelectedId { get; set; } = "";
    public string SmithsonianSelectedImageUrl { get; set; } = "";

    // Unified api.data.gov API key (works for NASA APOD, NPS, and Smithsonian)
    public string ApiDataGovKey { get; set; } = "DEMO_KEY";

    // Image quality filter (minimum quality for still images)
    public ImageQualityTier MinImageQuality { get; set; } = ImageQualityTier.SD;

    // Random rotation
    public bool RandomRotationEnabled { get; set; } = false;
    public bool RandomFromFavoritesOnly { get; set; } = false;

    // Favorites
    public List<FavoriteImage> Favorites { get; set; } = new();

    /// <summary>Lock object for thread-safe access to the Favorites list.</summary>
    [JsonIgnore]
    public readonly object FavoritesLock = new();

    // Settings presets (appearance-only saved configurations)
    public List<SettingsPreset> Presets { get; set; } = new();

    // User images settings
    public string UserImageSelectedId { get; set; } = "";
    public string UserImageSelectedPath { get; set; } = "";

    // First-run detection (used to skip wallpaper re-render on subsequent launches)
    public bool HasLaunchedBefore { get; set; } = false;

    // Cache duration in days (0 = keep forever)
    public int CacheDurationDays { get; set; } = 30;
    public int EpicCacheDurationDays { get; set; } = 14;

    // Window state (remembered across sessions)
    public int WindowWidth { get; set; } = 560;
    public int WindowHeight { get; set; } = 790;

    // Per-display configurations (used when MultiMonitorMode == PerDisplay)
    public List<DisplayConfig> DisplayConfigs { get; set; } = new();

    // Captures unknown JSON properties during deserialization (used for backward compat migration)
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>
/// Per-display configuration. Each monitor can have its own independent settings.
/// </summary>
public class DisplayConfig
{
    public string DeviceName { get; set; } = "";
    public DisplayMode DisplayMode { get; set; } = DisplayMode.Spherical;
    public ImageSource StillImageSource { get; set; } = ImageSource.NasaEpic;
    public float ZoomLevel { get; set; } = 3.94f;
    public float FieldOfView { get; set; } = 50.3f;
    public float CameraTilt { get; set; } = 42.0f;
    public float LongitudeOffset { get; set; } = 88.0f;
    public bool NightLightsEnabled { get; set; } = true;
    public float NightLightsBrightness { get; set; } = 1.5f;
    public float AmbientLight { get; set; } = 0.40f;
    public float ImageOffsetX { get; set; } = 0.0f;
    public float ImageOffsetY { get; set; } = 0.0f;
    public ImageStyle ImageStyle { get; set; } = ImageStyle.TopoBathy;

    // NASA EPIC settings
    public EpicImageType EpicImageType { get; set; } = EpicImageType.Natural;
    public bool EpicUseLatest { get; set; } = true;
    public string EpicSelectedDate { get; set; } = "";
    public string EpicSelectedImage { get; set; } = "";

    // NASA APOD settings
    public bool ApodUseLatest { get; set; } = true;
    public string ApodSelectedDate { get; set; } = "";
    public string ApodSelectedImageId { get; set; } = "";
    public string ApodSelectedImageUrl { get; set; } = "";

    // National Park Service settings
    public string NpsSearchQuery { get; set; } = "";
    public string NpsSelectedParkCode { get; set; } = "";
    public string NpsSelectedImageId { get; set; } = "";
    public string NpsSelectedImageUrl { get; set; } = "";

    // Smithsonian settings
    public string SmithsonianSearchQuery { get; set; } = "landscape painting";
    public string SmithsonianSelectedId { get; set; } = "";
    public string SmithsonianSelectedImageUrl { get; set; } = "";

    // Image quality filter
    public ImageQualityTier MinImageQuality { get; set; } = ImageQualityTier.SD;

    // Random rotation
    public bool RandomRotationEnabled { get; set; } = false;
    public bool RandomFromFavoritesOnly { get; set; } = false;

    // User images per-display
    public string UserImageSelectedId { get; set; } = "";
    public string UserImageSelectedPath { get; set; } = "";
}

/// <summary>
/// A user-favorited image from any source.
/// </summary>
public class FavoriteImage
{
    public ImageSource Source { get; set; }
    public string ImageId { get; set; } = "";
    public string Title { get; set; } = "";
    public string ThumbnailUrl { get; set; } = "";
    public string FullImageUrl { get; set; } = "";
    public string LocalCachePath { get; set; } = "";
}

/// <summary>
/// A saved appearance configuration (preset).
/// Contains all view settings but no system/API settings.
/// </summary>
public class SettingsPreset
{
    public string Name { get; set; } = "";
    public DisplayMode DisplayMode { get; set; }
    public ImageSource StillImageSource { get; set; }
    public float ZoomLevel { get; set; } = 3.94f;
    public float FieldOfView { get; set; } = 50.3f;
    public float CameraTilt { get; set; } = 42.0f;
    public float LongitudeOffset { get; set; } = 88.0f;
    public float ImageOffsetX { get; set; } = 0f;
    public float ImageOffsetY { get; set; } = 0f;
    public bool NightLightsEnabled { get; set; } = true;
    public float NightLightsBrightness { get; set; } = 1.5f;
    public float AmbientLight { get; set; } = 0.40f;
    public ImageStyle ImageStyle { get; set; } = ImageStyle.TopoBathy;
    public EpicImageType EpicImageType { get; set; } = EpicImageType.Natural;
    public ImageQualityTier MinImageQuality { get; set; } = ImageQualityTier.SD;
}

public enum DisplayMode { Spherical, FlatMap, Moon, StillImage }
public enum ImageSource { NasaEpic, NasaApod, NationalParks, Smithsonian, UserImages }
public enum ImageStyle { Topo, TopoBathy }
public enum MultiMonitorMode { SameForAll, SpanAcross, PerDisplay }
public enum EpicImageType { Natural, Enhanced }
public enum ImageQualityTier { Unknown, SD, HD, UD }
