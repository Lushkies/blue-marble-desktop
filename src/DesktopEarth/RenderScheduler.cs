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
        _wallpaperPath = Path.Combine(Path.GetTempPath(), "BlueMarbleDesktop_wallpaper.bmp");
    }

    public void Start()
    {
        if (_renderThread != null) return;

        _stopRequested = false;
        _renderThread = new Thread(RenderLoop)
        {
            Name = "BlueMarbleDesktop-Render",
            IsBackground = true
        };
        _renderThread.Start();
    }

    public void Stop()
    {
        _stopRequested = true;
        _renderNowSignal.Set();
        _renderThread?.Join(5000);
        _renderThread = null;
    }

    public void TriggerUpdate()
    {
        _renderNowSignal.Set();
    }

    private void RenderLoop()
    {
        var settings = _settingsManager.Settings;
        var (renderWidth, renderHeight) = MonitorManager.GetRenderResolution(settings);

        var options = WindowOptions.Default;
        options.Size = new Vector2D<int>(renderWidth, renderHeight);
        options.Title = "Blue Marble Desktop";
        options.IsVisible = false;
        options.VSync = false;
        options.API = new GraphicsAPI(ContextAPI.OpenGL, ContextProfile.Core, ContextFlags.Default, new APIVersion(3, 3));

        // Renderers (created on GL thread)
        EarthRenderer? earthRenderer = null;
        FlatMapRenderer? flatMapRenderer = null;
        MoonRenderer? moonRenderer = null;
        DisplayMode currentMode = settings.DisplayMode;

        DateTime lastUpdate = DateTime.MinValue;
        bool firstRender = true;
        GL? gl = null;

        var window = Window.Create(options);

        window.Load += () =>
        {
            gl = GL.GetApi(window);
            InitializeRenderers(gl, settings, ref earthRenderer, ref flatMapRenderer, ref moonRenderer);
            ReportStatus("Renderer initialized.");
        };

        window.Render += (_) =>
        {
            if (gl == null || _stopRequested)
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

            try
            {
                // Re-read settings
                settings = _settingsManager.Settings;
                (renderWidth, renderHeight) = MonitorManager.GetRenderResolution(settings);

                // Re-initialize renderers if display mode changed
                if (settings.DisplayMode != currentMode)
                {
                    currentMode = settings.DisplayMode;
                    DisposeRenderers(ref earthRenderer, ref flatMapRenderer, ref moonRenderer);
                    InitializeRenderers(gl, settings, ref earthRenderer, ref flatMapRenderer, ref moonRenderer);
                }

                string modeName = settings.DisplayMode switch
                {
                    DisplayMode.FlatMap => "flat map",
                    DisplayMode.Moon => "moon",
                    _ => "earth"
                };
                ReportStatus($"Rendering {modeName}...");

                byte[] pixels = settings.DisplayMode switch
                {
                    DisplayMode.FlatMap => flatMapRenderer!.Render(renderWidth, renderHeight),
                    DisplayMode.Moon => moonRenderer!.Render(renderWidth, renderHeight),
                    _ => earthRenderer!.Render(renderWidth, renderHeight),
                };

                SaveAsBmp(pixels, renderWidth, renderHeight, _wallpaperPath);
                WallpaperSetter.SetWallpaper(_wallpaperPath, settings.MultiMonitorMode);
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
            DisposeRenderers(ref earthRenderer, ref flatMapRenderer, ref moonRenderer);
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

    private void InitializeRenderers(GL gl, AppSettings settings,
        ref EarthRenderer? earthRenderer, ref FlatMapRenderer? flatMapRenderer, ref MoonRenderer? moonRenderer)
    {
        string dayTexPath = _assets.GetDayTexturePath(settings.ImageStyle);
        string nightTexPath = _assets.GetNightTexturePath();
        string? bathyMaskPath = _assets.GetBathyMaskPath();

        switch (settings.DisplayMode)
        {
            case DisplayMode.FlatMap:
                flatMapRenderer = new FlatMapRenderer(settings);
                flatMapRenderer.Initialize(gl, dayTexPath, nightTexPath);
                break;
            case DisplayMode.Moon:
                try
                {
                    string moonTexPath = _assets.GetMoonTexturePath();
                    moonRenderer = new MoonRenderer(settings);
                    moonRenderer.Initialize(gl, moonTexPath);
                }
                catch (FileNotFoundException)
                {
                    ReportStatus("Moon texture not found, falling back to globe.");
                    earthRenderer = new EarthRenderer(settings);
                    earthRenderer.Initialize(gl, dayTexPath, nightTexPath, bathyMaskPath);
                }
                break;
            default:
                earthRenderer = new EarthRenderer(settings);
                earthRenderer.Initialize(gl, dayTexPath, nightTexPath, bathyMaskPath);
                break;
        }
    }

    private static void DisposeRenderers(ref EarthRenderer? earth, ref FlatMapRenderer? flatMap, ref MoonRenderer? moon)
    {
        earth?.Dispose(); earth = null;
        flatMap?.Dispose(); flatMap = null;
        moon?.Dispose(); moon = null;
    }

    private void ReportStatus(string message)
    {
        StatusChanged?.Invoke(message);
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
