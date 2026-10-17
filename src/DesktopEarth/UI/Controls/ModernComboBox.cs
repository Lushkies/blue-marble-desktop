using System.Drawing;
using System.Runtime.InteropServices;

namespace DesktopEarth.UI.Controls;

/// <summary>
/// A drop-down list styled to match the Studio theme: the closed box is
/// repainted (rounded, themed, chevron) and list items are owner-drawn.
/// </summary>
public class ModernComboBox : ComboBox
{
    private const int WM_PAINT = 0x000F;
    private bool _hover;

    public ModernComboBox()
    {
        DropDownStyle = ComboBoxStyle.DropDownList;
        DrawMode = DrawMode.OwnerDrawFixed;
        FlatStyle = FlatStyle.Flat;
        Font = Theme.UiFont(9.75f);
        BackColor = Theme.Raised;
        ForeColor = Theme.Text;
        ItemHeight = 22;
        Cursor = Cursors.Hand;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.StyleScrollableControl(this); // dark scrollbar in the drop-down
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0) return;
        bool selected = (e.State & DrawItemState.Selected) != 0;
        bool inEdit = (e.State & DrawItemState.ComboBoxEdit) != 0;
        var bg = selected && !inEdit ? Theme.Accent : Theme.Surface;
        var fg = selected && !inEdit ? Theme.OnAccent : Theme.Text;
        using (var b = new SolidBrush(bg))
            e.Graphics.FillRectangle(b, e.Bounds);
        var rect = Rectangle.Inflate(e.Bounds, -6, 0);
        TextRenderer.DrawText(e.Graphics, GetItemText(Items[e.Index]), Font, rect, fg,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg == WM_PAINT) PaintClosedBox();
    }

    /// <summary>Draws over the native closed box so it matches the other controls.</summary>
    private void PaintClosedBox()
    {
        using var g = Graphics.FromHwnd(Handle);
        Theme.HighQuality(g);
        float s = DeviceDpi / 96f;

        using (var bg = new SolidBrush(Parent?.BackColor ?? Theme.Ground))
            g.FillRectangle(bg, ClientRectangle);

        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        using (var path = Theme.RoundedRect(r, Theme.RadiusControl * s))
        {
            using var fill = new SolidBrush(_hover && Enabled ? Theme.RaisedHover : Theme.Raised);
            g.FillPath(fill, path);
            using var pen = new Pen(Focused ? Theme.Accent : Theme.BorderStrong, 1);
            g.DrawPath(pen, path);
        }

        int chevronW = (int)(28 * s);
        var textRect = new Rectangle((int)(10 * s), 0, Width - chevronW - (int)(10 * s), Height);
        string text = SelectedIndex >= 0 ? GetItemText(SelectedItem) ?? "" : "";
        TextRenderer.DrawText(g, text, Font, textRect, Enabled ? Theme.Text : Theme.TextMuted,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
            TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);

        var chevronRect = new Rectangle(Width - chevronW, 0, chevronW - (int)(4 * s), Height);
        TextRenderer.DrawText(g, Glyph.ChevronDown, Glyph.Font(8f), chevronRect, Theme.TextMuted,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    protected override void OnSelectedIndexChanged(EventArgs e)
    {
        base.OnSelectedIndexChanged(e);
        Invalidate();
    }

    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
}

/// <summary>
/// A rounded text field matching the other controls: borderless text box,
/// optional magnifier glyph (search), Enter raises <see cref="Submitted"/>.
/// </summary>
public class TextField : StudioControl
{
    private readonly TextBox _box;

    public event EventHandler? Submitted;

    public TextField()
    {
        Height = 34;
        _box = new TextBox
        {
            BorderStyle = BorderStyle.None,
            BackColor = Theme.Raised,
            ForeColor = Theme.Text,
            Font = Theme.UiFont(10f),
        };
        _box.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            Submitted?.Invoke(this, EventArgs.Empty);
        };
        _box.GotFocus += (_, _) => Invalidate();
        _box.LostFocus += (_, _) => Invalidate();
        Controls.Add(_box);
        Cursor = Cursors.IBeam;
    }

    public override string Text
    {
        get => _box.Text;
#pragma warning disable CS8765 // Control.Text setter is nullable-annotated loosely
        set => _box.Text = value ?? "";
#pragma warning restore CS8765
    }

    /// <summary>Draw the magnifier and indent the text (search fields).</summary>
    public bool ShowSearchIcon
    {
        get => _showSearchIcon;
        set { _showSearchIcon = value; PerformLayout(); Invalidate(); }
    }
    private bool _showSearchIcon = true;

    /// <summary>Raised when the text changes (typed or set).</summary>
    public new event EventHandler? TextChanged
    {
        add => _box.TextChanged += value;
        remove => _box.TextChanged -= value;
    }

    public string PlaceholderText
    {
        get => _box.PlaceholderText;
        set { _box.PlaceholderText = value; AccessibleName = value; _box.AccessibleName = value; }
    }

    protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); _box.Focus(); }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        if (_box is null) return; // Base constructor lays out before _box exists
        int left = ShowSearchIcon ? Px(34) : Px(12);
        _box.SetBounds(left, (Height - _box.PreferredHeight) / 2 + 1, Width - left - Px(10), _box.PreferredHeight);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.HighQuality(g);
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        using var path = Theme.RoundedRect(r, PxF(Theme.RadiusControl));
        using (var fill = new SolidBrush(Theme.Raised)) g.FillPath(fill, path);
        using (var pen = new Pen(_box.Focused ? Theme.Accent : Theme.BorderStrong, 1)) g.DrawPath(pen, path);
        if (!ShowSearchIcon) return;
        TextRenderer.DrawText(g, Glyph.Search, Glyph.Font(9f), new Rectangle(Px(10), 0, Px(18), Height),
            Theme.TextMuted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }
}
