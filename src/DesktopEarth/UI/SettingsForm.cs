using System.Drawing;

namespace DesktopEarth.UI;

public class SettingsForm : Form
{
    private readonly SettingsManager _settingsManager;
    private readonly RenderScheduler _renderScheduler;
    private AppSettings _settings;

    // General tab
    private ComboBox _updateIntervalCombo = null!;
    private CheckBox _runOnStartupCheck = null!;
    private ComboBox _displayModeCombo = null!;

    // View tab
    private TrackBar _zoomSlider = null!;
    private TrackBar _fovSlider = null!;
    private TrackBar _tiltSlider = null!;
    private Label _zoomValue = null!;
    private Label _fovValue = null!;
    private Label _tiltValue = null!;

    // Lighting tab
    private CheckBox _nightLightsCheck = null!;
    private TrackBar _nightBrightnessSlider = null!;
    private TrackBar _ambientSlider = null!;
    private CheckBox _specularCheck = null!;
    private TrackBar _specularIntensitySlider = null!;
    private Label _nightBrightnessValue = null!;
    private Label _ambientValue = null!;
    private Label _specularIntensityValue = null!;

    // Textures tab
    private RadioButton _topoRadio = null!;
    private RadioButton _topoBathyRadio = null!;

    // Display tab
    private RadioButton _sameForAllRadio = null!;
    private RadioButton _spanAcrossRadio = null!;
    private ComboBox _renderResCombo = null!;

    public SettingsForm(SettingsManager settingsManager, RenderScheduler renderScheduler)
    {
        _settingsManager = settingsManager;
        _renderScheduler = renderScheduler;
        _settings = settingsManager.Settings;

        InitializeForm();
        LoadCurrentSettings();
    }

    private void InitializeForm()
    {
        Text = "Blue Marble Desktop Settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(460, 420);
        ShowInTaskbar = true;

        var tabControl = new TabControl
        {
            Dock = DockStyle.Fill,
            Padding = new Point(8, 4)
        };

        tabControl.TabPages.Add(CreateGeneralTab());
        tabControl.TabPages.Add(CreateViewTab());
        tabControl.TabPages.Add(CreateLightingTab());
        tabControl.TabPages.Add(CreateTexturesTab());
        tabControl.TabPages.Add(CreateDisplayTab());

        var bottomPanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 50,
            Padding = new Padding(10)
        };

        var applyButton = new Button
        {
            Text = "Apply",
            Size = new Size(80, 30),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right
        };
        applyButton.Location = new Point(bottomPanel.Width - applyButton.Width - 190, 10);
        applyButton.Click += (_, _) => ApplySettings();

        var okButton = new Button
        {
            Text = "OK",
            Size = new Size(80, 30),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right
        };
        okButton.Location = new Point(bottomPanel.Width - okButton.Width - 100, 10);
        okButton.Click += (_, _) =>
        {
            ApplySettings();
            Close();
        };

        var cancelButton = new Button
        {
            Text = "Cancel",
            Size = new Size(80, 30),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
            DialogResult = DialogResult.Cancel
        };
        cancelButton.Location = new Point(bottomPanel.Width - cancelButton.Width - 10, 10);

        bottomPanel.Controls.AddRange([applyButton, okButton, cancelButton]);
        CancelButton = cancelButton;

        Controls.Add(tabControl);
        Controls.Add(bottomPanel);
    }

    private TabPage CreateGeneralTab()
    {
        var tab = new TabPage("General");
        int y = 20;

        // Update interval
        tab.Controls.Add(MakeLabel("Update every:", 20, y));
        _updateIntervalCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(160, y - 2),
            Width = 200
        };
        _updateIntervalCombo.Items.AddRange([
            "1 minute", "2 minutes", "5 minutes", "10 minutes",
            "15 minutes", "30 minutes", "1 hour"
        ]);
        tab.Controls.Add(_updateIntervalCombo);
        y += 40;

        // Display mode
        tab.Controls.Add(MakeLabel("Display mode:", 20, y));
        _displayModeCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(160, y - 2),
            Width = 200
        };
        _displayModeCombo.Items.AddRange(["Spherical Globe", "Flat Map", "Moon"]);
        tab.Controls.Add(_displayModeCombo);
        y += 40;

        // Run on startup
        _runOnStartupCheck = new CheckBox
        {
            Text = "Run Blue Marble Desktop when Windows starts",
            AutoSize = true,
            Location = new Point(20, y)
        };
        tab.Controls.Add(_runOnStartupCheck);

        return tab;
    }

    private TabPage CreateViewTab()
    {
        var tab = new TabPage("View");
        int y = 20;

        // Zoom
        tab.Controls.Add(MakeLabel("Zoom:", 20, y));
        _zoomValue = MakeLabel("2.8", 370, y);
        tab.Controls.Add(_zoomValue);
        y += 22;
        _zoomSlider = MakeSlider(20, y, 10, 60, 28); // 1.0 to 6.0, value * 10
        _zoomSlider.Scroll += (_, _) =>
            _zoomValue.Text = (_zoomSlider.Value / 10f).ToString("F1");
        tab.Controls.Add(_zoomSlider);
        y += 50;

        // FOV
        tab.Controls.Add(MakeLabel("Field of View:", 20, y));
        _fovValue = MakeLabel("45", 370, y);
        tab.Controls.Add(_fovValue);
        y += 22;
        _fovSlider = MakeSlider(20, y, 20, 90, 45);
        _fovSlider.Scroll += (_, _) =>
            _fovValue.Text = _fovSlider.Value.ToString();
        tab.Controls.Add(_fovSlider);
        y += 50;

        // Camera tilt
        tab.Controls.Add(MakeLabel("Camera Tilt:", 20, y));
        _tiltValue = MakeLabel("20", 370, y);
        tab.Controls.Add(_tiltValue);
        y += 22;
        _tiltSlider = MakeSlider(20, y, -45, 45, 20);
        _tiltSlider.Scroll += (_, _) =>
            _tiltValue.Text = _tiltSlider.Value.ToString() + "\u00b0";
        tab.Controls.Add(_tiltSlider);

        return tab;
    }

    private TabPage CreateLightingTab()
    {
        var tab = new TabPage("Lighting");
        int y = 20;

        // Night lights
        _nightLightsCheck = new CheckBox
        {
            Text = "Show night lights",
            AutoSize = true,
            Location = new Point(20, y)
        };
        _nightLightsCheck.CheckedChanged += (_, _) =>
            _nightBrightnessSlider.Enabled = _nightLightsCheck.Checked;
        tab.Controls.Add(_nightLightsCheck);
        y += 30;

        tab.Controls.Add(MakeLabel("Night brightness:", 40, y));
        _nightBrightnessValue = MakeLabel("1.2", 370, y);
        tab.Controls.Add(_nightBrightnessValue);
        y += 22;
        _nightBrightnessSlider = MakeSlider(40, y, 1, 30, 12); // 0.1 to 3.0, value * 10
        _nightBrightnessSlider.Scroll += (_, _) =>
            _nightBrightnessValue.Text = (_nightBrightnessSlider.Value / 10f).ToString("F1");
        tab.Controls.Add(_nightBrightnessSlider);
        y += 50;

        // Ambient light
        tab.Controls.Add(MakeLabel("Ambient light:", 20, y));
        _ambientValue = MakeLabel("0.15", 370, y);
        tab.Controls.Add(_ambientValue);
        y += 22;
        _ambientSlider = MakeSlider(20, y, 0, 50, 15); // 0.00 to 0.50, value / 100
        _ambientSlider.Scroll += (_, _) =>
            _ambientValue.Text = (_ambientSlider.Value / 100f).ToString("F2");
        tab.Controls.Add(_ambientSlider);
        y += 50;

        // Specular
        _specularCheck = new CheckBox
        {
            Text = "Sun specular reflection on water",
            AutoSize = true,
            Location = new Point(20, y)
        };
        _specularCheck.CheckedChanged += (_, _) =>
            _specularIntensitySlider.Enabled = _specularCheck.Checked;
        tab.Controls.Add(_specularCheck);
        y += 30;

        tab.Controls.Add(MakeLabel("Specular intensity:", 40, y));
        _specularIntensityValue = MakeLabel("0.5", 370, y);
        tab.Controls.Add(_specularIntensityValue);
        y += 22;
        _specularIntensitySlider = MakeSlider(40, y, 0, 20, 5); // 0.0 to 2.0, value * 10
        _specularIntensitySlider.Scroll += (_, _) =>
            _specularIntensityValue.Text = (_specularIntensitySlider.Value / 10f).ToString("F1");
        tab.Controls.Add(_specularIntensitySlider);

        return tab;
    }

    private TabPage CreateTexturesTab()
    {
        var tab = new TabPage("Textures");
        int y = 20;

        var groupBox = new GroupBox
        {
            Text = "Earth Image Style",
            Location = new Point(20, y),
            Size = new Size(380, 100)
        };

        _topoRadio = new RadioButton
        {
            Text = "Topographic (land only)",
            AutoSize = true,
            Location = new Point(20, 30)
        };

        _topoBathyRadio = new RadioButton
        {
            Text = "Topographic + Bathymetry (land + ocean floor)",
            AutoSize = true,
            Location = new Point(20, 58)
        };

        groupBox.Controls.AddRange([_topoRadio, _topoBathyRadio]);
        tab.Controls.Add(groupBox);

        y += 120;

        var noteLabel = new Label
        {
            Text = "Note: Changing texture style requires a wallpaper refresh.\nClick Apply, then use 'Update Now' from the tray menu.",
            Font = new Font("Segoe UI", 8.5f),
            ForeColor = Color.Gray,
            AutoSize = true,
            Location = new Point(22, y)
        };
        tab.Controls.Add(noteLabel);

        return tab;
    }

    private TabPage CreateDisplayTab()
    {
        var tab = new TabPage("Display");
        int y = 20;

        // Multi-monitor
        var monitorGroup = new GroupBox
        {
            Text = "Multi-Monitor",
            Location = new Point(20, y),
            Size = new Size(380, 90)
        };

        _sameForAllRadio = new RadioButton
        {
            Text = "Same wallpaper on all monitors",
            AutoSize = true,
            Location = new Point(20, 28)
        };

        _spanAcrossRadio = new RadioButton
        {
            Text = "Span wallpaper across all monitors",
            AutoSize = true,
            Location = new Point(20, 56)
        };

        monitorGroup.Controls.AddRange([_sameForAllRadio, _spanAcrossRadio]);
        tab.Controls.Add(monitorGroup);

        y += 110;

        // Render resolution
        tab.Controls.Add(MakeLabel("Render resolution:", 20, y));
        _renderResCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(160, y - 2),
            Width = 200
        };
        _renderResCombo.Items.AddRange([
            "Auto (match monitor)",
            "1920 x 1080",
            "2560 x 1440",
            "3840 x 2160",
            "5120 x 2880"
        ]);
        tab.Controls.Add(_renderResCombo);

        return tab;
    }

    private void LoadCurrentSettings()
    {
        // General
        _updateIntervalCombo.SelectedIndex = _settings.UpdateIntervalSeconds switch
        {
            60 => 0,
            120 => 1,
            300 => 2,
            600 => 3,
            900 => 4,
            1800 => 5,
            3600 => 6,
            _ => 3
        };

        _displayModeCombo.SelectedIndex = (int)_settings.DisplayMode;
        _runOnStartupCheck.Checked = StartupManager.IsRunOnStartup();

        // View
        _zoomSlider.Value = Math.Clamp((int)(_settings.ZoomLevel * 10), _zoomSlider.Minimum, _zoomSlider.Maximum);
        _zoomValue.Text = _settings.ZoomLevel.ToString("F1");
        _fovSlider.Value = Math.Clamp((int)_settings.FieldOfView, _fovSlider.Minimum, _fovSlider.Maximum);
        _fovValue.Text = _settings.FieldOfView.ToString("F0");
        _tiltSlider.Value = Math.Clamp((int)_settings.CameraTilt, _tiltSlider.Minimum, _tiltSlider.Maximum);
        _tiltValue.Text = _settings.CameraTilt.ToString("F0") + "\u00b0";

        // Lighting
        _nightLightsCheck.Checked = _settings.NightLightsEnabled;
        _nightBrightnessSlider.Value = Math.Clamp((int)(_settings.NightLightsBrightness * 10), _nightBrightnessSlider.Minimum, _nightBrightnessSlider.Maximum);
        _nightBrightnessValue.Text = _settings.NightLightsBrightness.ToString("F1");
        _nightBrightnessSlider.Enabled = _settings.NightLightsEnabled;
        _ambientSlider.Value = Math.Clamp((int)(_settings.AmbientLight * 100), _ambientSlider.Minimum, _ambientSlider.Maximum);
        _ambientValue.Text = _settings.AmbientLight.ToString("F2");
        _specularCheck.Checked = _settings.SunSpecularIntensity > 0;
        _specularIntensitySlider.Value = Math.Clamp((int)(_settings.SunSpecularIntensity * 10), _specularIntensitySlider.Minimum, _specularIntensitySlider.Maximum);
        _specularIntensityValue.Text = _settings.SunSpecularIntensity.ToString("F1");
        _specularIntensitySlider.Enabled = _settings.SunSpecularIntensity > 0;

        // Textures
        if (_settings.ImageStyle == ImageStyle.TopoBathy)
            _topoBathyRadio.Checked = true;
        else
            _topoRadio.Checked = true;

        // Display
        if (_settings.MultiMonitorMode == MultiMonitorMode.SpanAcross)
            _spanAcrossRadio.Checked = true;
        else
            _sameForAllRadio.Checked = true;

        _renderResCombo.SelectedIndex = (_settings.RenderWidth, _settings.RenderHeight) switch
        {
            (1920, 1080) => 1,
            (2560, 1440) => 2,
            (3840, 2160) => 3,
            (5120, 2880) => 4,
            _ => 0
        };
    }

    private void ApplySettings()
    {
        _settingsManager.ApplyAndSave(s =>
        {
            // General
            s.UpdateIntervalSeconds = _updateIntervalCombo.SelectedIndex switch
            {
                0 => 60,
                1 => 120,
                2 => 300,
                3 => 600,
                4 => 900,
                5 => 1800,
                6 => 3600,
                _ => 600
            };

            s.DisplayMode = (DisplayMode)_displayModeCombo.SelectedIndex;

            // View
            s.ZoomLevel = _zoomSlider.Value / 10f;
            s.FieldOfView = _fovSlider.Value;
            s.CameraTilt = _tiltSlider.Value;

            // Lighting
            s.NightLightsEnabled = _nightLightsCheck.Checked;
            s.NightLightsBrightness = _nightBrightnessSlider.Value / 10f;
            s.AmbientLight = _ambientSlider.Value / 100f;
            s.SunSpecularIntensity = _specularCheck.Checked ? _specularIntensitySlider.Value / 10f : 0.0f;

            // Textures
            s.ImageStyle = _topoBathyRadio.Checked ? ImageStyle.TopoBathy : ImageStyle.Topo;

            // Display
            s.MultiMonitorMode = _spanAcrossRadio.Checked
                ? MultiMonitorMode.SpanAcross
                : MultiMonitorMode.SameForAll;

            (s.RenderWidth, s.RenderHeight) = _renderResCombo.SelectedIndex switch
            {
                1 => (1920, 1080),
                2 => (2560, 1440),
                3 => (3840, 2160),
                4 => (5120, 2880),
                _ => (0, 0)
            };
        });

        // Apply startup setting separately (registry)
        StartupManager.SetRunOnStartup(_runOnStartupCheck.Checked);
    }

    // Helper methods
    private static Label MakeLabel(string text, int x, int y) => new()
    {
        Text = text,
        AutoSize = true,
        Location = new Point(x, y),
        Font = new Font("Segoe UI", 9)
    };

    private static TrackBar MakeSlider(int x, int y, int min, int max, int value) => new()
    {
        Location = new Point(x, y),
        Width = 340,
        Minimum = min,
        Maximum = max,
        Value = Math.Clamp(value, min, max),
        TickStyle = TickStyle.None
    };
}
