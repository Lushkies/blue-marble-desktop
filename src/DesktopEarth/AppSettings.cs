namespace DesktopEarth;

public class AppSettings
{
    // Update frequency in seconds
    public int UpdateIntervalSeconds { get; set; } = 600; // 10 minutes

    // Display mode
    public DisplayMode DisplayMode { get; set; } = DisplayMode.Spherical;

    // View controls — Zoom combines old ZoomLevel + FieldOfView into a single control
    public float ZoomLevel { get; set; } = 2.8f;
    public float FieldOfView { get; set; } = 45.0f;

    // Latitude (degrees north/south). Was "CameraTilt" — kept same JSON key for back-compat.
    public float CameraTilt { get; set; } = 20.0f;

    // Longitude offset (degrees). Default -90 shows Americas
    public float LongitudeOffset { get; set; } = -90.0f;

    // Image offset (percentage of screen, -25 to +25). Moves rendered image without affecting view.
    public float ImageOffsetX { get; set; } = 0.0f;
    public float ImageOffsetY { get; set; } = 0.0f;

    // Night lights
    public bool NightLightsEnabled { get; set; } = true;
    public float NightLightsBrightness { get; set; } = 1.2f;

    // Image style
    public ImageStyle ImageStyle { get; set; } = ImageStyle.Topo;

    // Multi-monitor
    public MultiMonitorMode MultiMonitorMode { get; set; } = MultiMonitorMode.SameForAll;

    // Startup
    public bool RunOnStartup { get; set; } = false;

    // Rendering (0 = auto-detect from primary monitor)
    public int RenderWidth { get; set; } = 0;
    public int RenderHeight { get; set; } = 0;

    // Ambient light level
    public float AmbientLight { get; set; } = 0.15f;

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
    public float ZoomLevel { get; set; } = 2.8f;
    public float FieldOfView { get; set; } = 45.0f;
    public float CameraTilt { get; set; } = 20.0f;
    public float LongitudeOffset { get; set; } = -90.0f;
    public bool NightLightsEnabled { get; set; } = true;
    public float NightLightsBrightness { get; set; } = 1.2f;
    public float AmbientLight { get; set; } = 0.15f;
    public float ImageOffsetX { get; set; } = 0.0f;
    public float ImageOffsetY { get; set; } = 0.0f;
    public ImageStyle ImageStyle { get; set; } = ImageStyle.Topo;
}

public enum DisplayMode { Spherical, FlatMap, Moon }
public enum ImageStyle { Topo, TopoBathy }
public enum MultiMonitorMode { SameForAll, SpanAcross, PerDisplay }
