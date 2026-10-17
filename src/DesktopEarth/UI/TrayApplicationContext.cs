using System.Drawing;

namespace DesktopEarth.UI;

public class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _trayIcon;
    private readonly SettingsManager _settingsManager;
    private readonly RenderScheduler _renderScheduler;
    private SettingsForm? _settingsForm;

    // App-level so the ~975 MB HD texture download survives the settings form
    // being closed or recreated (dark-mode toggle) mid-download
    private readonly HiResTextureManager _hdTextureManager = new();

    public TrayApplicationContext(SettingsManager settingsManager, RenderScheduler renderScheduler,
        bool showSettingsOnLaunch = true)
    {
        _settingsManager = settingsManager;
        _renderScheduler = renderScheduler;

        // Initialize UI theme from saved settings
        Theme.IsDarkMode = _settingsManager.Settings.DarkModeEnabled;
        Theme.EnableDarkScrollbars();

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

        // Open settings window on launch — skipped for silent --startup launches
        // (Windows login) so the app starts quietly in the tray
        if (showSettingsOnLaunch)
            ShowSettings();
    }

    private ContextMenuStrip BuildContextMenu()
    {
        var menu = new ContextMenuStrip { Renderer = new Controls.StudioMenuRenderer() };

        var updateNowItem = new ToolStripMenuItem("Update wallpaper now");
        updateNowItem.Click += (_, _) => _renderScheduler.TriggerUserUpdate();

        var favoriteItem = new ToolStripMenuItem("Favorite current wallpaper");
        favoriteItem.Click += (_, _) => FavoriteCurrentWallpaper();

        var settingsItem = new ToolStripMenuItem("Open Blue Marble Desktop") { Font = Theme.Semibold(9.5f) };
        settingsItem.Click += (_, _) => ShowSettings();

        var aboutItem = new ToolStripMenuItem("About");
        aboutItem.Click += (_, _) => ShowAbout();

        var exitItem = new ToolStripMenuItem("Exit");
        exitItem.Click += (_, _) => ExitApplication();

        menu.Font = Theme.UiFont(9.5f);
        menu.Items.Add(settingsItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(updateNowItem);
        menu.Items.Add(favoriteItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(aboutItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exitItem);

        return menu;
    }

    private void ShowSettings()
    {
        if (_settingsForm != null && !_settingsForm.IsDisposed)
        {
            // BringToFront/Activate don't restore a minimized window
            if (_settingsForm.WindowState == FormWindowState.Minimized)
                _settingsForm.WindowState = FormWindowState.Normal;
            _settingsForm.BringToFront();
            _settingsForm.Activate();
            return;
        }

        _settingsForm = new SettingsForm(_settingsManager, _renderScheduler, _hdTextureManager);
        _settingsForm.RequestReopen += RecreateSettingsForm;
        _settingsForm.Show();
    }

    /// <summary>
    /// Close and recreate the settings form (used for theme changes that require full repaint).
    /// Preserves window position so the form doesn't jump to center-screen.
    /// </summary>
    internal void RecreateSettingsForm()
    {
        Point? savedLocation = null;
        int savedTabIndex = 0;
        if (_settingsForm != null && !_settingsForm.IsDisposed)
        {
            savedLocation = _settingsForm.Location;
            savedTabIndex = _settingsForm.SelectedPage;
            _settingsForm.RequestReopen -= RecreateSettingsForm;
            _settingsForm.Close();
            _settingsForm.Dispose();
        }
        _settingsForm = null;

        // Re-enable dark scrollbars (needed when toggling dark mode on)
        Theme.EnableDarkScrollbars();

        // Recreate directly (not via ShowSettings) so the saved position is
        // applied BEFORE Show — otherwise the window flashes at center-screen
        // and then jumps to its old spot
        _settingsForm = new SettingsForm(_settingsManager, _renderScheduler, _hdTextureManager);
        _settingsForm.RequestReopen += RecreateSettingsForm;
        if (savedLocation.HasValue)
        {
            _settingsForm.StartPosition = FormStartPosition.Manual;
            _settingsForm.Location = savedLocation.Value;
        }
        _settingsForm.Show();
        _settingsForm.SelectedPage = savedTabIndex;
    }

    private void FavoriteCurrentWallpaper()
    {
        var fav = _renderScheduler.GetCurrentAsFavorite();
        if (fav == null)
        {
            _trayIcon.BalloonTipTitle = "Blue Marble Desktop";
            _trayIcon.BalloonTipText = "No still image is currently displayed to favorite.";
            _trayIcon.ShowBalloonTip(3000);
            return;
        }

        var settings = _settingsManager.Settings;

        // Thread-safe access to Favorites list (render thread may be iterating it)
        lock (settings.FavoritesLock)
        {
            // Check if already favorited
            if (settings.Favorites.Any(f => f.Source == fav.Source && f.ImageId == fav.ImageId))
            {
                _trayIcon.BalloonTipTitle = "Blue Marble Desktop";
                _trayIcon.BalloonTipText = "This image is already in your favorites.";
                _trayIcon.ShowBalloonTip(3000);
                return;
            }

            settings.Favorites.Add(fav);
        }
        _settingsManager.Save();

        _trayIcon.BalloonTipTitle = "Blue Marble Desktop";
        _trayIcon.BalloonTipText = "Added to favorites! View in My Collection tab.";
        _trayIcon.ShowBalloonTip(3000);
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

    internal static Icon LoadAppIcon()
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
