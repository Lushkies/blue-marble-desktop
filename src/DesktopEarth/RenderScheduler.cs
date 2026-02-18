using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using DesktopEarth.Rendering;

namespace DesktopEarth;

/// <summary>
/// Manages a dedicated background thread that owns the GLFW/OpenGL context,
/// renders the earth on a timer, and sets the wallpaper.
/// </summary>
public class RenderScheduler : IDisposable
{
    private readonly SettingsManager _settingsManager;
    private readonly AssetLocator _assets;
    private Thread? _renderThread;
    private volatile bool _stopRequested;
    private readonly ManualResetEventSlim _renderNowSignal = new(false);
    private readonly string _wallpaperPath;

    public event Action<string>? StatusChanged;

    public RenderScheduler(SettingsManager settingsManager, AssetLocator assets)
    {
        _settingsManager = settingsManager;
        _assets = assets;
        _wallpaperPath = Path.Combine(Path.GetTempPath(), "DesktopEarth_wallpaper.bmp");
    }

    public void Start()
    {
        if (_renderThread != null) return;

        _stopRequested = false;
        _renderThread = new Thread(RenderLoop)
        {
            Name = "DesktopEarth-Render",
            IsBackground = true
        };
        _renderThread.Start();
    }

    public void Stop()
    {
        _stopRequested = true;
        _renderNowSignal.Set(); // Wake up if sleeping
        _renderThread?.Join(5000);
        _renderThread = null;
    }

    /// <summary>
    /// Triggers an immediate wallpaper update (e.g. from tray menu "Update Now").
    /// </summary>
    public void TriggerUpdate()
    {
        _renderNowSignal.Set();
    }

    private void RenderLoop()
    {
        var settings = _settingsManager.Settings;

        int renderWidth = settings.RenderWidth > 0 ? settings.RenderWidth : GetPrimaryScreenWidth();
        int renderHeight = settings.RenderHeight > 0 ? settings.RenderHeight : GetPrimaryScreenHeight();

        string dayTexPath = _assets.GetDayTexturePath(settings.ImageStyle);
        string nightTexPath = _assets.GetNightTexturePath();
        string? bathyMaskPath = _assets.GetBathyMaskPath();

        // Create hidden GLFW window (must be on this thread since it owns the GL context)
        var options = WindowOptions.Default;
        options.Size = new Vector2D<int>(renderWidth, renderHeight);
        options.Title = "Desktop Earth";
        options.IsVisible = false;
        options.VSync = false;
        options.API = new GraphicsAPI(ContextAPI.OpenGL, ContextProfile.Core, ContextFlags.Default, new APIVersion(3, 3));

        EarthRenderer? renderer = null;
        DateTime lastUpdate = DateTime.MinValue;
        bool firstRender = true;

        var window = Window.Create(options);

        window.Load += () =>
        {
            var gl = GL.GetApi(window);
            renderer = new EarthRenderer(settings);
            renderer.Initialize(gl, dayTexPath, nightTexPath, bathyMaskPath);
            ReportStatus("Renderer initialized.");
        };

        window.Render += (_) =>
        {
            if (renderer == null || _stopRequested)
            {
                if (_stopRequested) window.Close();
                return;
            }

            var now = DateTime.UtcNow;
            bool signaled = _renderNowSignal.IsSet;

            if (!firstRender && !signaled &&
                (now - lastUpdate).TotalSeconds < settings.UpdateIntervalSeconds)
            {
                Thread.Sleep(500);
                return;
            }

            _renderNowSignal.Reset();
            firstRender = false;

            ReportStatus($"Rendering earth...");

            try
            {
                // Re-read settings in case they changed
                settings = _settingsManager.Settings;
                renderWidth = settings.RenderWidth > 0 ? settings.RenderWidth : GetPrimaryScreenWidth();
                renderHeight = settings.RenderHeight > 0 ? settings.RenderHeight : GetPrimaryScreenHeight();

                byte[] pixels = renderer.Render(renderWidth, renderHeight);
                SaveAsBmp(pixels, renderWidth, renderHeight, _wallpaperPath);
                WallpaperSetter.SetWallpaper(_wallpaperPath);
                lastUpdate = DateTime.UtcNow;
                ReportStatus($"Wallpaper updated at {DateTime.Now:HH:mm:ss}");
            }
            catch (Exception ex)
            {
                ReportStatus($"Render error: {ex.Message}");
            }
        };

        window.Closing += () =>
        {
            renderer?.Dispose();
        };

        try
        {
            window.Run();
        }
        catch (Exception ex)
        {
            ReportStatus($"Render thread error: {ex.Message}");
        }
    }

    private void ReportStatus(string message)
    {
        StatusChanged?.Invoke(message);
    }

    private static int GetPrimaryScreenWidth()
    {
        try { return System.Windows.Forms.Screen.PrimaryScreen?.Bounds.Width ?? 1920; }
        catch { return 1920; }
    }

    private static int GetPrimaryScreenHeight()
    {
        try { return System.Windows.Forms.Screen.PrimaryScreen?.Bounds.Height ?? 1080; }
        catch { return 1080; }
    }

    private static void SaveAsBmp(byte[] rgbaPixels, int width, int height, string path)
    {
        using var image = new SixLabors.ImageSharp.Image<Rgba32>(width, height);
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

    public void Dispose()
    {
        Stop();
        _renderNowSignal.Dispose();
    }
}
