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

    // EPIC support (legacy, kept for backward compatibility)
    private readonly EpicApiClient _epicApi = new();
    private readonly EpicImageCache _epicCache = new();
    private string? _lastEpicImagePath;

    // New image sources cache
    private readonly ImageCache _imageCache = new();

    // Image rotation (sequential cycling through source pools)
    private int _rotationIndex;
    private string? _randomImageOverride;
    private ImageSource? _randomImageSource;
    private int _poolBuildCycleCount;

    // Gate: prevents all rendering until user explicitly triggers an update.
    // Defaults to true (first-ever run renders immediately).
    // Set to false on subsequent launches so the existing wallpaper is preserved.
    private volatile bool _userHasTriggered = true;

    // Track currently displayed image for "Favorite Current Wallpaper" feature
    public ImageSource? CurrentImageSource { get; private set; }
    public string? CurrentImageId { get; private set; }
    public string? CurrentImageUrl { get; private set; }
    public string? CurrentImagePath { get; private set; }
    public string? CurrentImageTitle { get; private set; }

    public event Action<string>? StatusChanged;

    public RenderScheduler(SettingsManager settingsManager, AssetLocator assets)
    {
        _settingsManager = settingsManager;
        _assets = assets;
        _wallpaperPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "BlueMarbleDesktop", "wallpaper.bmp");

        // On subsequent launches, don't render until user explicitly changes something
        if (_settingsManager.SkipFirstRender)
            _userHasTriggered = false;

        // Re-render whenever settings change
        _settingsManager.SettingsChanged += TriggerUserUpdate;
    }

    public void Start()
    {
        if (_renderThread != null) return;

        // Re-apply persisted wallpaper immediately on startup (no re-render needed).
        // The BMP survives across restarts in %AppData%, so we just tell Windows to use it again.
        // PerDisplay is internally implemented as a composite image with SpanAcross,
        // so we must re-apply with Span style (not the PerDisplay enum, which has no wallpaper style).
        if (File.Exists(_wallpaperPath) && new FileInfo(_wallpaperPath).Length > 0)
        {
            var wallpaperMode = _settingsManager.Settings.MultiMonitorMode == MultiMonitorMode.PerDisplay
                ? MultiMonitorMode.SpanAcross
                : _settingsManager.Settings.MultiMonitorMode;
            WallpaperSetter.SetWallpaper(_wallpaperPath, wallpaperMode);
        }

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

    /// <summary>
    /// Trigger a render update from an explicit user action (settings change, tray button, etc.).
    /// Also enables rendering if it was disabled for wallpaper preservation on restart.
    /// </summary>
    public void TriggerUserUpdate()
    {
        _userHasTriggered = true;
        _renderNowSignal.Set();
    }

    /// <summary>
    /// Build a FavoriteImage from the currently displayed wallpaper, or null if not a still image.
    /// </summary>
    public FavoriteImage? GetCurrentAsFavorite()
    {
        if (CurrentImageSource == null || string.IsNullOrEmpty(CurrentImageId))
            return null;

        return new FavoriteImage
        {
            Source = CurrentImageSource.Value,
            ImageId = CurrentImageId,
            Title = CurrentImageTitle ?? CurrentImageId,
            FullImageUrl = CurrentImageUrl ?? "",
            LocalCachePath = CurrentImagePath ?? ""
        };
    }

    private void SetCurrentImage(ImageSource source, string? id, string? url, string? path, string? title = null)
    {
        CurrentImageSource = source;
        CurrentImageId = id;
        CurrentImageUrl = url;
        CurrentImagePath = path;
        CurrentImageTitle = title;
    }

    private void ClearCurrentImage()
    {
        CurrentImageSource = null;
        CurrentImageId = null;
        CurrentImageUrl = null;
        CurrentImagePath = null;
        CurrentImageTitle = null;
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
            // Clean old caches on startup (protect favorited images)
            int epicDays = settings.EpicCacheDurationDays;
            int imageDays = settings.CacheDurationDays;
            if (epicDays > 0) _epicCache.CleanOldCache(epicDays);
            if (imageDays > 0)
            {
                var protectedIds = GetProtectedImageIds(settings);
                _imageCache.CleanOldCache(protectedIds, imageDays);
            }
        };

        window.Render += (_) =>
        {
            if (gl == null || _stopRequested)
            {
                if (_stopRequested) window.Close();
                return;
            }

            // On subsequent launches, don't render until user explicitly triggers an update.
            // This preserves the existing wallpaper indefinitely until the user changes settings
            // or clicks "Update Wallpaper Now" in the tray menu.
            if (!_userHasTriggered)
            {
                _renderNowSignal.Reset(); // Drain any stale signals
                Thread.Sleep(200);
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

                    // Handle image rotation for still image sources
                    if (settings.DisplayMode == DisplayMode.StillImage && settings.RandomRotationEnabled)
                    {
                        PickNextImage(settings, settings.RandomRotationSource);
                        MaybeProactiveFetch(settings);
                    }

                    string modeName = GetModeName(settings);
                    ReportStatus($"Rendering {modeName}...");

                    byte[] pixels;
                    if (settings.DisplayMode == DisplayMode.StillImage)
                    {
                        if (settings.StillImageSource == ImageSource.NasaEpic)
                        {
                            pixels = RenderEpicImage(gl, settings, ref stillImageRenderer, renderWidth, renderHeight);
                        }
                        else
                        {
                            pixels = RenderImageSource(gl, settings, settings.StillImageSource,
                                ref stillImageRenderer, renderWidth, renderHeight);
                        }
                    }
                    else
                    {
                        pixels = settings.DisplayMode switch
                        {
                            DisplayMode.FlatMap => flatMapRenderer!.Render(renderWidth, renderHeight),
                            DisplayMode.Moon when moonRenderer != null => moonRenderer.Render(renderWidth, renderHeight),
                            _ => earthRenderer!.Render(renderWidth, renderHeight),
                        };
                        ClearCurrentImage(); // Globe/Flat Map/Moon are not favoritable
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(_wallpaperPath)!);
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

    private static string GetModeName(AppSettings settings) => settings.DisplayMode switch
    {
        DisplayMode.FlatMap => "flat map",
        DisplayMode.Moon => "moon",
        DisplayMode.StillImage => settings.StillImageSource switch
        {
            ImageSource.NasaEpic => "NASA EPIC",
            ImageSource.NasaApod => "NASA APOD",
            ImageSource.NationalParks => "National Parks",
            ImageSource.Smithsonian => "Smithsonian",
            ImageSource.UserImages => "user image",
            _ => "still image"
        },
        _ => "earth"
    };

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
                StillImageSource = displayConfig?.StillImageSource ?? settings.StillImageSource,
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
                // NASA APOD per-display
                ApodRecentDays = displayConfig?.ApodRecentDays ?? settings.ApodRecentDays,
                ApodUseLatest = displayConfig?.ApodUseLatest ?? settings.ApodUseLatest,
                ApodSelectedDate = displayConfig?.ApodSelectedDate ?? settings.ApodSelectedDate,
                ApodSelectedImageId = displayConfig?.ApodSelectedImageId ?? settings.ApodSelectedImageId,
                ApodSelectedImageUrl = displayConfig?.ApodSelectedImageUrl ?? settings.ApodSelectedImageUrl,
                // NPS per-display
                NpsSearchQuery = displayConfig?.NpsSearchQuery ?? settings.NpsSearchQuery,
                NpsSelectedParkCode = displayConfig?.NpsSelectedParkCode ?? settings.NpsSelectedParkCode,
                NpsSelectedImageId = displayConfig?.NpsSelectedImageId ?? settings.NpsSelectedImageId,
                NpsSelectedImageUrl = displayConfig?.NpsSelectedImageUrl ?? settings.NpsSelectedImageUrl,
                // Smithsonian per-display
                SmithsonianSearchQuery = displayConfig?.SmithsonianSearchQuery ?? settings.SmithsonianSearchQuery,
                SmithsonianSelectedId = displayConfig?.SmithsonianSelectedId ?? settings.SmithsonianSelectedId,
                SmithsonianSelectedImageUrl = displayConfig?.SmithsonianSelectedImageUrl ?? settings.SmithsonianSelectedImageUrl,
                // API key (always from global settings)
                ApiDataGovKey = settings.ApiDataGovKey,
                // Auto-rotation per-display
                RandomRotationEnabled = displayConfig?.RandomRotationEnabled ?? settings.RandomRotationEnabled,
                RandomRotationSource = displayConfig?.RandomRotationSource ?? settings.RandomRotationSource,
                Favorites = settings.Favorites, // Favorites are always global
                // User images per-display
                UserImageSelectedId = displayConfig?.UserImageSelectedId ?? settings.UserImageSelectedId,
                UserImageSelectedPath = displayConfig?.UserImageSelectedPath ?? settings.UserImageSelectedPath,
            };

            // Always create fresh renderers for each display
            DisposeRenderers(ref earthRenderer, ref flatMapRenderer, ref moonRenderer, ref stillImageRenderer);
            currentMode = displaySettings.DisplayMode;
            currentImageStyle = displaySettings.ImageStyle;
            InitializeRenderers(gl, displaySettings, ref earthRenderer, ref flatMapRenderer, ref moonRenderer, ref stillImageRenderer);

            // Handle image rotation for this display
            if (displaySettings.DisplayMode == DisplayMode.StillImage && displaySettings.RandomRotationEnabled)
            {
                PickNextImage(displaySettings, displaySettings.RandomRotationSource);
                MaybeProactiveFetch(displaySettings);
            }

            // Render this display
            byte[] pixels;
            if (displaySettings.DisplayMode == DisplayMode.StillImage)
            {
                if (displaySettings.StillImageSource == ImageSource.NasaEpic)
                {
                    pixels = RenderEpicImage(gl, displaySettings, ref stillImageRenderer, sw, sh);
                }
                else
                {
                    pixels = RenderImageSource(gl, displaySettings, displaySettings.StillImageSource,
                        ref stillImageRenderer, sw, sh);
                }
            }
            else
            {
                pixels = displaySettings.DisplayMode switch
                {
                    DisplayMode.FlatMap => flatMapRenderer!.Render(sw, sh),
                    DisplayMode.Moon when moonRenderer != null => moonRenderer.Render(sw, sh),
                    _ => earthRenderer!.Render(sw, sh),
                };
            }

            // Place rendered pixels into composite at the correct position
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
        Directory.CreateDirectory(Path.GetDirectoryName(_wallpaperPath)!);
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
            case DisplayMode.StillImage:
                stillImageRenderer = new StillImageRenderer(settings);
                stillImageRenderer.Initialize(gl);
                break;
            default:
                earthRenderer = new EarthRenderer(settings);
                earthRenderer.Initialize(gl, dayTexPath, nightTexPath);
                break;
        }
    }

    /// <summary>
    /// Render an image from a non-EPIC image source (APOD, NPS, Smithsonian).
    /// Downloads to cache if needed, falls back to cached images on error.
    /// </summary>
    private byte[] RenderImageSource(GL gl, AppSettings settings, ImageSource source,
        ref StillImageRenderer? renderer, int width, int height)
    {
        // Ensure renderer exists
        if (renderer == null)
        {
            renderer = new StillImageRenderer(settings);
            renderer.Initialize(gl);
        }

        // If random rotation picked an image, use it (may be from a different source in "All" mode)
        if (_randomImageOverride != null)
        {
            var overridePath = _randomImageOverride;
            _randomImageOverride = null;
            var effectiveSource = _randomImageSource ?? source;
            _randomImageSource = null;

            if (File.Exists(overridePath))
            {
                renderer.LoadImage(gl, overridePath);
                if (!renderer.IsBelowMinimumQuality)
                {
                    SetCurrentImage(effectiveSource, Path.GetFileNameWithoutExtension(overridePath), null, overridePath);
                    return renderer.Render(width, height);
                }

                Console.WriteLine($"RenderScheduler: Random image below 1080p minimum, skipping");
            }
        }

        // User images are local files — no download needed
        if (source == ImageSource.UserImages)
        {
            var path = settings.UserImageSelectedPath;
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                renderer.LoadImage(gl, path);
                if (!renderer.IsBelowMinimumQuality)
                {
                    SetCurrentImage(source, settings.UserImageSelectedId, null, path,
                        Path.GetFileNameWithoutExtension(path));
                    return renderer.Render(width, height);
                }
            }
            // Fallback: try any user image
            var userMgr = new UserImageManager();
            var allPaths = userMgr.GetAllImagePaths();
            if (allPaths.Count > 0)
            {
                renderer.LoadImage(gl, allPaths[0]);
                if (!renderer.IsBelowMinimumQuality)
                {
                    SetCurrentImage(source, Path.GetFileNameWithoutExtension(allPaths[0]), null, allPaths[0]);
                    return renderer.Render(width, height);
                }
            }
            ReportStatus("User images: No images available");
            return new byte[width * height * 4];
        }

        // Resolve the selected image URL for this source
        string? imageUrl = GetSelectedImageUrl(settings, source);
        string? imageId = GetSelectedImageId(settings, source);

        if (string.IsNullOrEmpty(imageUrl) || string.IsNullOrEmpty(imageId))
        {
            // No image selected — try cached fallback
            var cached = _imageCache.GetLatestCachedImagePath(source);
            if (cached != null)
            {
                ReportStatus($"{source}: Using cached image (no selection)");
                renderer.LoadImage(gl, cached);
                SetCurrentImage(source, Path.GetFileNameWithoutExtension(cached), null, cached);
                return renderer.Render(width, height);
            }
            ReportStatus($"{source}: No image selected");
            return new byte[width * height * 4];
        }

        // Try to download or use cache
        string? imagePath = null;
        try
        {
            if (_imageCache.IsCached(source, imageId))
            {
                imagePath = _imageCache.GetCachePath(source, imageId);
            }
            else
            {
                imagePath = _imageCache.DownloadToCache(source, imageId, imageUrl)
                    .GetAwaiter().GetResult();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"{source} download error: {ex.Message}");
        }

        if (imagePath != null && File.Exists(imagePath))
        {
            renderer.LoadImage(gl, imagePath);

            // Enforce minimum 1080p quality -- skip sub-1080p images
            if (renderer.IsBelowMinimumQuality)
            {
                ReportStatus($"{source}: Image below minimum 1080p quality, trying fallback");
                Console.WriteLine($"RenderScheduler: Skipping {imageId} -- below 1080p minimum");
            }
            else
            {
                SetCurrentImage(source, imageId, imageUrl, imagePath);
                return renderer.Render(width, height);
            }
        }

        // Download failed or below quality — try any cached image for this source
        var fallback = _imageCache.GetLatestCachedImagePath(source);
        if (fallback != null)
        {
            ReportStatus($"{source}: Using cached image (offline)");
            renderer.LoadImage(gl, fallback);
            SetCurrentImage(source, Path.GetFileNameWithoutExtension(fallback), null, fallback);
            return renderer.Render(width, height);
        }

        ReportStatus($"{source}: No images available");
        return new byte[width * height * 4];
    }

    /// <summary>
    /// Get the selected image URL for a given source from settings.
    /// </summary>
    private static string? GetSelectedImageUrl(AppSettings settings, ImageSource source) => source switch
    {
        ImageSource.NasaApod => settings.ApodSelectedImageUrl,
        ImageSource.NationalParks => settings.NpsSelectedImageUrl,
        ImageSource.Smithsonian => settings.SmithsonianSelectedImageUrl,
        ImageSource.UserImages => settings.UserImageSelectedPath,
        _ => null
    };

    /// <summary>
    /// Get the selected image ID for a given source from settings.
    /// </summary>
    private static string? GetSelectedImageId(AppSettings settings, ImageSource source) => source switch
    {
        ImageSource.NasaApod => settings.ApodSelectedImageId,
        ImageSource.NationalParks => settings.NpsSelectedImageId,
        ImageSource.Smithsonian => settings.SmithsonianSelectedId,
        ImageSource.UserImages => settings.UserImageSelectedId,
        _ => null
    };

    /// <summary>
    /// Pick the next image for sequential rotation. Sets _randomImageOverride to a local file path.
    /// Cycles through images in order from the selected rotation source pool.
    /// </summary>
    private void PickNextImage(AppSettings settings, RotationSource rotationSource)
    {
        try
        {
            var pool = BuildRotationPool(settings, rotationSource);
            if (pool.Count > 0)
            {
                var picked = pool[_rotationIndex % pool.Count];
                _randomImageOverride = picked;
                _randomImageSource = DetectSourceFromPath(picked);
                _rotationIndex++;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Image rotation error ({rotationSource}): {ex.Message}");
        }
    }

    /// <summary>
    /// Build a list of local file paths that form the rotation pool for a given source.
    /// </summary>
    private List<string> BuildRotationPool(AppSettings settings, RotationSource source)
    {
        return source switch
        {
            RotationSource.NasaEpic => _epicCache.GetAllCachedImagePaths(settings.EpicImageType),
            RotationSource.NasaApod => _imageCache.GetAllCachedImagePaths(ImageSource.NasaApod),
            RotationSource.NationalParks => _imageCache.GetAllCachedImagePaths(ImageSource.NationalParks),
            RotationSource.Smithsonian => _imageCache.GetAllCachedImagePaths(ImageSource.Smithsonian),
            RotationSource.UserImages => new UserImageManager().GetAllImagePaths(),
            RotationSource.Favorites => GetFavoriteImagePaths(settings),
            RotationSource.All => BuildWeightedAllPool(settings),
            _ => new List<string>()
        };
    }

    /// <summary>
    /// Get cached file paths for all favorites (thread-safe).
    /// </summary>
    private List<string> GetFavoriteImagePaths(AppSettings settings)
    {
        List<FavoriteImage> favs;
        lock (settings.FavoritesLock)
        {
            favs = settings.Favorites.ToList();
        }

        var paths = new List<string>();
        foreach (var fav in favs)
        {
            var path = _imageCache.GetCachedPathForFavorite(fav);
            if (path != null && File.Exists(path))
                paths.Add(path);
        }
        return paths;
    }

    /// <summary>
    /// Build a weighted rotation pool from all sources.
    /// Weights: EPIC 3x, APOD 3x, NPS 3x, Smithsonian 1x, User 2x.
    /// Shuffled deterministically so sequential cycling produces mixed order.
    /// </summary>
    private List<string> BuildWeightedAllPool(AppSettings settings)
    {
        var pool = new List<string>();
        AddWeighted(pool, _epicCache.GetAllCachedImagePaths(settings.EpicImageType), 3);
        AddWeighted(pool, _imageCache.GetAllCachedImagePaths(ImageSource.NasaApod), 3);
        AddWeighted(pool, _imageCache.GetAllCachedImagePaths(ImageSource.NationalParks), 3);
        AddWeighted(pool, _imageCache.GetAllCachedImagePaths(ImageSource.Smithsonian), 1);
        AddWeighted(pool, new UserImageManager().GetAllImagePaths(), 2);

        // Add favorites that aren't already in the pool (e.g., from sources without local cache)
        foreach (var path in GetFavoriteImagePaths(settings))
        {
            if (!pool.Contains(path))
                pool.Add(path);
        }

        // Deterministic shuffle so cycling through with _rotationIndex gives mixed order
        var rng = new Random(42);
        for (int i = pool.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (pool[i], pool[j]) = (pool[j], pool[i]);
        }

        return pool;
    }

    private static void AddWeighted(List<string> pool, List<string> source, int weight)
    {
        for (int w = 0; w < weight; w++)
            pool.AddRange(source);
    }

    /// <summary>
    /// Detect which ImageSource a file path belongs to based on its directory.
    /// </summary>
    private static ImageSource? DetectSourceFromPath(string path)
    {
        if (path.Contains("epic_images")) return ImageSource.NasaEpic;
        if (path.Contains("nasaapod")) return ImageSource.NasaApod;
        if (path.Contains("nationalparks")) return ImageSource.NationalParks;
        if (path.Contains("smithsonian")) return ImageSource.Smithsonian;
        if (path.Contains("user_images")) return ImageSource.UserImages;
        return null;
    }

    /// <summary>
    /// Proactively fetch new images to build the rotation pool over time.
    /// Aggressive at first (every cycle while pool &lt; 20), then tapers to every 5th cycle.
    /// </summary>
    private void MaybeProactiveFetch(AppSettings settings)
    {
        _poolBuildCycleCount++;

        var source = settings.RandomRotationSource;
        // No proactive fetching for user-managed pools
        if (source == RotationSource.Favorites || source == RotationSource.UserImages)
            return;

        int currentPoolSize = BuildRotationPool(settings, source).Count;

        // Determine fetch frequency based on pool size and API key
        bool isDemoKey = settings.ApiDataGovKey == "DEMO_KEY";
        bool shouldFetch;
        if (isDemoKey)
            shouldFetch = (_poolBuildCycleCount % 5) == 0; // Conservative for DEMO_KEY
        else if (currentPoolSize < 20)
            shouldFetch = true; // Aggressive when pool is small
        else
            shouldFetch = (_poolBuildCycleCount % 5) == 0; // Maintenance

        if (!shouldFetch) return;

        // Fire-and-forget background fetch (doesn't block render)
        var apiKey = settings.ApiDataGovKey;
        var epicType = settings.EpicImageType;
        Task.Run(async () =>
        {
            try
            {
                await FetchNewImageForPool(source, apiKey, epicType);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Pool build fetch error: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// Download new images from a source to grow the rotation pool.
    /// </summary>
    private async Task FetchNewImageForPool(RotationSource source, string apiKey, EpicImageType epicType)
    {
        switch (source)
        {
            case RotationSource.NasaApod:
            {
                // Fetch a random APOD from the last 2 years
                var randomDate = DateTime.UtcNow.AddDays(-Random.Shared.Next(1, 730));
                var apodImage = await new ApodApiClient().GetByDateAsync(apiKey, randomDate.ToString("yyyy-MM-dd"));
                if (apodImage != null)
                {
                    var url = ApodApiClient.GetBestUrl(apodImage);
                    await _imageCache.DownloadToCache(ImageSource.NasaApod, apodImage.Id, url);
                    Console.WriteLine($"Pool build: Fetched APOD {apodImage.Id}");
                }
                break;
            }
            case RotationSource.NationalParks:
            {
                // Fetch images from a random curated park
                var parkCodes = NpsApiClient.ParkCodes.Values.ToList();
                var randomPark = parkCodes[Random.Shared.Next(parkCodes.Count)];
                var images = await new NpsApiClient().GetParkImagesAsync(apiKey, randomPark);
                if (images != null)
                {
                    foreach (var img in images.Take(3))
                    {
                        var url = !string.IsNullOrEmpty(img.HdImageUrl) ? img.HdImageUrl : img.FullImageUrl;
                        if (!string.IsNullOrEmpty(url))
                            await _imageCache.DownloadToCache(ImageSource.NationalParks, img.Id, url);
                    }
                    Console.WriteLine($"Pool build: Fetched NPS {randomPark} ({images.Count} images)");
                }
                break;
            }
            case RotationSource.Smithsonian:
            {
                // Smithsonian uses sort=random, so each call returns different results
                var images = await new SmithsonianApiClient().SearchImagesAsync(apiKey, "landscape painting", 0, 5);
                if (images != null)
                {
                    foreach (var img in images.Take(3))
                    {
                        var url = !string.IsNullOrEmpty(img.HdImageUrl) ? img.HdImageUrl : img.FullImageUrl;
                        if (!string.IsNullOrEmpty(url))
                            await _imageCache.DownloadToCache(ImageSource.Smithsonian, img.Id, url);
                    }
                    Console.WriteLine($"Pool build: Fetched {images.Count} Smithsonian images");
                }
                break;
            }
            case RotationSource.NasaEpic:
            {
                // Fetch latest EPIC images
                var epicImages = await _epicApi.GetLatestImagesAsync(epicType);
                if (epicImages != null)
                {
                    foreach (var img in epicImages.Take(3))
                    {
                        if (!_epicCache.IsCached(img, epicType))
                            await _epicApi.DownloadImageAsync(img, epicType, _epicCache);
                    }
                    Console.WriteLine($"Pool build: Fetched {Math.Min(3, epicImages.Count)} EPIC images");
                }
                break;
            }
            case RotationSource.All:
            {
                // Pick a random source (weighted toward nature/space)
                var sources = new[] {
                    RotationSource.NasaApod, RotationSource.NasaApod,
                    RotationSource.NationalParks, RotationSource.NationalParks,
                    RotationSource.NasaEpic,
                    RotationSource.Smithsonian
                };
                await FetchNewImageForPool(sources[Random.Shared.Next(sources.Length)], apiKey, epicType);
                break;
            }
        }
    }

    /// <summary>
    /// Build set of sanitized favorite image IDs for cache protection.
    /// </summary>
    private static HashSet<string> GetProtectedImageIds(AppSettings settings)
    {
        return new HashSet<string>(
            settings.Favorites.Select(f => ImageCache.SanitizeFileName(f.ImageId)));
    }

    /// <summary>
    /// Fetch (or use cached) EPIC image and render it via the StillImageRenderer.
    /// </summary>
    private byte[] RenderEpicImage(GL gl, AppSettings settings, ref StillImageRenderer? renderer,
        int width, int height)
    {
        if (renderer == null)
        {
            renderer = new StillImageRenderer(settings);
            renderer.Initialize(gl);
        }

        // If random rotation picked an image, use it (may be from any source in "All" mode)
        if (_randomImageOverride != null)
        {
            var overridePath = _randomImageOverride;
            _randomImageOverride = null;

            if (File.Exists(overridePath))
            {
                renderer.LoadImage(gl, overridePath);
                if (!renderer.IsBelowMinimumQuality)
                {
                    SetCurrentImage(_randomImageSource ?? ImageSource.NasaEpic,
                        Path.GetFileNameWithoutExtension(overridePath), null, overridePath);
                    _randomImageSource = null;
                    return renderer.Render(width, height);
                }

                Console.WriteLine($"RenderScheduler: Random image below 1080p minimum, skipping");
            }
            _randomImageSource = null;
        }

        string? imagePath = ResolveEpicImage(settings);

        if (imagePath != null)
        {
            renderer.LoadImage(gl, imagePath);
            SetCurrentImage(ImageSource.NasaEpic,
                Path.GetFileNameWithoutExtension(imagePath), null, imagePath, "NASA EPIC");
            return renderer.Render(width, height);
        }

        return new byte[width * height * 4];
    }

    /// <summary>
    /// Resolve which EPIC image file to use.
    /// </summary>
    private string? ResolveEpicImage(AppSettings settings)
    {
        try
        {
            List<EpicImageInfo>? images = null;

            if (settings.EpicUseLatest)
            {
                images = _epicApi.GetLatestImagesAsync(settings.EpicImageType)
                    .GetAwaiter().GetResult();
            }
            else if (!string.IsNullOrEmpty(settings.EpicSelectedDate))
            {
                images = _epicApi.GetImagesByDateAsync(settings.EpicImageType, settings.EpicSelectedDate)
                    .GetAwaiter().GetResult();
            }

            if (images != null && images.Count > 0)
            {
                EpicImageInfo selectedImage;
                if (!string.IsNullOrEmpty(settings.EpicSelectedImage))
                {
                    selectedImage = images.Find(i => i.Image == settings.EpicSelectedImage) ?? images[0];
                }
                else
                {
                    selectedImage = images[0];
                }

                var path = _epicApi.DownloadImageAsync(selectedImage, settings.EpicImageType, _epicCache)
                    .GetAwaiter().GetResult();

                if (path != null)
                {
                    _lastEpicImagePath = path;
                    return path;
                }
            }

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
        _settingsManager.SettingsChanged -= TriggerUserUpdate;
        Stop();
        _renderNowSignal.Dispose();
    }
}
