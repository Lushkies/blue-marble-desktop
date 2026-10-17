using System.Drawing;
using System.Drawing.Drawing2D;
using Glyph = DesktopEarth.UI.Controls.Glyph;

namespace DesktopEarth.UI;

/// <summary>
/// A scrollable grid of image thumbnails with lazy loading, favorites, quality
/// badges and an "on display" marker. Columns adapt to the width. Clicking a
/// cell only selects it (<see cref="ImageSelected"/>); double-click or Enter
/// raises <see cref="ImageActivated"/>.
/// </summary>
public class ThumbnailGridPanel : Panel
{
    // Design-time (96 DPI) metrics, scaled by DeviceDpi at layout time
    private const int MinCellWidth = 168;
    private const int Gap = 12;
    private const int TitleHeight = 22;
    private const int StarSize = 26;

    private int _columns = 3;
    private int _cellWidth, _thumbHeight, _cellHeight, _gap;
    private List<ImageSourceInfo> _images = new();
    private int _selectedIndex = -1, _hoverIndex = -1;
    private readonly ImageCache _cache;
    private readonly Dictionary<string, Image?> _loadedThumbs = new();
    private readonly HashSet<string> _loadingThumbs = new();
    private CancellationTokenSource _loadCts = new();
    private readonly Panel _innerPanel;
    private string _emptyText = "";

    /// <summary>Fires when the user clicks an image (selection only).</summary>
    public event EventHandler<ImageSourceInfo>? ImageSelected;

    /// <summary>Fires on double-click or Enter — "use this one".</summary>
    public event EventHandler<ImageSourceInfo>? ImageActivated;

    /// <summary>Fires when the user clicks the star on an image.</summary>
    public event EventHandler<ImageSourceInfo>? FavoriteToggled;

    /// <summary>Show a source label (APOD, NASA…) on each thumbnail.</summary>
    public bool ShowSourceBadge { get; set; }

    /// <summary>Ids of the images currently on screen; they get an "ON DISPLAY" tag.</summary>
    public HashSet<string> OnDisplayIds { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Message shown when the grid is empty ("Loading…", "No results").</summary>
    public string EmptyText
    {
        get => _emptyText;
        set { _emptyText = value ?? ""; _innerPanel.Invalidate(); Invalidate(); }
    }

    public ImageSourceInfo? SelectedImage =>
        _selectedIndex >= 0 && _selectedIndex < _images.Count ? _images[_selectedIndex] : null;

    public int Count => _images.Count;

    public ThumbnailGridPanel(ImageCache cache)
    {
        _cache = cache;
        AutoScroll = true;
        BackColor = Theme.Ground;
        TabStop = true;
        SetStyle(ControlStyles.Selectable, true);
        Theme.StyleScrollableControl(this);

        _innerPanel = new Panel { Location = new Point(0, 0), BackColor = Theme.Ground };
        _innerPanel.Paint += InnerPanel_Paint;
        _innerPanel.MouseClick += InnerPanel_MouseClick;
        _innerPanel.MouseDoubleClick += InnerPanel_MouseDoubleClick;
        _innerPanel.MouseMove += (_, e) => SetHover(GetIndexAtPoint(e.Location));
        _innerPanel.MouseLeave += (_, _) => SetHover(-1);
        typeof(Panel).GetProperty("DoubleBuffered",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?
            .SetValue(_innerPanel, true);
        Controls.Add(_innerPanel);
    }

    protected override void OnBackColorChanged(EventArgs e)
    {
        base.OnBackColorChanged(e);
        if (_innerPanel is not null) _innerPanel.BackColor = BackColor; // Null during base construction
    }

    /// <summary>Replace all images. Cancels in-flight thumbnail loads; clears selection.</summary>
    public void SetImages(List<ImageSourceInfo> images)
    {
        _loadCts.Cancel();
        _loadCts.Dispose();
        _loadCts = new CancellationTokenSource();

        _images = images ?? new List<ImageSourceInfo>();
        _selectedIndex = -1;
        _hoverIndex = -1;

        foreach (var thumb in _loadedThumbs.Values) thumb?.Dispose();
        _loadedThumbs.Clear();
        _loadingThumbs.Clear();

        AutoScrollPosition = new Point(0, 0);
        RecalculateLayout();
    }

    /// <summary>Select an image by id (no event). Returns true if found.</summary>
    public bool SelectById(string? imageId)
    {
        if (string.IsNullOrEmpty(imageId)) return false;
        int i = _images.FindIndex(img => string.Equals(img.Id, imageId, StringComparison.OrdinalIgnoreCase));
        if (i < 0) return false;
        _selectedIndex = i;
        _innerPanel.Invalidate();
        return true;
    }

    public void ClearSelection()
    {
        _selectedIndex = -1;
        _innerPanel.Invalidate();
    }

    public void UpdateFavoriteStatus(string imageId, bool isFavorited)
    {
        var img = _images.Find(i => i.Id == imageId);
        if (img == null) return;
        img.IsFavorited = isFavorited;
        _innerPanel.Invalidate();
    }

    /// <summary>Repaint after changing <see cref="OnDisplayIds"/> or favorites.</summary>
    public void RepaintCells() => _innerPanel.Invalidate();

    /// <summary>The loaded thumbnail for an image, if any (owned by the grid — copy it to keep it).</summary>
    public Image? GetThumbnail(string imageId) =>
        _loadedThumbs.TryGetValue(imageId, out var t) ? t : null;

    /// <summary>All images currently in the grid.</summary>
    public IReadOnlyList<ImageSourceInfo> Images => _images;

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (_innerPanel is not null) RecalculateLayout();
    }

    private int Px(float v) => (int)Math.Round(v * DeviceDpi / 96f);

    private void RecalculateLayout()
    {
        _gap = Px(Gap);
        int available = Math.Max(1, ClientSize.Width - SystemInformation.VerticalScrollBarWidth - _gap);
        _columns = Math.Max(2, (available + _gap) / (Px(MinCellWidth) + _gap));
        _cellWidth = (available - _gap * (_columns - 1)) / _columns;
        _thumbHeight = _cellWidth * 10 / 16;
        _cellHeight = _thumbHeight + Px(TitleHeight) + _gap;

        int rows = (_images.Count + _columns - 1) / _columns;
        _innerPanel.Size = new Size(available, Math.Max(rows * _cellHeight, 10));
        _innerPanel.Invalidate();
        Invalidate();
    }

    private Rectangle GetCellBounds(int index)
    {
        int col = index % _columns, row = index / _columns;
        return new Rectangle(col * (_cellWidth + _gap), row * _cellHeight, _cellWidth, _cellHeight - _gap);
    }

    private Rectangle GetThumbBounds(int index)
    {
        var c = GetCellBounds(index);
        return new Rectangle(c.X, c.Y, c.Width, _thumbHeight);
    }

    private Rectangle GetStarBounds(int index)
    {
        var t = GetThumbBounds(index);
        int s = Px(StarSize);
        return new Rectangle(t.Right - s - Px(6), t.Top + Px(6), s, s);
    }

    private int GetIndexAtPoint(Point p)
    {
        for (int i = 0; i < _images.Count; i++)
            if (GetCellBounds(i).Contains(p)) return i;
        return -1;
    }

    private void SetHover(int i)
    {
        if (i == _hoverIndex) return;
        _hoverIndex = i;
        _innerPanel.Cursor = i >= 0 ? Cursors.Hand : Cursors.Default;
        _innerPanel.Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (_images.Count == 0 && _emptyText.Length > 0)
            TextRenderer.DrawText(e.Graphics, _emptyText, Theme.UiFont(10f), ClientRectangle, Theme.TextMuted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
    }

    private void InnerPanel_Paint(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.HighQuality(g);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;

        if (_images.Count == 0 && _emptyText.Length > 0)
        {
            TextRenderer.DrawText(g, _emptyText, Theme.UiFont(10f),
                new Rectangle(0, 0, _innerPanel.Width, Math.Max(Px(120), ClientSize.Height)), Theme.TextMuted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
            return;
        }

        float radius = Px(8);
        var titleFont = Theme.UiFont(8.75f);
        var badgeFont = Theme.Semibold(7f);

        for (int i = 0; i < _images.Count; i++)
        {
            var cell = GetCellBounds(i);
            if (!e.ClipRectangle.IntersectsWith(cell)) continue;

            var img = _images[i];
            var thumbRect = GetThumbBounds(i);
            using var thumbPath = Theme.RoundedRect(thumbRect, radius);

            var state = g.Save();
            g.SetClip(thumbPath);
            if (_loadedThumbs.TryGetValue(img.Id, out var thumb) && thumb != null)
            {
                DrawCover(g, thumb, thumbRect);
            }
            else
            {
                using var ph = new SolidBrush(Theme.Placeholder);
                g.FillRectangle(ph, thumbRect);
                if (!_loadingThumbs.Contains(img.Id) &&
                    (!string.IsNullOrEmpty(img.ThumbnailUrl) || _cache.IsThumbCached(img.Source, img.Id)))
                {
                    _loadingThumbs.Add(img.Id);
                    _ = LoadThumbnailAsync(img, _loadCts.Token);
                }
            }
            if (i == _hoverIndex && i != _selectedIndex)
            {
                using var hover = new SolidBrush(Color.FromArgb(28, 255, 255, 255));
                g.FillRectangle(hover, thumbRect);
            }
            g.Restore(state);

            if (i == _selectedIndex)
            {
                using var sel = new Pen(Theme.Accent, Px(2.5f));
                using var selPath = Theme.RoundedRect(RectangleF.Inflate(thumbRect, Px(1), Px(1)), radius + Px(1));
                g.DrawPath(sel, selPath);
            }

            // Bottom-right quality tag, bottom-left source tag, top-left "on display"
            DrawTag(g, QualityText(img.QualityTier), badgeFont, thumbRect, alignRight: true, bottom: true,
                fore: img.QualityTier == ImageQualityTier.UD ? Theme.Gold : img.QualityTier == ImageQualityTier.HD ? Theme.Hex(0x7FB2FF) : Color.FromArgb(220, 220, 225),
                back: Color.FromArgb(200, 11, 13, 18));
            if (ShowSourceBadge)
                DrawTag(g, SourceText(img.Source), badgeFont, thumbRect, alignRight: false, bottom: true,
                    fore: Color.FromArgb(225, 230, 235), back: Color.FromArgb(200, 11, 13, 18));
            if (OnDisplayIds.Contains(img.Id) || OnDisplayIds.Contains(ImageCache.SanitizeFileName(img.Id)))
                DrawTag(g, "ON DISPLAY", badgeFont, thumbRect, alignRight: false, bottom: false,
                    fore: Theme.OnAccent, back: Theme.Accent);

            // Star: always visible when favorited, otherwise on hover/selection
            if (img.IsFavorited || i == _hoverIndex || i == _selectedIndex)
            {
                var s = GetStarBounds(i);
                using (var circle = new SolidBrush(Color.FromArgb(170, 11, 13, 18)))
                    g.FillEllipse(circle, s);
                TextRenderer.DrawText(g, img.IsFavorited ? Glyph.StarFilled : Glyph.Star,
                    Glyph.Font(9.5f), s, img.IsFavorited ? Theme.Star : Color.White,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }

            var titleRect = new Rectangle(cell.X + Px(2), thumbRect.Bottom + Px(4), cell.Width - Px(4), Px(TitleHeight) - Px(4));
            TextRenderer.DrawText(g, img.Title, titleFont, titleRect,
                i == _selectedIndex ? Theme.Text : Theme.TextSecondary,
                TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.EndEllipsis |
                TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
        }

        if (Focused && ShowFocusCues && _selectedIndex >= 0)
        {
            using var focus = new Pen(Theme.Text, 1) { DashStyle = DashStyle.Dot };
            var fr = Rectangle.Inflate(GetThumbBounds(_selectedIndex), Px(4), Px(4));
            g.DrawRectangle(focus, fr);
        }
    }

    private void DrawTag(Graphics g, string text, Font font, Rectangle thumb, bool alignRight, bool bottom, Color fore, Color back)
    {
        if (text.Length == 0) return;
        var size = TextRenderer.MeasureText(text, font, Size.Empty, TextFormatFlags.NoPadding);
        int w = size.Width + Px(10), h = Px(17);
        int x = alignRight ? thumb.Right - w - Px(6) : thumb.Left + Px(6);
        int y = bottom ? thumb.Bottom - h - Px(6) : thumb.Top + Px(6);
        var r = new Rectangle(x, y, w, h);
        using (var path = Theme.RoundedRect(r, h / 2f))
        using (var b = new SolidBrush(back))
            g.FillPath(b, path);
        TextRenderer.DrawText(g, text, font, r, fore,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    private static string QualityText(ImageQualityTier tier) => tier switch
    {
        ImageQualityTier.UD => "UD",
        ImageQualityTier.HD => "HD",
        ImageQualityTier.SD => "SD",
        _ => ""
    };

    private static string SourceText(ImageSource source) => source switch
    {
        ImageSource.NasaEpic => "EPIC",
        ImageSource.NasaApod => "APOD",
        ImageSource.NationalParks => "PARKS",
        ImageSource.Smithsonian => "SMITHSONIAN",
        ImageSource.UserImages => "MINE",
        ImageSource.NasaGallery => "NASA",
        _ => ""
    };

    private static void DrawCover(Graphics g, Image img, Rectangle dest)
    {
        float scale = Math.Max((float)dest.Width / img.Width, (float)dest.Height / img.Height);
        float w = img.Width * scale, h = img.Height * scale;
        g.DrawImage(img, dest.X + (dest.Width - w) / 2, dest.Y + (dest.Height - h) / 2, w, h);
    }

    private void InnerPanel_MouseClick(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        Focus();
        int index = GetIndexAtPoint(e.Location);
        if (index < 0) return;
        var img = _images[index];

        if (GetStarBounds(index).Contains(e.Location))
        {
            img.IsFavorited = !img.IsFavorited;
            FavoriteToggled?.Invoke(this, img);
            _innerPanel.Invalidate();
            return;
        }

        _selectedIndex = index;
        _innerPanel.Invalidate();
        ImageSelected?.Invoke(this, img);
    }

    private void InnerPanel_MouseDoubleClick(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        int index = GetIndexAtPoint(e.Location);
        if (index < 0 || GetStarBounds(index).Contains(e.Location)) return;
        ImageActivated?.Invoke(this, _images[index]);
    }

    protected override bool IsInputKey(Keys keyData) =>
        keyData is Keys.Left or Keys.Right or Keys.Up or Keys.Down || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_images.Count == 0) return;
        if (e.KeyCode == Keys.Enter && SelectedImage != null)
        {
            ImageActivated?.Invoke(this, SelectedImage);
            e.Handled = true;
            return;
        }
        int i = _selectedIndex < 0 ? 0 : _selectedIndex;
        int next = e.KeyCode switch
        {
            Keys.Left => i - 1, Keys.Right => i + 1,
            Keys.Up => i - _columns, Keys.Down => i + _columns,
            _ => int.MinValue
        };
        if (next == int.MinValue) return;
        next = Math.Clamp(next, 0, _images.Count - 1);
        _selectedIndex = next;
        ScrollControlIntoView(_innerPanel);
        var cell = GetCellBounds(next);
        if (cell.Top + AutoScrollPosition.Y < 0 || cell.Bottom + AutoScrollPosition.Y > ClientSize.Height)
            AutoScrollPosition = new Point(0, Math.Max(0, cell.Top - _gap));
        _innerPanel.Invalidate();
        ImageSelected?.Invoke(this, _images[next]);
        e.Handled = true;
    }

    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); _innerPanel.Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); _innerPanel.Invalidate(); }

    private async Task LoadThumbnailAsync(ImageSourceInfo image, CancellationToken ct)
    {
        try
        {
            string? localPath = _cache.IsThumbCached(image.Source, image.Id)
                ? _cache.GetThumbCachePath(image.Source, image.Id)
                : await _cache.DownloadThumbnail(image.Source, image.Id, image.ThumbnailUrl, ct);

            if (ct.IsCancellationRequested) return;
            if (localPath == null)
            {
                // Download failed — allow a retry on a later paint
                _loadingThumbs.Remove(image.Id);
                return;
            }

            Image? bitmap = null;
            await Task.Run(() =>
            {
                try
                {
                    // Copy via Bitmap so the file handle is released immediately —
                    // Image.FromFile keeps the file locked for the image's lifetime,
                    // which silently blocks cache pruning and deletion
                    using var fromFile = Image.FromFile(localPath);
                    bitmap = new Bitmap(fromFile);
                }
                catch { bitmap = null; }
            }, ct);

            if (ct.IsCancellationRequested || IsDisposed || _innerPanel.IsDisposed)
            {
                bitmap?.Dispose();
                return;
            }

            // Continuations run on the UI thread (WinForms context)
            if (!_loadedThumbs.ContainsKey(image.Id))
            {
                _loadedThumbs[image.Id] = bitmap;
                _innerPanel.Invalidate();
            }
            else bitmap?.Dispose();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Console.WriteLine($"Thumbnail load error ({image.Id}): {ex.Message}");
            _loadingThumbs.Remove(image.Id);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _loadCts.Cancel();
            _loadCts.Dispose();
            foreach (var thumb in _loadedThumbs.Values) thumb?.Dispose();
            _loadedThumbs.Clear();
        }
        base.Dispose(disposing);
    }
}
