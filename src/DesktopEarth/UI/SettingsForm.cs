using System.Drawing;

namespace DesktopEarth.UI;

public class SettingsForm : Form
{
    private readonly SettingsManager _settingsManager;
    private readonly RenderScheduler _renderScheduler;
    private AppSettings _settings;

    // Debounce timer for live preview
    private readonly System.Windows.Forms.Timer _debounceTimer;
    private bool _isLoading; // Suppress events during load

    // Layout constants
    private const int LeftMargin = 20;
    private const int RightValueX = 430;
    private const int SliderWidth = 430;
    private const int IndentMargin = 40;
    private const int IndentSliderWidth = 410;
    private const int LabelHeight = 20;
    private const int SliderRowHeight = 30;
    private const int RowGap = 8;

    // Location presets: (Name, Longitude, Latitude)
    // Longitude values are negated from geographic convention to match the
    // renderer's coordinate system (row-major → column-major transpose).
    private static readonly (string Name, float Lon, float Lat)[] LocationPresets =
    [
        ("Custom", 0, 0),               // placeholder for manual
        ("New York", 74, 41),
        ("Chicago", 88, 42),
        ("Los Angeles", 118, 34),
        ("London", 0, 52),
        ("Paris", -2, 49),
        ("Tokyo", -140, 36),
        ("Hong Kong", -114, 22),
        ("Sydney", -151, -34),
        ("Dubai", -55, 25),
        ("Sao Paulo", 47, -24),
        ("Cape Town", -18, -34),
        ("Mumbai", -73, 19),
    ];

    // Appearance tab controls
    private ComboBox _displayModeCombo = null!;
    private ComboBox _locationCombo = null!;
    private Panel _sphericalPanel = null!;
    private TrackBar _longitudeSlider = null!;
    private Label _longitudeValue = null!;
    private TrackBar _latitudeSlider = null!;
    private Label _latitudeValue = null!;
    private TrackBar _zoomSlider = null!;
    private Label _zoomValue = null!;
    private CheckBox _nightLightsCheck = null!;
    private TrackBar _nightBrightnessSlider = null!;
    private Label _nightBrightnessValue = null!;
    private TrackBar _ambientSlider = null!;
    private Label _ambientValue = null!;
    private TrackBar _offsetXSlider = null!;
    private Label _offsetXValue = null!;
    private TrackBar _offsetYSlider = null!;
    private Label _offsetYValue = null!;
    private RadioButton _topoRadio = null!;
    private RadioButton _topoBathyRadio = null!;

    // System tab controls
    private ComboBox _updateIntervalCombo = null!;
    private CheckBox _runOnStartupCheck = null!;
    private RadioButton _sameForAllRadio = null!;
    private RadioButton _spanAcrossRadio = null!;
    private RadioButton _perDisplayRadio = null!;
    private ComboBox _renderResCombo = null!;

    // Per-display controls
    private Panel _perDisplayPanel = null!;
    private ComboBox _monitorSelectCombo = null!;
    private string _selectedMonitorDevice = "";

    public SettingsForm(SettingsManager settingsManager, RenderScheduler renderScheduler)
    {
        _settingsManager = settingsManager;
        _renderScheduler = renderScheduler;
        _settings = settingsManager.Settings;

        _debounceTimer = new System.Windows.Forms.Timer { Interval = 300 };
        _debounceTimer.Tick += (_, _) =>
        {
            _debounceTimer.Stop();
            ApplyLivePreview();
        };

        _isLoading = true;
        InitializeForm();
        LoadCurrentSettings();
        _isLoading = false;
    }

    private void SchedulePreview()
    {
        if (_isLoading) return;
        _debounceTimer.Stop();
        _debounceTimer.Start();
    }

    private void ApplyLivePreview()
    {
        _settingsManager.ApplyAndSave(s =>
        {
            SaveAppearanceToGlobalSettings(s);

            if (_perDisplayRadio.Checked && !string.IsNullOrEmpty(_selectedMonitorDevice))
            {
                var config = GetOrCreateDisplayConfig(_selectedMonitorDevice);
                SaveAppearanceToDisplayConfig(config);
            }

            s.UpdateIntervalSeconds = _updateIntervalCombo.SelectedIndex switch
            {
                0 => 60, 1 => 120, 2 => 300, 3 => 600,
                4 => 900, 5 => 1800, 6 => 3600, _ => 600
            };

            if (_sameForAllRadio.Checked)
                s.MultiMonitorMode = MultiMonitorMode.SameForAll;
            else if (_spanAcrossRadio.Checked)
                s.MultiMonitorMode = MultiMonitorMode.SpanAcross;
            else
                s.MultiMonitorMode = MultiMonitorMode.PerDisplay;

            (s.RenderWidth, s.RenderHeight) = _renderResCombo.SelectedIndex switch
            {
                1 => (1920, 1080), 2 => (2560, 1440),
                3 => (3840, 2160), 4 => (5120, 2880), _ => (0, 0)
            };
        });

        StartupManager.SetRunOnStartup(_runOnStartupCheck.Checked);
        _renderScheduler.TriggerUpdate();
    }

    /// <summary>
    /// Maps a zoom slider value (1-100) to camera distance and FoV.
    /// Higher slider = more zoomed in (closer camera, narrower FoV).
    /// Slider 1 = fully zoomed out, 100 = extreme close-up.
    /// Default is 30 (globe fills ~60% of screen).
    /// </summary>
    private static (float distance, float fov) ZoomSliderToCamera(int sliderValue)
    {
        // Normalize to 0..1 (slider is 1..100)
        float t = (sliderValue - 1) / 99f;
        // Distance: 6.0 (zoomed out) to 1.15 (zoomed in) — exponential curve
        float distance = 6.0f * MathF.Pow(1.15f / 6.0f, t);
        // FoV: 60° (zoomed out) to 20° (zoomed in) — linear
        float fov = 60f - t * 40f;
        return (distance, fov);
    }

    private void SaveAppearanceToGlobalSettings(AppSettings s)
    {
        s.DisplayMode = (DisplayMode)_displayModeCombo.SelectedIndex;
        s.LongitudeOffset = _longitudeSlider.Value;
        s.CameraTilt = _latitudeSlider.Value;

        var (distance, fov) = ZoomSliderToCamera(_zoomSlider.Value);
        s.ZoomLevel = distance;
        s.FieldOfView = fov;

        s.ImageOffsetX = _offsetXSlider.Value;
        s.ImageOffsetY = _offsetYSlider.Value;

        s.NightLightsEnabled = _nightLightsCheck.Checked;
        s.NightLightsBrightness = _nightBrightnessSlider.Value / 10f;
        s.AmbientLight = _ambientSlider.Value / 100f;
        s.ImageStyle = _topoBathyRadio.Checked ? ImageStyle.TopoBathy : ImageStyle.Topo;
    }

    private void SaveAppearanceToDisplayConfig(DisplayConfig config)
    {
        config.DisplayMode = (DisplayMode)_displayModeCombo.SelectedIndex;
        config.LongitudeOffset = _longitudeSlider.Value;
        config.CameraTilt = _latitudeSlider.Value;

        var (distance, fov) = ZoomSliderToCamera(_zoomSlider.Value);
        config.ZoomLevel = distance;
        config.FieldOfView = fov;

        config.ImageOffsetX = _offsetXSlider.Value;
        config.ImageOffsetY = _offsetYSlider.Value;

        config.NightLightsEnabled = _nightLightsCheck.Checked;
        config.NightLightsBrightness = _nightBrightnessSlider.Value / 10f;
        config.AmbientLight = _ambientSlider.Value / 100f;
        config.ImageStyle = _topoBathyRadio.Checked ? ImageStyle.TopoBathy : ImageStyle.Topo;
    }

    private DisplayConfig GetOrCreateDisplayConfig(string deviceName)
    {
        var existing = _settings.DisplayConfigs.Find(c => c.DeviceName == deviceName);
        if (existing != null) return existing;

        var config = new DisplayConfig { DeviceName = deviceName };
        _settings.DisplayConfigs.Add(config);
        return config;
    }

    private void InitializeForm()
    {
        Text = "Blue Marble Desktop Settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(510, 840);
        ShowInTaskbar = true;

        var tabControl = new TabControl
        {
            Dock = DockStyle.Fill,
            Padding = new Point(8, 4)
        };

        tabControl.TabPages.Add(CreateAppearanceTab());
        tabControl.TabPages.Add(CreateSystemTab());

        Controls.Add(tabControl);
    }

    /// <summary>
    /// Adds a label + value row, then a slider row below. Returns Y after the slider.
    /// </summary>
    private int AddSliderRow(Control parent, string labelText, string defaultValue,
        int x, int y, int min, int max, int initial, int sliderWidth,
        out TrackBar slider, out Label valueLabel, Action<TrackBar, Label> onScroll)
    {
        parent.Controls.Add(MakeLabel(labelText, x, y));
        valueLabel = MakeLabel(defaultValue, RightValueX, y);
        parent.Controls.Add(valueLabel);

        slider = MakeSlider(x, y + LabelHeight, min, max, initial, sliderWidth);
        var sl = slider;
        var vl = valueLabel;
        slider.Scroll += (_, _) =>
        {
            onScroll(sl, vl);
            SchedulePreview();
        };
        parent.Controls.Add(slider);

        return y + LabelHeight + SliderRowHeight + RowGap;
    }

    private TabPage CreateAppearanceTab()
    {
        var tab = new TabPage("Appearance");
        tab.AutoScroll = false;
        int y = 10;

        // ── Display mode ──
        tab.Controls.Add(MakeLabel("View:", LeftMargin, y + 3));
        _displayModeCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(100, y),
            Width = 150
        };
        _displayModeCombo.Items.AddRange(["Globe", "Flat Map", "Moon"]);
        _displayModeCombo.SelectedIndexChanged += (_, _) =>
        {
            UpdateModeVisibility();
            SchedulePreview();
        };
        tab.Controls.Add(_displayModeCombo);

        // ── Location preset ──
        tab.Controls.Add(MakeLabel("Location:", 270, y + 3));
        _locationCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(340, y),
            Width = 120
        };
        foreach (var preset in LocationPresets)
            _locationCombo.Items.Add(preset.Name);
        _locationCombo.SelectedIndex = 0;
        _locationCombo.SelectedIndexChanged += (_, _) =>
        {
            if (_isLoading || _locationCombo.SelectedIndex <= 0) return;
            var preset = LocationPresets[_locationCombo.SelectedIndex];
            _isLoading = true;
            _longitudeSlider.Value = Math.Clamp((int)preset.Lon, -180, 180);
            _longitudeValue.Text = preset.Lon.ToString("F0") + "\u00b0";
            _latitudeSlider.Value = Math.Clamp((int)preset.Lat, -60, 60);
            _latitudeValue.Text = preset.Lat.ToString("F0") + "\u00b0";
            _isLoading = false;
            SchedulePreview();
        };
        tab.Controls.Add(_locationCombo);
        y += 30;

        // ── Spherical panel (longitude + latitude — disabled in flat map mode) ──
        _sphericalPanel = new Panel
        {
            Location = new Point(0, y),
            Size = new Size(480, (LabelHeight + SliderRowHeight + RowGap) * 2),
        };
        int sy = 0;

        // Longitude
        _sphericalPanel.Controls.Add(MakeLabel("Longitude:", LeftMargin, sy));
        _longitudeValue = MakeLabel("-100\u00b0", RightValueX, sy);
        _sphericalPanel.Controls.Add(_longitudeValue);
        sy += LabelHeight;
        _longitudeSlider = MakeSlider(LeftMargin, sy, -180, 180, -100);
        _longitudeSlider.Scroll += (_, _) =>
        {
            _longitudeValue.Text = _longitudeSlider.Value + "\u00b0";
            _locationCombo.SelectedIndex = 0; // Switch to "Custom"
            SchedulePreview();
        };
        _sphericalPanel.Controls.Add(_longitudeSlider);
        sy += SliderRowHeight + RowGap;

        // Latitude
        _sphericalPanel.Controls.Add(MakeLabel("Latitude:", LeftMargin, sy));
        _latitudeValue = MakeLabel("42\u00b0", RightValueX, sy);
        _sphericalPanel.Controls.Add(_latitudeValue);
        sy += LabelHeight;
        _latitudeSlider = MakeSlider(LeftMargin, sy, -60, 60, 42);
        _latitudeSlider.Scroll += (_, _) =>
        {
            _latitudeValue.Text = _latitudeSlider.Value + "\u00b0";
            _locationCombo.SelectedIndex = 0; // Switch to "Custom"
            SchedulePreview();
        };
        _sphericalPanel.Controls.Add(_latitudeSlider);

        tab.Controls.Add(_sphericalPanel);
        y += _sphericalPanel.Height;

        // ── Zoom (combined distance + FoV) ──
        // Slider 1..100, default 100 (extreme close-up). Higher = more zoomed in.
        y = AddSliderRow(tab, "Zoom:", "100", LeftMargin, y, 1, 100, 100, SliderWidth,
            out _zoomSlider, out _zoomValue,
            (s, v) => v.Text = s.Value.ToString());

        // ── Image offset X/Y (Globe + Moon only) ──
        y = AddSliderRow(tab, "Horizontal offset:", "0", LeftMargin, y, -25, 25, 0, SliderWidth,
            out _offsetXSlider, out _offsetXValue,
            (s, v) => v.Text = s.Value.ToString());

        y = AddSliderRow(tab, "Vertical offset:", "-15", LeftMargin, y, -25, 25, -15, SliderWidth,
            out _offsetYSlider, out _offsetYValue,
            (s, v) => v.Text = s.Value.ToString());

        // ── Night lights ──
        _nightLightsCheck = new CheckBox
        {
            Text = "City lights at night",
            AutoSize = true,
            Location = new Point(LeftMargin, y)
        };
        _nightLightsCheck.CheckedChanged += (_, _) =>
        {
            _nightBrightnessSlider.Enabled = _nightLightsCheck.Checked;
            SchedulePreview();
        };
        tab.Controls.Add(_nightLightsCheck);
        y += 24;

        y = AddSliderRow(tab, "Brightness:", "1.7", IndentMargin, y, 1, 30, 17, IndentSliderWidth,
            out _nightBrightnessSlider, out _nightBrightnessValue,
            (s, v) => v.Text = (s.Value / 10f).ToString("F1"));

        // ── Daytime light ──
        y = AddSliderRow(tab, "Daytime light:", "0.15", LeftMargin, y, 0, 50, 15, SliderWidth,
            out _ambientSlider, out _ambientValue,
            (s, v) => v.Text = (s.Value / 100f).ToString("F2"));

        // ── Earth image style ──
        var styleGroup = new GroupBox
        {
            Text = "Earth image style",
            Location = new Point(LeftMargin, y),
            Size = new Size(440, 65)
        };

        _topoRadio = new RadioButton
        {
            Text = "Topographic (land only)",
            AutoSize = true,
            Location = new Point(15, 20)
        };
        _topoRadio.CheckedChanged += (_, _) => SchedulePreview();

        _topoBathyRadio = new RadioButton
        {
            Text = "Topographic + Bathymetry (land + ocean floor)",
            AutoSize = true,
            Location = new Point(15, 42)
        };
        _topoBathyRadio.CheckedChanged += (_, _) => SchedulePreview();

        styleGroup.Controls.AddRange([_topoRadio, _topoBathyRadio]);
        tab.Controls.Add(styleGroup);
        y += 75;

        // ── Reset to defaults ──
        var resetButton = new Button
        {
            Text = "Reset to Defaults",
            Location = new Point(LeftMargin, y),
            Width = 140,
            Height = 30
        };
        resetButton.Click += (_, _) => ResetToDefaults();
        tab.Controls.Add(resetButton);

        return tab;
    }

    private TabPage CreateSystemTab()
    {
        var tab = new TabPage("System");
        int y = 15;

        // Update interval
        tab.Controls.Add(MakeLabel("Update every:", LeftMargin, y + 3));
        _updateIntervalCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(180, y),
            Width = 270
        };
        _updateIntervalCombo.Items.AddRange([
            "1 minute", "2 minutes", "5 minutes", "10 minutes",
            "15 minutes", "30 minutes", "1 hour"
        ]);
        _updateIntervalCombo.SelectedIndexChanged += (_, _) => SchedulePreview();
        tab.Controls.Add(_updateIntervalCombo);
        y += 35;

        // Run on startup
        _runOnStartupCheck = new CheckBox
        {
            Text = "Run Blue Marble Desktop when Windows starts",
            AutoSize = true,
            Location = new Point(LeftMargin, y)
        };
        _runOnStartupCheck.CheckedChanged += (_, _) => SchedulePreview();
        tab.Controls.Add(_runOnStartupCheck);
        y += 35;

        // Multi-monitor
        var monitorGroup = new GroupBox
        {
            Text = "Multi-Display Mode",
            Location = new Point(LeftMargin, y),
            Size = new Size(440, 105)
        };

        _sameForAllRadio = new RadioButton
        {
            Text = "Same wallpaper on all displays",
            AutoSize = true,
            Location = new Point(15, 22)
        };
        _sameForAllRadio.CheckedChanged += (_, _) =>
        {
            UpdatePerDisplayVisibility();
            SchedulePreview();
        };

        _spanAcrossRadio = new RadioButton
        {
            Text = "Span wallpaper across all displays",
            AutoSize = true,
            Location = new Point(15, 45)
        };
        _spanAcrossRadio.CheckedChanged += (_, _) =>
        {
            UpdatePerDisplayVisibility();
            SchedulePreview();
        };

        _perDisplayRadio = new RadioButton
        {
            Text = "Per display (independent settings)",
            AutoSize = true,
            Location = new Point(15, 68)
        };
        _perDisplayRadio.CheckedChanged += (_, _) =>
        {
            UpdatePerDisplayVisibility();
            SchedulePreview();
        };

        monitorGroup.Controls.AddRange([_sameForAllRadio, _spanAcrossRadio, _perDisplayRadio]);
        tab.Controls.Add(monitorGroup);
        y += 115;

        // Per-display panel (hidden by default)
        _perDisplayPanel = new Panel
        {
            Location = new Point(LeftMargin, y),
            Size = new Size(440, 40),
            Visible = false
        };

        _perDisplayPanel.Controls.Add(MakeLabel("Configure:", 0, 5));
        _monitorSelectCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(80, 2),
            Width = 350
        };

        // Populate monitors with real model names
        var screens = MonitorManager.GetAllScreens();
        var friendlyNames = MonitorNameHelper.GetMonitorFriendlyNames();

        for (int i = 0; i < screens.Length; i++)
        {
            var s = screens[i];
            string deviceName = s.DeviceName.TrimEnd('\0');
            string monitorName = friendlyNames.TryGetValue(deviceName, out var name)
                ? name
                : deviceName;
            string primary = s.Primary ? " [Primary]" : "";
            _monitorSelectCombo.Items.Add(
                $"Display {i + 1}: {monitorName} ({s.Bounds.Width}x{s.Bounds.Height}){primary}");
        }
        if (_monitorSelectCombo.Items.Count > 0)
            _monitorSelectCombo.SelectedIndex = 0;

        _monitorSelectCombo.SelectedIndexChanged += (_, _) =>
        {
            if (_monitorSelectCombo.SelectedIndex >= 0 && _monitorSelectCombo.SelectedIndex < screens.Length)
            {
                _selectedMonitorDevice = screens[_monitorSelectCombo.SelectedIndex].DeviceName;
                LoadAppearanceFromDisplayConfig(_selectedMonitorDevice);
            }
        };

        if (screens.Length > 0)
            _selectedMonitorDevice = screens[0].DeviceName;

        _perDisplayPanel.Controls.Add(_monitorSelectCombo);
        tab.Controls.Add(_perDisplayPanel);
        y += 50;

        // Render resolution
        tab.Controls.Add(MakeLabel("Render resolution:", LeftMargin, y + 3));
        _renderResCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(180, y),
            Width = 270
        };
        _renderResCombo.Items.AddRange([
            "Auto (match display)",
            "1920 x 1080",
            "2560 x 1440",
            "3840 x 2160",
            "5120 x 2880"
        ]);
        _renderResCombo.SelectedIndexChanged += (_, _) => SchedulePreview();
        tab.Controls.Add(_renderResCombo);
        y += 40;

        // HD Textures section
        var hdGroup = new GroupBox
        {
            Text = "HD Textures (NASA)",
            Location = new Point(LeftMargin, y),
            Size = new Size(440, 90)
        };

        bool hdAvailable = HiResTextureManager.AreHiResTexturesAvailable();
        var hdStatusLabel = new Label
        {
            Text = hdAvailable
                ? "✓ HD textures installed (21600×10800 day, Black Marble night)"
                : $"Standard textures in use. Download ~{HiResTextureManager.GetEstimatedDownloadSizeMB()} MB for HD quality.",
            AutoSize = true,
            Location = new Point(15, 22),
            Font = new Font("Segoe UI", 8.5f)
        };
        hdGroup.Controls.Add(hdStatusLabel);

        var hdProgressLabel = new Label
        {
            Text = "",
            AutoSize = true,
            Location = new Point(15, 42),
            Font = new Font("Segoe UI", 8.5f),
            Visible = false
        };
        hdGroup.Controls.Add(hdProgressLabel);

        var hdButton = new Button
        {
            Text = hdAvailable ? "Re-download HD Textures" : "Download HD Textures",
            Location = new Point(15, 58),
            Width = 200,
            Height = 26
        };

        var hdManager = new HiResTextureManager();

        hdManager.ProgressChanged += (progress, message) =>
        {
            if (hdProgressLabel.InvokeRequired)
                hdProgressLabel.Invoke(() => { hdProgressLabel.Text = message; hdProgressLabel.Visible = true; });
            else
            {
                hdProgressLabel.Text = message;
                hdProgressLabel.Visible = true;
            }
        };

        hdManager.DownloadCompleted += (success, message) =>
        {
            void UpdateUI()
            {
                hdProgressLabel.Text = message;
                hdButton.Enabled = true;
                hdButton.Text = success ? "Re-download HD Textures" : "Retry Download";
                if (success)
                {
                    hdStatusLabel.Text = "✓ HD textures installed! Restart app or change a setting to use them.";
                    _renderScheduler.TriggerUpdate();
                }
            }

            if (hdButton.InvokeRequired)
                hdButton.Invoke(UpdateUI);
            else
                UpdateUI();
        };

        hdButton.Click += (_, _) =>
        {
            if (hdManager.IsDownloading)
            {
                hdManager.CancelDownload();
                hdButton.Text = "Download HD Textures";
                hdProgressLabel.Visible = false;
            }
            else
            {
                hdManager.StartDownload();
                hdButton.Text = "Cancel Download";
                hdButton.Enabled = true;
            }
        };

        hdGroup.Controls.Add(hdButton);
        tab.Controls.Add(hdGroup);

        return tab;
    }

    private void UpdateModeVisibility()
    {
        bool isFlatMap = _displayModeCombo.SelectedIndex == 1;
        bool isMoon = _displayModeCombo.SelectedIndex == 2;

        // In flat map mode: disable controls that don't apply
        _sphericalPanel.Enabled = !isFlatMap;
        _zoomSlider.Enabled = !isFlatMap;
        _offsetXSlider.Enabled = !isFlatMap;
        _offsetYSlider.Enabled = !isFlatMap;

        // Location presets only apply to Globe view (lon/lat sliders still work for Moon)
        _locationCombo.Enabled = !isFlatMap && !isMoon;
    }

    private void UpdatePerDisplayVisibility()
    {
        _perDisplayPanel.Visible = _perDisplayRadio.Checked;
    }

    private static int CameraToZoomSlider(float distance)
    {
        // Reverse of ZoomSliderToCamera: t = log(distance/6.0) / log(1.15/6.0)
        if (distance <= 1.15f) return 100;
        if (distance >= 6.0f) return 1;
        float t = MathF.Log(distance / 6.0f) / MathF.Log(1.15f / 6.0f);
        return Math.Clamp((int)(t * 99f + 1), 1, 100);
    }

    private void LoadAppearanceFromDisplayConfig(string deviceName)
    {
        var config = _settings.DisplayConfigs.Find(c => c.DeviceName == deviceName);
        if (config == null)
        {
            LoadAppearanceFromGlobalSettings();
            return;
        }

        _isLoading = true;

        _displayModeCombo.SelectedIndex = (int)config.DisplayMode;
        _longitudeSlider.Value = Math.Clamp((int)config.LongitudeOffset, -180, 180);
        _longitudeValue.Text = config.LongitudeOffset.ToString("F0") + "\u00b0";
        _latitudeSlider.Value = Math.Clamp((int)config.CameraTilt, -60, 60);
        _latitudeValue.Text = config.CameraTilt.ToString("F0") + "\u00b0";
        _zoomSlider.Value = CameraToZoomSlider(config.ZoomLevel);
        _zoomValue.Text = _zoomSlider.Value.ToString();
        _offsetXSlider.Value = Math.Clamp((int)config.ImageOffsetX, -25, 25);
        _offsetXValue.Text = config.ImageOffsetX.ToString("F0");
        _offsetYSlider.Value = Math.Clamp((int)config.ImageOffsetY, -25, 25);
        _offsetYValue.Text = config.ImageOffsetY.ToString("F0");
        _nightLightsCheck.Checked = config.NightLightsEnabled;
        _nightBrightnessSlider.Value = Math.Clamp((int)(config.NightLightsBrightness * 10), _nightBrightnessSlider.Minimum, _nightBrightnessSlider.Maximum);
        _nightBrightnessValue.Text = config.NightLightsBrightness.ToString("F1");
        _nightBrightnessSlider.Enabled = config.NightLightsEnabled;
        _ambientSlider.Value = Math.Clamp((int)(config.AmbientLight * 100), _ambientSlider.Minimum, _ambientSlider.Maximum);
        _ambientValue.Text = config.AmbientLight.ToString("F2");
        if (config.ImageStyle == ImageStyle.TopoBathy)
            _topoBathyRadio.Checked = true;
        else
            _topoRadio.Checked = true;

        _locationCombo.SelectedIndex = 0; // Custom
        UpdateModeVisibility();
        _isLoading = false;
    }

    private void ResetToDefaults()
    {
        _isLoading = true;

        // Reset all appearance controls to the app's built-in defaults
        _displayModeCombo.SelectedIndex = (int)DisplayMode.Spherical;
        _longitudeSlider.Value = -100;
        _longitudeValue.Text = "-100\u00b0";
        _latitudeSlider.Value = 42;
        _latitudeValue.Text = "42\u00b0";
        _zoomSlider.Value = 100;
        _zoomValue.Text = "100";
        _offsetXSlider.Value = 0;
        _offsetXValue.Text = "0";
        _offsetYSlider.Value = -15;
        _offsetYValue.Text = "-15";
        _nightLightsCheck.Checked = true;
        _nightBrightnessSlider.Value = 17; // 1.7 * 10
        _nightBrightnessValue.Text = "1.7";
        _nightBrightnessSlider.Enabled = true;
        _ambientSlider.Value = 15; // 0.15 * 100
        _ambientValue.Text = "0.15";
        _topoBathyRadio.Checked = true;
        _locationCombo.SelectedIndex = 0; // Custom

        UpdateModeVisibility();
        _isLoading = false;
        SchedulePreview();
    }

    private void LoadAppearanceFromGlobalSettings()
    {
        _isLoading = true;

        _displayModeCombo.SelectedIndex = (int)_settings.DisplayMode;
        _longitudeSlider.Value = Math.Clamp((int)_settings.LongitudeOffset, -180, 180);
        _longitudeValue.Text = _settings.LongitudeOffset.ToString("F0") + "\u00b0";
        _latitudeSlider.Value = Math.Clamp((int)_settings.CameraTilt, -60, 60);
        _latitudeValue.Text = _settings.CameraTilt.ToString("F0") + "\u00b0";
        _zoomSlider.Value = CameraToZoomSlider(_settings.ZoomLevel);
        _zoomValue.Text = _zoomSlider.Value.ToString();
        _offsetXSlider.Value = Math.Clamp((int)_settings.ImageOffsetX, -25, 25);
        _offsetXValue.Text = _settings.ImageOffsetX.ToString("F0");
        _offsetYSlider.Value = Math.Clamp((int)_settings.ImageOffsetY, -25, 25);
        _offsetYValue.Text = _settings.ImageOffsetY.ToString("F0");
        _nightLightsCheck.Checked = _settings.NightLightsEnabled;
        _nightBrightnessSlider.Value = Math.Clamp((int)(_settings.NightLightsBrightness * 10), _nightBrightnessSlider.Minimum, _nightBrightnessSlider.Maximum);
        _nightBrightnessValue.Text = _settings.NightLightsBrightness.ToString("F1");
        _nightBrightnessSlider.Enabled = _settings.NightLightsEnabled;
        _ambientSlider.Value = Math.Clamp((int)(_settings.AmbientLight * 100), _ambientSlider.Minimum, _ambientSlider.Maximum);
        _ambientValue.Text = _settings.AmbientLight.ToString("F2");
        if (_settings.ImageStyle == ImageStyle.TopoBathy)
            _topoBathyRadio.Checked = true;
        else
            _topoRadio.Checked = true;

        _locationCombo.SelectedIndex = 0; // Custom
        UpdateModeVisibility();
        _isLoading = false;
    }

    private void LoadCurrentSettings()
    {
        LoadAppearanceFromGlobalSettings();

        _isLoading = true;

        _updateIntervalCombo.SelectedIndex = _settings.UpdateIntervalSeconds switch
        {
            60 => 0, 120 => 1, 300 => 2, 600 => 3,
            900 => 4, 1800 => 5, 3600 => 6, _ => 3
        };

        _runOnStartupCheck.Checked = StartupManager.IsRunOnStartup();

        switch (_settings.MultiMonitorMode)
        {
            case MultiMonitorMode.SpanAcross:
                _spanAcrossRadio.Checked = true;
                break;
            case MultiMonitorMode.PerDisplay:
                _perDisplayRadio.Checked = true;
                break;
            default:
                _sameForAllRadio.Checked = true;
                break;
        }

        _renderResCombo.SelectedIndex = (_settings.RenderWidth, _settings.RenderHeight) switch
        {
            (1920, 1080) => 1, (2560, 1440) => 2,
            (3840, 2160) => 3, (5120, 2880) => 4, _ => 0
        };

        UpdatePerDisplayVisibility();
        _isLoading = false;
    }

    private static Label MakeLabel(string text, int x, int y) => new()
    {
        Text = text,
        AutoSize = true,
        Location = new Point(x, y),
        Font = new Font("Segoe UI", 9)
    };

    private static TrackBar MakeSlider(int x, int y, int min, int max, int value, int width = SliderWidth) => new()
    {
        Location = new Point(x, y),
        Width = width,
        Minimum = min,
        Maximum = max,
        Value = Math.Clamp(value, min, max),
        TickStyle = TickStyle.None,
        AutoSize = false,
        Height = 25
    };

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _debounceTimer.Stop();
            _debounceTimer.Dispose();
        }
        base.Dispose(disposing);
    }
}
