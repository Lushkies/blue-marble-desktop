using System.Drawing;

namespace DesktopEarth.UI;

public class SettingsForm : Form
{
    private readonly SettingsManager _settingsManager;
    private readonly RenderScheduler _renderScheduler;
    private AppSettings _settings;

    // Debounce timer for live preview
    private readonly System.Windows.Forms.Timer _debounceTimer;
    private bool _isLoading;

    // Layout constants (compacted to eliminate scrollbar)
    private const int LeftMargin = 20;
    private const int RightValueX = 480;
    private const int SliderWidth = 480;
    private const int IndentMargin = 40;
    private const int IndentSliderWidth = 460;
    private const int LabelHeight = 20;
    private const int SliderRowHeight = 26;
    private const int RowGap = 4;

    // Location presets
    private static readonly (string Name, float Lon, float Lat)[] LocationPresets =
    [
        ("Custom", 0, 0),
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

    // Mode panels
    private Panel _globeControlsPanel = null!;
    private Panel _epicPanel = null!;
    private Panel _apodPanel = null!;
    private Panel _npsPanel = null!;
    private Panel _unsplashPanel = null!;
    private Panel _smithsonianPanel = null!;

    // EPIC controls
    private ComboBox _epicTypeCombo = null!;
    private RadioButton _epicLatestRadio = null!;
    private RadioButton _epicDateRadio = null!;
    private DateTimePicker _epicDatePicker = null!;
    private ThumbnailGridPanel _epicGrid = null!;
    private Label _epicStatusLabel = null!;
    private Button _epicRefreshButton = null!;
    private readonly EpicApiClient _epicApi = new();
    private DateTime _lastEpicRefresh = DateTime.MinValue;

    // APOD controls
    private RadioButton _apodLatestRadio = null!;
    private RadioButton _apodDateRadio = null!;
    private DateTimePicker _apodDatePicker = null!;
    private ThumbnailGridPanel _apodGrid = null!;
    private Label _apodStatusLabel = null!;
    private Button _apodRefreshButton = null!;
    private readonly ApodApiClient _apodApi = new();
    private DateTime _lastApodRefresh = DateTime.MinValue;

    // NPS controls
    private TextBox _npsSearchBox = null!;
    private ThumbnailGridPanel _npsGrid = null!;
    private Label _npsStatusLabel = null!;
    private Button _npsSearchButton = null!;
    private readonly NpsApiClient _npsApi = new();

    // Unsplash controls
    private ComboBox _unsplashTopicCombo = null!;
    private TextBox _unsplashSearchBox = null!;
    private ThumbnailGridPanel _unsplashGrid = null!;
    private Label _unsplashStatusLabel = null!;
    private Label _unsplashAttributionLabel = null!;
    private Button _unsplashSearchButton = null!;
    private readonly UnsplashApiClient _unsplashApi = new();

    // Smithsonian controls
    private TextBox _smithsonianSearchBox = null!;
    private ThumbnailGridPanel _smithsonianGrid = null!;
    private Label _smithsonianStatusLabel = null!;
    private Button _smithsonianSearchButton = null!;
    private readonly SmithsonianApiClient _smithsonianApi = new();

    // Unified image cache for new sources
    private readonly ImageCache _imageCache = new();

    // Random rotation controls
    private CheckBox _randomRotationCheck = null!;
    private CheckBox _randomFavoritesOnlyCheck = null!;

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

    // API Keys tab controls
    private TextBox _nasaApiKeyBox = null!;
    private TextBox _npsApiKeyBox = null!;
    private TextBox _unsplashKeyBox = null!;
    private TextBox _smithsonianKeyBox = null!;

    // Shared Reset button
    private Button _resetButton = null!;

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
            if (_perDisplayRadio.Checked && !string.IsNullOrEmpty(_selectedMonitorDevice))
            {
                var config = GetOrCreateDisplayConfig(_selectedMonitorDevice);
                SaveAppearanceToDisplayConfig(config);
            }
            else
            {
                SaveAppearanceToGlobalSettings(s);
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

            // API keys
            s.NasaApiKey = _nasaApiKeyBox.Text.Trim();
            s.NpsApiKey = _npsApiKeyBox.Text.Trim();
            s.UnsplashAccessKey = _unsplashKeyBox.Text.Trim();
            s.SmithsonianApiKey = _smithsonianKeyBox.Text.Trim();
        });

        StartupManager.SetRunOnStartup(_runOnStartupCheck.Checked);
        _renderScheduler.TriggerUpdate();
    }

    private static (float distance, float fov) ZoomSliderToCamera(int sliderValue)
    {
        float t = (sliderValue - 1) / 99f;
        float distance = 6.0f * MathF.Pow(1.15f / 6.0f, t);
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

        // EPIC
        s.EpicImageType = (EpicImageType)_epicTypeCombo.SelectedIndex;
        s.EpicUseLatest = _epicLatestRadio.Checked;
        s.EpicSelectedDate = _epicDatePicker.Value.ToString("yyyy-MM-dd");
        if (_epicGrid.SelectedImage != null)
            s.EpicSelectedImage = _epicGrid.SelectedImage.Id;

        // APOD
        s.ApodUseLatest = _apodLatestRadio.Checked;
        s.ApodSelectedDate = _apodDatePicker.Value.ToString("yyyy-MM-dd");
        if (_apodGrid.SelectedImage != null)
        {
            s.ApodSelectedImageId = _apodGrid.SelectedImage.Id;
            s.ApodSelectedImageUrl = ApodApiClient.GetBestUrl(_apodGrid.SelectedImage);
        }

        // NPS
        s.NpsSearchQuery = _npsSearchBox.Text;
        if (_npsGrid.SelectedImage != null)
        {
            s.NpsSelectedImageId = _npsGrid.SelectedImage.Id;
            s.NpsSelectedImageUrl = _npsGrid.SelectedImage.HdImageUrl;
        }

        // Unsplash
        s.UnsplashTopic = GetUnsplashTopicSlug();
        s.UnsplashSearchQuery = _unsplashSearchBox.Text;
        if (_unsplashGrid.SelectedImage != null)
        {
            s.UnsplashSelectedImageId = _unsplashGrid.SelectedImage.Id;
            s.UnsplashSelectedImageUrl = _unsplashGrid.SelectedImage.HdImageUrl;
            s.UnsplashPhotographerName = _unsplashGrid.SelectedImage.PhotographerName;
        }

        // Smithsonian
        s.SmithsonianSearchQuery = _smithsonianSearchBox.Text;
        if (_smithsonianGrid.SelectedImage != null)
        {
            s.SmithsonianSelectedId = _smithsonianGrid.SelectedImage.Id;
            s.SmithsonianSelectedImageUrl = _smithsonianGrid.SelectedImage.HdImageUrl;
        }

        // Rotation
        s.RandomRotationEnabled = _randomRotationCheck.Checked;
        s.RandomFromFavoritesOnly = _randomFavoritesOnlyCheck.Checked;
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

        // EPIC
        config.EpicImageType = (EpicImageType)_epicTypeCombo.SelectedIndex;
        config.EpicUseLatest = _epicLatestRadio.Checked;
        config.EpicSelectedDate = _epicDatePicker.Value.ToString("yyyy-MM-dd");
        if (_epicGrid.SelectedImage != null)
            config.EpicSelectedImage = _epicGrid.SelectedImage.Id;

        // APOD
        config.ApodUseLatest = _apodLatestRadio.Checked;
        config.ApodSelectedDate = _apodDatePicker.Value.ToString("yyyy-MM-dd");
        if (_apodGrid.SelectedImage != null)
        {
            config.ApodSelectedImageId = _apodGrid.SelectedImage.Id;
            config.ApodSelectedImageUrl = ApodApiClient.GetBestUrl(_apodGrid.SelectedImage);
        }

        // NPS
        config.NpsSearchQuery = _npsSearchBox.Text;
        if (_npsGrid.SelectedImage != null)
        {
            config.NpsSelectedImageId = _npsGrid.SelectedImage.Id;
            config.NpsSelectedImageUrl = _npsGrid.SelectedImage.HdImageUrl;
        }

        // Unsplash
        config.UnsplashTopic = GetUnsplashTopicSlug();
        config.UnsplashSearchQuery = _unsplashSearchBox.Text;
        if (_unsplashGrid.SelectedImage != null)
        {
            config.UnsplashSelectedImageId = _unsplashGrid.SelectedImage.Id;
            config.UnsplashSelectedImageUrl = _unsplashGrid.SelectedImage.HdImageUrl;
            config.UnsplashPhotographerName = _unsplashGrid.SelectedImage.PhotographerName;
        }

        // Smithsonian
        config.SmithsonianSearchQuery = _smithsonianSearchBox.Text;
        if (_smithsonianGrid.SelectedImage != null)
        {
            config.SmithsonianSelectedId = _smithsonianGrid.SelectedImage.Id;
            config.SmithsonianSelectedImageUrl = _smithsonianGrid.SelectedImage.HdImageUrl;
        }

        // Rotation
        config.RandomRotationEnabled = _randomRotationCheck.Checked;
        config.RandomFromFavoritesOnly = _randomFavoritesOnlyCheck.Checked;
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
        Size = new Size(560, 790);
        ShowInTaskbar = true;

        var tabControl = new TabControl
        {
            Dock = DockStyle.Fill,
            Padding = new Point(8, 4)
        };

        tabControl.TabPages.Add(CreateAppearanceTab());
        tabControl.TabPages.Add(CreateSystemTab());
        tabControl.TabPages.Add(CreateApiKeysTab());

        Controls.Add(tabControl);
    }

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
        int y = 10;

        // ── MULTI-DISPLAY MODE ──
        var monitorGroup = new GroupBox
        {
            Text = "Multi-Display Mode",
            Location = new Point(LeftMargin, y),
            Size = new Size(490, 95)
        };

        _sameForAllRadio = new RadioButton
        {
            Text = "Same wallpaper on all displays",
            AutoSize = true,
            Location = new Point(15, 20)
        };
        _sameForAllRadio.CheckedChanged += (_, _) => { UpdatePerDisplayVisibility(); SchedulePreview(); };

        _spanAcrossRadio = new RadioButton
        {
            Text = "Span wallpaper across all displays",
            AutoSize = true,
            Location = new Point(15, 40)
        };
        _spanAcrossRadio.CheckedChanged += (_, _) => { UpdatePerDisplayVisibility(); SchedulePreview(); };

        _perDisplayRadio = new RadioButton
        {
            Text = "Per display (independent settings)",
            AutoSize = true,
            Location = new Point(15, 60)
        };
        _perDisplayRadio.CheckedChanged += (_, _) => { UpdatePerDisplayVisibility(); SchedulePreview(); };

        monitorGroup.Controls.AddRange([_sameForAllRadio, _spanAcrossRadio, _perDisplayRadio]);
        tab.Controls.Add(monitorGroup);
        y += 100;

        // Per-display panel
        _perDisplayPanel = new Panel
        {
            Location = new Point(LeftMargin, y),
            Size = new Size(490, 30),
            Visible = false
        };

        _perDisplayPanel.Controls.Add(MakeLabel("Configure:", 0, 5));
        _monitorSelectCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(80, 2),
            Width = 400
        };

        var screens = MonitorManager.GetAllScreens();
        var friendlyNames = MonitorNameHelper.GetMonitorFriendlyNames();
        for (int i = 0; i < screens.Length; i++)
        {
            var s = screens[i];
            string deviceName = s.DeviceName.TrimEnd('\0');
            string monitorName = friendlyNames.TryGetValue(deviceName, out var name) ? name : deviceName;
            string primary = s.Primary ? " [Primary]" : "";
            _monitorSelectCombo.Items.Add($"Display {i + 1}: {monitorName} ({s.Bounds.Width}x{s.Bounds.Height}){primary}");
        }
        if (_monitorSelectCombo.Items.Count > 0) _monitorSelectCombo.SelectedIndex = 0;

        _monitorSelectCombo.SelectedIndexChanged += (_, _) =>
        {
            if (_monitorSelectCombo.SelectedIndex >= 0 && _monitorSelectCombo.SelectedIndex < screens.Length)
            {
                _selectedMonitorDevice = screens[_monitorSelectCombo.SelectedIndex].DeviceName;
                LoadAppearanceFromDisplayConfig(_selectedMonitorDevice);
            }
        };
        if (screens.Length > 0) _selectedMonitorDevice = screens[0].DeviceName;

        _perDisplayPanel.Controls.Add(_monitorSelectCombo);
        tab.Controls.Add(_perDisplayPanel);
        y += 35;

        // ── VIEW MODE ──
        tab.Controls.Add(MakeLabel("View:", LeftMargin, y + 3));
        _displayModeCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(100, y),
            Width = 170
        };
        _displayModeCombo.Items.AddRange([
            "Globe", "Flat Map", "Moon", "NASA EPIC",
            "NASA APOD", "National Parks", "Unsplash", "Smithsonian"
        ]);
        _displayModeCombo.SelectedIndexChanged += (_, _) => { UpdateModeVisibility(); SchedulePreview(); };
        tab.Controls.Add(_displayModeCombo);
        y += 30;

        // ── RANDOM ROTATION (visible for image sources only) ──
        _randomRotationCheck = new CheckBox
        {
            Text = "Rotate randomly each update",
            AutoSize = true,
            Location = new Point(LeftMargin, y),
            Visible = false
        };
        _randomRotationCheck.CheckedChanged += (_, _) =>
        {
            _randomFavoritesOnlyCheck.Visible = _randomRotationCheck.Visible && _randomRotationCheck.Checked;
            SchedulePreview();
        };
        tab.Controls.Add(_randomRotationCheck);

        _randomFavoritesOnlyCheck = new CheckBox
        {
            Text = "Favorites only",
            AutoSize = true,
            Location = new Point(250, y),
            Visible = false
        };
        _randomFavoritesOnlyCheck.CheckedChanged += (_, _) => SchedulePreview();
        tab.Controls.Add(_randomFavoritesOnlyCheck);
        y += 25;

        // ── MODE PANELS (all start at same Y, only one visible at a time) ──
        int panelY = y;

        _globeControlsPanel = CreateGlobePanel(panelY);
        tab.Controls.Add(_globeControlsPanel);

        _epicPanel = CreateEpicPanel(panelY);
        tab.Controls.Add(_epicPanel);

        _apodPanel = CreateApodPanel(panelY);
        tab.Controls.Add(_apodPanel);

        _npsPanel = CreateNpsPanel(panelY);
        tab.Controls.Add(_npsPanel);

        _unsplashPanel = CreateUnsplashPanel(panelY);
        tab.Controls.Add(_unsplashPanel);

        _smithsonianPanel = CreateSmithsonianPanel(panelY);
        tab.Controls.Add(_smithsonianPanel);

        // ── SHARED RESET BUTTON ──
        _resetButton = new Button
        {
            Text = "Reset to Defaults",
            Location = new Point(LeftMargin, panelY + 430),
            Width = 140,
            Height = 28
        };
        _resetButton.Click += (_, _) => ResetToDefaults();
        tab.Controls.Add(_resetButton);

        return tab;
    }

    private Panel CreateGlobePanel(int y)
    {
        var panel = new Panel { Location = new Point(0, y), Size = new Size(530, 420) };
        int gy = 0;

        // Location
        panel.Controls.Add(MakeLabel("Location:", LeftMargin, gy + 3));
        _locationCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(100, gy),
            Width = 150
        };
        foreach (var preset in LocationPresets) _locationCombo.Items.Add(preset.Name);
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
        panel.Controls.Add(_locationCombo);
        gy += 28;

        // Spherical panel (lon + lat)
        _sphericalPanel = new Panel
        {
            Location = new Point(0, gy),
            Size = new Size(530, (LabelHeight + SliderRowHeight + RowGap) * 2),
        };
        int sy = 0;

        _sphericalPanel.Controls.Add(MakeLabel("Longitude:", LeftMargin, sy));
        _longitudeValue = MakeLabel("-100\u00b0", RightValueX, sy);
        _sphericalPanel.Controls.Add(_longitudeValue);
        sy += LabelHeight;
        _longitudeSlider = MakeSlider(LeftMargin, sy, -180, 180, -100);
        _longitudeSlider.Scroll += (_, _) =>
        {
            _longitudeValue.Text = _longitudeSlider.Value + "\u00b0";
            _locationCombo.SelectedIndex = 0;
            SchedulePreview();
        };
        _sphericalPanel.Controls.Add(_longitudeSlider);
        sy += SliderRowHeight + RowGap;

        _sphericalPanel.Controls.Add(MakeLabel("Latitude:", LeftMargin, sy));
        _latitudeValue = MakeLabel("42\u00b0", RightValueX, sy);
        _sphericalPanel.Controls.Add(_latitudeValue);
        sy += LabelHeight;
        _latitudeSlider = MakeSlider(LeftMargin, sy, -60, 60, 42);
        _latitudeSlider.Scroll += (_, _) =>
        {
            _latitudeValue.Text = _latitudeSlider.Value + "\u00b0";
            _locationCombo.SelectedIndex = 0;
            SchedulePreview();
        };
        _sphericalPanel.Controls.Add(_latitudeSlider);

        panel.Controls.Add(_sphericalPanel);
        gy += _sphericalPanel.Height;

        // Zoom
        gy = AddSliderRow(panel, "Zoom:", "100", LeftMargin, gy, 1, 100, 100, SliderWidth,
            out _zoomSlider, out _zoomValue, (s, v) => v.Text = s.Value.ToString());

        // Offsets
        gy = AddSliderRow(panel, "Horizontal offset:", "0", LeftMargin, gy, -25, 25, 0, SliderWidth,
            out _offsetXSlider, out _offsetXValue, (s, v) => v.Text = s.Value.ToString());

        gy = AddSliderRow(panel, "Vertical offset:", "-15", LeftMargin, gy, -25, 25, -15, SliderWidth,
            out _offsetYSlider, out _offsetYValue, (s, v) => v.Text = s.Value.ToString());

        // Night lights
        _nightLightsCheck = new CheckBox
        {
            Text = "City lights at night",
            AutoSize = true,
            Location = new Point(LeftMargin, gy)
        };
        _nightLightsCheck.CheckedChanged += (_, _) =>
        {
            _nightBrightnessSlider.Enabled = _nightLightsCheck.Checked;
            SchedulePreview();
        };
        panel.Controls.Add(_nightLightsCheck);
        gy += 22;

        gy = AddSliderRow(panel, "Brightness:", "1.7", IndentMargin, gy, 1, 30, 17, IndentSliderWidth,
            out _nightBrightnessSlider, out _nightBrightnessValue,
            (s, v) => v.Text = (s.Value / 10f).ToString("F1"));

        // Daytime light
        gy = AddSliderRow(panel, "Daytime light:", "0.15", LeftMargin, gy, 0, 50, 15, SliderWidth,
            out _ambientSlider, out _ambientValue,
            (s, v) => v.Text = (s.Value / 100f).ToString("F2"));

        // Earth image style (inline, no GroupBox)
        panel.Controls.Add(MakeLabel("Earth image style:", LeftMargin, gy + 2));
        _topoRadio = new RadioButton
        {
            Text = "Topo",
            AutoSize = true,
            Location = new Point(160, gy)
        };
        _topoRadio.CheckedChanged += (_, _) => SchedulePreview();

        _topoBathyRadio = new RadioButton
        {
            Text = "Topo + Bathymetry",
            AutoSize = true,
            Location = new Point(230, gy)
        };
        _topoBathyRadio.CheckedChanged += (_, _) => SchedulePreview();

        panel.Controls.AddRange([_topoRadio, _topoBathyRadio]);
        gy += 28;

        panel.Size = new Size(530, gy);
        return panel;
    }

    private Panel CreateEpicPanel(int y)
    {
        var panel = new Panel { Location = new Point(0, y), Size = new Size(530, 420), Visible = false };
        int ey = 0;

        panel.Controls.Add(MakeLabel("Image type:", LeftMargin, ey + 3));
        _epicTypeCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(120, ey),
            Width = 150
        };
        _epicTypeCombo.Items.AddRange(["Natural Color", "Enhanced Color"]);
        _epicTypeCombo.SelectedIndex = 0;
        _epicTypeCombo.SelectedIndexChanged += (_, _) =>
        {
            if (!_isLoading) RefreshEpicImages();
            SchedulePreview();
        };
        panel.Controls.Add(_epicTypeCombo);
        ey += 30;

        _epicLatestRadio = new RadioButton
        {
            Text = "Show latest image", AutoSize = true,
            Location = new Point(LeftMargin, ey), Checked = true
        };
        _epicLatestRadio.CheckedChanged += (_, _) =>
        {
            _epicDatePicker.Enabled = !_epicLatestRadio.Checked;
            if (!_isLoading && _epicLatestRadio.Checked) RefreshEpicImages();
            SchedulePreview();
        };
        panel.Controls.Add(_epicLatestRadio);
        ey += 22;

        _epicDateRadio = new RadioButton
        {
            Text = "Choose date:", AutoSize = true,
            Location = new Point(LeftMargin, ey)
        };
        _epicDateRadio.CheckedChanged += (_, _) =>
        {
            _epicDatePicker.Enabled = _epicDateRadio.Checked;
            if (!_isLoading && _epicDateRadio.Checked) RefreshEpicImages();
            SchedulePreview();
        };
        panel.Controls.Add(_epicDateRadio);

        _epicDatePicker = new DateTimePicker
        {
            Location = new Point(140, ey - 2), Width = 170,
            Format = DateTimePickerFormat.Short,
            Value = DateTime.Now.AddDays(-1),
            MaxDate = DateTime.Now,
            MinDate = new DateTime(2015, 6, 13),
            Enabled = false
        };
        _epicDatePicker.ValueChanged += (_, _) =>
        {
            if (!_isLoading && _epicDateRadio.Checked) RefreshEpicImages();
            SchedulePreview();
        };
        panel.Controls.Add(_epicDatePicker);
        ey += 30;

        // Thumbnail grid instead of ListBox
        _epicGrid = new ThumbnailGridPanel(_imageCache)
        {
            Location = new Point(LeftMargin, ey),
            Size = new Size(490, 250)
        };
        _epicGrid.ImageSelected += (_, img) =>
        {
            if (!_isLoading) SchedulePreview();
        };
        _epicGrid.FavoriteToggled += (_, img) => ToggleFavorite(img);
        panel.Controls.Add(_epicGrid);
        ey += 255;

        _epicStatusLabel = new Label
        {
            Text = "Select NASA EPIC view to load satellite images.",
            Location = new Point(LeftMargin, ey),
            Size = new Size(490, 20),
            Font = new Font("Segoe UI", 8.5f),
            ForeColor = Color.Gray
        };
        panel.Controls.Add(_epicStatusLabel);
        ey += 22;

        _epicRefreshButton = new Button
        {
            Text = "Refresh", Location = new Point(LeftMargin, ey), Width = 100, Height = 28
        };
        _epicRefreshButton.Click += (_, _) =>
        {
            if ((DateTime.Now - _lastEpicRefresh).TotalSeconds < 5)
            {
                _epicStatusLabel.Text = "Please wait a few seconds before refreshing.";
                _epicStatusLabel.ForeColor = Color.Gray;
                return;
            }
            RefreshEpicImages();
        };
        panel.Controls.Add(_epicRefreshButton);

        return panel;
    }

    private Panel CreateApodPanel(int y)
    {
        var panel = new Panel { Location = new Point(0, y), Size = new Size(530, 420), Visible = false };
        int py = 0;

        _apodLatestRadio = new RadioButton
        {
            Text = "Show latest week", AutoSize = true,
            Location = new Point(LeftMargin, py), Checked = true
        };
        _apodLatestRadio.CheckedChanged += (_, _) =>
        {
            _apodDatePicker.Enabled = !_apodLatestRadio.Checked;
            if (!_isLoading && _apodLatestRadio.Checked) RefreshApodImages();
            SchedulePreview();
        };
        panel.Controls.Add(_apodLatestRadio);
        py += 22;

        _apodDateRadio = new RadioButton
        {
            Text = "Choose date:", AutoSize = true,
            Location = new Point(LeftMargin, py)
        };
        _apodDateRadio.CheckedChanged += (_, _) =>
        {
            _apodDatePicker.Enabled = _apodDateRadio.Checked;
            if (!_isLoading && _apodDateRadio.Checked) RefreshApodImages();
            SchedulePreview();
        };
        panel.Controls.Add(_apodDateRadio);

        _apodDatePicker = new DateTimePicker
        {
            Location = new Point(140, py - 2), Width = 170,
            Format = DateTimePickerFormat.Short,
            Value = DateTime.Now.AddDays(-1),
            MaxDate = DateTime.Now,
            MinDate = new DateTime(1995, 6, 16),
            Enabled = false
        };
        _apodDatePicker.ValueChanged += (_, _) =>
        {
            if (!_isLoading && _apodDateRadio.Checked) RefreshApodImages();
            SchedulePreview();
        };
        panel.Controls.Add(_apodDatePicker);
        py += 30;

        _apodGrid = new ThumbnailGridPanel(_imageCache)
        {
            Location = new Point(LeftMargin, py),
            Size = new Size(490, 280)
        };
        _apodGrid.ImageSelected += (_, img) => { if (!_isLoading) SchedulePreview(); };
        _apodGrid.FavoriteToggled += (_, img) => ToggleFavorite(img);
        panel.Controls.Add(_apodGrid);
        py += 285;

        _apodStatusLabel = new Label
        {
            Text = "Select NASA APOD view to load images.",
            Location = new Point(LeftMargin, py),
            Size = new Size(490, 20),
            Font = new Font("Segoe UI", 8.5f),
            ForeColor = Color.Gray
        };
        panel.Controls.Add(_apodStatusLabel);
        py += 22;

        _apodRefreshButton = new Button
        {
            Text = "Refresh", Location = new Point(LeftMargin, py), Width = 100, Height = 28
        };
        _apodRefreshButton.Click += (_, _) =>
        {
            if ((DateTime.Now - _lastApodRefresh).TotalSeconds < 5) return;
            RefreshApodImages();
        };
        panel.Controls.Add(_apodRefreshButton);

        return panel;
    }

    private Panel CreateNpsPanel(int y)
    {
        var panel = new Panel { Location = new Point(0, y), Size = new Size(530, 420), Visible = false };
        int py = 0;

        panel.Controls.Add(MakeLabel("Search parks:", LeftMargin, py + 3));
        _npsSearchBox = new TextBox
        {
            Location = new Point(120, py), Width = 280,
            Text = "Yellowstone"
        };
        _npsSearchBox.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) RefreshNpsImages(); };
        panel.Controls.Add(_npsSearchBox);

        _npsSearchButton = new Button
        {
            Text = "Search", Location = new Point(410, py), Width = 80, Height = 24
        };
        _npsSearchButton.Click += (_, _) => RefreshNpsImages();
        panel.Controls.Add(_npsSearchButton);
        py += 30;

        _npsGrid = new ThumbnailGridPanel(_imageCache)
        {
            Location = new Point(LeftMargin, py),
            Size = new Size(490, 300)
        };
        _npsGrid.ImageSelected += (_, img) => { if (!_isLoading) SchedulePreview(); };
        _npsGrid.FavoriteToggled += (_, img) => ToggleFavorite(img);
        panel.Controls.Add(_npsGrid);
        py += 305;

        _npsStatusLabel = new Label
        {
            Text = "Enter a park name and click Search. Requires NPS API key (see API Keys tab).",
            Location = new Point(LeftMargin, py),
            Size = new Size(490, 35),
            Font = new Font("Segoe UI", 8.5f),
            ForeColor = Color.Gray
        };
        panel.Controls.Add(_npsStatusLabel);

        return panel;
    }

    private Panel CreateUnsplashPanel(int y)
    {
        var panel = new Panel { Location = new Point(0, y), Size = new Size(530, 420), Visible = false };
        int py = 0;

        panel.Controls.Add(MakeLabel("Topic:", LeftMargin, py + 3));
        _unsplashTopicCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(80, py), Width = 130
        };
        _unsplashTopicCombo.Items.AddRange(["Nature", "Wallpapers", "Travel", "Animals", "Architecture"]);
        _unsplashTopicCombo.SelectedIndex = 0;
        _unsplashTopicCombo.SelectedIndexChanged += (_, _) =>
        {
            if (!_isLoading) RefreshUnsplashImages();
        };
        panel.Controls.Add(_unsplashTopicCombo);

        panel.Controls.Add(MakeLabel("or search:", 225, py + 3));
        _unsplashSearchBox = new TextBox
        {
            Location = new Point(295, py), Width = 135
        };
        _unsplashSearchBox.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) RefreshUnsplashImages(); };
        panel.Controls.Add(_unsplashSearchBox);

        _unsplashSearchButton = new Button
        {
            Text = "Go", Location = new Point(440, py), Width = 50, Height = 24
        };
        _unsplashSearchButton.Click += (_, _) => RefreshUnsplashImages();
        panel.Controls.Add(_unsplashSearchButton);
        py += 30;

        _unsplashGrid = new ThumbnailGridPanel(_imageCache)
        {
            Location = new Point(LeftMargin, py),
            Size = new Size(490, 280)
        };
        _unsplashGrid.ImageSelected += (_, img) =>
        {
            if (!_isLoading)
            {
                _unsplashAttributionLabel.Text = img.SourceAttribution;
                SchedulePreview();
            }
        };
        _unsplashGrid.FavoriteToggled += (_, img) => ToggleFavorite(img);
        panel.Controls.Add(_unsplashGrid);
        py += 285;

        _unsplashAttributionLabel = new Label
        {
            Text = "",
            Location = new Point(LeftMargin, py),
            Size = new Size(490, 18),
            Font = new Font("Segoe UI", 8f, FontStyle.Italic),
            ForeColor = Color.FromArgb(80, 80, 80)
        };
        panel.Controls.Add(_unsplashAttributionLabel);
        py += 20;

        _unsplashStatusLabel = new Label
        {
            Text = "Requires Unsplash API key (see API Keys tab).",
            Location = new Point(LeftMargin, py),
            Size = new Size(490, 20),
            Font = new Font("Segoe UI", 8.5f),
            ForeColor = Color.Gray
        };
        panel.Controls.Add(_unsplashStatusLabel);

        return panel;
    }

    private Panel CreateSmithsonianPanel(int y)
    {
        var panel = new Panel { Location = new Point(0, y), Size = new Size(530, 420), Visible = false };
        int py = 0;

        panel.Controls.Add(MakeLabel("Search:", LeftMargin, py + 3));
        _smithsonianSearchBox = new TextBox
        {
            Location = new Point(80, py), Width = 320,
            Text = "nature"
        };
        _smithsonianSearchBox.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) RefreshSmithsonianImages(); };
        panel.Controls.Add(_smithsonianSearchBox);

        _smithsonianSearchButton = new Button
        {
            Text = "Search", Location = new Point(410, py), Width = 80, Height = 24
        };
        _smithsonianSearchButton.Click += (_, _) => RefreshSmithsonianImages();
        panel.Controls.Add(_smithsonianSearchButton);
        py += 30;

        _smithsonianGrid = new ThumbnailGridPanel(_imageCache)
        {
            Location = new Point(LeftMargin, py),
            Size = new Size(490, 300)
        };
        _smithsonianGrid.ImageSelected += (_, img) => { if (!_isLoading) SchedulePreview(); };
        _smithsonianGrid.FavoriteToggled += (_, img) => ToggleFavorite(img);
        panel.Controls.Add(_smithsonianGrid);
        py += 305;

        _smithsonianStatusLabel = new Label
        {
            Text = "Requires Smithsonian API key (see API Keys tab).",
            Location = new Point(LeftMargin, py),
            Size = new Size(490, 35),
            Font = new Font("Segoe UI", 8.5f),
            ForeColor = Color.Gray
        };
        panel.Controls.Add(_smithsonianStatusLabel);

        return panel;
    }

    private TabPage CreateSystemTab()
    {
        var tab = new TabPage("System");
        int y = 15;

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

        _runOnStartupCheck = new CheckBox
        {
            Text = "Run Blue Marble Desktop when Windows starts",
            AutoSize = true,
            Location = new Point(LeftMargin, y)
        };
        _runOnStartupCheck.CheckedChanged += (_, _) => SchedulePreview();
        tab.Controls.Add(_runOnStartupCheck);
        y += 35;

        tab.Controls.Add(MakeLabel("Render resolution:", LeftMargin, y + 3));
        _renderResCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(180, y),
            Width = 270
        };
        _renderResCombo.Items.AddRange([
            "Auto (match display)", "1920 x 1080", "2560 x 1440", "3840 x 2160", "5120 x 2880"
        ]);
        _renderResCombo.SelectedIndexChanged += (_, _) => SchedulePreview();
        tab.Controls.Add(_renderResCombo);
        y += 40;

        // HD Textures section
        var hdGroup = new GroupBox
        {
            Text = "HD Textures (NASA)",
            Location = new Point(LeftMargin, y),
            Size = new Size(490, 90)
        };

        bool hdAvailable = HiResTextureManager.AreHiResTexturesAvailable();
        var hdStatusLabel = new Label
        {
            Text = hdAvailable
                ? "\u2713 HD textures installed (21600\u00d710800 day, Black Marble night)"
                : $"Standard textures in use. Download ~{HiResTextureManager.GetEstimatedDownloadSizeMB()} MB for HD quality.",
            AutoSize = true,
            Location = new Point(15, 22),
            Font = new Font("Segoe UI", 8.5f)
        };
        hdGroup.Controls.Add(hdStatusLabel);

        var hdProgressLabel = new Label
        {
            Text = "", AutoSize = true, Location = new Point(15, 42),
            Font = new Font("Segoe UI", 8.5f), Visible = false
        };
        hdGroup.Controls.Add(hdProgressLabel);

        var hdButton = new Button
        {
            Text = hdAvailable ? "Re-download HD Textures" : "Download HD Textures",
            Location = new Point(15, 58), Width = 200, Height = 26
        };

        var hdManager = new HiResTextureManager();
        hdManager.ProgressChanged += (progress, message) =>
        {
            if (hdProgressLabel.InvokeRequired)
                hdProgressLabel.Invoke(() => { hdProgressLabel.Text = message; hdProgressLabel.Visible = true; });
            else { hdProgressLabel.Text = message; hdProgressLabel.Visible = true; }
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
                    hdStatusLabel.Text = "\u2713 HD textures installed! Restart app or change a setting to use them.";
                    _renderScheduler.TriggerUpdate();
                }
            }
            if (hdButton.InvokeRequired) hdButton.Invoke(UpdateUI); else UpdateUI();
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

    private TabPage CreateApiKeysTab()
    {
        var tab = new TabPage("API Keys");
        int y = 15;

        var infoLabel = new Label
        {
            Text = "Enter API keys for image sources. All keys are free to obtain. Leave blank to disable a source.",
            Location = new Point(LeftMargin, y),
            Size = new Size(490, 35),
            Font = new Font("Segoe UI", 9f)
        };
        tab.Controls.Add(infoLabel);
        y += 40;

        // NASA API Key
        tab.Controls.Add(MakeLabel("NASA API Key:", LeftMargin, y + 3));
        _nasaApiKeyBox = new TextBox { Location = new Point(170, y), Width = 310 };
        tab.Controls.Add(_nasaApiKeyBox);
        y += 26;
        var nasaNote = new Label
        {
            Text = "Used for APOD. Default: DEMO_KEY (50 req/day). Get key: api.nasa.gov",
            Location = new Point(170, y),
            AutoSize = true,
            Font = new Font("Segoe UI", 7.5f),
            ForeColor = Color.Gray
        };
        tab.Controls.Add(nasaNote);
        y += 30;

        // NPS API Key
        tab.Controls.Add(MakeLabel("NPS API Key:", LeftMargin, y + 3));
        _npsApiKeyBox = new TextBox { Location = new Point(170, y), Width = 310 };
        tab.Controls.Add(_npsApiKeyBox);
        y += 26;
        var npsNote = new Label
        {
            Text = "Required. Register free at: developer.nps.gov",
            Location = new Point(170, y),
            AutoSize = true,
            Font = new Font("Segoe UI", 7.5f),
            ForeColor = Color.Gray
        };
        tab.Controls.Add(npsNote);
        y += 30;

        // Unsplash Access Key
        tab.Controls.Add(MakeLabel("Unsplash Key:", LeftMargin, y + 3));
        _unsplashKeyBox = new TextBox { Location = new Point(170, y), Width = 310 };
        tab.Controls.Add(_unsplashKeyBox);
        y += 26;
        var unsplashNote = new Label
        {
            Text = "Required. Register free at: unsplash.com/developers",
            Location = new Point(170, y),
            AutoSize = true,
            Font = new Font("Segoe UI", 7.5f),
            ForeColor = Color.Gray
        };
        tab.Controls.Add(unsplashNote);
        y += 30;

        // Smithsonian API Key
        tab.Controls.Add(MakeLabel("Smithsonian Key:", LeftMargin, y + 3));
        _smithsonianKeyBox = new TextBox { Location = new Point(170, y), Width = 310 };
        tab.Controls.Add(_smithsonianKeyBox);
        y += 26;
        var smithNote = new Label
        {
            Text = "Required. Register free at: api.data.gov",
            Location = new Point(170, y),
            AutoSize = true,
            Font = new Font("Segoe UI", 7.5f),
            ForeColor = Color.Gray
        };
        tab.Controls.Add(smithNote);

        return tab;
    }

    // ── MODE VISIBILITY ──

    private void UpdateModeVisibility()
    {
        int mode = _displayModeCombo.SelectedIndex;

        _globeControlsPanel.Visible = mode <= 2;
        _epicPanel.Visible = mode == 3;
        _apodPanel.Visible = mode == 4;
        _npsPanel.Visible = mode == 5;
        _unsplashPanel.Visible = mode == 6;
        _smithsonianPanel.Visible = mode == 7;

        // Random rotation visible for image sources (3+)
        _randomRotationCheck.Visible = mode >= 3;
        _randomFavoritesOnlyCheck.Visible = mode >= 3 && _randomRotationCheck.Checked;

        // Globe-specific logic
        if (mode <= 2)
        {
            bool isFlatMap = mode == 1;
            bool isMoon = mode == 2;
            _sphericalPanel.Enabled = !isFlatMap;
            _zoomSlider.Enabled = !isFlatMap;
            _offsetXSlider.Enabled = !isFlatMap;
            _offsetYSlider.Enabled = !isFlatMap;
            _locationCombo.Enabled = !isFlatMap && !isMoon;
        }

        // Auto-load content on first switch
        if (mode == 3 && _epicGrid.SelectedImage == null) RefreshEpicImages();
        if (mode == 4 && _apodGrid.SelectedImage == null) RefreshApodImages();
    }

    private void UpdatePerDisplayVisibility()
    {
        _perDisplayPanel.Visible = _perDisplayRadio.Checked;
    }

    // ── REFRESH METHODS ──

    private void RefreshEpicImages()
    {
        _lastEpicRefresh = DateTime.Now;
        _epicStatusLabel.Text = "Loading images from NASA EPIC...";
        _epicStatusLabel.ForeColor = Color.Gray;
        _epicRefreshButton.Enabled = false;

        var imageType = (EpicImageType)_epicTypeCombo.SelectedIndex;
        bool useLatest = _epicLatestRadio.Checked;
        string date = _epicDatePicker.Value.ToString("yyyy-MM-dd");

        Task.Run(async () =>
        {
            List<EpicImageInfo>? images;
            if (useLatest)
                images = await _epicApi.GetLatestImagesAsync(imageType);
            else
                images = await _epicApi.GetImagesByDateAsync(imageType, date);

            if (IsDisposed) return;

            try
            {
                Invoke(() =>
                {
                    _epicRefreshButton.Enabled = true;

                    if (images == null || images.Count == 0)
                    {
                        _epicStatusLabel.Text = images == null
                            ? "Could not connect to NASA EPIC. Check your internet connection."
                            : $"No images available for {(useLatest ? "the latest date" : date)}.";
                        _epicStatusLabel.ForeColor = Color.FromArgb(180, 80, 80);
                        _epicGrid.SetImages(new List<ImageSourceInfo>());
                        return;
                    }

                    // Map EPIC images to ImageSourceInfo for the thumbnail grid
                    var collection = imageType == EpicImageType.Enhanced ? "enhanced" : "natural";
                    var sourceInfos = images.Select(img =>
                    {
                        DateTime.TryParse(img.Date, out var dt);
                        var yyyy = dt.Year.ToString("D4");
                        var mm = dt.Month.ToString("D2");
                        var dd = dt.Day.ToString("D2");

                        return new ImageSourceInfo
                        {
                            Source = DisplayMode.NasaEpic,
                            Id = img.Image,
                            Title = $"{dt:HH:mm UTC} - {dt:MMM dd}",
                            Date = img.Date,
                            ThumbnailUrl = $"https://epic.gsfc.nasa.gov/archive/{collection}/{yyyy}/{mm}/{dd}/thumbs/{img.Image}.jpg",
                            FullImageUrl = $"https://epic.gsfc.nasa.gov/archive/{collection}/{yyyy}/{mm}/{dd}/jpg/{img.Image}.jpg",
                            HdImageUrl = $"https://epic.gsfc.nasa.gov/archive/{collection}/{yyyy}/{mm}/{dd}/png/{img.Image}.png",
                            SourceAttribution = "NASA EPIC / DSCOVR",
                            IsFavorited = _settings.Favorites.Any(f => f.Source == DisplayMode.NasaEpic && f.ImageId == img.Image)
                        };
                    }).ToList();

                    _epicGrid.SetImages(sourceInfos);
                    _epicStatusLabel.Text = $"{sourceInfos.Count} image{(sourceInfos.Count != 1 ? "s" : "")} available.";
                    _epicStatusLabel.ForeColor = Color.FromArgb(60, 130, 60);
                });
            }
            catch (ObjectDisposedException) { }
        });
    }

    private void RefreshApodImages()
    {
        _lastApodRefresh = DateTime.Now;
        _apodStatusLabel.Text = "Loading from NASA APOD...";
        _apodStatusLabel.ForeColor = Color.Gray;
        _apodRefreshButton.Enabled = false;

        var apiKey = _settings.NasaApiKey;
        bool useLatest = _apodLatestRadio.Checked;
        string date = _apodDatePicker.Value.ToString("yyyy-MM-dd");

        Task.Run(async () =>
        {
            List<ImageSourceInfo>? images;
            if (useLatest)
                images = await _apodApi.GetRecentAsync(apiKey, 7);
            else
            {
                var single = await _apodApi.GetByDateAsync(apiKey, date);
                images = single != null ? new List<ImageSourceInfo> { single } : null;
            }

            if (IsDisposed) return;

            try
            {
                Invoke(() =>
                {
                    _apodRefreshButton.Enabled = true;

                    if (images == null || images.Count == 0)
                    {
                        _apodStatusLabel.Text = images == null
                            ? "Could not connect to NASA APOD. Check API key and connection."
                            : "No images available.";
                        _apodStatusLabel.ForeColor = Color.FromArgb(180, 80, 80);
                        _apodGrid.SetImages(new List<ImageSourceInfo>());
                        return;
                    }

                    // Mark favorites
                    foreach (var img in images)
                        img.IsFavorited = _settings.Favorites.Any(f => f.Source == DisplayMode.NasaApod && f.ImageId == img.Id);

                    _apodGrid.SetImages(images);
                    _apodStatusLabel.Text = $"{images.Count} image{(images.Count != 1 ? "s" : "")} loaded.";
                    _apodStatusLabel.ForeColor = Color.FromArgb(60, 130, 60);
                });
            }
            catch (ObjectDisposedException) { }
        });
    }

    private void RefreshNpsImages()
    {
        var query = _npsSearchBox.Text.Trim();
        if (string.IsNullOrEmpty(query)) return;

        var apiKey = _settings.NpsApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _npsStatusLabel.Text = "NPS API key required. Add it in the API Keys tab.";
            _npsStatusLabel.ForeColor = Color.FromArgb(180, 80, 80);
            return;
        }

        _npsStatusLabel.Text = $"Searching parks for \"{query}\"...";
        _npsStatusLabel.ForeColor = Color.Gray;
        _npsSearchButton.Enabled = false;

        Task.Run(async () =>
        {
            var images = await _npsApi.SearchParksAsync(apiKey, query);

            if (IsDisposed) return;
            try
            {
                Invoke(() =>
                {
                    _npsSearchButton.Enabled = true;
                    if (images == null || images.Count == 0)
                    {
                        _npsStatusLabel.Text = images == null
                            ? "Could not connect to NPS. Check API key and connection."
                            : "No park images found. Try a different search.";
                        _npsStatusLabel.ForeColor = Color.FromArgb(180, 80, 80);
                        _npsGrid.SetImages(new List<ImageSourceInfo>());
                        return;
                    }

                    foreach (var img in images)
                        img.IsFavorited = _settings.Favorites.Any(f => f.Source == DisplayMode.NationalParks && f.ImageId == img.Id);

                    _npsGrid.SetImages(images);
                    _npsStatusLabel.Text = $"{images.Count} image{(images.Count != 1 ? "s" : "")} found.";
                    _npsStatusLabel.ForeColor = Color.FromArgb(60, 130, 60);
                });
            }
            catch (ObjectDisposedException) { }
        });
    }

    private void RefreshUnsplashImages()
    {
        var accessKey = _settings.UnsplashAccessKey;
        if (string.IsNullOrWhiteSpace(accessKey))
        {
            _unsplashStatusLabel.Text = "Unsplash access key required. Add it in the API Keys tab.";
            _unsplashStatusLabel.ForeColor = Color.FromArgb(180, 80, 80);
            return;
        }

        var searchQuery = _unsplashSearchBox.Text.Trim();
        var topicSlug = GetUnsplashTopicSlug();

        _unsplashStatusLabel.Text = "Loading Unsplash photos...";
        _unsplashStatusLabel.ForeColor = Color.Gray;

        Task.Run(async () =>
        {
            List<ImageSourceInfo>? images;
            if (!string.IsNullOrEmpty(searchQuery))
                images = await _unsplashApi.SearchPhotosAsync(accessKey, searchQuery);
            else
                images = await _unsplashApi.GetTopicPhotosAsync(accessKey, topicSlug);

            if (IsDisposed) return;
            try
            {
                Invoke(() =>
                {
                    if (images == null || images.Count == 0)
                    {
                        _unsplashStatusLabel.Text = images == null
                            ? "Could not connect to Unsplash. Check access key."
                            : "No photos found.";
                        _unsplashStatusLabel.ForeColor = Color.FromArgb(180, 80, 80);
                        _unsplashGrid.SetImages(new List<ImageSourceInfo>());
                        return;
                    }

                    foreach (var img in images)
                        img.IsFavorited = _settings.Favorites.Any(f => f.Source == DisplayMode.Unsplash && f.ImageId == img.Id);

                    _unsplashGrid.SetImages(images);
                    _unsplashStatusLabel.Text = $"{images.Count} photo{(images.Count != 1 ? "s" : "")} loaded.";
                    _unsplashStatusLabel.ForeColor = Color.FromArgb(60, 130, 60);
                });
            }
            catch (ObjectDisposedException) { }
        });
    }

    private void RefreshSmithsonianImages()
    {
        var query = _smithsonianSearchBox.Text.Trim();
        if (string.IsNullOrEmpty(query)) query = "nature";

        var apiKey = _settings.SmithsonianApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _smithsonianStatusLabel.Text = "Smithsonian API key required. Add it in the API Keys tab.";
            _smithsonianStatusLabel.ForeColor = Color.FromArgb(180, 80, 80);
            return;
        }

        _smithsonianStatusLabel.Text = $"Searching Smithsonian for \"{query}\"...";
        _smithsonianStatusLabel.ForeColor = Color.Gray;
        _smithsonianSearchButton.Enabled = false;

        Task.Run(async () =>
        {
            var images = await _smithsonianApi.SearchImagesAsync(apiKey, query);

            if (IsDisposed) return;
            try
            {
                Invoke(() =>
                {
                    _smithsonianSearchButton.Enabled = true;
                    if (images == null || images.Count == 0)
                    {
                        _smithsonianStatusLabel.Text = images == null
                            ? "Could not connect to Smithsonian. Check API key."
                            : "No images found. Try a different search.";
                        _smithsonianStatusLabel.ForeColor = Color.FromArgb(180, 80, 80);
                        _smithsonianGrid.SetImages(new List<ImageSourceInfo>());
                        return;
                    }

                    foreach (var img in images)
                        img.IsFavorited = _settings.Favorites.Any(f => f.Source == DisplayMode.Smithsonian && f.ImageId == img.Id);

                    _smithsonianGrid.SetImages(images);
                    _smithsonianStatusLabel.Text = $"{images.Count} image{(images.Count != 1 ? "s" : "")} found.";
                    _smithsonianStatusLabel.ForeColor = Color.FromArgb(60, 130, 60);
                });
            }
            catch (ObjectDisposedException) { }
        });
    }

    // ── FAVORITES ──

    private void ToggleFavorite(ImageSourceInfo img)
    {
        var existing = _settings.Favorites.Find(f => f.Source == img.Source && f.ImageId == img.Id);
        if (existing != null)
        {
            _settings.Favorites.Remove(existing);
            img.IsFavorited = false;
        }
        else
        {
            _settings.Favorites.Add(new FavoriteImage
            {
                Source = img.Source,
                ImageId = img.Id,
                Title = img.Title,
                ThumbnailUrl = img.ThumbnailUrl,
                FullImageUrl = !string.IsNullOrEmpty(img.HdImageUrl) ? img.HdImageUrl : img.FullImageUrl,
            });
            img.IsFavorited = true;
        }
        SchedulePreview(); // Persist
    }

    // ── HELPERS ──

    private string GetUnsplashTopicSlug()
    {
        return _unsplashTopicCombo.SelectedIndex switch
        {
            0 => "nature",
            1 => "wallpapers",
            2 => "travel",
            3 => "animals",
            4 => "architecture",
            _ => "nature"
        };
    }

    private static int CameraToZoomSlider(float distance)
    {
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

        _displayModeCombo.SelectedIndex = Math.Min((int)config.DisplayMode, _displayModeCombo.Items.Count - 1);
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
        if (config.ImageStyle == ImageStyle.TopoBathy) _topoBathyRadio.Checked = true;
        else _topoRadio.Checked = true;

        // EPIC
        _epicTypeCombo.SelectedIndex = (int)config.EpicImageType;
        _epicLatestRadio.Checked = config.EpicUseLatest;
        _epicDateRadio.Checked = !config.EpicUseLatest;
        if (!string.IsNullOrEmpty(config.EpicSelectedDate) && DateTime.TryParse(config.EpicSelectedDate, out var epicDate))
            _epicDatePicker.Value = epicDate;

        // Rotation
        _randomRotationCheck.Checked = config.RandomRotationEnabled;
        _randomFavoritesOnlyCheck.Checked = config.RandomFromFavoritesOnly;

        _locationCombo.SelectedIndex = 0;
        UpdateModeVisibility();
        _isLoading = false;
    }

    private void ResetToDefaults()
    {
        _isLoading = true;

        _displayModeCombo.SelectedIndex = (int)DisplayMode.Spherical;
        _longitudeSlider.Value = 88;
        _longitudeValue.Text = "88\u00b0";
        _latitudeSlider.Value = 42;
        _latitudeValue.Text = "42\u00b0";
        _zoomSlider.Value = 25;
        _zoomValue.Text = "25";
        _offsetXSlider.Value = 0;
        _offsetXValue.Text = "0";
        _offsetYSlider.Value = 0;
        _offsetYValue.Text = "0";
        _nightLightsCheck.Checked = true;
        _nightBrightnessSlider.Value = 17;
        _nightBrightnessValue.Text = "1.7";
        _nightBrightnessSlider.Enabled = true;
        _ambientSlider.Value = 15;
        _ambientValue.Text = "0.15";
        _topoBathyRadio.Checked = true;
        _locationCombo.SelectedIndex = 0;

        _epicTypeCombo.SelectedIndex = 0;
        _epicLatestRadio.Checked = true;
        _randomRotationCheck.Checked = false;
        _randomFavoritesOnlyCheck.Checked = false;

        UpdateModeVisibility();
        _isLoading = false;
        SchedulePreview();
    }

    private void LoadAppearanceFromGlobalSettings()
    {
        _isLoading = true;

        _displayModeCombo.SelectedIndex = Math.Min((int)_settings.DisplayMode, _displayModeCombo.Items.Count - 1);
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
        if (_settings.ImageStyle == ImageStyle.TopoBathy) _topoBathyRadio.Checked = true;
        else _topoRadio.Checked = true;

        // EPIC
        _epicTypeCombo.SelectedIndex = (int)_settings.EpicImageType;
        _epicLatestRadio.Checked = _settings.EpicUseLatest;
        _epicDateRadio.Checked = !_settings.EpicUseLatest;
        if (!string.IsNullOrEmpty(_settings.EpicSelectedDate) && DateTime.TryParse(_settings.EpicSelectedDate, out var epicDate))
            _epicDatePicker.Value = epicDate;

        // API keys
        _nasaApiKeyBox.Text = _settings.NasaApiKey;
        _npsApiKeyBox.Text = _settings.NpsApiKey;
        _unsplashKeyBox.Text = _settings.UnsplashAccessKey;
        _smithsonianKeyBox.Text = _settings.SmithsonianApiKey;

        // Rotation
        _randomRotationCheck.Checked = _settings.RandomRotationEnabled;
        _randomFavoritesOnlyCheck.Checked = _settings.RandomFromFavoritesOnly;

        _locationCombo.SelectedIndex = 0;
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
            case MultiMonitorMode.SpanAcross: _spanAcrossRadio.Checked = true; break;
            case MultiMonitorMode.PerDisplay: _perDisplayRadio.Checked = true; break;
            default: _sameForAllRadio.Checked = true; break;
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
