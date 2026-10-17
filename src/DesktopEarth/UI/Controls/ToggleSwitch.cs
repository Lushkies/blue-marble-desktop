using System.Drawing;

namespace DesktopEarth.UI.Controls;

/// <summary>
/// On/off switch. <see cref="Toggled"/> fires only when the user flips it;
/// setting <see cref="Checked"/> from code is silent.
/// </summary>
public class ToggleSwitch : StudioControl
{
    private bool _checked;

    public event EventHandler? Toggled;

    public ToggleSwitch()
    {
        SetStyle(ControlStyles.Selectable, true);
        TabStop = true;
        Size = new Size(42, 22);
        Cursor = Cursors.Hand;
        AccessibleRole = AccessibleRole.CheckButton;
    }

    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked == value) return;
            _checked = value;
            AccessibilityNotifyClients(AccessibleEvents.StateChange, -1);
            Invalidate();
        }
    }

    private void Flip()
    {
        if (!Enabled) return;
        Checked = !Checked;
        Toggled?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnClick(EventArgs e) { base.OnClick(e); Focus(); Flip(); }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode is Keys.Space or Keys.Enter) { Flip(); e.Handled = true; }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.HighQuality(g);
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        bool on = _checked && Enabled;

        using (var path = Theme.RoundedRect(r, r.Height / 2))
        {
            using var fill = new SolidBrush(on ? Theme.Accent : Theme.Raised);
            g.FillPath(fill, path);
            using var pen = new Pen(on ? Theme.Accent : Theme.BorderStrong, 1);
            g.DrawPath(pen, path);
        }

        float d = r.Height - PxF(6);
        float x = _checked ? r.Right - PxF(3) - d : r.Left + PxF(3);
        using (var knob = new SolidBrush(on ? Theme.OnAccent : Theme.TextMuted))
            g.FillEllipse(knob, x, r.Top + PxF(3), d, d);

        DrawFocusRing(g, RectangleF.Inflate(r, PxF(2), PxF(2)), r.Height / 2 + PxF(2));
    }

    protected override AccessibleObject CreateAccessibilityInstance() => new SwitchAccessible(this);

    private sealed class SwitchAccessible(ToggleSwitch owner) : ControlAccessibleObject(owner)
    {
        public override AccessibleStates State =>
            base.State | (owner.Checked ? AccessibleStates.Checked : AccessibleStates.None);
        public override string DefaultAction => owner.Checked ? "Turn off" : "Turn on";
        public override void DoDefaultAction() => owner.Flip();
    }
}

/// <summary>
/// A row of mutually exclusive segments (Globe | Flat map | Moon | Photos).
/// <see cref="SelectionChanged"/> fires only for user picks.
/// </summary>
public class SegmentedControl : StudioControl
{
    private readonly List<string> _items = new();
    private int _selected = -1, _hover = -1;

    public event EventHandler? SelectionChanged;

    public SegmentedControl()
    {
        SetStyle(ControlStyles.Selectable, true);
        TabStop = true;
        Height = 34;
        Cursor = Cursors.Hand;
        AccessibleRole = AccessibleRole.PageTabList;
    }

    /// <summary>Background behind the segments (defaults to the parent's).</summary>
    public Color? TrackColor { get; set; }

    public void SetItems(params string[] items)
    {
        _items.Clear();
        _items.AddRange(items);
        if (_selected >= _items.Count) _selected = -1;
        Invalidate();
    }

    public int SelectedIndex
    {
        get => _selected;
        set { if (_selected == value) return; _selected = value; Invalidate(); }
    }

    /// <summary>Width that fits every label comfortably.</summary>
    public int PreferredWidth =>
        _items.Count == 0 ? 0 :
        _items.Sum(i => TextRenderer.MeasureText(i, Font).Width + Px(28)) + Px(6);

    private RectangleF SegmentRect(int i)
    {
        float inner = Width - PxF(6);
        float w = inner / Math.Max(1, _items.Count);
        return new RectangleF(PxF(3) + i * w, PxF(3), w, Height - PxF(6));
    }

    private int HitTest(Point p)
    {
        for (int i = 0; i < _items.Count; i++)
            if (SegmentRect(i).Contains(p)) return i;
        return -1;
    }

    private void Pick(int i)
    {
        if (i < 0 || i >= _items.Count || i == _selected || !Enabled) return;
        _selected = i;
        Invalidate();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int h = HitTest(e.Location);
        if (h != _hover) { _hover = h; Invalidate(); }
    }

    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = -1; Invalidate(); }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left) { Focus(); Pick(HitTest(e.Location)); }
    }

    protected override bool IsInputKey(Keys keyData) =>
        keyData is Keys.Left or Keys.Right || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Left) { Pick(Math.Max(0, _selected - 1)); e.Handled = true; }
        if (e.KeyCode == Keys.Right) { Pick(Math.Min(_items.Count - 1, _selected + 1)); e.Handled = true; }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.HighQuality(g);
        var outer = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        float radius = PxF(Theme.RadiusControl + 1);
        using (var path = Theme.RoundedRect(outer, radius))
        {
            using var b = new SolidBrush(TrackColor ?? Theme.Well);
            g.FillPath(b, path);
            using var pen = new Pen(Theme.Border, 1);
            g.DrawPath(pen, path);
        }

        for (int i = 0; i < _items.Count; i++)
        {
            var r = SegmentRect(i);
            bool sel = i == _selected;
            if (sel || i == _hover)
            {
                using var path = Theme.RoundedRect(r, PxF(Theme.RadiusControl - 1));
                using var b = new SolidBrush(sel ? (Enabled ? Theme.Accent : Theme.BorderStrong) : Theme.Raised);
                g.FillPath(b, path);
            }
            var font = sel ? Theme.Semibold(Font.SizeInPoints) : Font;
            var color = sel ? Theme.OnAccent : (i == _hover ? Theme.Text : Theme.TextMuted);
            if (!Enabled) color = Theme.TextMuted;
            TextRenderer.DrawText(g, _items[i], font, Rectangle.Round(r), color,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
        }

        DrawFocusRing(g, outer, radius);
    }
}
