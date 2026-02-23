using System.Drawing;
using System.Drawing.Drawing2D;

namespace DesktopEarth.UI;

/// <summary>
/// A scrollable grid of image thumbnails with lazy loading, favorites, and quality badges.
/// Dynamic column count based on panel width (minimum 3 columns).
/// </summary>
public class ThumbnailGridPanel : Panel
{
    private const int MinColumns = 3;
    private const int ThumbSize = 130;
    private const int CellPadding = 6;
    private const int TitleHeight = 20;
    private const int CellWidth = ThumbSize + CellPadding * 2;
    private const int CellHeight = ThumbSize + TitleHeight + CellPadding * 2;
    private const int StarSize = 20;
    private const int BadgeWidth = 24;
    private const int BadgeHeight = 14;

    private int _columns = MinColumns;
    private List<ImageSourceInfo> _images = new();
    private int _selectedIndex = -1;
    private readonly ImageCache _cache;
    private readonly Dictionary<string, System.Drawing.Image?> _loadedThumbs = new();
    private readonly HashSet<string> _loadingThumbs = new();
    private CancellationTokenSource _loadCts = new();

    // Inner panel for scrolling (holds the actual painted content)
    private readonly Panel _innerPanel;

    /// <summary>Fires when a user clicks on an image cell.</summary>
    public event EventHandler<ImageSourceInfo>? ImageSelected;

    /// <summary>Fires when a user clicks the star icon on an image.</summary>
    public event EventHandler<ImageSourceInfo>? FavoriteToggled;

    /// <summary>When true, show a source label badge on each thumbnail (e.g. "APOD", "NPS").</summary>
    public bool ShowSourceBadge { get; set; } = false;

    private bool _dimmed;
    /// <summary>When true, draws a semi-transparent overlay to visually mute the grid (clicks still work).</summary>
    public bool Dimmed
    {
        get => _dimmed;
        set { if (_dimmed == value) return; _dimmed = value; _innerPanel.Invalidate(); }
    }

    public ImageSourceInfo? SelectedImage =>
        _selectedIndex >= 0 && _selectedIndex < _images.Count
            ? _images[_selectedIndex] : null;

    public ThumbnailGridPanel(ImageCache cache)
    {
        _cache = cache;
        AutoScroll = true;
        BackColor = Theme.ControlBackground;
        BorderStyle = BorderStyle.FixedSingle;

        // Dark scrollbars for this panel
        Theme.StyleScrollableControl(this);

        _innerPanel = new Panel
        {
            Location = new Point(0, 0),
            BackColor = Theme.ControlBackground
        };
        _innerPanel.Paint += InnerPanel_Paint;
        _innerPanel.MouseClick += InnerPanel_MouseClick;

        // Enable double-buffering on inner panel via reflection
        typeof(Panel).GetProperty("DoubleBuffered",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?
            .SetValue(_innerPanel, true);

        Controls.Add(_innerPanel);
    }

    /// <summary>
    /// Replace all images in the grid. Cancels in-flight thumbnail loads.
    /// </summary>
    public void SetImages(List<ImageSourceInfo> images)
    {
        // Cancel pending thumbnail downloads
        _loadCts.Cancel();
        _loadCts.Dispose();
        _loadCts = new CancellationTokenSource();

        _images = images ?? new List<ImageSourceInfo>();
        _selectedIndex = -1; // No pre-selection — user must click to select

        // Dispose old thumbnails
        foreach (var thumb in _loadedThumbs.Values)
            thumb?.Dispose();
        _loadedThumbs.Clear();
        _loadingThumbs.Clear();

        RecalculateLayout();
    }

    /// <summary>
    /// Select an image by its ID. Used to restore saved selection after SetImages().
    /// Returns true if the image was found and selected.
    /// </summary>
    public bool SelectById(string? imageId)
    {
        if (string.IsNullOrEmpty(imageId))
            return false;

        for (int i = 0; i < _images.Count; i++)
        {
            if (_images[i].Id == imageId)
            {
                _selectedIndex = i;
                _innerPanel.Invalidate();
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Update the favorite status of a specific image in the grid.
    /// </summary>
    public void UpdateFavoriteStatus(string imageId, bool isFavorited)
    {
        var img = _images.Find(i => i.Id == imageId);
        if (img != null)
        {
            img.IsFavorited = isFavorited;
            _innerPanel.Invalidate();
        }
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        RecalculateLayout();
    }

    private void RecalculateLayout()
    {
        // Calculate columns based on available width (account for scrollbar)
        int availableWidth = ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 2;
        _columns = Math.Max(MinColumns, availableWidth / CellWidth);

        int rows = (_images.Count + _columns - 1) / Math.Max(_columns, 1);
        _innerPanel.Size = new Size(_columns * CellWidth + 2, Math.Max(rows * CellHeight, 10));
        _innerPanel.Invalidate();
    }

    private void InnerPanel_Paint(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        using var titleFont = new Font("Segoe UI", 7.5f);
        using var titleBrush = new SolidBrush(Theme.ThumbnailTitle);
        using var placeholderBrush = new SolidBrush(Theme.Placeholder);
        using var selectedPen = new Pen(Color.FromArgb(0, 120, 215), 2);
        using var starBrush = new SolidBrush(Color.FromArgb(255, 200, 50));
        using var starOutlinePen = new Pen(Theme.StarOutline, 1);
        using var badgeFont = new Font("Segoe UI", 7f, FontStyle.Bold);

        for (int i = 0; i < _images.Count; i++)
        {
            var cellRect = GetCellBounds(i);

            // Only paint visible cells
            if (!e.ClipRectangle.IntersectsWith(cellRect))
                continue;

            var img = _images[i];
            var thumbRect = new Rectangle(
                cellRect.X + CellPadding,
                cellRect.Y + CellPadding,
                ThumbSize, ThumbSize);

            // Draw thumbnail or placeholder
            if (_loadedThumbs.TryGetValue(img.Id, out var thumb) && thumb != null)
            {
                // Draw with aspect ratio maintained, centered in the thumb rect
                DrawThumbnailFit(g, thumb, thumbRect);
            }
            else
            {
                // Draw placeholder
                g.FillRectangle(placeholderBrush, thumbRect);

                // Queue thumbnail load if not already loading
                // Load if there's a URL to download, OR if the cache already has it (local images)
                if (!_loadingThumbs.Contains(img.Id) &&
                    (!string.IsNullOrEmpty(img.ThumbnailUrl) || _cache.IsThumbCached(img.Source, img.Id)))
                {
                    _loadingThumbs.Add(img.Id);
                    _ = LoadThumbnailAsync(img, _loadCts.Token);
                }
            }

            // Selection highlight
            if (i == _selectedIndex)
            {
                g.DrawRectangle(selectedPen, thumbRect.X - 1, thumbRect.Y - 1,
                    thumbRect.Width + 2, thumbRect.Height + 2);
            }

            // Quality badge (bottom-right of thumbnail)
            DrawQualityBadge(g, img.QualityTier, thumbRect, badgeFont);

            // Source badge (bottom-left of thumbnail, only when ShowSourceBadge is enabled)
            if (ShowSourceBadge)
                DrawSourceBadge(g, img.Source, thumbRect, badgeFont);

            // Title text (truncated)
            var titleRect = new Rectangle(
                cellRect.X + CellPadding,
                cellRect.Y + CellPadding + ThumbSize + 2,
                ThumbSize, TitleHeight);
            var title = TruncateText(img.Title, titleFont, ThumbSize, g);
            g.DrawString(title, titleFont, titleBrush, titleRect);

            // Favorite star
            var starRect = GetStarBounds(i);
            if (img.IsFavorited)
            {
                DrawStar(g, starRect, starBrush, null);
            }
            else
            {
                // Only draw outline star on hover area (always draw faintly for discoverability)
                using var faintBrush = new SolidBrush(Color.FromArgb(60, 200, 200, 200));
                DrawStar(g, starRect, faintBrush, starOutlinePen);
            }
        }

        // Draw dim overlay when auto-rotate is active
        if (_dimmed)
        {
            using var dimBrush = new SolidBrush(Color.FromArgb(100,
                Theme.IsDarkMode ? Color.FromArgb(20, 20, 20) : Color.FromArgb(255, 255, 255)));
            g.FillRectangle(dimBrush, 0, 0, _innerPanel.Width, _innerPanel.Height);
        }
    }

    private static void DrawQualityBadge(Graphics g, ImageQualityTier tier,
        Rectangle thumbRect, Font font)
    {
        string text;
        Color bgColor;

        switch (tier)
        {
            case ImageQualityTier.UD:
                text = "UD";
                bgColor = Color.FromArgb(200, 180, 140, 20); // Gold
                break;
            case ImageQualityTier.HD:
                text = "HD";
                bgColor = Color.FromArgb(200, 0, 100, 200); // Blue
                break;
            case ImageQualityTier.SD:
                text = "SD";
                bgColor = Color.FromArgb(200, 120, 120, 120); // Gray
                break;
            default:
                text = "?";
                bgColor = Color.FromArgb(160, 60, 60, 60); // Dark gray
                break;
        }

        var badgeRect = new Rectangle(
            thumbRect.Right - BadgeWidth - 2,
            thumbRect.Bottom - BadgeHeight - 2,
            BadgeWidth, BadgeHeight);

        using var bgBrush = new SolidBrush(bgColor);
        using var textBrush = new SolidBrush(Color.White);

        // Rounded rectangle badge
        g.FillRectangle(bgBrush, badgeRect);

        // Center text in badge
        var textSize = g.MeasureString(text, font);
        float tx = badgeRect.X + (badgeRect.Width - textSize.Width) / 2;
        float ty = badgeRect.Y + (badgeRect.Height - textSize.Height) / 2;
        g.DrawString(text, font, textBrush, tx, ty);
    }

    private static void DrawSourceBadge(Graphics g, ImageSource source,
        Rectangle thumbRect, Font font)
    {
        string text = source switch
        {
            ImageSource.NasaEpic => "EPIC",
            ImageSource.NasaApod => "APOD",
            ImageSource.NationalParks => "NPS",
            ImageSource.Smithsonian => "SI",
            ImageSource.UserImages => "USER",
            ImageSource.NasaGallery => "NASA",
            _ => ""
        };
        if (string.IsNullOrEmpty(text)) return;

        var textSize = g.MeasureString(text, font);
        int badgeW = (int)textSize.Width + 6;
        var badgeRect = new Rectangle(
            thumbRect.Left + 2,
            thumbRect.Bottom - BadgeHeight - 2,
            badgeW, BadgeHeight);

        using var bgBrush = new SolidBrush(Color.FromArgb(180, 30, 30, 30));
        using var textBrush = new SolidBrush(Color.White);

        g.FillRectangle(bgBrush, badgeRect);
        float tx = badgeRect.X + 3;
        float ty = badgeRect.Y + (badgeRect.Height - textSize.Height) / 2;
        g.DrawString(text, font, textBrush, tx, ty);
    }

    private static void DrawThumbnailFit(Graphics g, System.Drawing.Image thumb, Rectangle destRect)
    {
        float imgAspect = (float)thumb.Width / thumb.Height;
        float destAspect = (float)destRect.Width / destRect.Height;

        int drawW, drawH;
        if (imgAspect > destAspect)
        {
            drawW = destRect.Width;
            drawH = (int)(destRect.Width / imgAspect);
        }
        else
        {
            drawH = destRect.Height;
            drawW = (int)(destRect.Height * imgAspect);
        }

        int drawX = destRect.X + (destRect.Width - drawW) / 2;
        int drawY = destRect.Y + (destRect.Height - drawH) / 2;

        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.DrawImage(thumb, drawX, drawY, drawW, drawH);
    }

    private static void DrawStar(Graphics g, Rectangle rect, Brush fill, Pen? outline)
    {
        // 5-pointed star
        float cx = rect.X + rect.Width / 2f;
        float cy = rect.Y + rect.Height / 2f;
        float outerR = rect.Width / 2f;
        float innerR = outerR * 0.4f;

        var points = new PointF[10];
        for (int i = 0; i < 10; i++)
        {
            float angle = (float)(Math.PI / 2 + i * Math.PI / 5);
            float r = i % 2 == 0 ? outerR : innerR;
            points[i] = new PointF(
                cx + r * (float)Math.Cos(angle),
                cy - r * (float)Math.Sin(angle));
        }

        g.FillPolygon(fill, points);
        if (outline != null)
            g.DrawPolygon(outline, points);
    }

    private void InnerPanel_MouseClick(object? sender, MouseEventArgs e)
    {
        int index = GetIndexAtPoint(e.Location);
        if (index < 0 || index >= _images.Count) return;

        var img = _images[index];

        // Check if click was on the star
        var starRect = GetStarBounds(index);
        if (starRect.Contains(e.Location))
        {
            img.IsFavorited = !img.IsFavorited;
            FavoriteToggled?.Invoke(this, img);
            _innerPanel.Invalidate();
            return;
        }

        // Regular selection
        _selectedIndex = index;
        _innerPanel.Invalidate();
        ImageSelected?.Invoke(this, img);
    }

    private async Task LoadThumbnailAsync(ImageSourceInfo image, CancellationToken ct)
    {
        try
        {
            // Try to load from thumbnail cache first
            string? localPath = null;
            if (_cache.IsThumbCached(image.Source, image.Id))
            {
                localPath = _cache.GetThumbCachePath(image.Source, image.Id);
            }
            else
            {
                localPath = await _cache.DownloadThumbnail(
                    image.Source, image.Id, image.ThumbnailUrl, ct);
            }

            if (ct.IsCancellationRequested || localPath == null) return;

            // Load bitmap on background thread
            System.Drawing.Image? bitmap = null;
            await Task.Run(() =>
            {
                try
                {
                    bitmap = System.Drawing.Image.FromFile(localPath);
                }
                catch
                {
                    bitmap = null;
                }
            }, ct);

            if (ct.IsCancellationRequested)
            {
                bitmap?.Dispose();
                return;
            }

            // Store and invalidate on UI thread
            if (IsDisposed || _innerPanel.IsDisposed) { bitmap?.Dispose(); return; }

            try
            {
                _innerPanel.Invoke(() =>
                {
                    if (!_loadedThumbs.ContainsKey(image.Id))
                    {
                        _loadedThumbs[image.Id] = bitmap;
                        _innerPanel.Invalidate();
                    }
                    else
                    {
                        bitmap?.Dispose();
                    }
                });
            }
            catch (ObjectDisposedException)
            {
                bitmap?.Dispose();
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Console.WriteLine($"Thumbnail load error ({image.Id}): {ex.Message}");
        }
    }

    private int GetIndexAtPoint(Point p)
    {
        int col = p.X / CellWidth;
        int row = p.Y / CellHeight;
        if (col < 0 || col >= _columns) return -1;
        int index = row * _columns + col;
        return index >= 0 && index < _images.Count ? index : -1;
    }

    private Rectangle GetCellBounds(int index)
    {
        int col = index % _columns;
        int row = index / _columns;
        return new Rectangle(col * CellWidth, row * CellHeight, CellWidth, CellHeight);
    }

    private Rectangle GetStarBounds(int index)
    {
        var cell = GetCellBounds(index);
        return new Rectangle(
            cell.X + CellPadding + ThumbSize - StarSize - 2,
            cell.Y + CellPadding + 2,
            StarSize, StarSize);
    }

    private static string TruncateText(string text, Font font, int maxWidth, Graphics g)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var size = g.MeasureString(text, font);
        if (size.Width <= maxWidth) return text;

        for (int i = text.Length - 1; i > 0; i--)
        {
            var truncated = text[..i] + "...";
            if (g.MeasureString(truncated, font).Width <= maxWidth)
                return truncated;
        }
        return "...";
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _loadCts.Cancel();
            _loadCts.Dispose();
            foreach (var thumb in _loadedThumbs.Values)
                thumb?.Dispose();
            _loadedThumbs.Clear();
        }
        base.Dispose(disposing);
    }
}
