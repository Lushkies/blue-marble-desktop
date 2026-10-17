using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
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
    private bool _isTimerTick; // Distinguishes timer ticks from user-triggered renders

    // Set by "Next image" / turning auto-rotate on: the next render advances
    // rotation even though it is user-triggered, not a timer tick
    private volatile bool _advanceRotationRequested;

    // Gate: prevents all rendering until user explicitly triggers an update.
    // Defaults to true (first-ever run renders immediately).
    // Set to false on subsequent launches so the existing wallpaper is preserved.
    private volatile bool _userHasTriggered = true;

    /// <summary>A still image currently on screen.</summary>
    public sealed record CurrentImageInfo(ImageSource Source, string Id, string? Url, string? Path, string? Title);

    /// <summary>Key for the single image shown in Same-for-all / Span modes.</summary>
    public const string AllDisplaysKey = "*";

    // What each display is showing, keyed by DisplayInfo.MonitorId (or
    // AllDisplaysKey) — drives "Favorite Current Wallpaper" and the window's
    // "on display" markers. Written on the render thread, read on the UI thread.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, CurrentImageInfo> _currentImages = new();
    private string _currentDisplayKey = AllDisplaysKey;

    /// <summary>When the wallpaper was last applied, and when the next scheduled update is due (UTC).</summary>
    public DateTime? LastAppliedUtc { get; private set; }
    public DateTime? NextUpdateUtc { get; private set; }

    /// <summary>Raised on the render thread after a new wallpaper has been applied.</summary>
    public event Action? WallpaperApplied;

    // Reusable BMP save buffer (avoids LOH churn from creating Image<Rgba32> every cycle)
    private SixLabors.ImageSharp.Image<Rgba32>? _bmpBuffer;
    private int _bmpBufferWidth;
    private int _bmpBufferHeight;

    // PerDisplay renderer cache (avoids destroying/recreating renderers every cycle)
    private class PerDisplayState : IDisposable
    {
        public DisplayMode Mode;
        public ImageStyle Style;

        // Persistent settings snapshot for this display. Renderers capture it at
        // construction and read it at render time, so it must be refreshed in
        // place each cycle — a fresh object per cycle would leave the cached
        // renderers reading stale values forever.
        public readonly AppSettings Settings = new();

        public EarthRenderer? Earth;
        public FlatMapRenderer? Flat;
        public MoonRenderer? Moon;
        public StillImageRenderer? Still;

        public void DisposeRenderers()
        {
            Earth?.Dispose(); Earth = null;
            Flat?.Dispose(); Flat = null;
            Moon?.Dispose(); Moon = null;
            Still?.Dispose(); Still = null;
        }

        public void Dispose() => DisposeRenderers();
    }
    private readonly Dictionary<string, PerDisplayState> _perDisplayRenderers = new();

    public event Action<string>? StatusChanged;

    public RenderScheduler(SettingsManager settingsManager, AssetLocator assets)
    {
        _settingsManager = settingsManager;
        _assets = assets;
        _wallpaperPath = AppPaths.WallpaperFile;

        // On subsequent launches, preserve the existing wallpaper by holding off
        // scheduled renders — but only when showing a static still image. Live
        // modes (globe/flat map/moon) and auto-rotate must keep updating on
        // schedule, otherwise a reboot silently freezes them.
        if (_settingsManager.SkipFirstRender && IsStaticStillImage(_settingsManager.Settings))
            _userHasTriggered = false;

        // Re-render whenever settings change
        _settingsManager.SettingsChanged += TriggerUserUpdate;
    }

    /// <summary>
    /// True when every active display shows a still image without auto-rotation —
    /// the only configuration where skipping scheduled renders preserves anything.
    /// </summary>
    private static bool IsStaticStillImage(AppSettings settings)
    {
        if (settings.MultiMonitorMode == MultiMonitorMode.PerDisplay)
        {
            foreach (var display in MonitorManager.GetDisplays())
            {
                IDisplaySettings effective = (IDisplaySettings?)settings.FindDisplayConfig(display) ?? settings;
                if (effective.DisplayMode != DisplayMode.StillImage || effective.RandomRotationEnabled)
                    return false;
            }
            return true;
        }

        return settings.DisplayMode == DisplayMode.StillImage && !settings.RandomRotationEnabled;
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
    /// Re-render now and move auto-rotate on to its next image (a plain user
    /// trigger re-renders the current image instead).
    /// </summary>
    public void TriggerNextImage()
    {
        _advanceRotationRequested = true;
        TriggerUserUpdate();
    }

    /// <summary>
    /// The still image a display is showing (pass <see cref="AllDisplaysKey"/>
    /// outside Per-display mode), or null for globe/map/moon or nothing yet.
    /// </summary>
    public CurrentImageInfo? GetCurrentImage(string displayKey) =>
        _currentImages.TryGetValue(displayKey, out var info) ? info : null;

    /// <summary>
    /// Build a FavoriteImage from the currently displayed wallpaper, or null if
    /// not a still image. With several displays, the main display's image wins.
    /// </summary>
    public FavoriteImage? GetCurrentAsFavorite()
    {
        CurrentImageInfo? current = GetCurrentImage(AllDisplaysKey);
        if (_settingsManager.Settings.MultiMonitorMode == MultiMonitorMode.PerDisplay)
        {
            var displays = MonitorManager.GetDisplays();
            current = displays.Where(d => d.Primary).Concat(displays)
                .Select(d => GetCurrentImage(d.MonitorId)).FirstOrDefault(c => c != null);
        }
        if (current == null || string.IsNullOrEmpty(current.Id))
            return null;

        return new FavoriteImage
        {
            Source = current.Source,
            ImageId = current.Id,
            Title = current.Title ?? current.Id,
            FullImageUrl = current.Url ?? "",
            LocalCachePath = current.Path ?? ""
        };
    }

    private void SetCurrentImage(ImageSource source, string? id, string? url, string? path, string? title = null)
    {
        if (string.IsNullOrEmpty(id)) { ClearCurrentImage(); return; }
        _currentImages[_currentDisplayKey] = new CurrentImageInfo(source, id, url, path, title);
    }

    private void ClearCurrentImage() => _currentImages.TryRemove(_currentDisplayKey, out _);

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
        DateTime lastCacheClean = DateTime.UtcNow;
        int textureMonth = DateTime.UtcNow.Month;
        bool firstRender = true;
        GL? gl = null;

        var window = Window.Create(options);

        window.Load += () =>
        {
            gl = GL.GetApi(window);

            // Init failures (e.g. an unreadable texture) must not kill the render
            // thread — mark the mode dirty so the render loop retries each interval.
            try
            {
                InitializeRenderers(gl, settings, ref earthRenderer, ref flatMapRenderer, ref moonRenderer, ref stillImageRenderer);
                ReportStatus("Renderer initialized.");
            }
            catch (Exception ex)
            {
                DisposeRenderers(ref earthRenderer, ref flatMapRenderer, ref moonRenderer, ref stillImageRenderer);
                currentMode = (DisplayMode)(-1); // Force the re-init branch next cycle
                ReportStatus($"Renderer init failed: {ex.Message}");
            }

            try
            {
                // Clean blacklisted images from cache on startup
                _imageCache.CleanBlacklistedImages(settings.BlacklistedIdPrefixes);
                _epicCache.CleanBlacklistedImages(settings.BlacklistedIdPrefixes);

                // Clean old caches on startup (protect favorited images)
                int epicDays = settings.EpicCacheDurationDays;
                int imageDays = settings.CacheDurationDays;
                var protectedIds = GetProtectedImageIds(settings);
                if (epicDays > 0) _epicCache.CleanOldCache(epicDays, protectedIds);
                if (imageDays > 0) _imageCache.CleanOldCache(protectedIds, imageDays);
            }
            catch (Exception ex)
            {
                ReportStatus($"Cache cleanup error: {ex.Message}");
            }
        };

        window.Render += (_) =>
        {
            if (gl == null || _stopRequested)
            {
                if (_stopRequested) window.Close();
                return;
            }

            // On subsequent launches with a static still image, don't render until
            // the user explicitly triggers an update — this preserves the existing
            // wallpaper. Live modes and auto-rotate never set this gate.
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
            // Timer ticks advance auto-rotate; user-triggered renders re-render the
            // current image unless "Next image" explicitly asked to advance
            _isTimerTick = !signaled || _advanceRotationRequested;
            _advanceRotationRequested = false;
            firstRender = false;

            try
            {
                // Re-read settings
                settings = _settingsManager.Settings;

                // The earth day texture is month-based; force renderer re-init
                // when the month rolls over during long uptimes so the texture
                // actually changes
                if (DateTime.UtcNow.Month != textureMonth)
                {
                    textureMonth = DateTime.UtcNow.Month;
                    currentMode = (DisplayMode)(-1);
                    foreach (var state in _perDisplayRenderers.Values)
                        state.Mode = (DisplayMode)(-1);
                }

                bool applied;
                if (settings.MultiMonitorMode == MultiMonitorMode.PerDisplay)
                {
                    // Per-display rendering: render each monitor independently
                    applied = RenderPerDisplay(gl, settings);
                }
                else
                {
                    // Leaving PerDisplay mode: release the per-display renderer cache
                    if (_perDisplayRenderers.Count > 0)
                    {
                        foreach (var state in _perDisplayRenderers.Values)
                            state.Dispose();
                        _perDisplayRenderers.Clear();
                    }

                    // Standard rendering: single image for all monitors
                    _currentDisplayKey = AllDisplaysKey;
                    foreach (var key in _currentImages.Keys.Where(k => k != AllDisplaysKey).ToList())
                        _currentImages.TryRemove(key, out CurrentImageInfo? _discarded);
                    (renderWidth, renderHeight) = MonitorManager.GetRenderResolution(settings);

                    // Re-initialize renderers if display mode or image style changed.
                    // currentMode/currentImageStyle are committed only after init
                    // succeeds, so a transient failure retries next cycle instead of
                    // leaving null renderers behind permanently.
                    if (settings.DisplayMode != currentMode || settings.ImageStyle != currentImageStyle)
                    {
                        DisposeRenderers(ref earthRenderer, ref flatMapRenderer, ref moonRenderer, ref stillImageRenderer);
                        InitializeRenderers(gl, settings, ref earthRenderer, ref flatMapRenderer, ref moonRenderer, ref stillImageRenderer);
                        currentMode = settings.DisplayMode;
                        currentImageStyle = settings.ImageStyle;
                    }

                    // Handle image rotation for still image sources (only on timer ticks, not user-triggered renders)
                    if (settings.DisplayMode == DisplayMode.StillImage && settings.RandomRotationEnabled && _isTimerTick)
                    {
                        PickNextImage(settings, settings.RandomRotationSource);
                        MaybeProactiveFetch(settings);
                    }

                    string modeName = GetModeName(settings);
                    ReportStatus($"Rendering {modeName}...");

                    byte[]? pixels;
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

                    // Null means nothing could be rendered (offline, empty cache,
                    // missing selection) — keep the existing wallpaper instead of
                    // applying a black frame over it.
                    applied = pixels != null;
                    if (pixels != null)
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(_wallpaperPath)!);
                        SaveAsBmp(pixels, renderWidth, renderHeight, _wallpaperPath);
                        WallpaperSetter.SetWallpaper(_wallpaperPath, settings.MultiMonitorMode);
                    }
                }

                // Prune caches daily — this tray app runs for weeks between
                // restarts, and startup-only pruning would let auto-rotate grow
                // the cache unboundedly
                if ((DateTime.UtcNow - lastCacheClean).TotalHours >= 24)
                {
                    lastCacheClean = DateTime.UtcNow;
                    var protectedIds = GetProtectedImageIds(_settingsManager.Settings);
                    if (settings.EpicCacheDurationDays > 0)
                        _epicCache.CleanOldCache(settings.EpicCacheDurationDays, protectedIds);
                    if (settings.CacheDurationDays > 0)
                        _imageCache.CleanOldCache(protectedIds, settings.CacheDurationDays);
                }

                // Advance the schedule even when nothing was applied, so failures
                // retry on the next interval rather than in a tight loop.
                lastUpdate = DateTime.UtcNow;
                if (applied)
                {
                    LastAppliedUtc = DateTime.UtcNow;
                    ReportStatus($"Wallpaper updated at {DateTime.Now:HH:mm:ss}");
                    try { WallpaperApplied?.Invoke(); } catch { /* UI subscriber racing disposal */ }
                }
                NextUpdateUtc = lastUpdate.AddSeconds(settings.UpdateIntervalSeconds);
            }
            catch (Exception ex)
            {
                ReportStatus($"Render error: {ex.Message}");
                // Back off ~60s after a failure instead of retrying immediately —
                // a persistent failure would otherwise spin the loop hot. User
                // triggers (signal) bypass the interval check and retry at once.
                lastUpdate = DateTime.UtcNow.AddSeconds(-Math.Max(0, settings.UpdateIntervalSeconds - 60));
                NextUpdateUtc = DateTime.UtcNow.AddSeconds(60);
            }
        };

        window.Closing += () =>
        {
            DisposeRenderers(ref earthRenderer, ref flatMapRenderer, ref moonRenderer, ref stillImageRenderer);
            foreach (var state in _perDisplayRenderers.Values)
                state.Dispose();
            _perDisplayRenderers.Clear();
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
            ImageSource.NasaGallery => "NASA Gallery",
            _ => "still image"
        },
        _ => "earth"
    };

    private bool RenderPerDisplay(GL gl, AppSettings settings)
    {
        var displays = MonitorManager.GetDisplays();
        if (displays.Count == 0) return false;

        // Get virtual desktop bounds for composite
        var (vdWidth, vdHeight) = MonitorManager.GetVirtualDesktopSize();
        int vdLeft = int.MaxValue, vdTop = int.MaxValue;
        foreach (var display in displays)
        {
            if (display.Bounds.Left < vdLeft) vdLeft = display.Bounds.Left;
            if (display.Bounds.Top < vdTop) vdTop = display.Bounds.Top;
        }

        ReportStatus($"Rendering per-display ({displays.Count} monitors)...");
        _currentImages.TryRemove(AllDisplaysKey, out _);

        // Create composite image, seeded from the previous wallpaper when the
        // layout matches — a display that fails to render this cycle keeps its
        // last image instead of going black.
        using var composite = new SixLabors.ImageSharp.Image<Rgba32>(vdWidth, vdHeight, new Rgba32(0, 0, 0, 255));
        try
        {
            if (File.Exists(_wallpaperPath))
            {
                using var previous = SixLabors.ImageSharp.Image.Load<Rgba32>(ImageDecoding.Options, _wallpaperPath);
                if (previous.Width == vdWidth && previous.Height == vdHeight)
                    composite.Mutate(x => x.DrawImage(previous, new SixLabors.ImageSharp.Point(0, 0), 1f));
            }
        }
        catch { /* Unreadable previous wallpaper — start from black */ }

        bool anyRendered = false;

        foreach (var display in displays)
        {
            int sw = display.Bounds.Width;
            int sh = display.Bounds.Height;

            // This display's own config, or null to use the global settings.
            // Keyed by the stable monitor id, not the \\.\DISPLAYn name Windows
            // renumbers after reboots and docking.
            DisplayConfig? displayConfig = settings.FindDisplayConfig(display);

            var key = display.MonitorId;
            _currentDisplayKey = key;
            _randomImageOverride = null; // A rotation pick never leaks to another display
            bool isNew = !_perDisplayRenderers.TryGetValue(key, out var displayState);
            if (isNew)
            {
                displayState = new PerDisplayState();
                _perDisplayRenderers[key] = displayState;
            }

            // Refresh this display's persistent settings snapshot in place so the
            // cached renderers (which captured it at construction) see current values
            var displaySettings = displayState!.Settings;
            CopyDisplaySettings(displaySettings, settings, displayConfig);

            // Reuse cached renderers for this display if mode/style haven't changed
            if (isNew ||
                displayState.Mode != displaySettings.DisplayMode ||
                displayState.Style != displaySettings.ImageStyle)
            {
                // Mode or style changed (or first time) — dispose old and create new
                displayState.DisposeRenderers();
                EarthRenderer? er = null; FlatMapRenderer? fr = null;
                MoonRenderer? mr = null; StillImageRenderer? sr = null;
                InitializeRenderers(gl, displaySettings, ref er, ref fr, ref mr, ref sr);
                displayState.Earth = er;
                displayState.Flat = fr;
                displayState.Moon = mr;
                displayState.Still = sr;
                displayState.Mode = displaySettings.DisplayMode;
                displayState.Style = displaySettings.ImageStyle;
            }

            // Handle image rotation for this display (only on timer ticks, not user-triggered renders)
            if (displaySettings.DisplayMode == DisplayMode.StillImage && displaySettings.RandomRotationEnabled && _isTimerTick)
            {
                PickNextImage(displaySettings, displaySettings.RandomRotationSource);
                MaybeProactiveFetch(displaySettings);
            }

            // Render this display with its own cached renderers. These are never
            // assigned into the single-mode renderer locals — aliasing them caused
            // use-after-dispose and GPU leaks when switching multi-monitor modes.
            byte[]? pixels;
            if (displaySettings.DisplayMode == DisplayMode.StillImage)
            {
                var still = displayState.Still;
                if (displaySettings.StillImageSource == ImageSource.NasaEpic)
                {
                    pixels = RenderEpicImage(gl, displaySettings, ref still, sw, sh);
                }
                else
                {
                    pixels = RenderImageSource(gl, displaySettings, displaySettings.StillImageSource,
                        ref still, sw, sh);
                }
                displayState.Still = still; // May have been lazily created
            }
            else
            {
                pixels = displaySettings.DisplayMode switch
                {
                    DisplayMode.FlatMap => displayState.Flat!.Render(sw, sh),
                    DisplayMode.Moon when displayState.Moon != null => displayState.Moon.Render(sw, sh),
                    _ => displayState.Earth!.Render(sw, sh),
                };
                ClearCurrentImage(); // Globe/Flat Map/Moon are not favoritable
            }

            // Nothing to show for this display — keep its previous content
            if (pixels == null) continue;
            byte[] px = pixels;
            anyRendered = true;

            // Place rendered pixels into composite at the correct position
            int offsetX = display.Bounds.Left - vdLeft;
            int offsetY = display.Bounds.Top - vdTop;

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
                        destRow[offsetX + x] = new Rgba32(px[idx], px[idx + 1], px[idx + 2], 255);
                    }
                }
            });
        }

        // Clean up renderers for disconnected monitors
        var connectedNames = new HashSet<string>(displays.Select(d => d.MonitorId));
        var staleKeys = _perDisplayRenderers.Keys.Where(k => !connectedNames.Contains(k)).ToList();
        foreach (var key in staleKeys)
        {
            _perDisplayRenderers[key].Dispose();
            _perDisplayRenderers.Remove(key);
        }

        // No display produced an image — keep the existing wallpaper untouched
        if (!anyRendered) return false;

        // Save composite and set as spanned wallpaper
        Directory.CreateDirectory(Path.GetDirectoryName(_wallpaperPath)!);
        composite.SaveAsBmp(_wallpaperPath);
        WallpaperSetter.SetWallpaper(_wallpaperPath, MultiMonitorMode.SpanAcross);
        return true;
    }

    /// <summary>
    /// Copy the effective settings for one display (per-display config values with
    /// global fallbacks) onto that display's persistent settings snapshot.
    /// </summary>
    private static void CopyDisplaySettings(AppSettings target, AppSettings settings, DisplayConfig? displayConfig)
    {
        DisplaySettingsCopier.Copy((IDisplaySettings?)displayConfig ?? settings, target);

        // Global-only settings, shared so per-display rendering honors them too
        target.ApiDataGovKey = settings.ApiDataGovKey;
        target.Favorites = settings.Favorites;
        target.ExcludedRotationSources = settings.ExcludedRotationSources;
        target.BlacklistedIdPrefixes = settings.BlacklistedIdPrefixes;
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
    /// Render an image from a non-EPIC image source (APOD, NPS, Smithsonian, NASA Gallery).
    /// Downloads to cache if needed, falls back to cached images on error.
    /// Returns null when nothing can be rendered so callers keep the existing wallpaper.
    /// </summary>
    private byte[]? RenderImageSource(GL gl, AppSettings settings, ImageSource source,
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

            if (File.Exists(overridePath) &&
                TryLoadImage(renderer, gl, overridePath,
                    deleteOnFailure: DetectSourceFromPath(overridePath) != ImageSource.UserImages))
            {
                if (!renderer.IsBelowMinimumQuality)
                {
                    SetCurrentImage(effectiveSource, Path.GetFileNameWithoutExtension(overridePath), null, overridePath);
                    return renderer.Render(width, height);
                }

                Console.WriteLine($"RenderScheduler: Random image below 1080p minimum, skipping");
            }
        }

        // User images are local files — no download needed (and never deleted on
        // a failed load: they belong to the user)
        if (source == ImageSource.UserImages)
        {
            var path = settings.UserImageSelectedPath;
            if (!string.IsNullOrEmpty(path) && File.Exists(path) &&
                TryLoadImage(renderer, gl, path, deleteOnFailure: false))
            {
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
            if (allPaths.Count > 0 && TryLoadImage(renderer, gl, allPaths[0], deleteOnFailure: false))
            {
                if (!renderer.IsBelowMinimumQuality)
                {
                    SetCurrentImage(source, Path.GetFileNameWithoutExtension(allPaths[0]), null, allPaths[0]);
                    return renderer.Render(width, height);
                }
            }
            ReportStatus("User images: No images available");
            return null;
        }

        // Resolve the selected image URL for this source
        string? imageUrl = GetSelectedImageUrl(settings, source);
        string? imageId = GetSelectedImageId(settings, source);

        if (string.IsNullOrEmpty(imageUrl) || string.IsNullOrEmpty(imageId))
        {
            // No image selected — try cached fallback
            var cached = _imageCache.GetLatestCachedImagePath(source);
            if (cached != null && TryLoadImage(renderer, gl, cached, deleteOnFailure: true))
            {
                ReportStatus($"{source}: Using cached image (no selection)");
                SetCurrentImage(source, Path.GetFileNameWithoutExtension(cached), null, cached);
                return renderer.Render(width, height);
            }
            ReportStatus($"{source}: No image selected");
            return null;
        }

        // For NASA Gallery, resolve the best image URL from the asset manifest
        // (search results only return thumbnail URLs)
        if (source == ImageSource.NasaGallery && !_imageCache.IsCached(source, imageId))
        {
            try
            {
                var bestUrl = new NasaGalleryApiClient().GetBestImageUrlAsync(imageId)
                    .GetAwaiter().GetResult();
                if (!string.IsNullOrEmpty(bestUrl))
                    imageUrl = bestUrl;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"NASA Gallery URL resolve error: {ex.Message}");
            }
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
                ReportStatus($"Downloading {source} image...");
                imagePath = _imageCache.DownloadToCache(source, imageId, imageUrl)
                    .GetAwaiter().GetResult();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"{source} download error: {ex.Message}");
        }

        if (imagePath != null && File.Exists(imagePath) &&
            TryLoadImage(renderer, gl, imagePath, deleteOnFailure: true))
        {
            // Enforce minimum 1080p quality -- skip sub-1080p images
            if (renderer.IsBelowMinimumQuality)
            {
                ReportStatus($"{source}: Image below minimum 1080p quality, trying fallback");
                Console.WriteLine($"RenderScheduler: Skipping {imageId} -- below 1080p minimum");
            }
            else
            {
                SetCurrentImage(source, imageId, imageUrl, imagePath);
                ReportStatus("Wallpaper updated");
                return renderer.Render(width, height);
            }
        }

        // Download failed or below quality — try any cached image for this source
        var fallback = _imageCache.GetLatestCachedImagePath(source);
        if (fallback != null && TryLoadImage(renderer, gl, fallback, deleteOnFailure: true))
        {
            ReportStatus($"{source}: Using cached image (offline)");
            SetCurrentImage(source, Path.GetFileNameWithoutExtension(fallback), null, fallback);
            return renderer.Render(width, height);
        }

        ReportStatus($"{source}: No images available");
        return null;
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
        ImageSource.NasaGallery => settings.NasaGallerySelectedImageUrl,
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
        ImageSource.NasaGallery => settings.NasaGallerySelectedId,
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
    /// Filters out images matching blacklisted ID prefixes.
    /// </summary>
    private List<string> BuildRotationPool(AppSettings settings, RotationSource source)
    {
        var pool = source switch
        {
            RotationSource.NasaEpic => _epicCache.GetAllCachedImagePaths(settings.EpicImageType),
            RotationSource.NasaApod => _imageCache.GetAllCachedImagePaths(ImageSource.NasaApod),
            RotationSource.NationalParks => _imageCache.GetAllCachedImagePaths(ImageSource.NationalParks),
            RotationSource.Smithsonian => _imageCache.GetAllCachedImagePaths(ImageSource.Smithsonian),
            RotationSource.UserImages => new UserImageManager().GetAllImagePaths(),
            RotationSource.NasaGallery => _imageCache.GetAllCachedImagePaths(ImageSource.NasaGallery),
            RotationSource.Favorites => GetFavoriteImagePaths(settings),
            RotationSource.All => BuildWeightedAllPool(settings),
            _ => new List<string>()
        };

        // Filter out blacklisted images
        var blacklist = settings.BlacklistedIdPrefixes;
        if (blacklist != null && blacklist.Count > 0)
        {
            pool.RemoveAll(path => ImageCache.IsBlacklisted(
                Path.GetFileNameWithoutExtension(path), blacklist));
        }

        return pool;
    }

    /// <summary>
    /// Get cached file paths for all favorites (thread-safe).
    /// </summary>
    private List<string> GetFavoriteImagePaths(AppSettings settings)
    {
        // Favorites are global — always lock the global settings instance, since a
        // per-display snapshot carries its own lock object that nobody else uses.
        List<FavoriteImage> favs;
        lock (_settingsManager.Settings.FavoritesLock)
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
    /// Weights (base 20): Gallery 10 (50%), APOD 4 (20%), EPIC 2 (10%),
    ///   NPS 2 (10%), Smithsonian 2 (10%), User Images 0-4 (0-17% scaled by collection size).
    /// Respects ExcludedRotationSources — excluded sources are skipped entirely.
    /// Shuffled deterministically so sequential cycling produces mixed order.
    /// </summary>
    private List<string> BuildWeightedAllPool(AppSettings settings)
    {
        var excluded = settings.ExcludedRotationSources;
        var pool = new List<string>();

        if (!excluded.Contains(RotationSource.NasaGallery))
            AddWeighted(pool, _imageCache.GetAllCachedImagePaths(ImageSource.NasaGallery), 10);
        if (!excluded.Contains(RotationSource.NasaApod))
            AddWeighted(pool, _imageCache.GetAllCachedImagePaths(ImageSource.NasaApod), 4);
        if (!excluded.Contains(RotationSource.NasaEpic))
            AddWeighted(pool, _epicCache.GetAllCachedImagePaths(settings.EpicImageType), 2);
        if (!excluded.Contains(RotationSource.NationalParks))
            AddWeighted(pool, _imageCache.GetAllCachedImagePaths(ImageSource.NationalParks), 2);
        if (!excluded.Contains(RotationSource.Smithsonian))
            AddWeighted(pool, _imageCache.GetAllCachedImagePaths(ImageSource.Smithsonian), 2);

        // User images: scaled by collection size (0=skip, 1=single copy, 2-14=10%, 15+=17%)
        if (!excluded.Contains(RotationSource.UserImages))
        {
            var userPaths = new UserImageManager().GetAllImagePaths();
            int userWeight = userPaths.Count switch { 0 => 0, 1 => 1, < 15 => 2, _ => 4 };
            if (userWeight > 0) AddWeighted(pool, userPaths, userWeight);
        }

        // Add favorites that aren't already in the pool (e.g., from sources without local cache)
        if (!excluded.Contains(RotationSource.Favorites))
        {
            foreach (var path in GetFavoriteImagePaths(settings))
            {
                if (!pool.Contains(path))
                    pool.Add(path);
            }
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
    /// user_images is checked first — a user file NAMED e.g. "smithsonian.jpg"
    /// must not be mislabeled as a cache image.
    /// </summary>
    private static ImageSource? DetectSourceFromPath(string path)
    {
        if (path.Contains("user_images")) return ImageSource.UserImages;
        if (path.Contains("epic_images")) return ImageSource.NasaEpic;
        if (path.Contains("nasaapod")) return ImageSource.NasaApod;
        if (path.Contains("nationalparks")) return ImageSource.NationalParks;
        if (path.Contains("smithsonian")) return ImageSource.Smithsonian;
        if (path.Contains("nasagallery")) return ImageSource.NasaGallery;
        return null;
    }

    /// <summary>
    /// Load an image into the renderer, returning false instead of throwing.
    /// A corrupt cached download is deleted so it can't wedge rendering forever;
    /// user-owned files are never deleted.
    /// </summary>
    private static bool TryLoadImage(StillImageRenderer renderer, GL gl, string path, bool deleteOnFailure)
    {
        try
        {
            renderer.LoadImage(gl, path);
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"RenderScheduler: Failed to load {Path.GetFileName(path)}: {ex.Message}");
            if (deleteOnFailure)
            {
                try { File.Delete(path); } catch { }
            }
            return false;
        }
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

        // Determine fetch frequency based on pool size, API key, and source.
        // The DEMO_KEY check comes FIRST: its 50 requests/day quota would be
        // burned within the first hour if the small-pool aggressive mode applied.
        bool isDemoKey = settings.ApiDataGovKey == "DEMO_KEY";
        bool isNoKeySource = source == RotationSource.NasaGallery; // No API key, no rate limit
        bool shouldFetch;
        if (isDemoKey && !isNoKeySource)
            shouldFetch = (_poolBuildCycleCount % 5) == 0; // Conservative for DEMO_KEY sources
        else if (currentPoolSize < 20)
            shouldFetch = true; // Aggressive when pool is small
        else if (isNoKeySource || source == RotationSource.All)
            shouldFetch = (_poolBuildCycleCount % 3) == 0; // Gallery + All: more frequent
        else
            shouldFetch = (_poolBuildCycleCount % 5) == 0; // Maintenance for keyed sources

        if (!shouldFetch) return;

        // Fire-and-forget background fetch (doesn't block render)
        var apiKey = settings.ApiDataGovKey;
        var epicType = settings.EpicImageType;
        var blacklist = settings.BlacklistedIdPrefixes;
        var excluded = settings.ExcludedRotationSources;
        Task.Run(async () =>
        {
            try
            {
                await FetchNewImageForPool(source, apiKey, epicType, blacklist, excluded);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Pool build fetch error: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// Download new images from a source to grow the rotation pool.
    /// Respects blacklisted ID prefixes and excluded rotation sources.
    /// </summary>
    private async Task FetchNewImageForPool(RotationSource source, string apiKey, EpicImageType epicType,
        List<string>? blacklist = null, List<RotationSource>? excluded = null)
    {
        switch (source)
        {
            case RotationSource.NasaApod:
            {
                // Fetch a random APOD from the last 2 years
                var randomDate = DateTime.UtcNow.AddDays(-Random.Shared.Next(1, 730));
                var apodImage = await new ApodApiClient().GetByDateAsync(apiKey, randomDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
                if (apodImage != null && !ImageCache.IsBlacklisted(apodImage.Id, blacklist))
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
                    foreach (var img in images.Where(i => !ImageCache.IsBlacklisted(i.Id, blacklist)).Take(3))
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
                    foreach (var img in images.Where(i => !ImageCache.IsBlacklisted(i.Id, blacklist)).Take(3))
                    {
                        var url = !string.IsNullOrEmpty(img.HdImageUrl) ? img.HdImageUrl : img.FullImageUrl;
                        if (!string.IsNullOrEmpty(url))
                            await _imageCache.DownloadToCache(ImageSource.Smithsonian, img.Id, url);
                    }
                    Console.WriteLine($"Pool build: Fetched Smithsonian images");
                }
                break;
            }
            case RotationSource.NasaEpic:
            {
                // Fetch latest EPIC images
                var epicImages = await _epicApi.GetLatestImagesAsync(epicType);
                if (epicImages != null)
                {
                    foreach (var img in epicImages.Where(i => !ImageCache.IsBlacklisted(i.Image, blacklist)).Take(3))
                    {
                        if (!_epicCache.IsCached(img, epicType))
                            await _epicApi.DownloadImageAsync(img, epicType, _epicCache);
                    }
                    Console.WriteLine($"Pool build: Fetched EPIC images");
                }
                break;
            }
            case RotationSource.NasaGallery:
            {
                // Fetch images from a random curated query — more aggressive caching
                // since Gallery is the primary rotation source (50% of weighted pool)
                var queries = NasaGalleryApiClient.SuggestedQueries;
                var randomQuery = queries[Random.Shared.Next(queries.Length)];
                var galleryClient = new NasaGalleryApiClient();
                var images = await galleryClient.SearchImagesAsync(randomQuery, 20);
                if (images != null)
                {
                    // Filter blacklisted IDs before downloading
                    var filtered = images.Where(i => !ImageCache.IsBlacklisted(i.Id, blacklist)).ToList();

                    // Fetch 5 images per call (higher than other sources) to build cache faster
                    foreach (var img in filtered.Take(5))
                    {
                        var bestUrl = await galleryClient.GetBestImageUrlAsync(img.Id);
                        if (!string.IsNullOrEmpty(bestUrl))
                            await _imageCache.DownloadToCache(ImageSource.NasaGallery, img.Id, bestUrl);
                    }
                    Console.WriteLine($"Pool build: Fetched NASA Gallery \"{randomQuery}\" ({Math.Min(5, filtered.Count)} images)");
                }
                break;
            }
            case RotationSource.All:
            {
                // Pick a random source (weighted: Gallery 5x, APOD 2x, EPIC 1x, NPS 1x, SI 1x)
                // Respects excluded sources
                var sources = new List<RotationSource>();
                if (excluded == null || !excluded.Contains(RotationSource.NasaGallery))
                    sources.AddRange(Enumerable.Repeat(RotationSource.NasaGallery, 5));
                if (excluded == null || !excluded.Contains(RotationSource.NasaApod))
                    sources.AddRange(Enumerable.Repeat(RotationSource.NasaApod, 2));
                if (excluded == null || !excluded.Contains(RotationSource.NasaEpic))
                    sources.Add(RotationSource.NasaEpic);
                if (excluded == null || !excluded.Contains(RotationSource.NationalParks))
                    sources.Add(RotationSource.NationalParks);
                if (excluded == null || !excluded.Contains(RotationSource.Smithsonian))
                    sources.Add(RotationSource.Smithsonian);

                if (sources.Count > 0)
                    await FetchNewImageForPool(sources[Random.Shared.Next(sources.Count)], apiKey, epicType, blacklist, excluded);
                break;
            }
        }
    }

    /// <summary>
    /// Build set of sanitized favorite image IDs for cache protection.
    /// </summary>
    private static HashSet<string> GetProtectedImageIds(AppSettings settings)
    {
        // Snapshot under the lock — the UI thread mutates Favorites
        lock (settings.FavoritesLock)
        {
            return new HashSet<string>(
                settings.Favorites.Select(f => ImageCache.SanitizeFileName(f.ImageId)));
        }
    }

    /// <summary>
    /// Fetch (or use cached) EPIC image and render it via the StillImageRenderer.
    /// Returns null when nothing can be rendered so callers keep the existing wallpaper.
    /// </summary>
    private byte[]? RenderEpicImage(GL gl, AppSettings settings, ref StillImageRenderer? renderer,
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

            if (File.Exists(overridePath) &&
                TryLoadImage(renderer, gl, overridePath,
                    deleteOnFailure: DetectSourceFromPath(overridePath) != ImageSource.UserImages))
            {
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

        if (imagePath != null && TryLoadImage(renderer, gl, imagePath, deleteOnFailure: true))
        {
            SetCurrentImage(ImageSource.NasaEpic,
                Path.GetFileNameWithoutExtension(imagePath), null, imagePath, "NASA EPIC");
            return renderer.Render(width, height);
        }

        return null;
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
        try
        {
            StatusChanged?.Invoke(message);
        }
        catch
        {
            // A UI subscriber racing form disposal must never kill the render thread
        }
    }

    private void SaveAsBmp(byte[] rgbaPixels, int width, int height, string path)
    {
        if (_bmpBuffer == null || _bmpBufferWidth != width || _bmpBufferHeight != height)
        {
            _bmpBuffer?.Dispose();
            _bmpBuffer = new SixLabors.ImageSharp.Image<Rgba32>(width, height);
            _bmpBufferWidth = width;
            _bmpBufferHeight = height;
        }

        _bmpBuffer.ProcessPixelRows(accessor =>
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
        _bmpBuffer.SaveAsBmp(path);
    }

    public void Dispose()
    {
        _settingsManager.SettingsChanged -= TriggerUserUpdate;
        Stop();
        _renderNowSignal.Dispose();
        _bmpBuffer?.Dispose();
        _bmpBuffer = null;
        foreach (var state in _perDisplayRenderers.Values)
            state.Dispose();
        _perDisplayRenderers.Clear();
    }
}
