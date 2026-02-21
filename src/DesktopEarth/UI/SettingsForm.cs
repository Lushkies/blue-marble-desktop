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
    private Panel _stillImagePanel = null!;

    // Still Image source sub-dropdown
    private ComboBox _stillImageSourceCombo = null!;

    // Sub-panels inside still image panel (toggled by source dropdown)
    private Panel _epicSubPanel = null!;
    private Panel _apodSubPanel = null!;
    private Panel _npsSubPanel = null!;
    private Panel _smithsonianSubPanel = null!;

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
    private Label _epicLatestDateLabel = null!;
    private DateTime? _epicLatestAvailableDate;

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
    private FlowLayoutPanel _npsChipsPanel = null!;
    private readonly NpsApiClient _npsApi = new();

    // Smithsonian controls
    private TextBox _smithsonianSearchBox = null!;
    private ThumbnailGridPanel _smithsonianGrid = null!;
    private Label _smithsonianStatusLabel = null!;
    private Button _smithsonianSearchButton = null!;
    private FlowLayoutPanel _smithsonianChipsPanel = null!;
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

    // Per-display controls (merged into GroupBox)
    private ComboBox _monitorSelectCombo = null!;
    private Label _monitorSelectLabel = null!;
    private string _selectedMonitorDevice = "";

    // API Keys tab controls
    private TextBox _apiKeyBox = null!;

    // Shared Reset button
    private Button _resetButton = null!;

    // Quality filter
    private ComboBox _qualityFilterCombo = null!;

    // Presets controls
    private ComboBox _presetCombo = null!;

    // User images controls
    private Panel _userImagesSubPanel = null!;
    private ThumbnailGridPanel _userImagesGrid = null!;
    private Label _userImagesStatusLabel = null!;
    private readonly UserImageManager _userImageManager = new();

    // My Collection tab controls
    private ThumbnailGridPanel? _favoritesGrid;
    private Label? _favoritesCountLabel;
    private ThumbnailGridPanel? _userImagesCollectionGrid;
    private Label? _userImagesCountLabel;

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

            // API key
            s.ApiDataGovKey = _apiKeyBox.Text.Trim();
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
        s.StillImageSource = (ImageSource)_stillImageSourceCombo.SelectedIndex;
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

        // Smithsonian
        s.SmithsonianSearchQuery = _smithsonianSearchBox.Text;
        if (_smithsonianGrid.SelectedImage != null)
        {
            s.SmithsonianSelectedId = _smithsonianGrid.SelectedImage.Id;
            s.SmithsonianSelectedImageUrl = _smithsonianGrid.SelectedImage.HdImageUrl;
        }

        // User images
        if (_userImagesGrid?.SelectedImage != null)
        {
            s.UserImageSelectedId = _userImagesGrid.SelectedImage.Id;
            s.UserImageSelectedPath = _userImageManager.GetImagePath(
                _userImagesGrid.SelectedImage.Id) ?? "";
        }

        // Quality filter
        s.MinImageQuality = _qualityFilterCombo.SelectedIndex switch
        {
            1 => ImageQualityTier.SD,
            2 => ImageQualityTier.HD,
            3 => ImageQualityTier.UD,
            _ => ImageQualityTier.Unknown // "Any"
        };

        // Rotation
        s.RandomRotationEnabled = _randomRotationCheck.Checked;
        s.RandomFromFavoritesOnly = _randomFavoritesOnlyCheck.Checked;
    }

    private void SaveAppearanceToDisplayConfig(DisplayConfig config)
    {
        config.DisplayMode = (DisplayMode)_displayModeCombo.SelectedIndex;
        config.StillImageSource = (ImageSource)_stillImageSourceCombo.SelectedIndex;
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

        // Smithsonian
        config.SmithsonianSearchQuery = _smithsonianSearchBox.Text;
        if (_smithsonianGrid.SelectedImage != null)
        {
            config.SmithsonianSelectedId = _smithsonianGrid.SelectedImage.Id;
            config.SmithsonianSelectedImageUrl = _smithsonianGrid.SelectedImage.HdImageUrl;
        }

        // User images
        if (_userImagesGrid?.SelectedImage != null)
        {
            config.UserImageSelectedId = _userImagesGrid.SelectedImage.Id;
            config.UserImageSelectedPath = _userImageManager.GetImagePath(
                _userImagesGrid.SelectedImage.Id) ?? "";
        }

        // Quality filter
        config.MinImageQuality = _qualityFilterCombo.SelectedIndex switch
        {
            1 => ImageQualityTier.SD,
            2 => ImageQualityTier.HD,
            3 => ImageQualityTier.UD,
            _ => ImageQualityTier.Unknown
        };

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
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(560, 790);
        ShowInTaskbar = true;

        var tabControl = new TabControl
        {
            Dock = DockStyle.Fill,
            Padding = new Point(8, 4)
        };

        tabControl.TabPages.Add(CreateAppearanceTab());
        tabControl.TabPages.Add(CreateMyCollectionTab());
        tabControl.TabPages.Add(CreateSystemTab());

        // Refresh My Collection tab when user switches to it
        tabControl.SelectedIndexChanged += (_, _) =>
        {
            if (tabControl.SelectedIndex == 1) RefreshMyCollectionTab();
        };

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

        // -- MULTI-DISPLAY MODE (with monitor selector merged inside) --
        var monitorGroup = new GroupBox
        {
            Text = "Multi-Display Mode",
            Location = new Point(LeftMargin, y),
            Size = new Size(490, 115)
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

        // Monitor selector (inside GroupBox, toggled by per-display radio)
        _monitorSelectLabel = MakeLabel("Configure:", 30, 84);
        _monitorSelectLabel.Enabled = false;

        _monitorSelectCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(105, 81),
            Width = 370,
            Enabled = false
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

        monitorGroup.Controls.AddRange([_sameForAllRadio, _spanAcrossRadio, _perDisplayRadio,
            _monitorSelectLabel, _monitorSelectCombo]);
        tab.Controls.Add(monitorGroup);
        y += 120;

        // -- VIEW MODE --
        tab.Controls.Add(MakeLabel("View:", LeftMargin, y + 3));
        _displayModeCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(100, y),
            Width = 170
        };
        _displayModeCombo.Items.AddRange(["Globe", "Flat Map", "Moon", "Still Image"]);
        _displayModeCombo.SelectedIndexChanged += (_, _) => { UpdateModeVisibility(); SchedulePreview(); };
        tab.Controls.Add(_displayModeCombo);
        y += 30;

        // -- RANDOM ROTATION (visible for still image mode only) --
        _randomRotationCheck = new CheckBox
        {
            Text = "Rotate randomly each update",
            AutoSize = true,
            Location = new Point(LeftMargin, y),
            Visible = false
        };
        _randomRotationCheck.CheckedChanged += (_, _) =>
        {
            _randomFavoritesOnlyCheck.Enabled = _randomRotationCheck.Checked;
            SchedulePreview();
        };
        tab.Controls.Add(_randomRotationCheck);

        _randomFavoritesOnlyCheck = new CheckBox
        {
            Text = "Favorites only",
            AutoSize = true,
            Location = new Point(250, y),
            Visible = false,
            Enabled = false
        };
        _randomFavoritesOnlyCheck.CheckedChanged += (_, _) => SchedulePreview();
        tab.Controls.Add(_randomFavoritesOnlyCheck);
        y += 25;

        // -- MODE PANELS (all start at same Y, only one visible at a time) --
        int panelY = y;

        _globeControlsPanel = CreateGlobePanel(panelY);
        tab.Controls.Add(_globeControlsPanel);

        _stillImagePanel = CreateStillImagePanel(panelY);
        tab.Controls.Add(_stillImagePanel);

        // -- SHARED RESET BUTTON --
        _resetButton = new Button
        {
            Text = "Reset to Defaults",
            Location = new Point(LeftMargin, panelY + 430),
            Width = 140,
            Height = 28
        };
        _resetButton.Click += (_, _) => ResetToDefaults();
        tab.Controls.Add(_resetButton);

        // -- PRESETS (always visible, below reset button) --
        var presetLabel = MakeLabel("Presets:", LeftMargin, panelY + 465);
        tab.Controls.Add(presetLabel);

        _presetCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(LeftMargin + 60, panelY + 462),
            Width = 180
        };
        tab.Controls.Add(_presetCombo);

        var presetSaveBtn = new Button
        {
            Text = "Save",
            Location = new Point(LeftMargin + 250, panelY + 461),
            Width = 60, Height = 26
        };
        presetSaveBtn.Click += (_, _) => SavePreset();
        tab.Controls.Add(presetSaveBtn);

        var presetLoadBtn = new Button
        {
            Text = "Load",
            Location = new Point(LeftMargin + 315, panelY + 461),
            Width = 60, Height = 26
        };
        presetLoadBtn.Click += (_, _) => LoadPreset();
        tab.Controls.Add(presetLoadBtn);

        var presetDeleteBtn = new Button
        {
            Text = "Delete",
            Location = new Point(LeftMargin + 380, panelY + 461),
            Width = 60, Height = 26
        };
        presetDeleteBtn.Click += (_, _) => DeletePreset();
        tab.Controls.Add(presetDeleteBtn);

        RefreshPresetCombo();

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

    // -- COMBINED STILL IMAGE PANEL --
    // Contains a source sub-dropdown and sub-panels for each source

    private Panel CreateStillImagePanel(int y)
    {
        var panel = new Panel { Location = new Point(0, y), Size = new Size(530, 430), Visible = false };
        int py = 0;

        // Source sub-dropdown
        panel.Controls.Add(MakeLabel("Source:", LeftMargin, py + 3));
        _stillImageSourceCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(85, py),
            Width = 180
        };
        _stillImageSourceCombo.Items.AddRange(["NASA EPIC", "NASA APOD", "National Parks", "Smithsonian", "My Images"]);
        _stillImageSourceCombo.SelectedIndex = 0;
        _stillImageSourceCombo.SelectedIndexChanged += (_, _) =>
        {
            UpdateStillImageSubPanel();
            // Don't trigger render — user must click an image to set wallpaper
        };
        panel.Controls.Add(_stillImageSourceCombo);

        // Min quality filter
        panel.Controls.Add(MakeLabel("Min quality:", 290, py + 3));
        _qualityFilterCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(380, py),
            Width = 90
        };
        _qualityFilterCombo.Items.AddRange(["Any", "SD (1080p+)", "HD (2160p+)", "UD (4K+)"]);
        _qualityFilterCombo.SelectedIndex = 1; // Default: SD
        _qualityFilterCombo.SelectedIndexChanged += (_, _) =>
        {
            // Save the quality filter setting without triggering a render.
            // The filter only affects which thumbnails are visible in the grid.
            if (!_isLoading) _settingsManager.ApplyAndSave(s =>
            {
                s.MinImageQuality = _qualityFilterCombo.SelectedIndex switch
                {
                    1 => ImageQualityTier.SD,
                    2 => ImageQualityTier.HD,
                    3 => ImageQualityTier.UD,
                    _ => ImageQualityTier.Unknown
                };
            });
        };
        panel.Controls.Add(_qualityFilterCombo);
        py += 30;

        // Sub-panels (all start at same Y inside the still image panel)
        int subPanelY = py;

        _epicSubPanel = CreateEpicSubPanel(subPanelY);
        panel.Controls.Add(_epicSubPanel);

        _apodSubPanel = CreateApodSubPanel(subPanelY);
        panel.Controls.Add(_apodSubPanel);

        _npsSubPanel = CreateNpsSubPanel(subPanelY);
        panel.Controls.Add(_npsSubPanel);

        _smithsonianSubPanel = CreateSmithsonianSubPanel(subPanelY);
        panel.Controls.Add(_smithsonianSubPanel);

        _userImagesSubPanel = CreateUserImagesSubPanel(subPanelY);
        panel.Controls.Add(_userImagesSubPanel);

        return panel;
    }

    private Panel CreateEpicSubPanel(int y)
    {
        var panel = new Panel { Location = new Point(0, y), Size = new Size(530, 400), Visible = true };
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

        _epicLatestDateLabel = new Label
        {
            Text = "Checking latest available date...",
            Location = new Point(320, ey + 1),
            AutoSize = true,
            Font = new Font("Segoe UI", 8f),
            ForeColor = Color.FromArgb(100, 100, 100)
        };
        panel.Controls.Add(_epicLatestDateLabel);
        ey += 30;

        // Fetch the latest available EPIC date in the background
        FetchEpicLatestDate();

        _epicGrid = new ThumbnailGridPanel(_imageCache)
        {
            Location = new Point(LeftMargin, ey),
            Size = new Size(490, 220)
        };
        _epicGrid.ImageSelected += (_, img) =>
        {
            if (!_isLoading) SchedulePreview();
        };
        _epicGrid.FavoriteToggled += (_, img) => ToggleFavorite(img);
        panel.Controls.Add(_epicGrid);
        ey += 225;

        _epicStatusLabel = new Label
        {
            Text = "Select NASA EPIC source to load satellite images.",
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

    private Panel CreateApodSubPanel(int y)
    {
        var panel = new Panel { Location = new Point(0, y), Size = new Size(530, 400), Visible = false };
        int py = 0;

        _apodLatestRadio = new RadioButton
        {
            Text = "Show latest 14 days", AutoSize = true,
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
            Size = new Size(490, 250)
        };
        _apodGrid.ImageSelected += (_, img) => { if (!_isLoading) SchedulePreview(); };
        _apodGrid.FavoriteToggled += (_, img) => ToggleFavorite(img);
        panel.Controls.Add(_apodGrid);
        py += 255;

        _apodStatusLabel = new Label
        {
            Text = "Select NASA APOD source to load images.",
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

    private Panel CreateNpsSubPanel(int y)
    {
        var panel = new Panel { Location = new Point(0, y), Size = new Size(530, 400), Visible = false };
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
        py += 28;

        // Suggestion chips (wrapping, scrollable, uses exact park codes)
        _npsChipsPanel = new FlowLayoutPanel
        {
            Location = new Point(LeftMargin, py),
            Size = new Size(490, 56),
            WrapContents = true,
            AutoScroll = true
        };
        foreach (var parkName in NpsApiClient.ParkCodes.Keys.OrderBy(k => k))
        {
            var chipName = parkName; // capture for closure
            var btn = new Button
            {
                Text = chipName,
                AutoSize = true,
                Height = 24,
                Padding = new Padding(2, 0, 2, 0),
                Margin = new Padding(0, 0, 4, 2),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 7.5f),
                BackColor = Color.FromArgb(235, 240, 250),
                ForeColor = Color.FromArgb(40, 60, 100),
                Cursor = Cursors.Hand
            };
            btn.FlatAppearance.BorderColor = Color.FromArgb(180, 195, 220);
            btn.FlatAppearance.BorderSize = 1;
            btn.Click += (_, _) =>
            {
                _npsSearchBox.Text = chipName;
                RefreshNpsImages();
            };
            _npsChipsPanel.Controls.Add(btn);
        }
        panel.Controls.Add(_npsChipsPanel);
        py += 60;

        _npsGrid = new ThumbnailGridPanel(_imageCache)
        {
            Location = new Point(LeftMargin, py),
            Size = new Size(490, 245)
        };
        _npsGrid.ImageSelected += (_, img) => { if (!_isLoading) SchedulePreview(); };
        _npsGrid.FavoriteToggled += (_, img) => ToggleFavorite(img);
        panel.Controls.Add(_npsGrid);
        py += 250;

        _npsStatusLabel = new Label
        {
            Text = "Enter a park name or click a suggestion. Requires API key (see API Keys tab).",
            Location = new Point(LeftMargin, py),
            Size = new Size(490, 35),
            Font = new Font("Segoe UI", 8.5f),
            ForeColor = Color.Gray
        };
        panel.Controls.Add(_npsStatusLabel);

        return panel;
    }

    private Panel CreateSmithsonianSubPanel(int y)
    {
        var panel = new Panel { Location = new Point(0, y), Size = new Size(530, 400), Visible = false };
        int py = 0;

        panel.Controls.Add(MakeLabel("Search:", LeftMargin, py + 3));
        _smithsonianSearchBox = new TextBox
        {
            Location = new Point(80, py), Width = 320,
            Text = "landscape painting"
        };
        _smithsonianSearchBox.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) RefreshSmithsonianImages(); };
        panel.Controls.Add(_smithsonianSearchBox);

        _smithsonianSearchButton = new Button
        {
            Text = "Search", Location = new Point(410, py), Width = 80, Height = 24
        };
        _smithsonianSearchButton.Click += (_, _) => RefreshSmithsonianImages();
        panel.Controls.Add(_smithsonianSearchButton);
        py += 28;

        // Suggestion chips (curated for art_design category)
        _smithsonianChipsPanel = new FlowLayoutPanel
        {
            Location = new Point(LeftMargin, py),
            Size = new Size(490, 56),
            WrapContents = true,
            AutoScroll = true
        };
        string[] smithChips = ["Landscape Painting", "Sunset", "Mountain", "Ocean", "Forest",
            "Butterfly", "Architecture", "Waterfall", "Desert", "Volcano",
            "Coral Reef", "Glacier", "Photography", "Parthenon", "Night Sky"];
        foreach (var chip in smithChips)
        {
            var chipText = chip; // capture for closure
            var btn = new Button
            {
                Text = chipText,
                AutoSize = true,
                Height = 24,
                Padding = new Padding(2, 0, 2, 0),
                Margin = new Padding(0, 0, 4, 2),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 7.5f),
                BackColor = Color.FromArgb(240, 235, 225),
                ForeColor = Color.FromArgb(80, 60, 30),
                Cursor = Cursors.Hand
            };
            btn.FlatAppearance.BorderColor = Color.FromArgb(210, 195, 170);
            btn.FlatAppearance.BorderSize = 1;
            btn.Click += (_, _) =>
            {
                _smithsonianSearchBox.Text = chipText;
                RefreshSmithsonianImages();
            };
            _smithsonianChipsPanel.Controls.Add(btn);
        }
        panel.Controls.Add(_smithsonianChipsPanel);
        py += 60;

        _smithsonianGrid = new ThumbnailGridPanel(_imageCache)
        {
            Location = new Point(LeftMargin, py),
            Size = new Size(490, 245)
        };
        _smithsonianGrid.ImageSelected += (_, img) => { if (!_isLoading) SchedulePreview(); };
        _smithsonianGrid.FavoriteToggled += (_, img) => ToggleFavorite(img);
        panel.Controls.Add(_smithsonianGrid);
        py += 250;

        _smithsonianStatusLabel = new Label
        {
            Text = "Search the Smithsonian collection. Requires API key (see API Keys tab).",
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
            Text = "HD Textures (Globe + Flat Map only)",
            Location = new Point(LeftMargin, y),
            Size = new Size(490, 105)
        };

        bool hdAvailable = HiResTextureManager.AreHiResTexturesAvailable();
        var hdDescLabel = new Label
        {
            Text = "HD textures enhance Globe and Flat Map views with ultra-high resolution\n" +
                   "(21600x10800). They do not affect Still Image, Moon, or EPIC views.",
            Location = new Point(15, 20),
            Size = new Size(460, 30),
            Font = new Font("Segoe UI", 8f),
            ForeColor = Color.FromArgb(80, 80, 80)
        };
        hdGroup.Controls.Add(hdDescLabel);

        var hdStatusLabel = new Label
        {
            Text = hdAvailable
                ? "\u2713 HD textures installed \u2014 Globe and Flat Map using ultra-high resolution."
                : $"Standard textures in use. Download ~{HiResTextureManager.GetEstimatedDownloadSizeMB()} MB for HD (may take 10\u201330 min).",
            AutoSize = true,
            Location = new Point(15, 52),
            Font = new Font("Segoe UI", 8.5f)
        };
        hdGroup.Controls.Add(hdStatusLabel);

        var hdProgressLabel = new Label
        {
            Text = "", AutoSize = true, Location = new Point(15, 52),
            Font = new Font("Segoe UI", 8.5f), Visible = false
        };
        hdGroup.Controls.Add(hdProgressLabel);

        var hdButton = new Button
        {
            Text = hdAvailable ? "Re-download HD Textures" : "Download HD Textures",
            Location = new Point(15, 72), Width = 200, Height = 26
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
                    hdStatusLabel.Text = "\u2713 HD textures installed \u2014 Globe and Flat Map using ultra-high resolution.";
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
        y += 115;

        // Cache Settings section
        var cacheGroup = new GroupBox
        {
            Text = "Cache Settings",
            Location = new Point(LeftMargin, y),
            Size = new Size(490, 120)
        };

        var cacheDescLabel = new Label
        {
            Text = "Controls how long downloaded images are kept. Favorites are never deleted.",
            Location = new Point(15, 20),
            Size = new Size(460, 18),
            Font = new Font("Segoe UI", 8f),
            ForeColor = Color.FromArgb(80, 80, 80)
        };
        cacheGroup.Controls.Add(cacheDescLabel);

        cacheGroup.Controls.Add(new Label
        {
            Text = "Keep images for:", AutoSize = true,
            Location = new Point(15, 44),
            Font = new Font("Segoe UI", 9f)
        });

        var cacheDurationCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(130, 41),
            Width = 150
        };
        cacheDurationCombo.Items.AddRange(["7 days", "14 days", "30 days (default)", "90 days", "Keep forever"]);
        cacheDurationCombo.SelectedIndex = _settings.CacheDurationDays switch
        {
            7 => 0, 14 => 1, 90 => 3, 0 => 4, _ => 2
        };
        cacheDurationCombo.SelectedIndexChanged += (_, _) =>
        {
            int days = cacheDurationCombo.SelectedIndex switch
            {
                0 => 7, 1 => 14, 3 => 90, 4 => 0, _ => 30
            };
            _settingsManager.ApplyAndSave(s =>
            {
                s.CacheDurationDays = days;
                s.EpicCacheDurationDays = Math.Min(days == 0 ? 0 : days, days == 0 ? 0 : Math.Max(days, 14));
            });
        };
        cacheGroup.Controls.Add(cacheDurationCombo);

        // Show cache size
        var cacheSizeLabel = new Label
        {
            Text = "Calculating cache size...",
            Location = new Point(290, 44),
            AutoSize = true,
            Font = new Font("Segoe UI", 8f),
            ForeColor = Color.Gray
        };
        cacheGroup.Controls.Add(cacheSizeLabel);
        Task.Run(() =>
        {
            long totalBytes = GetDirectorySize(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "BlueMarbleDesktop"));
            string sizeText = totalBytes switch
            {
                < 1024 * 1024 => $"{totalBytes / 1024} KB used",
                < 1024L * 1024 * 1024 => $"{totalBytes / (1024 * 1024)} MB used",
                _ => $"{totalBytes / (1024L * 1024 * 1024):F1} GB used"
            };
            try { Invoke(() => cacheSizeLabel.Text = sizeText); } catch { }
        });

        var clearCacheButton = new Button
        {
            Text = "Clear Cache Now",
            Location = new Point(15, 70),
            Width = 130, Height = 28
        };
        clearCacheButton.Click += (_, _) =>
        {
            var result = MessageBox.Show(
                "This will delete all cached images (except favorites and user images).\n\n" +
                "Images will be re-downloaded as needed. Continue?",
                "Clear Cache", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (result != DialogResult.Yes) return;
            try
            {
                var protectedIds = new HashSet<string>(
                    _settings.Favorites.Select(f => ImageCache.SanitizeFileName(f.ImageId)));
                // Use 1 day = effectively delete everything except today's files
                _imageCache.CleanOldCache(protectedIds, maxDays: 1);

                var epicDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "BlueMarbleDesktop", "epic_images");
                if (Directory.Exists(epicDir))
                    Directory.Delete(epicDir, true);

                cacheSizeLabel.Text = "Cache cleared";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error clearing cache: {ex.Message}", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        };

        var clearCacheNote = new Label
        {
            Text = "Favorites and user images are not affected.",
            Location = new Point(155, 75),
            AutoSize = true,
            Font = new Font("Segoe UI", 8f),
            ForeColor = Color.Gray
        };
        cacheGroup.Controls.Add(clearCacheNote);
        cacheGroup.Controls.Add(clearCacheButton);
        tab.Controls.Add(cacheGroup);
        y += 130;

        // === STORAGE LOCATIONS SECTION ===
        var appDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "BlueMarbleDesktop");

        var storageGroup = new GroupBox
        {
            Text = "Storage Locations",
            Location = new Point(LeftMargin, y),
            Size = new Size(490, 105)
        };

        var storageInfo = new Label
        {
            Text = $"Cached images:  {Path.Combine(appDataDir, "image_cache")}\n" +
                   $"User images:    {Path.Combine(appDataDir, "user_images")}\n" +
                   $"Settings:       {Path.Combine(appDataDir, "settings.json")}",
            Location = new Point(10, 20),
            AutoSize = true,
            Font = new Font("Segoe UI", 7.5f),
            ForeColor = Color.Gray
        };
        storageGroup.Controls.Add(storageInfo);

        var openFolderButton = new Button
        {
            Text = "Open Folder",
            Location = new Point(10, 73),
            Width = 100, Height = 24
        };
        openFolderButton.Click += (_, _) =>
        {
            try { System.Diagnostics.Process.Start("explorer.exe", appDataDir); }
            catch { }
        };
        storageGroup.Controls.Add(openFolderButton);

        var storageDisclaimer = new Label
        {
            Text = "Files are stored locally. You can copy or back up\nimages from these folders at any time.",
            Location = new Point(120, 73),
            AutoSize = true,
            Font = new Font("Segoe UI", 7.5f),
            ForeColor = Color.Gray
        };
        storageGroup.Controls.Add(storageDisclaimer);
        tab.Controls.Add(storageGroup);
        y += 115;

        // === API KEY SECTION ===
        var apiKeyGroup = new GroupBox
        {
            Text = "API Key",
            Location = new Point(LeftMargin, y),
            Size = new Size(490, 175)
        };
        int ay = 18;

        var apiInfoLabel = new Label
        {
            Text = "All image sources (NASA APOD, National Parks, Smithsonian) use a single free\n" +
                   "api.data.gov API key. The default DEMO_KEY has a 50 requests/day limit.",
            Location = new Point(15, ay),
            Size = new Size(460, 32),
            Font = new Font("Segoe UI", 8f),
            ForeColor = Color.FromArgb(80, 80, 80)
        };
        apiKeyGroup.Controls.Add(apiInfoLabel);
        ay += 36;

        apiKeyGroup.Controls.Add(new Label
        {
            Text = "API Key:", AutoSize = true,
            Location = new Point(15, ay + 3),
            Font = new Font("Segoe UI", 9f)
        });
        _apiKeyBox = new TextBox { Location = new Point(80, ay), Width = 390 };
        _apiKeyBox.TextChanged += (_, _) =>
        {
            // Auto-trim trailing whitespace
            var text = _apiKeyBox.Text;
            if (text != text.TrimEnd())
            {
                var pos = _apiKeyBox.SelectionStart;
                _apiKeyBox.Text = text.TrimEnd();
                _apiKeyBox.SelectionStart = Math.Min(pos, _apiKeyBox.Text.Length);
            }
            // Save API key to disk after debounce (300ms)
            SchedulePreview();
        };
        apiKeyGroup.Controls.Add(_apiKeyBox);
        ay += 28;

        var apiKeyNote = new Label
        {
            Text = "Used for NASA APOD, National Parks (NPS), and Smithsonian Open Access.",
            Location = new Point(80, ay),
            AutoSize = true,
            Font = new Font("Segoe UI", 7.5f),
            ForeColor = Color.Gray
        };
        apiKeyGroup.Controls.Add(apiKeyNote);
        ay += 18;

        var apiSignupLink = new LinkLabel
        {
            Text = "Get a free API key at: https://api.data.gov/signup/",
            Location = new Point(80, ay),
            AutoSize = true,
            Font = new Font("Segoe UI", 8.5f)
        };
        apiSignupLink.LinkClicked += (_, _) =>
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                    "https://api.data.gov/signup/") { UseShellExecute = true });
            }
            catch { }
        };
        apiKeyGroup.Controls.Add(apiSignupLink);
        ay += 24;

        var apiVerifyButton = new Button
        {
            Text = "Verify Key",
            Location = new Point(80, ay),
            Width = 100, Height = 26
        };
        var apiVerifyStatus = new Label
        {
            Text = "", Location = new Point(190, ay + 4),
            AutoSize = true,
            Font = new Font("Segoe UI", 8.5f)
        };
        apiKeyGroup.Controls.Add(apiVerifyStatus);
        apiVerifyButton.Click += (_, _) =>
        {
            var key = _apiKeyBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(key))
            {
                apiVerifyStatus.Text = "Please enter an API key first.";
                apiVerifyStatus.ForeColor = Color.FromArgb(180, 80, 80);
                return;
            }

            apiVerifyButton.Enabled = false;
            apiVerifyStatus.Text = "Verifying...";
            apiVerifyStatus.ForeColor = Color.Gray;

            Task.Run(async () =>
            {
                try
                {
                    var result = await _apodApi.GetByDateAsync(key, DateTime.UtcNow.AddDays(-1).ToString("yyyy-MM-dd"));
                    if (IsDisposed) return;
                    try
                    {
                        Invoke(() =>
                        {
                            apiVerifyButton.Enabled = true;
                            if (result != null)
                            {
                                apiVerifyStatus.Text = "API key is valid!";
                                apiVerifyStatus.ForeColor = Color.FromArgb(60, 130, 60);
                            }
                            else
                            {
                                apiVerifyStatus.Text = "Key may be invalid or rate-limited.";
                                apiVerifyStatus.ForeColor = Color.FromArgb(180, 120, 0);
                            }
                        });
                    }
                    catch (ObjectDisposedException) { }
                }
                catch (Exception ex)
                {
                    if (IsDisposed) return;
                    try
                    {
                        Invoke(() =>
                        {
                            apiVerifyButton.Enabled = true;
                            apiVerifyStatus.Text = $"Error: {ex.Message}";
                            apiVerifyStatus.ForeColor = Color.FromArgb(180, 80, 80);
                        });
                    }
                    catch (ObjectDisposedException) { }
                }
            });
        };
        apiKeyGroup.Controls.Add(apiVerifyButton);
        tab.Controls.Add(apiKeyGroup);
        y += 185;

        // Enable scrolling for the system tab
        tab.AutoScroll = true;
        tab.AutoScrollMinSize = new Size(510, y);

        return tab;
    }

    private static long GetDirectorySize(string path)
    {
        if (!Directory.Exists(path)) return 0;
        try
        {
            return new DirectoryInfo(path)
                .EnumerateFiles("*", SearchOption.AllDirectories)
                .Sum(f => f.Length);
        }
        catch { return 0; }
    }


    // -- MODE VISIBILITY --

    private void UpdateModeVisibility()
    {
        int mode = _displayModeCombo.SelectedIndex;

        _globeControlsPanel.Visible = mode <= 2;
        _stillImagePanel.Visible = mode == 3;
        _resetButton.Visible = mode != 3;

        // Random rotation visible for still image mode (always visible, greyed out when not checked)
        _randomRotationCheck.Visible = mode == 3;
        _randomFavoritesOnlyCheck.Visible = mode == 3;
        _randomFavoritesOnlyCheck.Enabled = _randomRotationCheck.Checked;

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

            // Moon doesn't have city lights or Earth-style daytime lighting
            _nightLightsCheck.Enabled = !isMoon;
            _nightBrightnessSlider.Enabled = !isMoon && _nightLightsCheck.Checked;
            _ambientSlider.Enabled = !isMoon;
        }

        // Auto-load content when switching to still image
        if (mode == 3)
        {
            UpdateStillImageSubPanel();
        }
    }

    private void UpdateStillImageSubPanel()
    {
        int source = _stillImageSourceCombo.SelectedIndex;

        _epicSubPanel.Visible = source == 0;
        _apodSubPanel.Visible = source == 1;
        _npsSubPanel.Visible = source == 2;
        _smithsonianSubPanel.Visible = source == 3;
        _userImagesSubPanel.Visible = source == 4;

        // Auto-load on first view
        if (source == 0 && _epicGrid.SelectedImage == null) RefreshEpicImages();
        if (source == 1 && _apodGrid.SelectedImage == null) RefreshApodImages();
        if (source == 4) RefreshUserImages(); // Always refresh (files may change externally)
    }

    private void UpdatePerDisplayVisibility()
    {
        bool perDisplay = _perDisplayRadio.Checked;
        _monitorSelectLabel.Enabled = perDisplay;
        _monitorSelectCombo.Enabled = perDisplay;
    }

    // -- REFRESH METHODS --

    private void FetchEpicLatestDate()
    {
        if (_epicLatestAvailableDate != null) return; // Already fetched this session

        var imageType = (EpicImageType)_epicTypeCombo.SelectedIndex;
        Task.Run(async () =>
        {
            try
            {
                var dates = await _epicApi.GetAvailableDatesAsync(imageType);
                if (dates == null || dates.Count == 0) return;

                // Parse the most recent date (first in the list — API returns newest first)
                var lastDateStr = dates[0].Date;
                if (!DateTime.TryParse(lastDateStr, out var latestDate)) return;

                _epicLatestAvailableDate = latestDate;

                if (IsDisposed) return;
                try
                {
                    Invoke(() =>
                    {
                        _epicLatestDateLabel.Text = $"Latest available: {latestDate:MMM d, yyyy}";
                        _epicLatestDateLabel.ForeColor = Color.FromArgb(60, 120, 60);
                        _epicDatePicker.MaxDate = latestDate;
                    });
                }
                catch { }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to fetch EPIC latest date: {ex.Message}");
                if (IsDisposed) return;
                try
                {
                    Invoke(() =>
                    {
                        _epicLatestDateLabel.Text = "Could not check latest date";
                        _epicLatestDateLabel.ForeColor = Color.Gray;
                    });
                }
                catch { }
            }
        });
    }

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
                            Source = ImageSource.NasaEpic,
                            Id = img.Image,
                            Title = $"{dt:HH:mm UTC} - {dt:MMM dd}",
                            Date = img.Date,
                            ThumbnailUrl = $"https://epic.gsfc.nasa.gov/archive/{collection}/{yyyy}/{mm}/{dd}/thumbs/{img.Image}.jpg",
                            FullImageUrl = $"https://epic.gsfc.nasa.gov/archive/{collection}/{yyyy}/{mm}/{dd}/jpg/{img.Image}.jpg",
                            HdImageUrl = $"https://epic.gsfc.nasa.gov/archive/{collection}/{yyyy}/{mm}/{dd}/png/{img.Image}.png",
                            ImageWidth = 2048,
                            ImageHeight = 2048,
                            QualityTier = ImageQualityTier.HD,
                            SourceAttribution = "NASA EPIC / DSCOVR",
                            IsFavorited = _settings.Favorites.Any(f => f.Source == ImageSource.NasaEpic && f.ImageId == img.Image)
                        };
                    }).ToList();

                    _epicGrid.SetImages(sourceInfos);
                    // Restore previously saved selection
                    _epicGrid.SelectById(_settings.EpicSelectedImage);
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

        var apiKey = _settings.ApiDataGovKey;
        bool useLatest = _apodLatestRadio.Checked;
        string date = _apodDatePicker.Value.ToString("yyyy-MM-dd");

        Task.Run(async () =>
        {
            List<ImageSourceInfo>? images;
            if (useLatest)
                images = await _apodApi.GetRecentAsync(apiKey, 14);
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
                        img.IsFavorited = _settings.Favorites.Any(f => f.Source == ImageSource.NasaApod && f.ImageId == img.Id);

                    _apodGrid.SetImages(images);
                    // Restore previously saved selection
                    _apodGrid.SelectById(_settings.ApodSelectedImageId);
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

        var apiKey = _settings.ApiDataGovKey;
        if (string.IsNullOrWhiteSpace(apiKey) || apiKey == "DEMO_KEY")
        {
            if (apiKey == "DEMO_KEY")
            {
                // DEMO_KEY works but with rate limits — allow it
            }
            else
            {
                _npsStatusLabel.Text = "API key required. Add it in the API Keys tab.";
                _npsStatusLabel.ForeColor = Color.FromArgb(180, 80, 80);
                return;
            }
        }

        _npsStatusLabel.Text = $"Searching parks for \"{query}\"...";
        _npsStatusLabel.ForeColor = Color.Gray;
        _npsSearchButton.Enabled = false;

        Task.Run(async () =>
        {
            // Use park code for exact match if query matches a known park name
            List<ImageSourceInfo>? images;
            if (NpsApiClient.ParkCodes.TryGetValue(query, out var parkCode))
                images = await _npsApi.GetParkImagesAsync(apiKey, parkCode);
            else
                images = await _npsApi.SearchParksAsync(apiKey, query);

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
                        img.IsFavorited = _settings.Favorites.Any(f => f.Source == ImageSource.NationalParks && f.ImageId == img.Id);

                    _npsGrid.SetImages(images);
                    // Restore previously saved selection
                    _npsGrid.SelectById(_settings.NpsSelectedImageId);
                    _npsStatusLabel.Text = $"{images.Count} image{(images.Count != 1 ? "s" : "")} found.";
                    _npsStatusLabel.ForeColor = Color.FromArgb(60, 130, 60);
                });
            }
            catch (ObjectDisposedException) { }
        });
    }

    private void RefreshSmithsonianImages()
    {
        var query = _smithsonianSearchBox.Text.Trim();
        if (string.IsNullOrEmpty(query)) query = "landscape painting";

        var apiKey = _settings.ApiDataGovKey;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _smithsonianStatusLabel.Text = "API key required. Add it in the API Keys tab.";
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
                            : "No images found. Try a different search or click a suggestion.";
                        _smithsonianStatusLabel.ForeColor = Color.FromArgb(180, 80, 80);
                        _smithsonianGrid.SetImages(new List<ImageSourceInfo>());
                        return;
                    }

                    foreach (var img in images)
                        img.IsFavorited = _settings.Favorites.Any(f => f.Source == ImageSource.Smithsonian && f.ImageId == img.Id);

                    _smithsonianGrid.SetImages(images);
                    // Restore previously saved selection
                    _smithsonianGrid.SelectById(_settings.SmithsonianSelectedId);
                    _smithsonianStatusLabel.Text = $"{images.Count} image{(images.Count != 1 ? "s" : "")} found.";
                    _smithsonianStatusLabel.ForeColor = Color.FromArgb(60, 130, 60);
                });
            }
            catch (ObjectDisposedException) { }
        });
    }

    // -- FAVORITES --

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

    // -- HELPERS --

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
        _stillImageSourceCombo.SelectedIndex = Math.Min((int)config.StillImageSource, _stillImageSourceCombo.Items.Count - 1);
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

        // Quality filter
        _qualityFilterCombo.SelectedIndex = config.MinImageQuality switch
        {
            ImageQualityTier.SD => 1,
            ImageQualityTier.HD => 2,
            ImageQualityTier.UD => 3,
            _ => 0
        };

        // Rotation
        _randomRotationCheck.Checked = config.RandomRotationEnabled;
        _randomFavoritesOnlyCheck.Checked = config.RandomFromFavoritesOnly;

        _locationCombo.SelectedIndex = 0;
        UpdateModeVisibility();
        _isLoading = false;
    }

    private void ResetToDefaults()
    {
        int mode = _displayModeCombo.SelectedIndex;
        _isLoading = true;

        switch (mode)
        {
            case 0: // Globe
                _locationCombo.SelectedIndex = 0;
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
                break;

            case 1: // Flat Map
                _nightLightsCheck.Checked = true;
                _nightBrightnessSlider.Value = 17;
                _nightBrightnessValue.Text = "1.7";
                _nightBrightnessSlider.Enabled = true;
                _ambientSlider.Value = 15;
                _ambientValue.Text = "0.15";
                _topoBathyRadio.Checked = true;
                break;

            case 2: // Moon
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
                break;
        }

        _isLoading = false;
        SchedulePreview();
    }

    private void LoadAppearanceFromGlobalSettings()
    {
        _isLoading = true;

        _displayModeCombo.SelectedIndex = Math.Min((int)_settings.DisplayMode, _displayModeCombo.Items.Count - 1);
        _stillImageSourceCombo.SelectedIndex = Math.Min((int)_settings.StillImageSource, _stillImageSourceCombo.Items.Count - 1);
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

        // API key
        _apiKeyBox.Text = _settings.ApiDataGovKey;

        // Quality filter
        _qualityFilterCombo.SelectedIndex = _settings.MinImageQuality switch
        {
            ImageQualityTier.SD => 1,
            ImageQualityTier.HD => 2,
            ImageQualityTier.UD => 3,
            _ => 0 // Any
        };

        // Rotation
        _randomRotationCheck.Checked = _settings.RandomRotationEnabled;
        _randomFavoritesOnlyCheck.Checked = _settings.RandomFromFavoritesOnly;

        // NPS search query
        if (!string.IsNullOrEmpty(_settings.NpsSearchQuery))
            _npsSearchBox.Text = _settings.NpsSearchQuery;

        // Smithsonian search query
        if (!string.IsNullOrEmpty(_settings.SmithsonianSearchQuery))
            _smithsonianSearchBox.Text = _settings.SmithsonianSearchQuery;

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

    // -- PRESET METHODS --

    private void RefreshPresetCombo()
    {
        _presetCombo.Items.Clear();
        if (_settings.Presets.Count == 0)
        {
            _presetCombo.Items.Add("(no presets saved)");
            _presetCombo.SelectedIndex = 0;
        }
        else
        {
            foreach (var p in _settings.Presets)
                _presetCombo.Items.Add(p.Name);
            _presetCombo.SelectedIndex = 0;
        }
    }

    private void SavePreset()
    {
        var name = PromptForName("Save Preset", "Enter a name for this preset:");
        if (string.IsNullOrWhiteSpace(name)) return;

        var preset = new SettingsPreset
        {
            Name = name.Trim(),
            DisplayMode = (DisplayMode)_displayModeCombo.SelectedIndex,
            StillImageSource = (ImageSource)_stillImageSourceCombo.SelectedIndex,
            LongitudeOffset = _longitudeSlider.Value,
            CameraTilt = _latitudeSlider.Value,
            ImageOffsetX = _offsetXSlider.Value,
            ImageOffsetY = _offsetYSlider.Value,
            NightLightsEnabled = _nightLightsCheck.Checked,
            NightLightsBrightness = _nightBrightnessSlider.Value / 10f,
            AmbientLight = _ambientSlider.Value / 100f,
            ImageStyle = _topoBathyRadio.Checked ? ImageStyle.TopoBathy : ImageStyle.Topo,
            EpicImageType = (EpicImageType)_epicTypeCombo.SelectedIndex,
            MinImageQuality = _qualityFilterCombo.SelectedIndex switch
            {
                1 => ImageQualityTier.SD,
                2 => ImageQualityTier.HD,
                3 => ImageQualityTier.UD,
                _ => ImageQualityTier.Unknown
            }
        };

        var (distance, fov) = ZoomSliderToCamera(_zoomSlider.Value);
        preset.ZoomLevel = distance;
        preset.FieldOfView = fov;

        _settings.Presets.Add(preset);
        _settingsManager.Save();
        RefreshPresetCombo();
        _presetCombo.SelectedIndex = _settings.Presets.Count - 1;
    }

    private void LoadPreset()
    {
        int idx = _presetCombo.SelectedIndex;
        if (idx < 0 || idx >= _settings.Presets.Count) return;

        var preset = _settings.Presets[idx];
        _isLoading = true;

        _displayModeCombo.SelectedIndex = Math.Min((int)preset.DisplayMode, _displayModeCombo.Items.Count - 1);
        _stillImageSourceCombo.SelectedIndex = Math.Min((int)preset.StillImageSource, _stillImageSourceCombo.Items.Count - 1);
        _longitudeSlider.Value = Math.Clamp((int)preset.LongitudeOffset, -180, 180);
        _longitudeValue.Text = preset.LongitudeOffset.ToString("F0") + "\u00b0";
        _latitudeSlider.Value = Math.Clamp((int)preset.CameraTilt, -60, 60);
        _latitudeValue.Text = preset.CameraTilt.ToString("F0") + "\u00b0";
        _zoomSlider.Value = CameraToZoomSlider(preset.ZoomLevel);
        _zoomValue.Text = _zoomSlider.Value.ToString();
        _offsetXSlider.Value = Math.Clamp((int)preset.ImageOffsetX, -25, 25);
        _offsetXValue.Text = preset.ImageOffsetX.ToString("F0");
        _offsetYSlider.Value = Math.Clamp((int)preset.ImageOffsetY, -25, 25);
        _offsetYValue.Text = preset.ImageOffsetY.ToString("F0");
        _nightLightsCheck.Checked = preset.NightLightsEnabled;
        _nightBrightnessSlider.Value = Math.Clamp((int)(preset.NightLightsBrightness * 10),
            _nightBrightnessSlider.Minimum, _nightBrightnessSlider.Maximum);
        _nightBrightnessValue.Text = preset.NightLightsBrightness.ToString("F1");
        _ambientSlider.Value = Math.Clamp((int)(preset.AmbientLight * 100),
            _ambientSlider.Minimum, _ambientSlider.Maximum);
        _ambientValue.Text = preset.AmbientLight.ToString("F2");
        if (preset.ImageStyle == ImageStyle.TopoBathy) _topoBathyRadio.Checked = true;
        else _topoRadio.Checked = true;
        _epicTypeCombo.SelectedIndex = (int)preset.EpicImageType;
        _qualityFilterCombo.SelectedIndex = preset.MinImageQuality switch
        {
            ImageQualityTier.SD => 1,
            ImageQualityTier.HD => 2,
            ImageQualityTier.UD => 3,
            _ => 0
        };

        _isLoading = false;
        UpdateModeVisibility();
        SchedulePreview();
    }

    private void DeletePreset()
    {
        int idx = _presetCombo.SelectedIndex;
        if (idx < 0 || idx >= _settings.Presets.Count) return;

        var name = _settings.Presets[idx].Name;
        if (MessageBox.Show($"Delete preset \"{name}\"?", "Delete Preset",
            MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

        _settings.Presets.RemoveAt(idx);
        _settingsManager.Save();
        RefreshPresetCombo();
    }

    private static string? PromptForName(string title, string prompt)
    {
        using var dialog = new Form
        {
            Text = title,
            Width = 350,
            Height = 150,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MaximizeBox = false,
            MinimizeBox = false
        };

        var label = new Label { Text = prompt, Left = 15, Top = 15, AutoSize = true };
        var textBox = new TextBox { Left = 15, Top = 40, Width = 300 };
        var okButton = new Button
        {
            Text = "OK",
            DialogResult = DialogResult.OK,
            Left = 155, Top = 75, Width = 75
        };
        var cancelButton = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            Left = 240, Top = 75, Width = 75
        };

        dialog.Controls.AddRange([label, textBox, okButton, cancelButton]);
        dialog.AcceptButton = okButton;
        dialog.CancelButton = cancelButton;

        return dialog.ShowDialog() == DialogResult.OK && !string.IsNullOrWhiteSpace(textBox.Text)
            ? textBox.Text.Trim() : null;
    }

    // -- USER IMAGES SUB-PANEL (5th source in Still Image dropdown) --

    private Panel CreateUserImagesSubPanel(int y)
    {
        var panel = new Panel { Location = new Point(0, y), Size = new Size(530, 400), Visible = false };
        int uy = 0;

        // Add images button
        var addButton = new Button
        {
            Text = "Add Images...",
            Location = new Point(LeftMargin, uy),
            Width = 110,
            Height = 28
        };
        addButton.Click += async (_, _) =>
        {
            try
            {
                // Run file dialog on a dedicated STA thread to avoid UI thread blocking
                var tcs = new TaskCompletionSource<string[]?>();
                var dialogThread = new Thread(() =>
                {
                    try
                    {
                        using var dialog = new OpenFileDialog
                        {
                            Title = "Select Images to Import",
                            Filter = "Image files|*.jpg;*.jpeg;*.png;*.bmp;*.webp|All files|*.*",
                            Multiselect = true
                        };
                        tcs.SetResult(dialog.ShowDialog() == DialogResult.OK
                            ? dialog.FileNames : null);
                    }
                    catch (Exception ex) { tcs.SetException(ex); }
                });
                dialogThread.SetApartmentState(ApartmentState.STA);
                dialogThread.Start();

                var filePaths = await tcs.Task;
                if (filePaths == null || filePaths.Length == 0) return;

                addButton.Enabled = false;
                addButton.Text = "Importing...";

                try
                {
                    int count = await Task.Run(() => _userImageManager.ImportImages(filePaths));
                    if (IsDisposed) return;
                    _userImagesStatusLabel.Text = $"Added {count} image{(count != 1 ? "s" : "")}";

                    var images = await Task.Run(() => _userImageManager.GetAllImages());
                    if (IsDisposed) return;

                    foreach (var img in images)
                        img.IsFavorited = _settings.Favorites.Any(f =>
                            f.Source == ImageSource.UserImages && f.ImageId == img.Id);
                    _userImagesGrid.SetImages(images);
                }
                finally
                {
                    if (!IsDisposed)
                    {
                        addButton.Enabled = true;
                        addButton.Text = "Add Images...";
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to import images: {ex.Message}",
                    "Import Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        };
        panel.Controls.Add(addButton);

        _userImagesStatusLabel = MakeLabel("", 140, uy + 5);
        _userImagesStatusLabel.ForeColor = Color.Gray;
        panel.Controls.Add(_userImagesStatusLabel);
        uy += 35;

        // Thumbnail grid
        _userImagesGrid = new ThumbnailGridPanel(_imageCache)
        {
            Location = new Point(LeftMargin, uy),
            Size = new Size(490, 340)
        };
        _userImagesGrid.ImageSelected += (_, img) =>
        {
            // Set this user image as the selected wallpaper
            SchedulePreview();
        };
        _userImagesGrid.FavoriteToggled += (_, img) => ToggleFavorite(img);
        panel.Controls.Add(_userImagesGrid);

        return panel;
    }

    private async void RefreshUserImages()
    {
        var images = await Task.Run(() => _userImageManager.GetAllImages());
        if (IsDisposed) return;

        foreach (var img in images)
            img.IsFavorited = _settings.Favorites.Any(f =>
                f.Source == ImageSource.UserImages && f.ImageId == img.Id);
        _userImagesGrid.SetImages(images);

        // Restore previously saved selection
        _userImagesGrid.SelectById(_settings.UserImageSelectedId);

        int count = images.Count;
        _userImagesStatusLabel.Text = $"{count} user image{(count != 1 ? "s" : "")}";
    }

    // -- MY COLLECTION TAB (4th tab) --

    private TabPage CreateMyCollectionTab()
    {
        var tab = new TabPage("My Collection");
        tab.AutoScroll = true;
        int y = 10;

        // === FAVORITES SECTION ===
        _favoritesCountLabel = new Label
        {
            Text = "Favorites (0)",
            Location = new Point(LeftMargin, y),
            AutoSize = true,
            Font = new Font("Segoe UI", 10, FontStyle.Bold)
        };
        tab.Controls.Add(_favoritesCountLabel);
        y += 28;

        _favoritesGrid = new ThumbnailGridPanel(_imageCache)
        {
            Location = new Point(LeftMargin, y),
            Size = new Size(490, 200),
            ShowSourceBadge = true
        };
        _favoritesGrid.ImageSelected += (_, img) => SetFavoriteAsWallpaper(img);
        _favoritesGrid.FavoriteToggled += (_, img) =>
        {
            ToggleFavoriteFromCollection(img);
            RefreshMyCollectionTab();
        };
        tab.Controls.Add(_favoritesGrid);
        y += 205;

        // Export / Clear buttons
        var exportFavButton = new Button
        {
            Text = "Export Favorites",
            Location = new Point(LeftMargin, y),
            Width = 130, Height = 28
        };
        exportFavButton.Click += (_, _) => ExportFavorites();
        tab.Controls.Add(exportFavButton);

        var clearFavButton = new Button
        {
            Text = "Clear All Favorites",
            Location = new Point(LeftMargin + 140, y),
            Width = 150, Height = 28
        };
        clearFavButton.Click += (_, _) => ClearAllFavorites();
        tab.Controls.Add(clearFavButton);
        y += 40;

        // === USER IMAGES SECTION ===
        _userImagesCountLabel = new Label
        {
            Text = "User Images (0)",
            Location = new Point(LeftMargin, y),
            AutoSize = true,
            Font = new Font("Segoe UI", 10, FontStyle.Bold)
        };
        tab.Controls.Add(_userImagesCountLabel);
        y += 28;

        _userImagesCollectionGrid = new ThumbnailGridPanel(_imageCache)
        {
            Location = new Point(LeftMargin, y),
            Size = new Size(490, 200)
        };
        _userImagesCollectionGrid.ImageSelected += (_, img) => SetUserImageAsWallpaper(img);
        _userImagesCollectionGrid.FavoriteToggled += (_, img) => ToggleFavorite(img);
        tab.Controls.Add(_userImagesCollectionGrid);
        y += 205;

        // Add / Clear buttons for user images
        var addImagesButton = new Button
        {
            Text = "Add Images...",
            Location = new Point(LeftMargin, y),
            Width = 110, Height = 28
        };
        addImagesButton.Click += async (_, _) =>
        {
            try
            {
                // Run file dialog on a dedicated STA thread to avoid UI thread blocking
                var tcs = new TaskCompletionSource<string[]?>();
                var dialogThread = new Thread(() =>
                {
                    try
                    {
                        using var dialog = new OpenFileDialog
                        {
                            Title = "Select Images to Import",
                            Filter = "Image files|*.jpg;*.jpeg;*.png;*.bmp;*.webp|All files|*.*",
                            Multiselect = true
                        };
                        tcs.SetResult(dialog.ShowDialog() == DialogResult.OK
                            ? dialog.FileNames : null);
                    }
                    catch (Exception ex) { tcs.SetException(ex); }
                });
                dialogThread.SetApartmentState(ApartmentState.STA);
                dialogThread.Start();

                var filePaths = await tcs.Task;
                if (filePaths == null || filePaths.Length == 0) return;

                addImagesButton.Enabled = false;
                addImagesButton.Text = "Importing...";

                try
                {
                    int count = await Task.Run(() => _userImageManager.ImportImages(filePaths));

                    // Refresh with images loaded on background thread
                    var userImages = await Task.Run(() => _userImageManager.GetAllImages());
                    if (IsDisposed) return;

                    foreach (var img in userImages)
                        img.IsFavorited = _settings.Favorites.Any(f =>
                            f.Source == ImageSource.UserImages && f.ImageId == img.Id);

                    if (_favoritesGrid != null && _userImagesCollectionGrid != null)
                    {
                        var favImages = _settings.Favorites.Select(fav => new ImageSourceInfo
                        {
                            Source = fav.Source,
                            Id = fav.ImageId,
                            Title = !string.IsNullOrEmpty(fav.Title) ? fav.Title : fav.ImageId,
                            ThumbnailUrl = fav.ThumbnailUrl,
                            FullImageUrl = fav.FullImageUrl,
                            IsFavorited = true
                        }).ToList();

                        _favoritesGrid.SetImages(favImages);
                        _favoritesCountLabel!.Text = $"Favorites ({favImages.Count})";
                        _userImagesCollectionGrid.SetImages(userImages);
                        _userImagesCountLabel!.Text = $"User Images ({userImages.Count})";
                    }
                }
                finally
                {
                    if (!IsDisposed)
                    {
                        addImagesButton.Enabled = true;
                        addImagesButton.Text = "Add Images...";
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to import images: {ex.Message}",
                    "Import Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        };
        tab.Controls.Add(addImagesButton);

        var clearUserButton = new Button
        {
            Text = "Clear All",
            Location = new Point(LeftMargin + 120, y),
            Width = 90, Height = 28
        };
        clearUserButton.Click += (_, _) => ClearAllUserImages();
        tab.Controls.Add(clearUserButton);
        y += 40;

        return tab;
    }

    private async void RefreshMyCollectionTab()
    {
        if (_favoritesGrid == null || _userImagesCollectionGrid == null) return;

        // Favorites section (lightweight, no I/O)
        var favImages = _settings.Favorites.Select(fav => new ImageSourceInfo
        {
            Source = fav.Source,
            Id = fav.ImageId,
            Title = !string.IsNullOrEmpty(fav.Title) ? fav.Title : fav.ImageId,
            ThumbnailUrl = fav.ThumbnailUrl,
            FullImageUrl = fav.FullImageUrl,
            IsFavorited = true
        }).ToList();

        _favoritesGrid.SetImages(favImages);
        _favoritesCountLabel!.Text = $"Favorites ({favImages.Count})";

        // User images section (heavy I/O: Image.Identify + EnsureThumbnail for each file)
        var userImages = await Task.Run(() => _userImageManager.GetAllImages());
        if (IsDisposed) return;

        foreach (var img in userImages)
            img.IsFavorited = _settings.Favorites.Any(f =>
                f.Source == ImageSource.UserImages && f.ImageId == img.Id);

        _userImagesCollectionGrid.SetImages(userImages);
        _userImagesCountLabel!.Text = $"User Images ({userImages.Count})";
    }

    private void SetFavoriteAsWallpaper(ImageSourceInfo img)
    {
        _settingsManager.ApplyAndSave(s =>
        {
            s.DisplayMode = DisplayMode.StillImage;
            s.StillImageSource = img.Source;
            switch (img.Source)
            {
                case ImageSource.NasaApod:
                    s.ApodSelectedImageId = img.Id;
                    s.ApodSelectedImageUrl = img.FullImageUrl;
                    break;
                case ImageSource.NationalParks:
                    s.NpsSelectedImageId = img.Id;
                    s.NpsSelectedImageUrl = img.FullImageUrl;
                    break;
                case ImageSource.Smithsonian:
                    s.SmithsonianSelectedId = img.Id;
                    s.SmithsonianSelectedImageUrl = img.FullImageUrl;
                    break;
                case ImageSource.UserImages:
                    s.UserImageSelectedId = img.Id;
                    s.UserImageSelectedPath = _userImageManager.GetImagePath(img.Id) ?? "";
                    break;
            }
        });
        _renderScheduler.TriggerUpdate();
    }

    private void SetUserImageAsWallpaper(ImageSourceInfo img)
    {
        var path = _userImageManager.GetImagePath(img.Id);
        if (path == null) return;

        _settingsManager.ApplyAndSave(s =>
        {
            s.DisplayMode = DisplayMode.StillImage;
            s.StillImageSource = ImageSource.UserImages;
            s.UserImageSelectedId = img.Id;
            s.UserImageSelectedPath = path;
        });
        _renderScheduler.TriggerUpdate();
    }

    private void ToggleFavoriteFromCollection(ImageSourceInfo img)
    {
        // In the favorites grid, clicking star always unfavorites
        var existing = _settings.Favorites.Find(f =>
            f.Source == img.Source && f.ImageId == img.Id);
        if (existing != null)
        {
            _settings.Favorites.Remove(existing);
            _settingsManager.Save();
        }
    }

    private void ExportFavorites()
    {
        if (_settings.Favorites.Count == 0)
        {
            MessageBox.Show("No favorites to export.", "Export Favorites",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dialog = new FolderBrowserDialog
        {
            Description = "Choose a folder to export favorites to",
            UseDescriptionForTitle = true
        };
        if (dialog.ShowDialog() != DialogResult.OK) return;

        int exported = 0;
        foreach (var fav in _settings.Favorites)
        {
            string? cachedPath = null;

            // For user images, find in user_images directory
            if (fav.Source == ImageSource.UserImages)
            {
                cachedPath = _userImageManager.GetImagePath(fav.ImageId);
            }
            else
            {
                cachedPath = _imageCache.GetCachedPathForFavorite(fav);
            }

            if (cachedPath != null && File.Exists(cachedPath))
            {
                var safeName = ImageCache.SanitizeFileName(
                    !string.IsNullOrEmpty(fav.Title) ? fav.Title : fav.ImageId);
                var destPath = Path.Combine(dialog.SelectedPath,
                    safeName + Path.GetExtension(cachedPath));

                // Avoid overwriting
                int counter = 1;
                while (File.Exists(destPath))
                {
                    destPath = Path.Combine(dialog.SelectedPath,
                        $"{safeName}_{counter}{Path.GetExtension(cachedPath)}");
                    counter++;
                }

                try { File.Copy(cachedPath, destPath); exported++; }
                catch { }
            }
        }

        MessageBox.Show($"Exported {exported} of {_settings.Favorites.Count} favorites.",
            "Export Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void ClearAllFavorites()
    {
        if (_settings.Favorites.Count == 0) return;

        if (MessageBox.Show(
            $"Remove all {_settings.Favorites.Count} favorites?\n\nThis does not delete cached image files.",
            "Clear Favorites", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;

        _settings.Favorites.Clear();
        _settingsManager.Save();
        RefreshMyCollectionTab();
    }

    private async void ClearAllUserImages()
    {
        var imagePaths = _userImageManager.GetAllImagePaths();
        if (imagePaths.Count == 0) return;

        if (MessageBox.Show(
            $"Delete all {imagePaths.Count} user images?\n\nThis permanently removes the files.",
            "Clear User Images", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            return;

        // Also remove any user image favorites
        _settings.Favorites.RemoveAll(f => f.Source == ImageSource.UserImages);

        await Task.Run(() => _userImageManager.DeleteAllImages());
        if (IsDisposed) return;

        _settingsManager.Save();
        RefreshMyCollectionTab();
    }

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
