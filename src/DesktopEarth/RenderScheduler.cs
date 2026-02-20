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

    // EPIC support
    private readonly EpicApiClient _epicApi = new();
    private readonly EpicImageCache _epicCache = new();
    private string? _lastEpicImagePath; // Track last rendered EPIC image to avoid re-downloads

    public event Action<string>? StatusChanged;

    public RenderScheduler(SettingsManager settingsManager, AssetLocator assets)
    {
        _settingsManager = settingsManager;
        _assets = assets;
        _wallpaperPath = Path.Combine(Path.GetTempPath(), "BlueMarbleDesktop_wallpaper.bmp");

        // Re-render whenever settings change (belt-and-suspenders with SettingsForm.TriggerUpdate)
        _settingsManager.SettingsChanged += TriggerUpdate;
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
        StillImageRenderer? stillImageRenderer = null;
        DisplayMode currentMode = settings.DisplayMode;
        ImageStyle currentImageStyle = settings.ImageStyle;

        DateTime lastUpdate = DateTime.MinValue;
        bool firstRender = true;
        GL? gl = null;

        var window = Window.Create(options);

        window.Load += () =>
        {
            gl = GL.GetApi(window);
            InitializeRenderers(gl, settings, ref earthRenderer, ref flatMapRenderer, ref moonRenderer, ref stillImageRenderer);
            ReportStatus("Renderer initialized.");
            // Clean old EPIC cache on startup
            _epicCache.CleanOldCache();
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
                Thread.Sleep(200);
                return;
            }

            _renderNowSignal.Reset();
            firstRender = false;

            try
            {
                // Re-read settings
                settings = _settingsManager.Settings;

                if (settings.MultiMonitorMode == MultiMonitorMode.PerDisplay)
                {
                    // Per-display rendering: render each monitor independently
                    RenderPerDisplay(gl, settings, ref earthRenderer, ref flatMapRenderer, ref moonRenderer,
                        ref stillImageRenderer, ref currentMode, ref currentImageStyle);
                }
                else
                {
                    // Standard rendering: single image for all monitors
                    (renderWidth, renderHeight) = MonitorManager.GetRenderResolution(settings);

                    // Re-initialize renderers if display mode or image style changed
                    if (settings.DisplayMode != currentMode || settings.ImageStyle != currentImageStyle)
                    {
                        currentMode = settings.DisplayMode;
                        currentImageStyle = settings.ImageStyle;
                        DisposeRenderers(ref earthRenderer, ref flatMapRenderer, ref moonRenderer, ref stillImageRenderer);
                        InitializeRenderers(gl, settings, ref earthRenderer, ref flatMapRenderer, ref moonRenderer, ref stillImageRenderer);
                    }

                    string modeName = settings.DisplayMode switch
                    {
                        DisplayMode.FlatMap => "flat map",
                        DisplayMode.Moon => "moon",
                        DisplayMode.NasaEpic => "NASA EPIC",
                        _ => "earth"
                    };
                    ReportStatus($"Rendering {modeName}...");

                    byte[] pixels;
                    if (settings.DisplayMode == DisplayMode.NasaEpic)
                    {
                        pixels = RenderEpicImage(gl, settings, ref stillImageRenderer, renderWidth, renderHeight);
                    }
                    else
                    {
                        pixels = settings.DisplayMode switch
                        {
                            DisplayMode.FlatMap => flatMapRenderer!.Render(renderWidth, renderHeight),
                            DisplayMode.Moon => moonRenderer!.Render(renderWidth, renderHeight),
                            _ => earthRenderer!.Render(renderWidth, renderHeight),
                        };
                    }

                    SaveAsBmp(pixels, renderWidth, renderHeight, _wallpaperPath);
                    WallpaperSetter.SetWallpaper(_wallpaperPath, settings.MultiMonitorMode);
                }

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
            DisposeRenderers(ref earthRenderer, ref flatMapRenderer, ref moonRenderer, ref stillImageRenderer);
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

    private void RenderPerDisplay(GL gl, AppSettings settings,
        ref EarthRenderer? earthRenderer, ref FlatMapRenderer? flatMapRenderer, ref MoonRenderer? moonRenderer,
        ref StillImageRenderer? stillImageRenderer,
        ref DisplayMode currentMode, ref ImageStyle currentImageStyle)
    {
        var screens = MonitorManager.GetAllScreens();
        if (screens.Length == 0) return;

        // Get virtual desktop bounds for composite
        var (vdWidth, vdHeight) = MonitorManager.GetVirtualDesktopSize();
        int vdLeft = int.MaxValue, vdTop = int.MaxValue;
        foreach (var screen in screens)
        {
            if (screen.Bounds.Left < vdLeft) vdLeft = screen.Bounds.Left;
            if (screen.Bounds.Top < vdTop) vdTop = screen.Bounds.Top;
        }

        ReportStatus($"Rendering per-display ({screens.Length} monitors)...");

        // Create composite image (filled black)
        using var composite = new SixLabors.ImageSharp.Image<Rgba32>(vdWidth, vdHeight, new Rgba32(0, 0, 0, 255));

        foreach (var screen in screens)
        {
            int sw = screen.Bounds.Width;
            int sh = screen.Bounds.Height;

            // Find per-display config or use global defaults
            var displayConfig = settings.DisplayConfigs.Find(c => c.DeviceName == screen.DeviceName);

            // Create temporary settings for this display
            var displaySettings = new AppSettings
            {
                DisplayMode = displayConfig?.DisplayMode ?? settings.DisplayMode,
                ZoomLevel = displayConfig?.ZoomLevel ?? settings.ZoomLevel,
                FieldOfView = displayConfig?.FieldOfView ?? settings.FieldOfView,
                CameraTilt = displayConfig?.CameraTilt ?? settings.CameraTilt,
                LongitudeOffset = displayConfig?.LongitudeOffset ?? settings.LongitudeOffset,
                ImageOffsetX = displayConfig?.ImageOffsetX ?? settings.ImageOffsetX,
                ImageOffsetY = displayConfig?.ImageOffsetY ?? settings.ImageOffsetY,
                NightLightsEnabled = displayConfig?.NightLightsEnabled ?? settings.NightLightsEnabled,
                NightLightsBrightness = displayConfig?.NightLightsBrightness ?? settings.NightLightsBrightness,
                AmbientLight = displayConfig?.AmbientLight ?? settings.AmbientLight,
                ImageStyle = displayConfig?.ImageStyle ?? settings.ImageStyle,
                EpicImageType = displayConfig?.EpicImageType ?? settings.EpicImageType,
                EpicUseLatest = displayConfig?.EpicUseLatest ?? settings.EpicUseLatest,
                EpicSelectedDate = displayConfig?.EpicSelectedDate ?? settings.EpicSelectedDate,
                EpicSelectedImage = displayConfig?.EpicSelectedImage ?? settings.EpicSelectedImage,
            };

            // Always create fresh renderers for each display so each gets
            // its own settings (zoom, longitude, etc.). Renderers store a
            // reference to settings at construction time.
            DisposeRenderers(ref earthRenderer, ref flatMapRenderer, ref moonRenderer, ref stillImageRenderer);
            currentMode = displaySettings.DisplayMode;
            currentImageStyle = displaySettings.ImageStyle;
            InitializeRenderers(gl, displaySettings, ref earthRenderer, ref flatMapRenderer, ref moonRenderer, ref stillImageRenderer);

            // Render this display
            byte[] pixels;
            if (displaySettings.DisplayMode == DisplayMode.NasaEpic)
            {
                pixels = RenderEpicImage(gl, displaySettings, ref stillImageRenderer, sw, sh);
            }
            else
            {
                pixels = displaySettings.DisplayMode switch
                {
                    DisplayMode.FlatMap => flatMapRenderer!.Render(sw, sh),
                    DisplayMode.Moon => moonRenderer!.Render(sw, sh),
                    _ => earthRenderer!.Render(sw, sh),
                };
            }

            // Place rendered pixels into composite at the correct position (row-span copy)
            int offsetX = screen.Bounds.Left - vdLeft;
            int offsetY = screen.Bounds.Top - vdTop;

            composite.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < sh; y++)
                {
                    int cy = offsetY + y;
                    if (cy < 0 || cy >= vdHeight) continue;

                    int srcRow = (sh - 1 - y) * sw * 4; // OpenGL is bottom-up
                    var destRow = accessor.GetRowSpan(cy);

                    int xStart = Math.Max(0, -offsetX);
                    int xEnd = Math.Min(sw, vdWidth - offsetX);

                    for (int x = xStart; x < xEnd; x++)
                    {
                        int idx = srcRow + x * 4;
                        destRow[offsetX + x] = new Rgba32(pixels[idx], pixels[idx + 1], pixels[idx + 2], 255);
                    }
                }
            });
        }

        // Save composite and set as spanned wallpaper
        composite.SaveAsBmp(_wallpaperPath);
        WallpaperSetter.SetWallpaper(_wallpaperPath, MultiMonitorMode.SpanAcross);
    }

    private void InitializeRenderers(GL gl, AppSettings settings,
        ref EarthRenderer? earthRenderer, ref FlatMapRenderer? flatMapRenderer,
        ref MoonRenderer? moonRenderer, ref StillImageRenderer? stillImageRenderer)
    {
        string dayTexPath = _assets.GetDayTexturePath(settings.ImageStyle);
        string nightTexPath = _assets.GetNightTexturePath();

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
                    moonRenderer.Initialize(gl, moonTexPath, dayTexPath, nightTexPath);
                }
                catch (FileNotFoundException)
                {
                    ReportStatus("Moon texture not found, falling back to globe.");
                    earthRenderer = new EarthRenderer(settings);
                    earthRenderer.Initialize(gl, dayTexPath, nightTexPath);
                }
                break;
            case DisplayMode.NasaEpic:
                stillImageRenderer = new StillImageRenderer(settings);
                stillImageRenderer.Initialize(gl);
                // Image will be loaded later in RenderEpicImage()
                break;
            default:
                earthRenderer = new EarthRenderer(settings);
                earthRenderer.Initialize(gl, dayTexPath, nightTexPath);
                break;
        }
    }

    /// <summary>
    /// Fetch (or use cached) EPIC image and render it via the StillImageRenderer.
    /// Returns pixel data for the wallpaper. Falls back to cached images on network errors.
    /// </summary>
    private byte[] RenderEpicImage(GL gl, AppSettings settings, ref StillImageRenderer? renderer,
        int width, int height)
    {
        // Ensure renderer exists
        if (renderer == null)
        {
            renderer = new StillImageRenderer(settings);
            renderer.Initialize(gl);
        }

        // Try to get an image to display
        string? imagePath = ResolveEpicImage(settings);

        if (imagePath != null)
        {
            renderer.LoadImage(gl, imagePath);
            return renderer.Render(width, height);
        }

        // No image available — return black screen
        return new byte[width * height * 4];
    }

    /// <summary>
    /// Resolve which EPIC image file to use: download latest, use specific date, or fall back to cache.
    /// Returns local file path or null if nothing available.
    /// </summary>
    private string? ResolveEpicImage(AppSettings settings)
    {
        try
        {
            List<EpicImageInfo>? images = null;

            if (settings.EpicUseLatest)
            {
                // Get the latest images from the API
                images = _epicApi.GetLatestImagesAsync(settings.EpicImageType)
                    .GetAwaiter().GetResult();
            }
            else if (!string.IsNullOrEmpty(settings.EpicSelectedDate))
            {
                // Get images for the selected date
                images = _epicApi.GetImagesByDateAsync(settings.EpicImageType, settings.EpicSelectedDate)
                    .GetAwaiter().GetResult();
            }

            if (images != null && images.Count > 0)
            {
                // If a specific image is selected, find it; otherwise use the first one
                EpicImageInfo selectedImage;
                if (!string.IsNullOrEmpty(settings.EpicSelectedImage))
                {
                    selectedImage = images.Find(i => i.Image == settings.EpicSelectedImage) ?? images[0];
                }
                else
                {
                    selectedImage = images[0];
                }

                // Download (or use cache)
                var path = _epicApi.DownloadImageAsync(selectedImage, settings.EpicImageType, _epicCache)
                    .GetAwaiter().GetResult();

                if (path != null)
                {
                    _lastEpicImagePath = path;
                    return path;
                }
            }

            // API failed or no images — try cached fallback
            if (_lastEpicImagePath != null && File.Exists(_lastEpicImagePath))
            {
                ReportStatus("EPIC: Using previously loaded image (offline)");
                return _lastEpicImagePath;
            }

            var cachedPath = _epicCache.GetLatestCachedImagePath(settings.EpicImageType);
            if (cachedPath != null)
            {
                ReportStatus("EPIC: Using cached image (offline)");
                _lastEpicImagePath = cachedPath;
                return cachedPath;
            }

            ReportStatus("EPIC: No images available");
            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"EPIC resolve error: {ex.Message}");

            // Fall back to any cached image
            if (_lastEpicImagePath != null && File.Exists(_lastEpicImagePath))
                return _lastEpicImagePath;

            return _epicCache.GetLatestCachedImagePath(settings.EpicImageType);
        }
    }

    private static void DisposeRenderers(ref EarthRenderer? earth, ref FlatMapRenderer? flatMap,
        ref MoonRenderer? moon, ref StillImageRenderer? stillImage)
    {
        earth?.Dispose(); earth = null;
        flatMap?.Dispose(); flatMap = null;
        moon?.Dispose(); moon = null;
        stillImage?.Dispose(); stillImage = null;
    }

    private void ReportStatus(string message)
    {
        StatusChanged?.Invoke(message);
    }

    private static void SaveAsBmp(byte[] rgbaPixels, int width, int height, string path)
    {
        using var image = new SixLabors.ImageSharp.Image<Rgba32>(width, height);
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < height; y++)
            {
                int srcRow = (height - 1 - y) * width * 4; // OpenGL is bottom-up
                var destRow = accessor.GetRowSpan(y);
                for (int x = 0; x < width; x++)
                {
                    int idx = srcRow + x * 4;
                    destRow[x] = new Rgba32(rgbaPixels[idx], rgbaPixels[idx + 1], rgbaPixels[idx + 2], 255);
                }
            }
        });
        image.SaveAsBmp(path);
    }

    public void Dispose()
    {
        _settingsManager.SettingsChanged -= TriggerUpdate;
        Stop();
        _renderNowSignal.Dispose();
    }
}
