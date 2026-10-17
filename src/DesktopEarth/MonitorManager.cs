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

        // Pick the single largest screen — taking max width and max height
        // independently can produce an aspect ratio matching no display
        // (e.g. ultrawide + portrait -> 3440x2560)
        var best = screens[0];
        foreach (var screen in screens)
        {
            if ((long)screen.Bounds.Width * screen.Bounds.Height >
                (long)best.Bounds.Width * best.Bounds.Height)
                best = screen;
        }

        return (best.Bounds.Width > 0 ? best.Bounds.Width : 1920,
                best.Bounds.Height > 0 ? best.Bounds.Height : 1080);
    }

    /// <summary>
    /// Gets all connected monitors.
    /// </summary>
    public static System.Windows.Forms.Screen[] GetAllScreens()
    {
        return System.Windows.Forms.Screen.AllScreens;
    }

    /// <summary>
    /// All connected monitors with a stable identity and human labels, ordered
    /// left to right (then top to bottom) the way they are physically arranged.
    /// </summary>
    public static List<DisplayInfo> GetDisplays()
    {
        var screens = System.Windows.Forms.Screen.AllScreens;
        Dictionary<string, (string FriendlyName, string DevicePath)> targets;
        try { targets = MonitorNameHelper.GetMonitorTargets(); }
        catch { targets = new(StringComparer.OrdinalIgnoreCase); }

        var ordered = screens
            .OrderBy(s => s.Bounds.Left + s.Bounds.Width / 2)
            .ThenBy(s => s.Bounds.Top)
            .ToList();

        string[] positions = ordered.Count switch
        {
            1 => ["Only"],
            2 when IsStackedVertically(ordered[0], ordered[1]) => ["Top", "Bottom"],
            2 => ["Left", "Right"],
            3 => ["Left", "Center", "Right"],
            _ => ordered.Select((_, i) => $"#{i + 1}").ToArray()
        };
        if (positions.Length == 2 && positions[0] == "Top" && ordered[0].Bounds.Top > ordered[1].Bounds.Top)
            (ordered[0], ordered[1]) = (ordered[1], ordered[0]);

        var result = new List<DisplayInfo>();
        for (int i = 0; i < ordered.Count; i++)
        {
            var s = ordered[i];
            string device = s.DeviceName.TrimEnd('\0');
            targets.TryGetValue(device, out var target);
            string friendly = target.FriendlyName ?? "";
            // Fall back to the GDI name when Windows reports no device path, so
            // every display still has a (less stable) key
            string id = string.IsNullOrEmpty(target.DevicePath) ? device : target.DevicePath;
            result.Add(new DisplayInfo(device, id, friendly, positions[i], s.Bounds, s.Primary));
        }

        // Two identical monitors share a friendly name — qualify it with position
        foreach (var dup in result.Where(d => d.FriendlyName.Length > 0)
                     .GroupBy(d => d.FriendlyName).Where(g => g.Count() > 1).SelectMany(g => g))
            dup.QualifyName = true;

        return result;
    }

    private static bool IsStackedVertically(System.Windows.Forms.Screen a, System.Windows.Forms.Screen b) =>
        a.Bounds.Right > b.Bounds.Left && b.Bounds.Right > a.Bounds.Left;

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
