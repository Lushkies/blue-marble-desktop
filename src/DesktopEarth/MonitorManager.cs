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
    /// Gets all connected monitors.
    /// </summary>
    public static System.Windows.Forms.Screen[] GetAllScreens()
    {
        return System.Windows.Forms.Screen.AllScreens;
    }

    /// <summary>
    /// Determines the render resolution based on settings and multi-monitor mode.
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
            _ => GetPrimaryMonitorSize()
        };
    }

    private const int SM_CXVIRTUALSCREEN = 78;
    private const int SM_CYVIRTUALSCREEN = 79;

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);
}
