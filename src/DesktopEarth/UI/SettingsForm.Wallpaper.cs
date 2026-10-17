using System.Drawing;
using DesktopEarth.UI.Controls;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace DesktopEarth.UI;

/// <summary>Wallpaper page: which display, what it shows, and the globe controls.</summary>
public partial class SettingsForm
{
    // Header
    private ModernComboBox _modeCombo = null!;
    private StudioLabel _modeLabel = null!;
    private MonitorPicker _monitorPicker = null!;
    private StudioLabel _displayName = null!;
    private StudioLabel _displayDetail = null!;
    private SegmentedControl _viewSegment = null!;

    private static readonly MultiMonitorMode[] ModeOrder =
        [MultiMonitorMode.PerDisplay, MultiMonitorMode.SameForAll, MultiMonitorMode.SpanAcross];

    // Bodies
    private Panel _renderedView = null!;
    private Panel _photosView = null!;

    // Rendered views (globe / flat map / moon)
    private PreviewPanel _preview = null!;
    private StudioLabel _sunLine = null!;
    private FlowLayoutPanel _presetFlow = null!;
    private Card _locationCard = null!, _framingCard = null!, _lightCard = null!;
    private WorldMapPicker _map = null!;
    private ModernComboBox _cityCombo = null!;
    private StudioLabel _coordLabel = null!;
    private ModernSlider _zoomSlider = null!, _shiftXSlider = null!, _shiftYSlider = null!;
    private StudioLabel _zoomValue = null!, _shiftXValue = null!, _shiftYValue = null!;
    private Panel _lightsRow = null!, _brightRow = null!, _styleRow = null!;
    private ToggleSwitch _lightsSwitch = null!;
    private ModernSlider _brightSlider = null!, _daySlider = null!;
    private StudioLabel _brightValue = null!, _dayValue = null!, _dayLabel = null!;
    private SegmentedControl _styleSegment = null!;

    // Displays where "Photos" is open for browsing but not yet committed
    private readonly HashSet<string> _browsingPhotos = new();

    /// <summary>City shortcuts in real geographic coordinates (north/east positive).</summary>
    private static readonly (string Name, double Lat, double Lon)[] Cities =
    [
        ("Chicago", 41.9, -87.6), ("New York", 40.7, -74.0), ("Los Angeles", 34.1, -118.2),
        ("Seattle", 47.6, -122.3), ("Denver", 39.7, -105.0), ("Toronto", 43.7, -79.4),
        ("Mexico City", 19.4, -99.1), ("Honolulu", 21.3, -157.9), ("São Paulo", -23.6, -46.6),
        ("Rio de Janeiro", -22.9, -43.2), ("Buenos Aires", -34.6, -58.4), ("Reykjavík", 64.1, -21.9),
        ("London", 51.5, -0.1), ("Paris", 48.9, 2.4), ("Berlin", 52.5, 13.4), ("Rome", 41.9, 12.5),
        ("Cairo", 30.0, 31.2), ("Nairobi", -1.3, 36.8), ("Cape Town", -33.9, 18.4), ("Moscow", 55.8, 37.6),
        ("Dubai", 25.2, 55.3), ("Mumbai", 19.1, 72.9), ("Singapore", 1.35, 103.8), ("Hong Kong", 22.3, 114.2),
        ("Beijing", 39.9, 116.4), ("Seoul", 37.6, 127.0), ("Tokyo", 35.7, 139.7), ("Sydney", -33.9, 151.2),
    ];

    private Panel BuildWallpaperPage()
    {
        var page = new Panel { BackColor = Theme.Ground };

        _renderedView = BuildRenderedView();
        _photosView = BuildPhotosView();
        var body = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Ground };
        _renderedView.Dock = DockStyle.Fill;
        _photosView.Dock = DockStyle.Fill;
        body.Controls.Add(_renderedView);
        body.Controls.Add(_photosView);

        page.Controls.Add(body);
        page.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 1, BackColor = Theme.Divider });
        page.Controls.Add(BuildWallpaperHeader());
        return page;
    }

    private Panel BuildWallpaperHeader()
    {
        var header = new Panel { Dock = DockStyle.Top, Height = 160, BackColor = Theme.Ground };

        _modeLabel = SectionLabel("Displays");
        _modeLabel.Location = new Point(24, 20);
        _modeCombo = new ModernComboBox { Location = new Point(110, 13), Width = 250, AccessibleName = "How wallpapers use your displays" };
        _modeCombo.Items.AddRange(["Each display separately", "Same picture on every display", "One picture across all displays"]);
        _modeCombo.SelectedIndexChanged += (_, _) => OnMultiMonitorModeChanged();

        _monitorPicker = new MonitorPicker { Location = new Point(20, 46), Size = new Size(320, 78) };
        _monitorPicker.SetDisplays(_displays);
        _monitorPicker.SelectionChanged += (_, _) =>
        {
            CommitNow();
            _displayIndex = _monitorPicker.SelectedIndex;
            _loading = true;
            try { LoadDisplayIntoUi(); }
            finally { _loading = false; }
        };

        _displayName = new StudioLabel { Font = Theme.Semibold(10.5f), Location = new Point(24, 130) };
        _displayDetail = new StudioLabel { ForeColor = Theme.TextMuted, Font = Theme.UiFont(9f), Location = new Point(24, 130) };

        var showLabel = SectionLabel("Show");
        showLabel.Location = new Point(479, 96);
        _viewSegment = new SegmentedControl
        {
            Size = new Size(400, 38),
            Location = new Point(479, 116),
            Font = Theme.UiFont(10f),
            AccessibleName = "What this display shows"
        };
        _viewSegment.SetItems("Globe", "Flat map", "Moon", "Photos");
        _viewSegment.SelectionChanged += (_, _) => OnViewSegmentChanged();

        header.Controls.AddRange([_modeLabel, _modeCombo, _monitorPicker, _displayName, _displayDetail, showLabel, _viewSegment]);
        header.Layout += (_, _) =>
        {
            // Right-aligned by hand: anchors are measured against the header's
            // width at construction time, before it is docked to its real size
            _viewSegment.Left = header.Width - Dpi(24) - _viewSegment.Width;
            showLabel.Left = _viewSegment.Left;
            _displayDetail.Left = _displayName.Right + Dpi(8);
            _displayDetail.Top = _displayName.Top + (_displayName.Height - _displayDetail.Height) / 2 + Dpi(1);
        };
        return header;
    }

    private Panel BuildRenderedView()
    {
        var view = new Panel { BackColor = Theme.Ground, Padding = new Padding(24, 20, 16, 0) };

        // Right: control cards in a scrolling column
        var column = new Panel { Dock = DockStyle.Right, Width = 392, AutoScroll = true, BackColor = Theme.Ground, Padding = new Padding(0, 0, 12, 20) };
        Theme.StyleScrollableControl(column);
        _locationCard = BuildLocationCard();
        _framingCard = BuildFramingCard();
        _lightCard = BuildLightCard();
        StackTop(column, _locationCard, Spacer(12), _framingCard, Spacer(12), _lightCard);

        // Left: live preview, sun note and presets
        var left = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Ground, Padding = new Padding(0, 0, 20, 0) };
        _preview = new PreviewPanel { Badge = "Live preview", Height = 300 };
        _sunLine = new StudioLabel
        {
            AutoSize = false, Height = 34, ForeColor = Theme.TextMuted, Font = Theme.UiFont(9f),
            TextAlign = ContentAlignment.MiddleLeft
        };
        var presetsLabel = SectionLabel("Presets");
        presetsLabel.AutoSize = false;
        presetsLabel.Height = 30;
        presetsLabel.TextAlign = ContentAlignment.BottomLeft;
        _presetFlow = new FlowLayoutPanel { AutoSize = true, WrapContents = true, BackColor = Theme.Ground, Padding = new Padding(0, 8, 0, 0) };
        StackTop(left, _preview, _sunLine, presetsLabel, _presetFlow);
        left.Resize += (_, _) => SizePreview(left);

        view.Controls.Add(left);
        view.Controls.Add(column);
        return view;
    }

    private void SizePreview(Control left)
    {
        int w = left.ClientSize.Width - left.Padding.Horizontal;
        if (w <= 0) return;
        float aspect = _preview.AspectRatio <= 0 ? 16f / 9f : _preview.AspectRatio;
        // Keep room for the sun line and presets below
        int maxH = Math.Max(Dpi(160), left.ClientSize.Height - Dpi(150));
        _preview.Height = Math.Min((int)(w / aspect), maxH);
    }

    // ─── Location ───

    private Card BuildLocationCard()
    {
        var card = new Card { Title = "Center on", TitleNote = "Click or drag on the map" };
        _map = new WorldMapPicker { Height = 170 };
        _map.LocationPicked += (_, _) =>
        {
            double lat = _map.Latitude, lon = _map.Longitude;
            Edit(t => { t.CameraTilt = (float)Math.Round(lat, 1); t.LongitudeOffset = (float)Math.Round(-lon, 1); });
            ShowLocation(lat, lon);
        };

        var row = new Panel { Height = 34, BackColor = Theme.Surface };
        _cityCombo = new ModernComboBox { Dock = DockStyle.Fill, AccessibleName = "City" };
        _cityCombo.Items.Add("Custom location");
        foreach (var c in Cities) _cityCombo.Items.Add(c.Name);
        _cityCombo.SelectedIndexChanged += (_, _) =>
        {
            if (_loading || _cityCombo.SelectedIndex <= 0) return;
            var c = Cities[_cityCombo.SelectedIndex - 1];
            Edit(t => { t.CameraTilt = (float)c.Lat; t.LongitudeOffset = (float)-c.Lon; });
            ShowLocation(c.Lat, c.Lon);
        };
        _coordLabel = new StudioLabel
        {
            AutoSize = false, Dock = DockStyle.Right, Width = 118, TextAlign = ContentAlignment.MiddleRight,
            ForeColor = Theme.TextSecondary
        };
        row.Controls.Add(_cityCombo);
        row.Controls.Add(_coordLabel);

        StackTop(card, _map, Spacer(12), row);
        card.Resize += (_, _) =>
        {
            int h = Math.Max(Dpi(100), _map.Width / 2);
            if (_map.Height != h) { _map.Height = h; FitCard(card); }
        };
        return card;
    }

    /// <summary>Show a location on the map, city list and coordinate text without saving.</summary>
    private void ShowLocation(double lat, double lon)
    {
        _map.SetLocation(lat, lon);
        _coordLabel.Text = $"{Math.Abs(lat):0.#}° {(lat >= 0 ? "N" : "S")}, {Math.Abs(lon):0.#}° {(lon >= 0 ? "E" : "W")}";

        int match = Array.FindIndex(Cities, c => Math.Abs(c.Lat - lat) <= 1.0 && Math.Abs(c.Lon - lon) <= 1.0);
        bool was = _loading;
        _loading = true;
        try { _cityCombo.SelectedIndex = match + 1; }
        finally { _loading = was; }
    }

    // ─── Framing ───

    private Card BuildFramingCard()
    {
        var card = new Card { Title = "Framing" };
        var reset = new StudioButton
        {
            Text = "Reset", Variant = ButtonVariant.Link, Size = new Size(64, 28),
            Location = new Point(300, 8),
            Font = Theme.UiFont(9f)
        };
        reset.Click += (_, _) =>
        {
            var defaults = new AppSettings();
            Edit(t =>
            {
                t.ZoomLevel = defaults.ZoomLevel;
                t.FieldOfView = defaults.FieldOfView;
                t.ImageOffsetX = 0;
                t.ImageOffsetY = 0;
            });
            _loading = true;
            try { LoadViewControls(); }
            finally { _loading = false; }
        };
        card.Controls.Add(reset);
        card.Layout += (_, _) => reset.Left = card.Width - card.Padding.Right - reset.Width + Dpi(6);

        _zoomSlider = new ModernSlider { Minimum = ViewMath.ZoomMin, Maximum = ViewMath.ZoomMax, AccessibleName = "Zoom" };
        _zoomSlider.Scroll += (_, _) =>
        {
            var (d, f) = ViewMath.SliderToCamera(_zoomSlider.Value);
            Edit(t => { t.ZoomLevel = d; t.FieldOfView = f; });
            _zoomValue.Text = ZoomText(d, f);
        };
        _shiftXSlider = new ModernSlider { Minimum = -25, Maximum = 25, Bipolar = true, AccessibleName = "Move left or right" };
        _shiftXSlider.Scroll += (_, _) =>
        {
            int v = _shiftXSlider.Value;
            Edit(t => t.ImageOffsetX = v);
            _shiftXValue.Text = ShiftText(v, "right", "left");
        };
        _shiftYSlider = new ModernSlider { Minimum = -25, Maximum = 25, Bipolar = true, AccessibleName = "Move up or down" };
        _shiftYSlider.Scroll += (_, _) =>
        {
            int v = _shiftYSlider.Value;
            Edit(t => t.ImageOffsetY = v);
            _shiftYValue.Text = ShiftText(v, "up", "down");
        };

        StackTop(card,
            SliderRow("Zoom", _zoomSlider, out _zoomValue), Spacer(10),
            SliderRow("Move left / right", _shiftXSlider, out _shiftXValue), Spacer(10),
            SliderRow("Move up / down", _shiftYSlider, out _shiftYValue));
        FitCard(card);
        return card;
    }

    private static string ZoomText(float distance, float fov) => $"{ViewMath.Magnification(distance, fov):0.0}×";

    private static string ShiftText(int v, string positive, string negative) =>
        v == 0 ? "Centered" : v > 0 ? $"{v}% {positive}" : $"{-v}% {negative}";

    // ─── Light ───

    private Card BuildLightCard()
    {
        var card = new Card { Title = "Light" };

        _lightsSwitch = new ToggleSwitch { AccessibleName = "City lights at night" };
        _lightsSwitch.Toggled += (_, _) =>
        {
            bool on = _lightsSwitch.Checked;
            Edit(t => t.NightLightsEnabled = on);
            _brightRow.Enabled = on;
        };
        _lightsRow = SwitchRow("City lights at night", _lightsSwitch);

        _brightSlider = new ModernSlider { Minimum = 1, Maximum = 30, AccessibleName = "City light brightness" };
        _brightSlider.Scroll += (_, _) =>
        {
            float v = _brightSlider.Value / 10f;
            Edit(t => t.NightLightsBrightness = v);
            _brightValue.Text = $"{v:0.0}×";
        };
        _brightRow = SliderRow("City light brightness", _brightSlider, out _brightValue);

        _daySlider = new ModernSlider { Minimum = 0, Maximum = 50, AccessibleName = "Daylight brightness" };
        _daySlider.Scroll += (_, _) =>
        {
            float v = _daySlider.Value / 100f;
            Edit(t => t.AmbientLight = v);
            _dayValue.Text = $"{_daySlider.Value * 2}%";
        };
        var dayRow = SliderRow("Daylight brightness", _daySlider, out _dayValue);
        _dayLabel = (StudioLabel)dayRow.Tag!;

        _styleSegment = new SegmentedControl { Height = 34, AccessibleName = "Map style", TrackColor = Theme.Well };
        _styleSegment.SetItems("Land only", "With ocean floor");
        _styleSegment.SelectionChanged += (_, _) =>
        {
            var style = _styleSegment.SelectedIndex == 1 ? ImageStyle.TopoBathy : ImageStyle.Topo;
            Edit(t => t.ImageStyle = style);
            LoadMapImageAsync();
        };
        _styleRow = new Panel { Height = 64, BackColor = Theme.Surface };
        var styleLabel = new StudioLabel { Text = "Map style", Dock = DockStyle.Top, AutoSize = false, Height = 26 };
        _styleSegment.Dock = DockStyle.Top;
        _styleRow.Controls.Add(_styleSegment);
        _styleRow.Controls.Add(styleLabel);

        StackTop(card, _lightsRow, Spacer(6), _brightRow, Spacer(10), dayRow, Spacer(10), _styleRow);
        FitCard(card);
        return card;
    }

    // ─── Row builders ───

    /// <summary>Label + value on one line, slider beneath. The row's Tag is its label.</summary>
    private static Panel SliderRow(string label, ModernSlider slider, out StudioLabel value)
    {
        var row = new Panel { Height = 52, BackColor = Theme.Surface };
        var top = new Panel { Dock = DockStyle.Top, Height = 24, BackColor = Theme.Surface };
        var name = new StudioLabel { Text = label, Dock = DockStyle.Left };
        value = new StudioLabel
        {
            AutoSize = false, Dock = DockStyle.Right, Width = 120, TextAlign = ContentAlignment.TopRight,
            ForeColor = Theme.TextMuted
        };
        top.Controls.Add(name);
        top.Controls.Add(value);
        slider.Dock = DockStyle.Bottom;
        slider.Height = 26;
        row.Controls.Add(slider);
        row.Controls.Add(top);
        row.Tag = name;
        return row;
    }

    private Panel SwitchRow(string label, ToggleSwitch toggle)
    {
        var row = new Panel { Height = 34, BackColor = Theme.Surface };
        var name = new StudioLabel { Text = label, AutoSize = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
        toggle.Anchor = AnchorStyles.Right;
        row.Controls.Add(toggle);
        row.Controls.Add(name);
        row.Layout += (_, _) => toggle.Location = new Point(row.Width - toggle.Width - 1, (row.Height - toggle.Height) / 2);
        return row;
    }

    /// <summary>Size a card to its visible rows (cards hold Dock.Top rows only).</summary>
    private static void FitCard(Card card)
    {
        int h = card.Padding.Vertical;
        foreach (Control c in card.Controls)
            if (c.Visible && c.Dock == DockStyle.Top) h += c.Height;
        card.Height = h;
    }

    // ─── Loading a display into the UI ───

    /// <summary>
    /// Fill every control from the selected display's settings. Call with
    /// _loading set; the custom controls never raise user events for code
    /// changes, and the combo handlers check _loading.
    /// </summary>
    private void LoadDisplayIntoUi()
    {
        bool multi = _displays.Count > 1;
        _modeLabel.Visible = multi;
        _modeCombo.Visible = multi;
        _modeCombo.SelectedIndex = Array.IndexOf(ModeOrder, _settings.MultiMonitorMode) is int m and >= 0 ? m : 1;

        _monitorPicker.SetDisplays(_displays);
        _monitorPicker.SelectedIndex = _displayIndex;
        _monitorPicker.AllSelected = !PerDisplay;

        var d = SelectedDisplay;
        if (PerDisplay && d != null)
        {
            _displayName.Text = d.Title;
            _displayDetail.Text = d.Detail;
        }
        else
        {
            _displayName.Text = multi ? (_settings.MultiMonitorMode == MultiMonitorMode.SpanAcross
                ? "All displays, as one picture" : "All displays") : d?.Title ?? "Display";
            _displayDetail.Text = multi ? $"{_displays.Count} displays" : d?.Detail ?? "";
        }

        var view = ViewSettings;
        bool browsing = _browsingPhotos.Contains(CurrentImageKey);
        _viewSegment.SelectedIndex = browsing ? 3 : (int)view.DisplayMode;

        LoadViewControls();
        LoadPhotosState();
        RefreshPresets();
        ApplyViewVisibility();
        ShowPreviewForSelectedDisplay();
        UpdateStatusCard();
    }

    private void LoadViewControls()
    {
        var v = ViewSettings;
        ShowLocation(v.CameraTilt, -v.LongitudeOffset);

        _zoomSlider.Value = ViewMath.CameraToSlider(v.ZoomLevel, v.FieldOfView);
        _zoomValue.Text = ZoomText(v.ZoomLevel, v.FieldOfView);
        _shiftXSlider.Value = (int)Math.Round(v.ImageOffsetX);
        _shiftXValue.Text = ShiftText(_shiftXSlider.Value, "right", "left");
        _shiftYSlider.Value = (int)Math.Round(v.ImageOffsetY);
        _shiftYValue.Text = ShiftText(_shiftYSlider.Value, "up", "down");

        _lightsSwitch.Checked = v.NightLightsEnabled;
        _brightRow.Enabled = v.NightLightsEnabled;
        _brightSlider.Value = (int)Math.Round(v.NightLightsBrightness * 10);
        _brightValue.Text = $"{_brightSlider.Value / 10f:0.0}×";
        _daySlider.Value = (int)Math.Round(v.AmbientLight * 100);
        _dayValue.Text = $"{_daySlider.Value * 2}%";
        _styleSegment.SelectedIndex = v.ImageStyle == ImageStyle.TopoBathy ? 1 : 0;
    }

    /// <summary>Show the body and cards that apply to the selected view.</summary>
    private void ApplyViewVisibility()
    {
        int view = _viewSegment.SelectedIndex;
        bool photos = view == 3;
        _renderedView.Visible = !photos;
        _photosView.Visible = photos;
        if (photos) return;

        var mode = (DisplayMode)view;
        _locationCard.Visible = mode == DisplayMode.Spherical;
        _framingCard.Visible = mode != DisplayMode.FlatMap;
        bool earth = mode != DisplayMode.Moon;
        _lightsRow.Visible = earth;
        _brightRow.Visible = earth;
        _styleRow.Visible = earth;
        _dayLabel.Text = earth ? "Daylight brightness" : "Brightness";
        FitCard(_lightCard);

        _sunLine.Text = mode switch
        {
            DisplayMode.Moon => "☀  The Moon's near side, lit by the real Sun",
            DisplayMode.FlatMap => "☀  Day and night follow the real Sun",
            _ => "☀  Day and night follow the real Sun · drag the map to move the view"
        };
    }

    private void OnViewSegmentChanged()
    {
        int index = _viewSegment.SelectedIndex;
        string key = CurrentImageKey;
        if (index < 3)
        {
            _browsingPhotos.Remove(key);
            var mode = (DisplayMode)index;
            Edit(t => t.DisplayMode = mode);
        }
        else if (ViewSettings.DisplayMode != DisplayMode.StillImage)
        {
            // Browsing only: the wallpaper changes when an image is Set
            _browsingPhotos.Add(key);
        }

        _loading = true;
        try
        {
            LoadPhotosState();
            ApplyViewVisibility();
            UpdateStatusCard();
        }
        finally { _loading = false; }
    }

    private void OnMultiMonitorModeChanged()
    {
        if (_loading || _modeCombo.SelectedIndex < 0) return;
        var mode = ModeOrder[_modeCombo.SelectedIndex];
        if (mode == _settings.MultiMonitorMode) return;
        CommitNow();
        _settings.MultiMonitorMode = mode;
        _settingsManager.Save();
        _renderScheduler.TriggerUserUpdate();
        SetStatusOverride("Updating…", Theme.Accent);

        _loading = true;
        try { LoadDisplayIntoUi(); }
        finally { _loading = false; }
    }

    private void ShowPreviewForSelectedDisplay()
    {
        var d = SelectedDisplay;
        if (d == null) return;
        _preview.AspectRatio = (float)d.Bounds.Width / d.Bounds.Height;
        _preview.Image = _previews.TryGetValue(d.MonitorId, out var bmp) ? new Bitmap(bmp) : null;
        if (_preview.Parent != null) SizePreview(_preview.Parent);
    }

    /// <summary>Load the Earth day texture, small, as the map picker's background.</summary>
    private async void LoadMapImageAsync()
    {
        var style = ViewSettings.ImageStyle;
        Bitmap? bmp = null;
        try
        {
            bmp = await Task.Run(() =>
            {
                var path = new AssetLocator().GetDayTexturePath(style);
                using var img = SixLabors.ImageSharp.Image.Load<Rgba32>(ImageDecoding.WithTargetSize(1024, 512), path);
                img.Mutate(x => x.Resize(720, 360));
                using var ms = new MemoryStream();
                img.Save(ms, new SixLabors.ImageSharp.Formats.Bmp.BmpEncoder());
                ms.Position = 0;
                using var gdi = Image.FromStream(ms);
                return new Bitmap(gdi);
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Map image load failed: {ex.Message}");
        }
        if (IsDisposed) { bmp?.Dispose(); return; }
        if (bmp != null) _map.MapImage = bmp;
    }

    // ─── Presets ───

    private void RefreshPresets()
    {
        _presetFlow.SuspendLayout();
        foreach (Control c in _presetFlow.Controls.Cast<Control>().ToList()) c.Dispose();
        _presetFlow.Controls.Clear();

        foreach (var preset in _settings.Presets.ToList())
        {
            var chip = new StudioButton
            {
                Text = preset.Name, Variant = ButtonVariant.Chip, AutoSize = true, AutoHeight = 32,
                Margin = new Padding(0, 0, Dpi(8), Dpi(8)), Font = Theme.UiFont(9.25f)
            };
            var p = preset;
            chip.Click += (_, _) => ApplyPreset(p);
            var menu = new ContextMenuStrip { Renderer = new StudioMenuRenderer() };
            menu.Items.Add("Apply", null, (_, _) => ApplyPreset(p));
            menu.Items.Add("Delete preset", null, (_, _) =>
            {
                _settings.Presets.Remove(p);
                _settingsManager.Save();
                RefreshPresets();
            });
            chip.ContextMenuStrip = menu;
            _presetFlow.Controls.Add(chip);
        }

        var save = new StudioButton
        {
            Text = "Save current view", IconGlyph = Glyph.Add, Variant = ButtonVariant.Chip,
            AutoSize = true, AutoHeight = 32, Margin = new Padding(0, 0, Dpi(8), Dpi(8)), Font = Theme.UiFont(9.25f)
        };
        save.Click += (_, _) => SavePreset();
        _presetFlow.Controls.Add(save);
        _presetFlow.ResumeLayout(true);
    }

    private void SavePreset()
    {
        var name = PromptForName(this, "Save preset", "Name this view:");
        if (name == null) return;
        var v = ViewSettings;
        _settings.Presets.Add(new SettingsPreset
        {
            Name = name,
            DisplayMode = v.DisplayMode,
            StillImageSource = v.StillImageSource,
            ZoomLevel = v.ZoomLevel,
            FieldOfView = v.FieldOfView,
            CameraTilt = v.CameraTilt,
            LongitudeOffset = v.LongitudeOffset,
            ImageOffsetX = v.ImageOffsetX,
            ImageOffsetY = v.ImageOffsetY,
            NightLightsEnabled = v.NightLightsEnabled,
            NightLightsBrightness = v.NightLightsBrightness,
            AmbientLight = v.AmbientLight,
            ImageStyle = v.ImageStyle,
            EpicImageType = v.EpicImageType
        });
        _settingsManager.Save();
        RefreshPresets();
    }

    private void ApplyPreset(SettingsPreset p)
    {
        Edit(t =>
        {
            t.DisplayMode = p.DisplayMode;
            t.StillImageSource = p.StillImageSource;
            t.ZoomLevel = p.ZoomLevel;
            t.FieldOfView = p.FieldOfView;
            t.CameraTilt = p.CameraTilt;
            t.LongitudeOffset = p.LongitudeOffset;
            t.ImageOffsetX = p.ImageOffsetX;
            t.ImageOffsetY = p.ImageOffsetY;
            t.NightLightsEnabled = p.NightLightsEnabled;
            t.NightLightsBrightness = p.NightLightsBrightness;
            t.AmbientLight = p.AmbientLight;
            t.ImageStyle = p.ImageStyle;
            t.EpicImageType = p.EpicImageType;
        });
        _browsingPhotos.Remove(CurrentImageKey);
        _loading = true;
        try { LoadDisplayIntoUi(); }
        finally { _loading = false; }
        LoadMapImageAsync();
    }
}
