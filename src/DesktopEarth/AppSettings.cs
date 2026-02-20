namespace DesktopEarth;

public class AppSettings
{
    // Update frequency in seconds
    public int UpdateIntervalSeconds { get; set; } = 600; // 10 minutes

    // Display mode
    public DisplayMode DisplayMode { get; set; } = DisplayMode.Spherical;

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
    public float NightLightsBrightness { get; set; } = 1.7f;

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
    public float AmbientLight { get; set; } = 0.15f;

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

    // Unsplash settings
    public string UnsplashTopic { get; set; } = "nature";
    public string UnsplashSearchQuery { get; set; } = "";
    public string UnsplashSelectedImageId { get; set; } = "";
    public string UnsplashSelectedImageUrl { get; set; } = "";
    public string UnsplashPhotographerName { get; set; } = "";

    // Smithsonian settings
    public string SmithsonianSearchQuery { get; set; } = "nature";
    public string SmithsonianSelectedId { get; set; } = "";
    public string SmithsonianSelectedImageUrl { get; set; } = "";

    // API keys (stored per-user, not shared)
    public string NasaApiKey { get; set; } = "DEMO_KEY";
    public string NpsApiKey { get; set; } = "";
    public string UnsplashAccessKey { get; set; } = "";
    public string SmithsonianApiKey { get; set; } = "";

    // Random rotation
    public bool RandomRotationEnabled { get; set; } = false;
    public bool RandomFromFavoritesOnly { get; set; } = false;

    // Favorites
    public List<FavoriteImage> Favorites { get; set; } = new();

    // Per-display configurations (used when MultiMonitorMode == PerDisplay)
    public List<DisplayConfig> DisplayConfigs { get; set; } = new();
}

/// <summary>
/// Per-display configuration. Each monitor can have its own independent settings.
/// </summary>
public class DisplayConfig
{
    public string DeviceName { get; set; } = "";
    public DisplayMode DisplayMode { get; set; } = DisplayMode.Spherical;
    public float ZoomLevel { get; set; } = 3.94f;
    public float FieldOfView { get; set; } = 50.3f;
    public float CameraTilt { get; set; } = 42.0f;
    public float LongitudeOffset { get; set; } = 88.0f;
    public bool NightLightsEnabled { get; set; } = true;
    public float NightLightsBrightness { get; set; } = 1.7f;
    public float AmbientLight { get; set; } = 0.15f;
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

    // Unsplash settings
    public string UnsplashTopic { get; set; } = "nature";
    public string UnsplashSearchQuery { get; set; } = "";
    public string UnsplashSelectedImageId { get; set; } = "";
    public string UnsplashSelectedImageUrl { get; set; } = "";
    public string UnsplashPhotographerName { get; set; } = "";

    // Smithsonian settings
    public string SmithsonianSearchQuery { get; set; } = "nature";
    public string SmithsonianSelectedId { get; set; } = "";
    public string SmithsonianSelectedImageUrl { get; set; } = "";

    // Random rotation
    public bool RandomRotationEnabled { get; set; } = false;
    public bool RandomFromFavoritesOnly { get; set; } = false;
}

/// <summary>
/// A user-favorited image from any source.
/// </summary>
public class FavoriteImage
{
    public DisplayMode Source { get; set; }
    public string ImageId { get; set; } = "";
    public string Title { get; set; } = "";
    public string ThumbnailUrl { get; set; } = "";
    public string FullImageUrl { get; set; } = "";
    public string LocalCachePath { get; set; } = "";
}

public enum DisplayMode { Spherical, FlatMap, Moon, NasaEpic, NasaApod, NationalParks, Unsplash, Smithsonian }
public enum ImageStyle { Topo, TopoBathy }
public enum MultiMonitorMode { SameForAll, SpanAcross, PerDisplay }
public enum EpicImageType { Natural, Enhanced }
