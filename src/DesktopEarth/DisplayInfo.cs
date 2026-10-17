using System.Drawing;

namespace DesktopEarth;

/// <summary>
/// One connected monitor. <see cref="DeviceName"/> (\\.\DISPLAYn) is what
/// Windows APIs need right now but is renumbered on reboots and docking;
/// <see cref="MonitorId"/> is the stable key per-display settings are stored under.
/// </summary>
public sealed class DisplayInfo(string deviceName, string monitorId, string friendlyName,
    string position, Rectangle bounds, bool primary)
{
    public string DeviceName { get; } = deviceName;
    public string MonitorId { get; } = monitorId;
    public string FriendlyName { get; } = friendlyName;

    /// <summary>"Left", "Center", "Right", "Top", "Bottom", "Only" or "#n".</summary>
    public string Position { get; } = position;
    public Rectangle Bounds { get; } = bounds;
    public bool Primary { get; } = primary;

    /// <summary>Set when another connected monitor has the same friendly name.</summary>
    internal bool QualifyName { get; set; }

    /// <summary>Short name for the UI: "LG ULTRAGEAR", or "Left display".</summary>
    public string Title
    {
        get
        {
            string where = Position switch
            {
                "Only" => "Display",
                _ when Position.StartsWith('#') => "Display " + Position,
                _ => Position + " display"
            };
            if (string.IsNullOrEmpty(FriendlyName)) return where;
            return QualifyName ? $"{FriendlyName} ({Position.ToLowerInvariant()})" : FriendlyName;
        }
    }

    /// <summary>"1920 × 1080 · main display" style detail line.</summary>
    public string Detail => $"{Bounds.Width} × {Bounds.Height}" + (Primary ? " · main display" : "");
}
