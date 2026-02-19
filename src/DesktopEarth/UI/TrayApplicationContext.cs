using System.Drawing;
using System.Reflection;
using AutoUpdaterDotNET;

namespace DesktopEarth.UI;

public class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _trayIcon;
    private readonly SettingsManager _settingsManager;
    private readonly RenderScheduler _renderScheduler;
    private SettingsForm? _settingsForm;

    // Update manifest URL — change this when hosting the update feed
    private const string UpdateUrl = "https://raw.githubusercontent.com/youruser/blue-marble-desktop/main/update.xml";

    public TrayApplicationContext(SettingsManager settingsManager, RenderScheduler renderScheduler)
    {
        _settingsManager = settingsManager;
        _renderScheduler = renderScheduler;

        _trayIcon = new NotifyIcon
        {
            Icon = LoadAppIcon(),
            Text = "Blue Marble Desktop",
            Visible = true,
            ContextMenuStrip = BuildContextMenu()
        };

        _trayIcon.DoubleClick += (_, _) => ShowSettings();

        _renderScheduler.StatusChanged += OnStatusChanged;
        _renderScheduler.Start();

        // Check for updates silently after 10 second delay
        ConfigureAutoUpdater();
        var updateTimer = new System.Windows.Forms.Timer { Interval = 10000 };
        updateTimer.Tick += (_, _) =>
        {
            updateTimer.Stop();
            updateTimer.Dispose();
            AutoUpdater.Start(UpdateUrl);
        };
        updateTimer.Start();
    }

    private static void ConfigureAutoUpdater()
    {
        AutoUpdater.ShowSkipButton = true;
        AutoUpdater.ShowRemindLaterButton = true;
        AutoUpdater.RunUpdateAsAdmin = false;
        AutoUpdater.ReportErrors = false; // Silent on network errors
        AutoUpdater.Synchronous = false;
        AutoUpdater.AppTitle = "Blue Marble Desktop";

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        if (version != null)
            AutoUpdater.InstalledVersion = version;
    }

    private ContextMenuStrip BuildContextMenu()
    {
        var menu = new ContextMenuStrip();

        var updateNowItem = new ToolStripMenuItem("Update Wallpaper Now");
        updateNowItem.Click += (_, _) => _renderScheduler.TriggerUpdate();

        var settingsItem = new ToolStripMenuItem("Settings...");
        settingsItem.Click += (_, _) => ShowSettings();

        var checkUpdatesItem = new ToolStripMenuItem("Check for Updates...");
        checkUpdatesItem.Click += (_, _) =>
        {
            AutoUpdater.ReportErrors = true; // Show errors when manually checking
            AutoUpdater.Start(UpdateUrl);
            AutoUpdater.ReportErrors = false;
        };

        var aboutItem = new ToolStripMenuItem("About Blue Marble Desktop");
        aboutItem.Click += (_, _) => ShowAbout();

        var exitItem = new ToolStripMenuItem("Exit");
        exitItem.Click += (_, _) => ExitApplication();

        menu.Items.Add(updateNowItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(settingsItem);
        menu.Items.Add(checkUpdatesItem);
        menu.Items.Add(aboutItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exitItem);

        return menu;
    }

    private void ShowSettings()
    {
        if (_settingsForm != null && !_settingsForm.IsDisposed)
        {
            _settingsForm.BringToFront();
            _settingsForm.Activate();
            return;
        }

        _settingsForm = new SettingsForm(_settingsManager, _renderScheduler);
        _settingsForm.Show();
    }

    private void ShowAbout()
    {
        using var about = new AboutForm();
        about.ShowDialog();
    }

    private void ExitApplication()
    {
        _renderScheduler.Stop();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        Application.Exit();
    }

    private void OnStatusChanged(string status)
    {
        try
        {
            string text = $"Blue Marble Desktop - {status}";
            if (text.Length > 63) text = text[..63];
            _trayIcon.Text = text;
        }
        catch { /* Ignore cross-thread issues during shutdown */ }
    }

    private static Icon LoadAppIcon()
    {
        string iconPath = Path.Combine(AppContext.BaseDirectory, "Resources", "bluemarbledesktop.ico");
        if (File.Exists(iconPath))
        {
            try { return new Icon(iconPath); }
            catch { }
        }

        string exeDir = AppContext.BaseDirectory;
        string[] searchPaths =
        [
            Path.Combine(exeDir, "bluemarbledesktop.ico"),
            Path.Combine(exeDir, "..", "..", "..", "Resources", "bluemarbledesktop.ico"),
        ];
        foreach (var path in searchPaths)
        {
            if (File.Exists(path))
            {
                try { return new Icon(path); }
                catch { }
            }
        }

        return SystemIcons.Application;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _renderScheduler.StatusChanged -= OnStatusChanged;
            _trayIcon.Dispose();
        }
        base.Dispose(disposing);
    }
}
