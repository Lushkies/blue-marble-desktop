using DesktopEarth;
using DesktopEarth.UI;

// ─── Single instance check ───
const string mutexName = "BlueMarbleDesktop-SingleInstance-Mutex";
using var mutex = new Mutex(true, mutexName, out bool isNewInstance);

if (!isNewInstance)
{
    // Another instance is already running
    MessageBox.Show(
        "Blue Marble Desktop is already running.\nCheck the system tray for its icon.",
        "Blue Marble Desktop",
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
        "Blue Marble Desktop - Missing Assets",
        MessageBoxButtons.OK,
        MessageBoxIcon.Error);
    return;
}

// ─── Start application ───
Application.EnableVisualStyles();
Application.SetCompatibleTextRenderingDefault(false);
Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);

// Global exception handlers for diagnostics
Application.ThreadException += (_, args) =>
{
    var logPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "BlueMarbleDesktop", "crash.log");
    try
    {
        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
        File.AppendAllText(logPath,
            $"\n[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] UI THREAD EXCEPTION:\n{args.Exception}\n");
    }
    catch { }
    MessageBox.Show(
        $"An error occurred:\n{args.Exception.Message}\n\nDetails saved to:\n{logPath}",
        "Blue Marble Desktop Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
};
AppDomain.CurrentDomain.UnhandledException += (_, args) =>
{
    var logPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "BlueMarbleDesktop", "crash.log");
    try
    {
        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
        File.AppendAllText(logPath,
            $"\n[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] UNHANDLED EXCEPTION:\n{args.ExceptionObject}\n");
    }
    catch { }
};

var renderScheduler = new RenderScheduler(settingsManager, assets);
var trayContext = new TrayApplicationContext(settingsManager, renderScheduler);

Application.Run(trayContext);

renderScheduler.Dispose();
