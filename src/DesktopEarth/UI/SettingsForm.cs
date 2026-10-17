using System.Drawing;
using DesktopEarth.UI.Controls;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace DesktopEarth.UI;

/// <summary>
/// The settings window ("Studio" layout): a sidebar with three pages —
/// Wallpaper, My Collection and Settings — split across partial files.
///
/// Editing model: every control writes only the setting it owns, to exactly one
/// place — the selected display's config in Per-display mode, otherwise the
/// global settings (<see cref="EditTarget"/>). Saves and re-renders are
/// debounced. Nothing ever re-saves the whole form, so a slider can't quietly
/// put back an old picture or view, and browsing images never changes the
/// wallpaper until "Set" is pressed.
/// </summary>
public partial class SettingsForm : Form
{
    private readonly SettingsManager _settingsManager;
    private readonly RenderScheduler _renderScheduler;
    private readonly AppSettings _settings;
    private readonly HiResTextureManager _hdTextureManager;
    private readonly ImageCache _imageCache = new();
    private readonly UserImageManager _userImageManager = new();

    // Displays, in physical left-to-right order, and the one being edited
    private List<DisplayInfo> _displays = new();
    private int _displayIndex;

    // Debounced save (+ optional re-render) after edits
    private readonly System.Windows.Forms.Timer _commitTimer;
    private bool _pendingRender;

    // True while code (not the user) is filling controls
    private bool _loading;

    // Pages and navigation
    private readonly Panel _content;
    private readonly StudioButton[] _navButtons = new StudioButton[3];
    private readonly Panel[] _pages = new Panel[3];
    private int _page = -1;

    // Sidebar status card
    private StudioLabel _statusTitle = null!;
    private StudioLabel _statusSub = null!;
    private StatusDot _statusDot = null!;
    private StudioButton _updateNowButton = null!;
    private readonly System.Windows.Forms.Timer _statusTimer;
    private string _statusOverride = "";
    private Color _statusOverrideColor;

    // Per-display wallpaper crops for the preview and monitor thumbnails
    private readonly Dictionary<string, Bitmap> _previews = new();
    private int _previewGeneration;

    public event Action? RequestReopen;

    public SettingsForm(SettingsManager settingsManager, RenderScheduler renderScheduler,
        HiResTextureManager hdTextureManager)
    {
        _settingsManager = settingsManager;
        _renderScheduler = renderScheduler;
        _hdTextureManager = hdTextureManager;
        _settings = settingsManager.Settings;

        _commitTimer = new System.Windows.Forms.Timer { Interval = 250 };
        _commitTimer.Tick += (_, _) => CommitNow();
        _statusTimer = new System.Windows.Forms.Timer { Interval = 15000 };
        _statusTimer.Tick += (_, _) => UpdateStatusCard();

        SuspendLayout();
        Text = "Blue Marble Desktop";
        Icon = TrayApplicationContext.LoadAppIcon();
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1120, 740);
        MinimumSize = new Size(980, 660);
        BackColor = Theme.Ground;
        ForeColor = Theme.Text;
        Font = Theme.UiFont();
        KeyPreview = true;
        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape && ActiveControl is not ComboBox { DroppedDown: true })
                Close();
        };

        _displays = MonitorManager.GetDisplays();
        _displayIndex = Math.Max(0, _displays.FindIndex(d => d.Primary));

        _content = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Ground };
        _pages[0] = BuildWallpaperPage();
        _pages[1] = BuildCollectionPage();
        _pages[2] = BuildSettingsPage();
        foreach (var page in _pages)
        {
            page.Dock = DockStyle.Fill;
            page.Visible = false;
            _content.Controls.Add(page);
        }

        Controls.Add(_content);
        Controls.Add(new Panel { Dock = DockStyle.Left, Width = 1, BackColor = Theme.Divider });
        Controls.Add(BuildSidebar());
        ResumeLayout(true);

        _renderScheduler.StatusChanged += OnRenderStatusChanged;
        _renderScheduler.WallpaperApplied += OnWallpaperApplied;

        _loading = true;
        try { LoadDisplayIntoUi(); }
        finally { _loading = false; }

        ShowPage(0);
        UpdateStatusCard();
        _statusTimer.Start();
    }

    /// <summary>The page shown (0 Wallpaper, 1 My Collection, 2 Settings); kept across theme switches.</summary>
    public int SelectedPage
    {
        get => _page;
        set => ShowPage(value);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.ApplyDarkMode(this);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        // On small high-DPI screens the window can be bigger than the desktop
        var workArea = Screen.FromControl(this).WorkingArea;
        if (Height > workArea.Height) Height = workArea.Height;
        if (Width > workArea.Width) Width = workArea.Width;
        if (Top < workArea.Top) Top = workArea.Top;

        LoadMapImageAsync();
        RefreshPreviewsAsync();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        CommitNow(); // Never lose an edit made in the last 250 ms
        base.OnFormClosing(e);
    }

    /// <summary>Scale a 96-DPI design value for layout done at runtime.</summary>
    private int Dpi(int designPixels) => (int)Math.Round(designPixels * (DeviceDpi / 96f));

    // ─────────────────────────────── Edit model ───────────────────────────────

    private DisplayInfo? SelectedDisplay =>
        _displays.Count > 0 ? _displays[Math.Clamp(_displayIndex, 0, _displays.Count - 1)] : null;

    // Must match RenderScheduler, which uses per-display configs whenever this mode
    // is on — even with one monitor connected (e.g. a laptop undocked)
    private bool PerDisplay => _settings.MultiMonitorMode == MultiMonitorMode.PerDisplay;

    /// <summary>The settings currently shown for the selected display (read-only use).</summary>
    private IDisplaySettings ViewSettings =>
        PerDisplay && SelectedDisplay != null
            ? (IDisplaySettings?)_settings.FindDisplayConfig(SelectedDisplay) ?? _settings
            : _settings;

    /// <summary>Where edits go: the display's own config (created on first edit) or the globals.</summary>
    private IDisplaySettings EditTarget =>
        PerDisplay && SelectedDisplay != null
            ? _settings.GetOrCreateDisplayConfig(SelectedDisplay)
            : _settings;

    /// <summary>RenderScheduler key for what the edited display(s) currently show.</summary>
    private string CurrentImageKey =>
        PerDisplay && SelectedDisplay != null ? SelectedDisplay.MonitorId : RenderScheduler.AllDisplaysKey;

    /// <summary>Who an edit affects, for button labels: "LG ULTRAGEAR", "all displays".</summary>
    private string TargetName =>
        PerDisplay && SelectedDisplay != null ? SelectedDisplay.Title
        : _displays.Count > 1 ? "all displays" : "your desktop";

    /// <summary>Apply one change to the edit target, then save (and optionally re-render) shortly.</summary>
    private void Edit(Action<IDisplaySettings> change, bool render = true)
    {
        if (_loading) return;
        change(EditTarget);
        QueueCommit(render);
    }

    private void QueueCommit(bool render)
    {
        _pendingRender |= render;
        _commitTimer.Stop();
        _commitTimer.Start();
    }

    private void CommitNow()
    {
        _commitTimer.Stop();
        _settingsManager.Save();
        if (_pendingRender)
        {
            _pendingRender = false;
            if (_advanceRotationOnCommit) _renderScheduler.TriggerNextImage(CurrentImageKey);
            else if (_showSelectionOnCommit) _renderScheduler.TriggerShowSelection(CurrentImageKey);
            else _renderScheduler.TriggerUserUpdate();
            BeginUpdateStatus();
        }
        _advanceRotationOnCommit = false;
        _showSelectionOnCommit = false;
    }

    // Set by "Set": the commit's render shows the chosen image even on a rotating display
    private bool _showSelectionOnCommit;

    // Set when turning auto-rotate on or changing its source: the commit's
    // render should move to a new image instead of re-rendering the current one
    private bool _advanceRotationOnCommit;

    // ─────────────────────────────── Sidebar ───────────────────────────────

    private Panel BuildSidebar()
    {
        var sidebar = new Panel { Dock = DockStyle.Left, Width = 216, BackColor = Theme.Chrome, Padding = new Padding(12, 18, 12, 14) };

        var brand = new Panel { Dock = DockStyle.Top, Height = 46, BackColor = Theme.Chrome };
        var logo = new PictureBox
        {
            Image = Icon?.ToBitmap(), SizeMode = PictureBoxSizeMode.Zoom,
            Size = new Size(24, 24), Location = new Point(8, 4), BackColor = Color.Transparent
        };
        logo.Disposed += (_, _) => logo.Image?.Dispose(); // PictureBox doesn't own its image
        var name = new StudioLabel { Text = "Blue Marble", Font = Theme.DisplayFont(12.5f, FontStyle.Bold), Location = new Point(40, 2) };
        var sub = new StudioLabel { Text = "Desktop", ForeColor = Theme.TextMuted, Font = Theme.UiFont(8.75f), Location = new Point(41, 24) };
        brand.Controls.AddRange([logo, name, sub]);

        string[] labels = ["Wallpaper", "My Collection", "Settings"];
        string[] glyphs = [Glyph.Monitor, Glyph.Library, Glyph.Settings];
        var nav = new Panel { Dock = DockStyle.Top, Height = 3 * 44 + 8, BackColor = Theme.Chrome, Padding = new Padding(0, 8, 0, 0) };
        for (int i = labels.Length - 1; i >= 0; i--)
        {
            int index = i;
            var btn = new StudioButton
            {
                Text = labels[i], IconGlyph = glyphs[i], Variant = ButtonVariant.Nav,
                Dock = DockStyle.Top, Height = 40, Font = Theme.UiFont(10.25f)
            };
            btn.Click += (_, _) => ShowPage(index);
            _navButtons[i] = btn;
            nav.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 4, BackColor = Theme.Chrome });
            nav.Controls.Add(btn);
        }

        var status = new Card { Dock = DockStyle.Bottom, Height = 124, Padding = new Padding(14, 12, 14, 12) };
        _statusDot = new StatusDot { Location = new Point(14, 19), ForeColor = Theme.Success };
        _statusTitle = new StudioLabel
        {
            AutoSize = false, Location = new Point(28, 11), Size = new Size(150, 22),
            Font = Theme.UiFont(9f), ForeColor = Theme.TextSecondary, AutoEllipsis = true
        };
        _statusSub = new StudioLabel
        {
            AutoSize = false, Location = new Point(14, 36), Size = new Size(164, 34),
            Font = Theme.UiFont(8.5f), ForeColor = Theme.TextMuted
        };
        _updateNowButton = new StudioButton
        {
            Text = "Update now", Variant = ButtonVariant.Secondary,
            Location = new Point(14, 76), Size = new Size(164, 34), Font = Theme.UiFont(9.25f)
        };
        _updateNowButton.Click += (_, _) =>
        {
            CommitNow();
            if (ViewSettings.DisplayMode == DisplayMode.StillImage && ViewSettings.RandomRotationEnabled)
                _renderScheduler.TriggerNextImage(CurrentImageKey);
            else
                _renderScheduler.TriggerShowSelection(CurrentImageKey);
            BeginUpdateStatus();
        };
        status.Controls.AddRange([_statusDot, _statusTitle, _statusSub, _updateNowButton]);

        sidebar.Controls.Add(nav);
        sidebar.Controls.Add(brand);
        sidebar.Controls.Add(status);
        return sidebar;
    }

    private void ShowPage(int index)
    {
        if (index < 0 || index >= _pages.Length || index == _page) return;
        _page = index;
        for (int i = 0; i < _pages.Length; i++)
        {
            _pages[i].Visible = i == index;
            _navButtons[i].Selected = i == index;
        }
        if (index == 1) RefreshCollection();
        if (index == 2) RefreshSettingsPage();
    }

    // ─────────────────────────────── Status ───────────────────────────────

    private void OnRenderStatusChanged(string status)
    {
        // Raised on the render thread; the form may be closing
        if (IsDisposed || !IsHandleCreated) return;
        try { BeginInvoke(() => HandleRenderStatus(status)); }
        catch (ObjectDisposedException) { }
        catch (InvalidOperationException) { }
    }

    private void HandleRenderStatus(string status)
    {
        if (IsDisposed) return;
        // A warning stays up until the user's next action — otherwise the
        // "updated" message from the other displays would hide it at once
        if (status.Contains("offline", StringComparison.OrdinalIgnoreCase))
            ShowWarning("Offline", "Showing a cached picture");
        else if (status.Contains("error", StringComparison.OrdinalIgnoreCase) ||
                 status.Contains("failed", StringComparison.OrdinalIgnoreCase) ||
                 status.Contains("No image", StringComparison.OrdinalIgnoreCase) ||
                 status.Contains("below minimum", StringComparison.OrdinalIgnoreCase))
            ShowWarning(FriendlyError(status));
        else if (_statusWarningSticky)
            return;
        else if (status.StartsWith("Downloading", StringComparison.OrdinalIgnoreCase))
            SetStatusOverride("Downloading image…", Theme.Accent);
        else if (status.StartsWith("Rendering", StringComparison.OrdinalIgnoreCase))
            SetStatusOverride("Updating…", Theme.Accent);
        else if (status.StartsWith("Wallpaper unchanged", StringComparison.OrdinalIgnoreCase))
        {
            SetStatusOverride("", Theme.Success);
            RefreshOnDisplayMarkers();
        }
        else if (status.StartsWith("Wallpaper updated", StringComparison.OrdinalIgnoreCase))
            SetStatusOverride("", Theme.Success);
    }

    private bool _statusWarningSticky;

    private string _statusDetail = "";

    private void ShowWarning((string Title, string Detail) warning) => ShowWarning(warning.Title, warning.Detail);

    private void ShowWarning(string title, string detail)
    {
        _statusWarningSticky = true;
        _statusDetail = detail;
        SetStatusOverride(title, Theme.Warning);
        RefreshOnDisplayMarkers(); // Re-enable "Set" after a failed attempt
    }

    /// <summary>A new user-triggered update: clear any old warning and show progress.</summary>
    private void BeginUpdateStatus()
    {
        _statusWarningSticky = false;
        SetStatusOverride("Updating…", Theme.Accent);
    }

    private static (string Title, string Detail) FriendlyError(string status)
    {
        if (status.Contains("No image", StringComparison.OrdinalIgnoreCase))
            return ("Couldn't get that picture", "Kept your wallpaper. Check your connection.");
        if (status.Contains("below minimum", StringComparison.OrdinalIgnoreCase))
            return ("Picture skipped", "It's smaller than 1080p and would look blurry.");
        return ("Update failed", "Trying again in a minute.");
    }

    private void SetStatusOverride(string text, Color color)
    {
        _statusOverride = text;
        _statusOverrideColor = color;
        UpdateStatusCard();
    }

    private void UpdateStatusCard()
    {
        if (IsDisposed) return;
        string title;
        Color dot;
        if (_statusOverride.Length > 0)
        {
            title = _statusOverride;
            dot = _statusOverrideColor;
        }
        else if (_renderScheduler.LastAppliedUtc is DateTime last)
        {
            title = $"Updated {last.ToLocalTime():h:mm tt}";
            dot = Theme.Success;
        }
        else
        {
            title = "Wallpaper is set";
            dot = Theme.Success;
        }
        _statusTitle.Text = title;
        _statusDot.ForeColor = dot;

        string next = "";
        if (_settings.UpdateIntervalSeconds == int.MaxValue)
        {
            next = "Automatic updates are off";
        }
        else if (_renderScheduler.NextUpdateUtc is DateTime nextUtc)
        {
            var wait = nextUtc - DateTime.UtcNow;
            next = wait.TotalSeconds <= 30 ? "Next update in a moment"
                : wait.TotalMinutes < 60 ? $"Next update in {Math.Ceiling(wait.TotalMinutes):0} min"
                : wait.TotalHours < 48 ? $"Next update in {wait.TotalHours:0.#} h"
                : $"Next update {nextUtc.ToLocalTime():MMM d}";
        }
        _statusSub.Text = _statusWarningSticky ? _statusDetail : next;

        bool rotating = ViewSettings.DisplayMode == DisplayMode.StillImage && ViewSettings.RandomRotationEnabled;
        _updateNowButton.Text = rotating ? "Next image" : "Update now";
        _updateNowButton.IconGlyph = rotating ? Glyph.Next : Glyph.Refresh;
    }

    private void OnWallpaperApplied()
    {
        if (IsDisposed || !IsHandleCreated) return;
        try
        {
            BeginInvoke(() =>
            {
                if (IsDisposed) return;
                if (!_statusWarningSticky) SetStatusOverride("", Theme.Success);
                RefreshPreviewsAsync();
                RefreshOnDisplayMarkers();
            });
        }
        catch (ObjectDisposedException) { }
        catch (InvalidOperationException) { }
    }

    // ─────────────────────────────── Previews ───────────────────────────────

    /// <summary>
    /// Crop the applied wallpaper.bmp into one image per display for the live
    /// preview and the monitor picker thumbnails (off the UI thread).
    /// </summary>
    private async void RefreshPreviewsAsync()
    {
        int generation = ++_previewGeneration;
        var displays = _displays.ToList();
        var mode = _settings.MultiMonitorMode;
        string path = AppPaths.WallpaperFile;

        Dictionary<string, Bitmap> crops;
        try
        {
            crops = await Task.Run(() => CropWallpaper(path, displays, mode));
        }
        catch
        {
            return; // No wallpaper yet, or it is being rewritten — the next apply retries
        }
        if (IsDisposed || generation != _previewGeneration)
        {
            foreach (var c in crops.Values) c.Dispose();
            return;
        }

        foreach (var old in _previews.Values) old.Dispose();
        _previews.Clear();
        foreach (var (id, bmp) in crops)
        {
            _previews[id] = bmp;
            _monitorPicker.SetThumbnail(id, new Bitmap(bmp));
        }
        ShowPreviewForSelectedDisplay();
    }

    private static Dictionary<string, Bitmap> CropWallpaper(string path, List<DisplayInfo> displays, MultiMonitorMode mode)
    {
        var result = new Dictionary<string, Bitmap>();
        if (!File.Exists(path) || displays.Count == 0) return result;

        using var image = SixLabors.ImageSharp.Image.Load<Rgba32>(ImageDecoding.Options, path);
        var union = displays.Select(d => d.Bounds).Aggregate(Rectangle.Union);
        bool spans = mode != MultiMonitorMode.SameForAll && displays.Count > 1;

        foreach (var d in displays)
        {
            SixLabors.ImageSharp.Rectangle crop;
            if (spans)
            {
                // The image covers the whole virtual desktop; scale in case the
                // layout changed since it was rendered
                float sx = (float)image.Width / union.Width, sy = (float)image.Height / union.Height;
                crop = new SixLabors.ImageSharp.Rectangle(
                    (int)((d.Bounds.Left - union.Left) * sx), (int)((d.Bounds.Top - union.Top) * sy),
                    (int)(d.Bounds.Width * sx), (int)(d.Bounds.Height * sy));
            }
            else
            {
                // One image filled onto every display: center-crop to its aspect
                float aspect = (float)d.Bounds.Width / d.Bounds.Height;
                int w = image.Width, h = image.Height;
                if ((float)w / h > aspect) w = (int)(h * aspect); else h = (int)(w / aspect);
                crop = new SixLabors.ImageSharp.Rectangle((image.Width - w) / 2, (image.Height - h) / 2, w, h);
            }
            crop.Intersect(new SixLabors.ImageSharp.Rectangle(0, 0, image.Width, image.Height));
            if (crop.Width < 2 || crop.Height < 2) continue;

            using var part = image.Clone(x => x.Crop(crop).Resize(new ResizeOptions
            {
                Size = new SixLabors.ImageSharp.Size(960, 0), Mode = ResizeMode.Max
            }));
            using var ms = new MemoryStream();
            part.Save(ms, new SixLabors.ImageSharp.Formats.Bmp.BmpEncoder());
            ms.Position = 0;
            using var gdi = System.Drawing.Image.FromStream(ms);
            result[d.MonitorId] = new Bitmap(gdi);
        }
        return result;
    }

    // ─────────────────────────────── Helpers ───────────────────────────────

    /// <summary>Stack controls top-to-bottom in a panel (Dock.Top adds in reverse).</summary>
    private static void StackTop(Control parent, params Control[] controls)
    {
        for (int i = controls.Length - 1; i >= 0; i--)
        {
            controls[i].Dock = DockStyle.Top;
            parent.Controls.Add(controls[i]);
        }
    }

    private static Panel Spacer(int height) =>
        new() { Height = height, Dock = DockStyle.Top, BackColor = Color.Transparent };

    private static StudioLabel SectionLabel(string text) => new()
    {
        Text = text.ToUpperInvariant(), Font = Theme.Semibold(8f), ForeColor = Theme.TextMuted
    };

    private static void OpenUrl(string url)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { }
    }

    private static string? PromptForName(IWin32Window owner, string title, string prompt, string initial = "")
    {
        using var dialog = new Form
        {
            Text = title,
            AutoScaleDimensions = new SizeF(96F, 96F),
            AutoScaleMode = AutoScaleMode.Dpi,
            ClientSize = new Size(380, 150),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MaximizeBox = false, MinimizeBox = false, ShowInTaskbar = false,
            BackColor = Theme.Ground, ForeColor = Theme.Text, Font = Theme.UiFont()
        };
        var label = new StudioLabel { Text = prompt, Location = new Point(20, 18) };
        var textBox = new TextBox { Location = new Point(20, 46), Width = 340, Text = initial };
        Theme.StyleTextBox(textBox);
        var ok = new StudioButton { Text = "Save", Variant = ButtonVariant.Primary, DialogResult = DialogResult.OK, Location = new Point(176, 98), Size = new Size(88, 34) };
        var cancel = new StudioButton { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(272, 98), Size = new Size(88, 34) };
        dialog.Controls.AddRange([label, textBox, ok, cancel]);
        dialog.AcceptButton = ok;
        dialog.CancelButton = cancel;
        dialog.HandleCreated += (_, _) => Theme.ApplyDarkMode(dialog);
        return dialog.ShowDialog(owner) == DialogResult.OK && !string.IsNullOrWhiteSpace(textBox.Text)
            ? textBox.Text.Trim() : null;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _renderScheduler.StatusChanged -= OnRenderStatusChanged;
            _renderScheduler.WallpaperApplied -= OnWallpaperApplied;
            if (_hdProgressHandler != null) _hdTextureManager.ProgressChanged -= _hdProgressHandler;
            if (_hdCompletedHandler != null) _hdTextureManager.DownloadCompleted -= _hdCompletedHandler;
            _commitTimer.Stop();
            _commitTimer.Dispose();
            _statusTimer.Stop();
            _statusTimer.Dispose();
            foreach (var p in _previews.Values) p.Dispose();
            _previews.Clear();
        }
        base.Dispose(disposing);
    }
}
