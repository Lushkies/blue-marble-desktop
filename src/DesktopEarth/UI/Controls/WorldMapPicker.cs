using System.Drawing;
using System.Drawing.Drawing2D;

namespace DesktopEarth.UI.Controls;

/// <summary>
/// An equirectangular world map you click (or drag, or arrow-key) to choose
/// the point the globe centers on. Uses real geographic coordinates:
/// latitude north-positive, longitude east-positive.
/// </summary>
public class WorldMapPicker : StudioControl
{
    private Image? _map;
    private double _lat, _lon;
    private bool _dragging;

    /// <summary>Latitude is limited so the globe never looks straight down a pole.</summary>
    public const double MaxLatitude = 75;

    /// <summary>Fires when the user moves the pin.</summary>
    public event EventHandler? LocationPicked;

    public WorldMapPicker()
    {
        SetStyle(ControlStyles.Selectable, true);
        TabStop = true;
        Cursor = Cursors.Cross;
        Size = new Size(320, 160);
        AccessibleRole = AccessibleRole.Graphic;
        AccessibleName = "World map — click to choose where the globe is centered";
    }

    /// <summary>The map image (takes ownership).</summary>
    public Image? MapImage
    {
        get => _map;
        set { _map?.Dispose(); _map = value; Invalidate(); }
    }

    public double Latitude => _lat;
    public double Longitude => _lon;

    public void SetLocation(double lat, double lon)
    {
        _lat = Math.Clamp(lat, -MaxLatitude, MaxLatitude);
        _lon = NormalizeLon(lon);
        AccessibleDescription = $"{_lat:0}°, {_lon:0}°";
        Invalidate();
    }

    private static double NormalizeLon(double lon)
    {
        lon %= 360;
        if (lon > 180) lon -= 360;
        if (lon < -180) lon += 360;
        return lon;
    }

    private RectangleF MapRect => new(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);

    private void PickAt(Point p)
    {
        var r = MapRect;
        double lon = (p.X - r.Left) / r.Width * 360 - 180;
        double lat = 90 - (p.Y - r.Top) / r.Height * 180;
        SetLocation(lat, lon);
        LocationPicked?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        Focus();
        _dragging = true;
        Capture = true;
        PickAt(e.Location);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragging) PickAt(e.Location);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _dragging = false;
        Capture = false;
    }

    protected override bool IsInputKey(Keys keyData) =>
        keyData is Keys.Left or Keys.Right or Keys.Up or Keys.Down || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        double step = e.Shift ? 10 : 1;
        (double dLat, double dLon) = e.KeyCode switch
        {
            Keys.Up => (step, 0d), Keys.Down => (-step, 0d),
            Keys.Left => (0d, -step), Keys.Right => (0d, step),
            _ => (0d, 0d)
        };
        if (dLat == 0 && dLon == 0) return;
        SetLocation(_lat + dLat, _lon + dLon);
        LocationPicked?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.HighQuality(g);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        var r = MapRect;
        float radius = PxF(Theme.RadiusControl);
        using var path = Theme.RoundedRect(r, radius);

        var state = g.Save();
        g.SetClip(path);
        using (var black = new SolidBrush(Color.Black)) g.FillRectangle(black, r);
        if (_map != null) g.DrawImage(_map, r);

        // Shade the polar bands the globe can't center on
        float band = (float)((90 - MaxLatitude) / 180 * r.Height);
        using (var shade = new SolidBrush(Color.FromArgb(110, 0, 0, 0)))
        {
            g.FillRectangle(shade, r.Left, r.Top, r.Width, band);
            g.FillRectangle(shade, r.Left, r.Bottom - band, r.Width, band);
        }

        float x = (float)(r.Left + (_lon + 180) / 360 * r.Width);
        float y = (float)(r.Top + (90 - _lat) / 180 * r.Height);
        using (var cross = new Pen(Color.FromArgb(120, Theme.Accent), 1))
        {
            g.DrawLine(cross, x, r.Top, x, r.Bottom);
            g.DrawLine(cross, r.Left, y, r.Right, y);
        }
        g.Restore(state);

        float d = PxF(13);
        using (var halo = new SolidBrush(Color.FromArgb(70, Theme.Accent)))
            g.FillEllipse(halo, x - d, y - d, 2 * d, 2 * d);
        float pin = PxF(6);
        using (var fill = new SolidBrush(Theme.Accent)) g.FillEllipse(fill, x - pin, y - pin, 2 * pin, 2 * pin);
        using (var ring = new Pen(Color.White, PxF(2))) g.DrawEllipse(ring, x - pin, y - pin, 2 * pin, 2 * pin);

        using (var border = new Pen(Focused && ShowFocusCues ? Theme.Accent : Theme.Border, 1))
            g.DrawPath(border, path);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _map?.Dispose(); _map = null; }
        base.Dispose(disposing);
    }
}

/// <summary>
/// Rounded image well with an optional pill badge ("Live preview") and a
/// centered message when there's no image yet.
/// </summary>
public class PreviewPanel : StudioControl
{
    private Image? _image;

    public PreviewPanel()
    {
        Size = new Size(480, 270);
        AccessibleRole = AccessibleRole.Graphic;
    }

    /// <summary>The image to show (takes ownership).</summary>
    public Image? Image
    {
        get => _image;
        set { _image?.Dispose(); _image = value; Invalidate(); }
    }

    public string Badge { get; set; } = "";
    public string EmptyText { get; set; } = "Preview appears after the next update";

    /// <summary>Aspect ratio (width / height) the panel keeps; set from the display.</summary>
    public float AspectRatio { get; set; } = 16f / 9f;

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.HighQuality(g);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        using var path = Theme.RoundedRect(r, PxF(Theme.RadiusCard));

        var state = g.Save();
        g.SetClip(path);
        using (var black = new SolidBrush(Color.Black)) g.FillRectangle(black, r);
        if (_image != null)
        {
            float scale = Math.Max(r.Width / _image.Width, r.Height / _image.Height);
            float w = _image.Width * scale, h = _image.Height * scale;
            g.DrawImage(_image, r.X + (r.Width - w) / 2, r.Y + (r.Height - h) / 2, w, h);
        }
        else
        {
            TextRenderer.DrawText(g, EmptyText, Theme.UiFont(9.5f), Rectangle.Round(r), Color.FromArgb(150, 255, 255, 255),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
        }
        g.Restore(state);

        using (var border = new Pen(Theme.Border, 1)) g.DrawPath(border, path);

        if (Badge.Length > 0 && _image != null)
        {
            var font = Theme.UiFont(8.5f);
            var size = TextRenderer.MeasureText(Badge, font);
            var pill = new RectangleF(PxF(12), PxF(12), size.Width + PxF(26), PxF(24));
            using var pp = Theme.RoundedRect(pill, pill.Height / 2);
            using (var b = new SolidBrush(Color.FromArgb(190, 11, 13, 18))) g.FillPath(b, pp);
            float dot = PxF(6);
            using (var gb = new SolidBrush(Theme.Hex(0x4CC38A)))
                g.FillEllipse(gb, pill.Left + PxF(10), pill.Top + (pill.Height - dot) / 2, dot, dot);
            TextRenderer.DrawText(g, Badge, font,
                new Rectangle((int)(pill.Left + PxF(20)), (int)pill.Top, size.Width + 2, (int)pill.Height),
                Color.FromArgb(230, 233, 237, 243), TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _image?.Dispose(); _image = null; }
        base.Dispose(disposing);
    }
}
