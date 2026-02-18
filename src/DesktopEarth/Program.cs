using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using DesktopEarth;
using DesktopEarth.Rendering;

// ─── Load settings ───
var settingsManager = new SettingsManager();
settingsManager.Load();
var settings = settingsManager.Settings;

// ─── Locate assets ───
AssetLocator assets;
try
{
    assets = new AssetLocator();
}
catch (DirectoryNotFoundException ex)
{
    Console.WriteLine($"ERROR: {ex.Message}");
    return;
}

Console.WriteLine($"Using textures from: {assets.TexturesDir}");

string dayTexPath = assets.GetDayTexturePath(settings.ImageStyle);
string nightTexPath = assets.GetNightTexturePath();
string? bathyMaskPath = assets.GetBathyMaskPath();

Console.WriteLine($"Day texture: {Path.GetFileName(dayTexPath)}");
Console.WriteLine($"Night texture: {Path.GetFileName(nightTexPath)}");

// ─── Determine render resolution ───
int renderWidth = settings.RenderWidth > 0 ? settings.RenderWidth : 1920;
int renderHeight = settings.RenderHeight > 0 ? settings.RenderHeight : 1080;

// ─── State ───
EarthRenderer? renderer = null;
string wallpaperPath = Path.Combine(Path.GetTempPath(), "DesktopEarth_wallpaper.bmp");
DateTime lastWallpaperUpdate = DateTime.MinValue;
bool firstRender = true;

// ─── Create hidden window for offscreen rendering ───
var options = WindowOptions.Default;
options.Size = new Vector2D<int>(renderWidth, renderHeight);
options.Title = "Desktop Earth";
options.IsVisible = false;
options.VSync = false;
options.API = new GraphicsAPI(ContextAPI.OpenGL, ContextProfile.Core, ContextFlags.Default, new APIVersion(3, 3));

var window = Window.Create(options);
window.Load += OnLoad;
window.Render += OnRender;
window.Closing += OnClose;

Console.WriteLine("Starting Desktop Earth renderer...");
Console.WriteLine($"Wallpaper will update every {settings.UpdateIntervalSeconds} seconds.");
Console.WriteLine($"Wallpaper saved to: {wallpaperPath}");
Console.WriteLine("Press Ctrl+C to stop.");

window.Run();

// ─── Callbacks ───

void OnLoad()
{
    var gl = GL.GetApi(window);
    renderer = new EarthRenderer(settings);
    renderer.Initialize(gl, dayTexPath, nightTexPath, bathyMaskPath);
    Console.WriteLine("Renderer initialized.");
}

void OnRender(double deltaTime)
{
    if (renderer == null) return;

    var now = DateTime.UtcNow;
    if (!firstRender && (now - lastWallpaperUpdate).TotalSeconds < settings.UpdateIntervalSeconds)
    {
        Thread.Sleep(1000);
        return;
    }

    firstRender = false;
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Rendering earth...");

    byte[] pixels = renderer.Render(renderWidth, renderHeight);

    SaveAsBmp(pixels, renderWidth, renderHeight, wallpaperPath);
    WallpaperSetter.SetWallpaper(wallpaperPath);
    lastWallpaperUpdate = now;
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Wallpaper updated.");
}

void OnClose()
{
    renderer?.Dispose();
    Console.WriteLine("Desktop Earth stopped.");
}

void SaveAsBmp(byte[] rgbaPixels, int width, int height, string path)
{
    using var image = new Image<Rgba32>(width, height);
    for (int y = 0; y < height; y++)
    {
        int srcRow = (height - 1 - y) * width * 4;
        for (int x = 0; x < width; x++)
        {
            int idx = srcRow + x * 4;
            image[x, y] = new Rgba32(rgbaPixels[idx], rgbaPixels[idx + 1], rgbaPixels[idx + 2], 255);
        }
    }
    image.SaveAsBmp(path);
}
