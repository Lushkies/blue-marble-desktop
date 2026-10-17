using System.Drawing;

namespace DesktopEarth.UI.Controls;

/// <summary>
/// A rounded surface that groups related controls, with an optional small-caps
/// title. Its BackColor is the card surface, so child controls blend in.
/// </summary>
public class Card : Panel
{
    private string _title = "";

    public Card()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        BackColor = Theme.Surface;
        ForeColor = Theme.Text;
        Padding = new Padding(16, 14, 16, 16);
        Font = Theme.UiFont();
    }

    /// <summary>Small-caps heading drawn in the top padding ("CENTER ON").</summary>
    public string Title
    {
        get => _title;
        set
        {
            _title = value ?? "";
            // Design-time (96 DPI) units: cards are built before the form's
            // DPI auto-scaling runs, which scales Padding along with everything else
            Padding = new Padding(Padding.Left, _title.Length > 0 ? 40 : 14, Padding.Right, Padding.Bottom);
            Invalidate();
        }
    }

    /// <summary>Optional text drawn at the right end of the title row.</summary>
    public string TitleNote { get; set; } = "";

    /// <summary>Card color; set false to draw only the border (a quieter group).</summary>
    public bool Filled { get; set; } = true;

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var g = e.Graphics;
        using (var bg = new SolidBrush(Parent?.BackColor ?? Theme.Ground))
            g.FillRectangle(bg, ClientRectangle);

        Theme.HighQuality(g);
        float scale = DeviceDpi / 96f;
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        using var path = Theme.RoundedRect(r, Theme.RadiusCard * scale);
        if (Filled)
        {
            using var fill = new SolidBrush(BackColor);
            g.FillPath(fill, path);
        }
        using var pen = new Pen(Theme.Border, 1);
        g.DrawPath(pen, path);

        if (_title.Length > 0)
        {
            var titleRect = new Rectangle(Padding.Left, (int)(14 * scale), Width - Padding.Horizontal, (int)(18 * scale));
            TextRenderer.DrawText(g, _title.ToUpperInvariant(), Theme.Semibold(8f), titleRect, Theme.TextMuted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            if (TitleNote.Length > 0)
                TextRenderer.DrawText(g, TitleNote, Theme.UiFont(8.75f), titleRect, Theme.TextMuted,
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    protected override void OnPaint(PaintEventArgs e) { }
}

/// <summary>
/// A plain label that paints its parent's background (no gray boxes on cards)
/// and wraps when <see cref="Label.AutoSize"/> is off.
/// </summary>
public class StudioLabel : Label
{
    public StudioLabel()
    {
        Font = Theme.UiFont();
        ForeColor = Theme.Text;
        BackColor = Color.Transparent;
        UseMnemonic = false;
        AutoSize = true;
    }
}
