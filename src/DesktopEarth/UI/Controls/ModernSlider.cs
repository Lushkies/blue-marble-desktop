using System.Drawing;

namespace DesktopEarth.UI.Controls;

/// <summary>
/// A flat slider: thin track, accent fill and a round thumb. <see cref="Scroll"/>
/// fires only for user input (mouse, wheel, keyboard), never when code sets
/// <see cref="Value"/> — so loading settings into the UI can't trigger a save.
/// </summary>
public class ModernSlider : StudioControl
{
    private int _min, _max = 100, _value;
    private bool _dragging, _hover;

    public event EventHandler? Scroll;

    public ModernSlider()
    {
        SetStyle(ControlStyles.Selectable, true);
        TabStop = true;
        Height = 24;
        AccessibleRole = AccessibleRole.Slider;
        Cursor = Cursors.Hand;
    }

    public int Minimum
    {
        get => _min;
        set { _min = value; if (_max < _min) _max = _min; Value = _value; Invalidate(); }
    }

    public int Maximum
    {
        get => _max;
        set { _max = Math.Max(value, _min); Value = _value; Invalidate(); }
    }

    public int Value
    {
        get => _value;
        set
        {
            int v = Math.Clamp(value, _min, _max);
            if (v == _value) return;
            _value = v;
            AccessibilityNotifyClients(AccessibleEvents.ValueChange, -1);
            Invalidate();
        }
    }

    /// <summary>Fill from the centre (for ±offset sliders) instead of from the left.</summary>
    public bool Bipolar { get; set; }

    public int SmallChange { get; set; } = 1;
    public int LargeChange { get; set; } = 10;

    private void SetByUser(int v)
    {
        int old = _value;
        Value = v;
        if (_value != old) Scroll?.Invoke(this, EventArgs.Empty);
    }

    private RectangleF TrackRect
    {
        get
        {
            float r = PxF(8); // thumb radius = inset so the thumb never clips
            return new RectangleF(r, Height / 2f - PxF(2), Width - 2 * r, PxF(4));
        }
    }

    private float ValueToX(int v)
    {
        var t = TrackRect;
        float frac = _max == _min ? 0 : (v - _min) / (float)(_max - _min);
        return t.Left + frac * t.Width;
    }

    private int XToValue(float x)
    {
        var t = TrackRect;
        float frac = Math.Clamp((x - t.Left) / Math.Max(1, t.Width), 0, 1);
        return (int)Math.Round(_min + frac * (_max - _min));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.HighQuality(g);
        var track = TrackRect;

        using (var path = Theme.RoundedRect(track, track.Height / 2))
        using (var b = new SolidBrush(Theme.BorderStrong))
            g.FillPath(b, path);

        float thumbX = ValueToX(_value);
        float fillFrom = Bipolar ? ValueToX((_min + _max) / 2) : track.Left;
        var fill = RectangleF.FromLTRB(Math.Min(fillFrom, thumbX), track.Top, Math.Max(fillFrom, thumbX), track.Bottom);
        if (fill.Width > 0.5f && Enabled)
        {
            using var path = Theme.RoundedRect(fill, track.Height / 2);
            using var b = new SolidBrush(Theme.Accent);
            g.FillPath(b, path);
        }
        if (Bipolar)
        {
            using var tick = new Pen(Theme.TextMuted, 1);
            float cx = ValueToX((_min + _max) / 2);
            g.DrawLine(tick, cx, track.Top - PxF(3), cx, track.Bottom + PxF(3));
        }

        float r = PxF(_hover || _dragging ? 8 : 7);
        var thumb = new RectangleF(thumbX - r, Height / 2f - r, 2 * r, 2 * r);
        if (Enabled && (_hover || _dragging || Focused))
        {
            using var halo = new SolidBrush(Theme.AccentSoft);
            g.FillEllipse(halo, RectangleF.Inflate(thumb, PxF(4), PxF(4)));
        }
        using (var tb = new SolidBrush(Enabled ? (Theme.IsDarkMode ? Theme.Text : Color.White) : Theme.TextMuted))
            g.FillEllipse(tb, thumb);
        using (var ring = new Pen(Enabled ? Theme.Accent : Theme.BorderStrong, PxF(3)))
            g.DrawEllipse(ring, RectangleF.Inflate(thumb, -PxF(1.5f), -PxF(1.5f)));

        if (Focused && ShowFocusCues)
        {
            using var focus = new Pen(Theme.Accent, 1);
            g.DrawEllipse(focus, RectangleF.Inflate(thumb, PxF(5), PxF(5)));
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left || !Enabled) return;
        Focus();
        _dragging = true;
        Capture = true;
        SetByUser(XToValue(e.X));
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragging) SetByUser(XToValue(e.X));
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _dragging = false;
        Capture = false;
        Invalidate();
    }

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = false; Invalidate(); }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        // Only react while hovered and focused, so scrolling the page past a
        // slider doesn't silently change it
        if (!Focused || !Enabled) return;
        SetByUser(_value + Math.Sign(e.Delta) * SmallChange);
        if (e is HandledMouseEventArgs h) h.Handled = true;
    }

    protected override bool IsInputKey(Keys keyData) =>
        keyData is Keys.Left or Keys.Right or Keys.Up or Keys.Down || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!Enabled) return;
        int? v = e.KeyCode switch
        {
            Keys.Left or Keys.Down => _value - SmallChange,
            Keys.Right or Keys.Up => _value + SmallChange,
            Keys.PageDown => _value - LargeChange,
            Keys.PageUp => _value + LargeChange,
            Keys.Home => _min,
            Keys.End => _max,
            _ => null
        };
        if (v.HasValue) { SetByUser(v.Value); e.Handled = true; }
    }

    protected override AccessibleObject CreateAccessibilityInstance() => new SliderAccessible(this);

    private sealed class SliderAccessible(ModernSlider owner) : ControlAccessibleObject(owner)
    {
        public override string? Value
        {
            get => owner.Value.ToString();
            set { if (int.TryParse(value, out int v)) owner.SetByUser(v); }
        }
    }
}
