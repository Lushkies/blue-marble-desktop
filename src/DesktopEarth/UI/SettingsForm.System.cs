using System.Drawing;
using DesktopEarth.UI.Controls;

namespace DesktopEarth.UI;

/// <summary>Settings page: schedule, startup, quality, storage, API key, theme.</summary>
public partial class SettingsForm
{
    private static readonly (string Label, int Seconds)[] Intervals =
    [
        ("Every minute", 60), ("Every 2 minutes", 120), ("Every 5 minutes", 300), ("Every 10 minutes", 600),
        ("Every 15 minutes", 900), ("Every 30 minutes", 1800), ("Every hour", 3600), ("Every 6 hours", 21600),
        ("Every 12 hours", 43200), ("Daily", 86400), ("Weekly", 604800), ("Every 2 weeks", 1209600),
        ("Monthly", 2592000), ("Never (manual only)", int.MaxValue)
    ];

    private static readonly (string Label, int W, int H)[] Resolutions =
        [("Match each display", 0, 0), ("1920 × 1080", 1920, 1080), ("2560 × 1440", 2560, 1440),
         ("3840 × 2160", 3840, 2160), ("5120 × 2880", 5120, 2880)];

    private static readonly (string Label, int Days)[] CacheDurations =
        [("7 days", 7), ("14 days", 14), ("30 days", 30), ("90 days", 90), ("Keep forever", 0)];

    private ModernComboBox _intervalCombo = null!, _resolutionCombo = null!, _cacheCombo = null!;
    private ToggleSwitch _startupSwitch = null!;
    private StudioLabel _cacheSub = null!, _hdSub = null!, _apiStatus = null!;
    private StudioButton _hdButton = null!;
    private Panel _hdProgress = null!;
    private float _hdProgressValue = -1;
    private TextField _apiKeyBox = null!;
    private SegmentedControl _themeSegment = null!;
    private Action<float, string>? _hdProgressHandler;
    private Action<bool, string>? _hdCompletedHandler;

    private Panel BuildSettingsPage()
    {
        var page = new Panel { BackColor = Theme.Ground, AutoScroll = true, Padding = new Padding(24, 18, 24, 24) };
        Theme.StyleScrollableControl(page);

        var title = new StudioLabel { Text = "Settings", Font = Theme.DisplayFont(15f), AutoSize = false, Height = 44 };

        // Schedule
        _intervalCombo = new ModernComboBox { Width = 220, AccessibleName = "Update frequency" };
        foreach (var i in Intervals) _intervalCombo.Items.Add(i.Label);
        _intervalCombo.SelectedIndexChanged += (_, _) =>
        {
            if (_loading || _intervalCombo.SelectedIndex < 0) return;
            _settings.UpdateIntervalSeconds = Intervals[_intervalCombo.SelectedIndex].Seconds;
            QueueCommit(render: true);
            UpdateRotationUi();
        };
        var interval = SettingRow("Update the wallpaper", "Re-lights the globe and advances auto-rotate", _intervalCombo);

        _startupSwitch = new ToggleSwitch { AccessibleName = "Start with Windows" };
        _startupSwitch.Toggled += (_, _) => StartupManager.SetRunOnStartup(_startupSwitch.Checked);
        var startup = SettingRow("Start with Windows", "Runs quietly in the system tray", _startupSwitch);

        _resolutionCombo = new ModernComboBox { Width = 220, AccessibleName = "Render resolution" };
        foreach (var r in Resolutions) _resolutionCombo.Items.Add(r.Label);
        _resolutionCombo.SelectedIndexChanged += (_, _) =>
        {
            if (_loading || _resolutionCombo.SelectedIndex < 0) return;
            var r = Resolutions[_resolutionCombo.SelectedIndex];
            _settings.RenderWidth = r.W;
            _settings.RenderHeight = r.H;
            QueueCommit(render: true);
        };
        var resolution = SettingRow("Render resolution", "Applies to Same-for-all and Span modes; Per-display always matches each screen", _resolutionCombo);

        // HD textures
        _hdButton = new StudioButton { Width = 170 };
        _hdSub = new StudioLabel();
        var hd = SettingRow("HD Earth textures", "", _hdButton, _hdSub);
        _hdProgress = new Panel { Height = 4, Dock = DockStyle.Bottom, BackColor = Theme.Surface, Visible = false };
        _hdProgress.Paint += (_, e) =>
        {
            e.Graphics.Clear(Theme.BorderStrong);
            if (_hdProgressValue < 0) return;
            using var b = new SolidBrush(Theme.Accent);
            e.Graphics.FillRectangle(b, 0, 0, _hdProgress.Width * _hdProgressValue, _hdProgress.Height);
        };
        hd.Controls.Add(_hdProgress);
        WireHdTextures();

        // Cache
        _cacheCombo = new ModernComboBox { Width = 150, AccessibleName = "Keep downloaded images for" };
        foreach (var c in CacheDurations) _cacheCombo.Items.Add(c.Label);
        _cacheCombo.SelectedIndexChanged += (_, _) =>
        {
            if (_loading || _cacheCombo.SelectedIndex < 0) return;
            int days = CacheDurations[_cacheCombo.SelectedIndex].Days;
            _settings.CacheDurationDays = days;
            // EPIC images arrive daily and age out fast — cap at 14 days (0 = forever)
            _settings.EpicCacheDurationDays = days == 0 ? 0 : Math.Min(days, 14);
            QueueCommit(render: false);
        };
        var clearCache = new StudioButton { Text = "Clear now", Width = 104 };
        clearCache.Click += (_, _) => ClearCache();
        _cacheSub = new StudioLabel();
        var cache = SettingRow("Keep downloaded images", "", Pair(_cacheCombo, clearCache), _cacheSub);

        var openFolder = new StudioButton { Text = "Open folder", IconGlyph = Glyph.Folder, Width = 140 };
        openFolder.Click += (_, _) => { try { System.Diagnostics.Process.Start("explorer.exe", AppPaths.DataDir); } catch { } };
        var storage = SettingRow("Storage", AppPaths.DataDir, openFolder);

        // API key
        _apiKeyBox = new TextField { Width = 300, ShowSearchIcon = false, PlaceholderText = "DEMO_KEY", AccessibleName = "api.data.gov API key" };
        _apiKeyBox.TextChanged += (_, _) =>
        {
            if (_loading) return;
            // Empty means the shared demo key (the box shows it as the placeholder)
            var key = _apiKeyBox.Text.Trim();
            _settings.ApiDataGovKey = key.Length == 0 ? "DEMO_KEY" : key;
            QueueCommit(render: false);
        };
        var verify = new StudioButton { Text = "Check", Width = 80 };
        _apiStatus = new StudioLabel();
        verify.Click += async (_, _) => await VerifyApiKeyAsync(verify);
        var api = SettingRow("api.data.gov key", "Picture of the Day, National Parks, Smithsonian · DEMO_KEY: 50 requests a day",
            Pair(_apiKeyBox, verify), _apiStatus);
        var signup = new StudioButton { Text = "Get a free key", Variant = ButtonVariant.Link, Width = 120, Height = 24, Font = Theme.UiFont(9f) };
        signup.Click += (_, _) => OpenUrl("https://api.data.gov/signup/");
        api.Height += 26;
        signup.Location = new Point(16, api.Height - 40);
        api.Controls.Add(signup);

        // Theme
        _themeSegment = new SegmentedControl { Size = new Size(200, 34), AccessibleName = "Theme" };
        _themeSegment.SetItems("Dark", "Light");
        _themeSegment.SelectionChanged += (_, _) =>
        {
            bool dark = _themeSegment.SelectedIndex == 0;
            if (dark == _settings.DarkModeEnabled) return;
            CommitNow();
            _settings.DarkModeEnabled = dark;
            Theme.IsDarkMode = dark;
            _settingsManager.Save();
            // Recreating the window disposes it — never from inside this control's own event
            BeginInvoke(() => RequestReopen?.Invoke());
        };
        var theme = SettingRow("Appearance", "Theme for this window", _themeSegment);

        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "";
        var credits = new StudioButton { Text = "Credits", Width = 100 };
        credits.Click += (_, _) => { using var about = new AboutForm(); about.ShowDialog(this); };
        var about = SettingRow($"Blue Marble Desktop {version}", "By Alex and Claude (Anthropic) · Imagery: NASA, National Park Service, Smithsonian", credits);

        StackTop(page, title,
            SectionLabel("Schedule"), Spacer(8), interval, Spacer(8), startup, Spacer(8), resolution, Spacer(22),
            SectionLabel("Images"), Spacer(8), hd, Spacer(8), cache, Spacer(8), storage, Spacer(8), api, Spacer(22),
            SectionLabel("Appearance"), Spacer(8), theme, Spacer(8), about);

        // Cap the content width so rows don't stretch across a maximized window
        page.Resize += (_, _) =>
        {
            int max = Dpi(820);
            int extra = Math.Max(0, page.ClientSize.Width - Dpi(48) - max);
            page.Padding = new Padding(Dpi(24), Dpi(18), Dpi(24) + extra, Dpi(24));
        };
        return page;
    }

    /// <summary>A card row: title and subtitle on the left, a control on the right.</summary>
    private Card SettingRow(string title, string subtitle, Control control, StudioLabel? liveSubtitle = null)
    {
        var card = new Card { Height = 72, Padding = new Padding(16, 12, 16, 12) };
        var t = new StudioLabel { Text = title, Font = Theme.UiFont(10.25f), Location = new Point(16, 14) };
        var sub = liveSubtitle ?? new StudioLabel();
        sub.Text = subtitle;
        sub.ForeColor = Theme.TextMuted;
        sub.Font = Theme.UiFont(8.75f);
        sub.AutoSize = false;
        sub.AutoEllipsis = true;
        sub.Location = new Point(16, 38);
        sub.Size = new Size(400, 20);
        control.Anchor = AnchorStyles.Right;
        card.Controls.AddRange([t, sub, control]);
        card.Layout += (_, _) =>
        {
            control.Location = new Point(card.Width - card.Padding.Right - control.Width, (Dpi(72) - control.Height) / 2);
            sub.Width = Math.Max(Dpi(60), control.Left - sub.Left - Dpi(16));
        };
        return card;
    }

    /// <summary>Two controls side by side as one right-hand control.</summary>
    private static Panel Pair(Control a, Control b)
    {
        int h = Math.Max(a.Height, b.Height);
        var p = new Panel { Size = new Size(a.Width + 8 + b.Width, Math.Max(h, 34)), BackColor = Theme.Surface };
        a.Location = new Point(0, (p.Height - a.Height) / 2);
        b.Location = new Point(a.Width + 8, (p.Height - b.Height) / 2);
        p.Controls.AddRange([a, b]);
        return p;
    }

    private void RefreshSettingsPage()
    {
        _loading = true;
        try
        {
            int idx = Array.FindIndex(Intervals, i => i.Seconds == _settings.UpdateIntervalSeconds);
            if (idx < 0)
                idx = Intervals.Select((i, n) => (Math.Abs((long)i.Seconds - _settings.UpdateIntervalSeconds), n)).Min().n;
            _intervalCombo.SelectedIndex = idx;
            _startupSwitch.Checked = StartupManager.IsRunOnStartup();
            _resolutionCombo.SelectedIndex = Math.Max(0, Array.FindIndex(Resolutions,
                r => r.W == _settings.RenderWidth && r.H == _settings.RenderHeight));
            _cacheCombo.SelectedIndex = Math.Max(0, Array.FindIndex(CacheDurations, c => c.Days == _settings.CacheDurationDays) is int c and >= 0 ? c : 2);
            if (_apiKeyBox.Text != _settings.ApiDataGovKey) _apiKeyBox.Text = _settings.ApiDataGovKey;
            _themeSegment.SelectedIndex = _settings.DarkModeEnabled ? 0 : 1;
        }
        finally { _loading = false; }
        UpdateCacheSize();
    }

    private async void UpdateCacheSize()
    {
        _cacheSub.Text = "Counting… Favorites are never deleted.";
        long bytes = await Task.Run(() =>
        {
            try
            {
                return new DirectoryInfo(AppPaths.DataDir).EnumerateFiles("*", SearchOption.AllDirectories)
                    .Sum(f => { try { return f.Length; } catch { return 0L; } });
            }
            catch { return 0L; }
        });
        if (IsDisposed) return;
        string size = bytes switch
        {
            < 1024L * 1024 => $"{bytes / 1024.0:0} KB",
            < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):0} MB",
            _ => $"{bytes / (1024.0 * 1024 * 1024):0.0} GB"
        };
        _cacheSub.Text = $"{size} used · favorites and your own images are never deleted";
    }

    private void ClearCache()
    {
        if (MessageBox.Show(this, "Delete downloaded images older than today?\n\nFavorites and your own images are kept. " +
                "Anything else is downloaded again when needed.", "Clear image cache",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;
        try
        {
            HashSet<string> protectedIds;
            lock (_settings.FavoritesLock)
                protectedIds = new HashSet<string>(_settings.Favorites.Select(f => ImageCache.SanitizeFileName(f.ImageId)));
            _imageCache.CleanOldCache(protectedIds, maxDays: 1);
            new EpicImageCache().CleanOldCache(maxDays: 1, protectedIds: protectedIds);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Couldn't clear the cache: {ex.Message}", "Clear image cache",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        UpdateCacheSize();
    }

    private async Task VerifyApiKeyAsync(StudioButton button)
    {
        string key = _apiKeyBox.Text.Trim();
        if (key.Length == 0) { _apiStatus.Text = "Enter a key first."; _apiStatus.ForeColor = Theme.Error; return; }
        button.Enabled = false;
        _apiStatus.Text = "Checking…";
        _apiStatus.ForeColor = Theme.TextMuted;
        ImageSourceInfo? result = null;
        string? error = null;
        try
        {
            var date = DateTime.UtcNow.AddDays(-1).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            result = await Task.Run(() => _apodApi.GetByDateAsync(key, date));
        }
        catch (Exception ex) { error = ex.Message; }
        if (IsDisposed) return;
        button.Enabled = true;
        if (error != null) { _apiStatus.Text = $"Error: {error}"; _apiStatus.ForeColor = Theme.Error; }
        else if (result != null) { _apiStatus.Text = "✓ Key works"; _apiStatus.ForeColor = Theme.Success; }
        else { _apiStatus.Text = "Key didn't work, or it's over today's limit"; _apiStatus.ForeColor = Theme.Warning; }
    }

    private void WireHdTextures()
    {
        void ShowState()
        {
            bool installed = HiResTextureManager.AreHiResTexturesAvailable();
            if (_hdTextureManager.IsDownloading)
            {
                _hdButton.Text = "Cancel download";
                _hdProgress.Visible = true;
            }
            else
            {
                _hdButton.Text = installed ? "Download again" : "Download";
                _hdButton.Variant = installed ? ButtonVariant.Secondary : ButtonVariant.Primary;
                _hdSub.Text = installed
                    ? "✓ Installed — 21600 × 10800 imagery for Globe and Flat map"
                    : $"Sharper Globe and Flat map when zoomed in · about {HiResTextureManager.GetEstimatedDownloadSizeMB()} MB";
                _hdProgress.Visible = false;
            }
        }

        _hdButton.Click += (_, _) =>
        {
            if (_hdTextureManager.IsDownloading) _hdTextureManager.CancelDownload();
            else { _hdTextureManager.StartDownload(); _hdSub.Text = "Starting download…"; }
            ShowState();
        };

        // The shared app-level manager keeps downloading if this window closes;
        // handlers are removed in Dispose and guarded against disposal races
        _hdProgressHandler = (progress, message) => RunOnUi(() =>
        {
            _hdSub.Text = message;
            _hdProgressValue = progress;
            _hdProgress.Visible = true;
            _hdProgress.Invalidate();
            _hdButton.Text = "Cancel download";
        });
        _hdCompletedHandler = (success, message) => RunOnUi(() =>
        {
            ShowState();
            _hdSub.Text = message;
            if (success) _renderScheduler.TriggerUpdate();
        });
        _hdTextureManager.ProgressChanged += _hdProgressHandler;
        _hdTextureManager.DownloadCompleted += _hdCompletedHandler;
        ShowState();
    }

    private void RunOnUi(Action action)
    {
        if (IsDisposed || !IsHandleCreated) return;
        try
        {
            if (InvokeRequired) BeginInvoke(() => { if (!IsDisposed) action(); });
            else action();
        }
        catch (ObjectDisposedException) { }
        catch (InvalidOperationException) { }
    }
}
