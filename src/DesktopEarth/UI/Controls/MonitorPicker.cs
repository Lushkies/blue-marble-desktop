using System.Drawing;
using System.Drawing.Drawing2D;

namespace DesktopEarth.UI.Controls;

/// <summary>
/// The connected monitors drawn to scale in their real arrangement, each
/// showing a thumbnail of its current wallpaper. Clicking one selects it for
/// editing — no guessing which "Display 2" is which.
/// </summary>
public class MonitorPicker : StudioControl
{
    private List<DisplayInfo> _displays = new();
    private readonly Dictionary<string, Image> _thumbs = new();
    private int _selected, _hover = -1;
    private bool _allSelected;

    public event EventHandler? SelectionChanged;

    public MonitorPicker()
    {
        SetStyle(ControlStyles.Selectable, true);
        TabStop = true;
        Size = new Size(320, 84);
        AccessibleRole = AccessibleRole.List;
        AccessibleName = "Displays";
    }

    public IReadOnlyList<DisplayInfo> Displays => _displays;

    public void SetDisplays(List<DisplayInfo> displays)
    {
        _displays = displays;
        _selected = Math.Clamp(_selected, 0, Math.Max(0, displays.Count - 1));
        Invalidate();
    }

    public int SelectedIndex
    {
        get => _selected;
        set { _selected = Math.Clamp(value, 0, Math.Max(0, _displays.Count - 1)); Invalidate(); }
    }

    public DisplayInfo? SelectedDisplay => _displays.Count > 0 ? _displays[_selected] : null;

    /// <summary>Same-for-all / Span modes: every monitor is highlighted and clicks are ignored.</summary>
    public bool AllSelected
    {
        get => _allSelected;
        set { _allSelected = value; Cursor = value ? Cursors.Default : Cursors.Hand; Invalidate(); }
    }

    /// <summary>Replace the thumbnail for a monitor (takes ownership of the image).</summary>
    public void SetThumbnail(string monitorId, Image? image)
    {
        if (_thumbs.Remove(monitorId, out var old)) old.Dispose();
        if (image != null) _thumbs[monitorId] = image;
        Invalidate();
    }

    private List<RectangleF> ComputeLayout()
    {
        var rects = new List<RectangleF>();
        if (_displays.Count == 0) return rects;
        var union = _displays.Select(d => d.Bounds).Aggregate(Rectangle.Union);
        float gap = PxF(5);
        float availW = Width - PxF(8) - gap * (_displays.Count - 1);
        float availH = Height - PxF(8);
        float scale = Math.Min(availW / union.Width, availH / union.Height);

        // Gaps are added between monitors left to right so their edges never touch
        var order = _displays.Select((d, i) => (d, i)).OrderBy(t => t.d.Bounds.Left).ToList();
        var result = new RectangleF[_displays.Count];
        float drawnW = union.Width * scale + gap * (_displays.Count - 1);
        float ox = (Width - drawnW) / 2f;
        float oy = (Height - union.Height * scale) / 2f;
        for (int k = 0; k < order.Count; k++)
        {
            var b = order[k].d.Bounds;
            result[order[k].i] = new RectangleF(
                ox + (b.Left - union.Left) * scale + gap * k,
                oy + (b.Top - union.Top) * scale,
                b.Width * scale, b.Height * scale);
        }
        rects.AddRange(result);
        return rects;
    }

    private int HitTest(Point p)
    {
        var rects = ComputeLayout();
        for (int i = 0; i < rects.Count; i++)
            if (rects[i].Contains(p)) return i;
        return -1;
    }

    private void Pick(int i)
    {
        if (_allSelected || i < 0 || i >= _displays.Count || i == _selected) return;
        _selected = i;
        Invalidate();
        AccessibilityNotifyClients(AccessibleEvents.Selection, i);
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
        Focus();
        Pick(HitTest(e.Location));
    }

    protected override bool IsInputKey(Keys keyData) =>
        keyData is Keys.Left or Keys.Right || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Left) Pick(Math.Max(0, _selected - 1));
        if (e.KeyCode == Keys.Right) Pick(Math.Min(_displays.Count - 1, _selected + 1));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.HighQuality(g);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        var rects = ComputeLayout();
        float radius = PxF(5);

        for (int i = 0; i < rects.Count; i++)
        {
            var r = rects[i];
            bool sel = _allSelected || i == _selected;
            using var path = Theme.RoundedRect(r, radius);

            using (var black = new SolidBrush(Color.Black)) g.FillPath(black, path);
            if (_thumbs.TryGetValue(_displays[i].MonitorId, out var img))
            {
                var state = g.Save();
                g.SetClip(path);
                DrawCover(g, img, r);
                if (!sel)
                {
                    using var dim = new SolidBrush(Color.FromArgb(i == _hover ? 70 : 120, Theme.Ground));
                    g.FillRectangle(dim, r);
                }
                g.Restore(state);
            }

            using var pen = new Pen(sel ? Theme.Accent : (i == _hover ? Theme.TextMuted : Theme.BorderStrong), PxF(sel ? 2 : 1.5f));
            g.DrawPath(pen, path);

            if (_displays[i].Primary)
            {
                // Small "main" dot, like the taskbar indicator
                float d = PxF(5);
                using var dot = new SolidBrush(sel ? Theme.Accent : Theme.TextMuted);
                g.FillEllipse(dot, r.Left + r.Width / 2 - d / 2, r.Bottom - PxF(8), d, d);
            }
        }

        if (Focused && ShowFocusCues && !_allSelected && _selected < rects.Count)
        {
            using var focus = new Pen(Theme.Text, 1) { DashStyle = DashStyle.Dot };
            var fr = RectangleF.Inflate(rects[_selected], PxF(3), PxF(3));
            g.DrawRectangle(focus, fr.X, fr.Y, fr.Width, fr.Height);
        }
    }

    private static void DrawCover(Graphics g, Image img, RectangleF dest)
    {
        float scale = Math.Max(dest.Width / img.Width, dest.Height / img.Height);
        float w = img.Width * scale, h = img.Height * scale;
        g.DrawImage(img, dest.X + (dest.Width - w) / 2, dest.Y + (dest.Height - h) / 2, w, h);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var t in _thumbs.Values) t.Dispose();
            _thumbs.Clear();
        }
        base.Dispose(disposing);
    }
}
