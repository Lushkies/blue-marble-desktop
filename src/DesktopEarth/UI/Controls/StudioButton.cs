using System.Drawing;

namespace DesktopEarth.UI.Controls;

public enum ButtonVariant
{
    /// <summary>Filled accent — the one main action in an area.</summary>
    Primary,
    /// <summary>Raised fill with a border — ordinary actions.</summary>
    Secondary,
    /// <summary>Pill-shaped toggle chip (sources, suggestions, presets).</summary>
    Chip,
    /// <summary>Sidebar navigation row with an accent bar when selected.</summary>
    Nav,
    /// <summary>Text-only accent link.</summary>
    Link
}

/// <summary>
/// The Studio button. A real <see cref="Button"/> underneath (keyboard,
/// AcceptButton, accessibility) with custom painting for each variant.
/// </summary>
public class StudioButton : Button
{
    private bool _hover, _pressed, _selected;
    private ButtonVariant _variant = ButtonVariant.Secondary;
    private string _glyph = "";

    public StudioButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Cursor = Cursors.Hand;
        Font = Theme.UiFont(9.75f);
        UseVisualStyleBackColor = false;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
    }

    public ButtonVariant Variant
    {
        get => _variant;
        set { _variant = value; Invalidate(); }
    }

    /// <summary>Selected state for chips and nav rows.</summary>
    public bool Selected
    {
        get => _selected;
        set
        {
            if (_selected == value) return;
            _selected = value;
            AccessibleDescription = value ? "Selected" : null;
            Invalidate();
        }
    }

    /// <summary>Optional icon-font glyph drawn before the text (see <see cref="Glyph"/>).</summary>
    public string IconGlyph
    {
        get => _glyph;
        set { _glyph = value ?? ""; Invalidate(); }
    }

    /// <summary>Optional color for the glyph (e.g. gold star); defaults to the text color.</summary>
    public Color? IconColor { get; set; }

    private int Px(float v) => (int)Math.Round(v * DeviceDpi / 96f);

    /// <summary>Width that fits the text (and glyph) for chips and toolbar buttons.</summary>
    public int PreferredWidth
    {
        get
        {
            int text = TextRenderer.MeasureText(Text, Font).Width;
            int glyph = _glyph.Length > 0 ? Px(22) : 0;
            int pad = _variant == ButtonVariant.Chip ? Px(28) : Px(32);
            return text + glyph + pad;
        }
    }

    /// <summary>Design-time height used when <see cref="Control.AutoSize"/> is on.</summary>
    public int AutoHeight { get; set; } = 32;

    // With AutoSize on, size comes from the text at the live DPI — never from a
    // width fixed at construction, which DPI auto-scaling would scale twice
    public override Size GetPreferredSize(Size proposedSize) => new(PreferredWidth, Px(AutoHeight));

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        if (AutoSize) Size = GetPreferredSize(Size.Empty);
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { _pressed = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.HighQuality(g);
        using (var bg = new SolidBrush(Parent?.BackColor ?? Theme.Ground))
            g.FillRectangle(bg, ClientRectangle);

        var rect = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        float radius = _variant is ButtonVariant.Chip ? rect.Height / 2 : Px(Theme.RadiusControl);

        Color fill, border, text;
        switch (_variant)
        {
            case ButtonVariant.Primary:
                fill = !Enabled ? Theme.Raised : _hover ? Theme.AccentHover : Theme.Accent;
                border = fill;
                text = Enabled ? Theme.OnAccent : Theme.TextMuted;
                break;
            case ButtonVariant.Chip:
                fill = _selected ? Theme.AccentSoft : _hover ? Theme.RaisedHover : Theme.Raised;
                border = _selected ? Theme.Accent : Theme.BorderStrong;
                text = _selected ? Theme.Text : _hover ? Theme.Text : Theme.TextSecondary;
                break;
            case ButtonVariant.Nav:
                fill = _selected ? Theme.Raised : _hover ? Theme.Raised : Theme.Chrome;
                border = fill;
                text = _selected || _hover ? Theme.Text : Theme.TextMuted;
                break;
            case ButtonVariant.Link:
                fill = Parent?.BackColor ?? Theme.Ground;
                border = fill;
                text = _hover ? Theme.AccentHover : Theme.Accent;
                break;
            default:
                fill = _hover ? Theme.RaisedHover : Theme.Raised;
                border = Theme.BorderStrong;
                text = Enabled ? Theme.Text : Theme.TextMuted;
                break;
        }
        if (_pressed && Enabled && _variant != ButtonVariant.Link)
            fill = Theme.Blend(fill, Theme.Ground, 0.25f);

        using (var path = Theme.RoundedRect(rect, radius))
        {
            using var fb = new SolidBrush(fill);
            g.FillPath(fb, path);
            if (border != fill)
            {
                using var pen = new Pen(border, 1f);
                g.DrawPath(pen, path);
            }
        }

        if (_variant == ButtonVariant.Nav && _selected)
        {
            using var bar = new SolidBrush(Theme.Accent);
            using var barPath = Theme.RoundedRect(new RectangleF(Px(6), (Height - Px(16)) / 2f, Px(3), Px(16)), Px(1.5f));
            g.FillPath(bar, barPath);
        }

        // Content: glyph + text, centered (left-aligned for nav rows)
        var font = _variant == ButtonVariant.Primary || (_variant == ButtonVariant.Nav && _selected)
            ? Theme.Semibold(Font.SizeInPoints) : Font;
        var textSize = TextRenderer.MeasureText(g, Text, font, Size.Empty, TextFormatFlags.NoPadding);
        int glyphW = _glyph.Length > 0 ? Px(16) : 0;
        int gap = _glyph.Length > 0 && Text.Length > 0 ? Px(8) : 0;
        int contentW = glyphW + gap + textSize.Width;
        int x = _variant == ButtonVariant.Nav ? Px(18) : (Width - contentW) / 2;

        if (glyphW > 0)
        {
            var glyphRect = new Rectangle(x, 0, glyphW, Height);
            TextRenderer.DrawText(g, _glyph, Glyph.Font(Font.SizeInPoints + 1), glyphRect,
                Enabled ? IconColor ?? text : Theme.TextMuted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
        var textRect = new Rectangle(x + glyphW + gap, 0, Math.Max(0, Width - x - glyphW - gap), Height);
        TextRenderer.DrawText(g, Text, font, textRect, text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding |
            TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);

        if (Focused && ShowFocusCues)
        {
            using var focus = new Pen(Theme.Accent, 1.5f);
            using var fp = Theme.RoundedRect(RectangleF.Inflate(rect, -2, -2), Math.Max(2, radius - 2));
            g.DrawPath(focus, fp);
        }
    }
}
