namespace DesktopEarth;

public class AppSettings
{
    // Update frequency in seconds
    public int UpdateIntervalSeconds { get; set; } = 600; // 10 minutes

    // Display mode
    public DisplayMode DisplayMode { get; set; } = DisplayMode.Spherical;

    // View controls
    public float ZoomLevel { get; set; } = 2.8f;
    public float FieldOfView { get; set; } = 45.0f;
    public float CameraTilt { get; set; } = 20.0f;

    // Night lights
    public bool NightLightsEnabled { get; set; } = true;
    public float NightLightsBrightness { get; set; } = 1.2f;

    // Sun reflection
    public float SunSpecularIntensity { get; set; } = 0.0f;
    public float SunSpecularPower { get; set; } = 32.0f;

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
}

public enum DisplayMode { Spherical, FlatMap, Moon }
public enum ImageStyle { Topo, TopoBathy }
public enum MultiMonitorMode { SameForAll, SpanAcross, PerDisplay }
