using DesktopEarth;
using DesktopEarth.UI;

// ─── Single instance check ───
const string mutexName = "DesktopEarth-SingleInstance-Mutex";
using var mutex = new Mutex(true, mutexName, out bool isNewInstance);

if (!isNewInstance)
{
    // Another instance is already running
    MessageBox.Show(
        "Desktop Earth is already running.\nCheck the system tray for its icon.",
        "Desktop Earth",
        MessageBoxButtons.OK,
        MessageBoxIcon.Information);
    return;
}

// ─── Load settings ───
var settingsManager = new SettingsManager();
settingsManager.Load();

// ─── Locate assets ───
AssetLocator assets;
try
{
    assets = new AssetLocator();
}
catch (DirectoryNotFoundException ex)
{
    MessageBox.Show(
        $"Could not find texture files:\n{ex.Message}\n\nMake sure the 'assets/textures' folder is present.",
        "Desktop Earth - Missing Assets",
        MessageBoxButtons.OK,
        MessageBoxIcon.Error);
    return;
}

// ─── Start application ───
Application.EnableVisualStyles();
Application.SetCompatibleTextRenderingDefault(false);
Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);

var renderScheduler = new RenderScheduler(settingsManager, assets);
var trayContext = new TrayApplicationContext(settingsManager, renderScheduler);

Application.Run(trayContext);

renderScheduler.Dispose();
