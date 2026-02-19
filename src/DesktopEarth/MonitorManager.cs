using System.Runtime.InteropServices;

namespace DesktopEarth;

public static class MonitorManager
{
    /// <summary>
    /// Gets the total virtual desktop bounds spanning all monitors.
    /// </summary>
    public static (int Width, int Height) GetVirtualDesktopSize()
    {
        int width = GetSystemMetrics(SM_CXVIRTUALSCREEN);
        int height = GetSystemMetrics(SM_CYVIRTUALSCREEN);
        return (width > 0 ? width : 1920, height > 0 ? height : 1080);
    }

    /// <summary>
    /// Gets the primary monitor resolution.
    /// </summary>
    public static (int Width, int Height) GetPrimaryMonitorSize()
    {
        var screen = System.Windows.Forms.Screen.PrimaryScreen;
        return screen != null ? (screen.Bounds.Width, screen.Bounds.Height) : (1920, 1080);
    }

    /// <summary>
    /// Gets the highest resolution among all connected monitors.
    /// This ensures the rendered wallpaper looks sharp on every display,
    /// even in mixed-resolution setups (e.g. 1080p + 1440p).
    /// </summary>
    public static (int Width, int Height) GetHighestMonitorResolution()
    {
        var screens = System.Windows.Forms.Screen.AllScreens;
        if (screens.Length == 0)
            return (1920, 1080);

        int maxWidth = 0;
        int maxHeight = 0;
        foreach (var screen in screens)
        {
            if (screen.Bounds.Width > maxWidth)
                maxWidth = screen.Bounds.Width;
            if (screen.Bounds.Height > maxHeight)
                maxHeight = screen.Bounds.Height;
        }

        return (maxWidth > 0 ? maxWidth : 1920, maxHeight > 0 ? maxHeight : 1080);
    }

    /// <summary>
    /// Gets all connected monitors.
    /// </summary>
    public static System.Windows.Forms.Screen[] GetAllScreens()
    {
        return System.Windows.Forms.Screen.AllScreens;
    }

    /// <summary>
    /// Determines the render resolution based on settings and multi-monitor mode.
    /// For SameForAll mode, uses the highest monitor resolution to avoid
    /// black bars or blurriness on higher-res displays in mixed setups.
    /// </summary>
    public static (int Width, int Height) GetRenderResolution(AppSettings settings)
    {
        // If user has manually set resolution, use that
        if (settings.RenderWidth > 0 && settings.RenderHeight > 0)
            return (settings.RenderWidth, settings.RenderHeight);

        // Auto-detect based on multi-monitor mode
        return settings.MultiMonitorMode switch
        {
            MultiMonitorMode.SpanAcross => GetVirtualDesktopSize(),
            _ => GetHighestMonitorResolution()
        };
    }

    private const int SM_CXVIRTUALSCREEN = 78;
    private const int SM_CYVIRTUALSCREEN = 79;

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);
}
