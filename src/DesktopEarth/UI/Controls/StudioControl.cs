using System.Drawing;

namespace DesktopEarth.UI.Controls;

/// <summary>
/// Base for the custom-painted Studio controls: double-buffered, repaints on
/// resize, paints its parent's background behind rounded shapes, and scales
/// its design-time (96 DPI) metrics with <see cref="Px"/>.
/// </summary>
public abstract class StudioControl : Control
{
    protected StudioControl()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.UserPaint |
                 ControlStyles.SupportsTransparentBackColor, true);
        Font = Theme.UiFont();
    }

    /// <summary>A 96-DPI design value scaled to this control's monitor.</summary>
    protected int Px(float designPixels) => (int)Math.Round(designPixels * DeviceDpi / 96f);
    protected float PxF(float designPixels) => designPixels * DeviceDpi / 96f;

    /// <summary>The color showing through around rounded shapes.</summary>
    protected Color Backdrop => Parent?.BackColor ?? Theme.Ground;

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        using var b = new SolidBrush(Backdrop);
        e.Graphics.FillRectangle(b, ClientRectangle);
    }

    protected override void OnParentBackColorChanged(EventArgs e)
    {
        base.OnParentBackColorChanged(e);
        Invalidate();
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        Invalidate();
    }

    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

    /// <summary>Keyboard focus ring, drawn only when the user navigates with the keyboard.</summary>
    protected void DrawFocusRing(Graphics g, RectangleF r, float radius)
    {
        if (!Focused || !ShowFocusCues) return;
        using var pen = new Pen(Theme.Accent, PxF(1.5f));
        using var path = Theme.RoundedRect(RectangleF.Inflate(r, PxF(-1), PxF(-1)), radius);
        g.DrawPath(pen, path);
    }
}

/// <summary>Icon glyphs from the Windows icon font (Segoe Fluent Icons on 11, MDL2 on 10).</summary>
public static class Glyph
{
    public const string Monitor = "";
    public const string Library = "";
    public const string Settings = "";
    public const string Star = "";
    public const string StarFilled = "";
    public const string Search = "";
    public const string Add = "";
    public const string Refresh = "";
    public const string Next = "";
    public const string Sun = "";
    public const string ChevronDown = "";
    public const string Folder = "";
    public const string Delete = "";
    public const string Check = "";
    public const string Info = "";

    private static readonly Lazy<string> Family = new(() =>
    {
        using var fonts = new System.Drawing.Text.InstalledFontCollection();
        var names = fonts.Families.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return names.Contains("Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";
    });

    private static readonly Dictionary<float, Font> Cache = new();

    public static Font Font(float points)
    {
        lock (Cache)
        {
            if (!Cache.TryGetValue(points, out var f))
                Cache[points] = f = new Font(Family.Value, points, GraphicsUnit.Point);
            return f;
        }
    }
}

/// <summary>A small filled circle (status indicator); its ForeColor is the color.</summary>
public class StatusDot : StudioControl
{
    public StatusDot() { Size = new Size(8, 8); }

    protected override void OnForeColorChanged(EventArgs e) { base.OnForeColorChanged(e); Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        Theme.HighQuality(e.Graphics);
        using var b = new SolidBrush(ForeColor);
        e.Graphics.FillEllipse(b, 0, 0, Width - 1, Height - 1);
    }
}
