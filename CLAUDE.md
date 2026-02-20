# Blue Marble Desktop

## Overview

A Windows desktop wallpaper app that renders a real-time 3D Earth globe with day/night lighting using OpenGL, and sets it as your wallpaper. Also supports flat map, moon, and still images from NASA, National Parks, and Smithsonian.

**Current version:** 4.2.0
**Authors:** Alex and Claude (Anthropic)
**Repo:** https://github.com/Lushkies/blue-marble-desktop

---

## Build & Run

```bash
cd src/DesktopEarth
dotnet build
dotnet run
```

- Requires .NET 8 SDK
- Runs as a system tray app (no console window)
- Double-click tray icon to open settings, right-click for context menu

## Publish (Self-contained)

```bash
# x64
dotnet publish src/DesktopEarth/DesktopEarth.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish/x64

# ARM64
dotnet publish src/DesktopEarth/DesktopEarth.csproj -c Release -r win-arm64 --self-contained -p:PublishSingleFile=true -o publish/arm64
```

Build installers with Inno Setup:
```bash
"C:\Users\Alex\AppData\Local\Programs\Inno Setup 6\ISCC.exe" installer/BlueMarbleDesktop-x64.iss
"C:\Users\Alex\AppData\Local\Programs\Inno Setup 6\ISCC.exe" installer/BlueMarbleDesktop-arm64.iss
```

---

## Architecture

### Enums (AppSettings.cs)

```
DisplayMode:   Spherical | FlatMap | Moon | StillImage
ImageSource:   NasaEpic | NasaApod | NationalParks | Smithsonian
ImageStyle:    Topo | TopoBathy
MultiMonitorMode: SameForAll | SpanAcross | PerDisplay
EpicImageType: Natural | Enhanced
```

When `DisplayMode == StillImage`, the `StillImageSource` property selects which service to use.

### Key Data Flow

1. **SettingsForm** (UI) writes to `AppSettings` via `SettingsManager.ApplyAndSave()`
2. **RenderScheduler** reads settings, routes to the correct renderer
3. For still images: RenderScheduler checks `StillImageSource` to pick EPIC/APOD/NPS/Smithsonian
4. Rendered output saved as BMP, then **WallpaperSetter** applies it via Windows API

### API Keys

All still image sources (except EPIC, which needs no key) use a **single api.data.gov API key** stored in `AppSettings.ApiDataGovKey`. Default is `DEMO_KEY` (50 requests/day). Users get a free key at https://api.data.gov/signup/

### Settings Migration (SettingsManager.cs)

The app auto-migrates old settings JSON:
- Old `DisplayMode` values (`NasaEpic`, `NasaApod`, `NationalParks`, `Smithsonian`, `Unsplash`) are mapped to `StillImage` with the appropriate `StillImageSource`
- Old separate API keys (`NasaApiKey`, `NpsApiKey`, `SmithsonianApiKey`, `UnsplashAccessKey`) are migrated to `ApiDataGovKey`
- Uses `[JsonExtensionData]` to capture unknown properties during deserialization

### Cache System

Two separate caches:
- **EpicImageCache** (`%AppData%/BlueMarbleDesktop/epic_images/`): Organized by type/date, 14-day retention
- **ImageCache** (`%AppData%/BlueMarbleDesktop/image_cache/`): For APOD/NPS/Smithsonian, 30-day retention, protects favorited images, always keeps at least 1 image per source for offline fallback

---

## Project Structure

```
src/DesktopEarth/
  Program.cs                # WinForms entry point, single-instance mutex
  AppSettings.cs            # Settings data model + all enums
  SettingsManager.cs        # JSON load/save + migration logic
  RenderScheduler.cs        # Background render loop, routes to renderers
  AssetLocator.cs           # Texture path discovery
  MonitorManager.cs         # Multi-monitor detection
  MonitorNameHelper.cs      # Friendly monitor names via Win32 API
  WallpaperSetter.cs        # Windows API wallpaper setter
  StartupManager.cs         # Windows startup registry
  ApodApiClient.cs          # NASA Astronomy Picture of the Day API
  EpicApiClient.cs          # NASA EPIC satellite imagery API
  EpicImageCache.cs         # EPIC-specific image cache (by date/type)
  HiResTextureManager.cs    # HD texture download manager
  ImageCache.cs             # Unified cache for APOD/NPS/Smithsonian
  ImageSourceInfo.cs        # Shared image metadata model
  NpsApiClient.cs           # National Park Service API
  SmithsonianApiClient.cs   # Smithsonian Open Access API
  Rendering/
    EarthRenderer.cs        # 3D globe (specular, night lights, bathy mask)
    FlatMapRenderer.cs      # 2D equirectangular projection
    MoonRenderer.cs         # 3D moon globe
    StillImageRenderer.cs   # Renders downloaded still images as wallpaper
    GlobeMesh.cs            # UV sphere mesh generation
    ShaderProgram.cs        # OpenGL shader helper
    Shaders.cs              # All GLSL shaders
    TextureManager.cs       # ImageSharp -> OpenGL texture loading
    SunPosition.cs          # Astronomical sun position calculation
    GraphicsCapabilityDetector.cs  # ARM64/Mesa3D detection
  UI/
    TrayApplicationContext.cs  # System tray icon + context menu
    SettingsForm.cs            # Tabbed settings dialog (3 tabs)
    ThumbnailGridPanel.cs      # Reusable image grid with selection + favorites
    AboutForm.cs               # Version/credits dialog
  Resources/
    bluemarbledesktop.ico      # Multi-size app icon
assets/
  textures/                    # 30 JPEG earth textures (12 topo + 12 topo-bathy + lights + moon + mask + black)
installer/
  BlueMarbleDesktop-x64.iss   # Inno Setup script (x64)
  BlueMarbleDesktop-arm64.iss # Inno Setup script (ARM64)
  output/                      # Built installer .exe files
build.ps1                      # PowerShell build script
lib/
  mesa3d/                      # Mesa3D software renderer for ARM64 fallback
```

---

## Tech Stack

- **.NET 8** (net8.0-windows) with WinForms
- **Silk.NET** 2.23.0 -- OpenGL 3.3 Core, GLFW windowing
- **SixLabors.ImageSharp** 3.1.12 -- texture loading, BMP export
- **GLSL 3.3** shaders
- **Inno Setup 6** -- Windows installers
- **GitHub Releases** -- distribution (no `gh` CLI, use curl + API)

---

## Development Standards

### Before Every Commit
1. `dotnet build` must pass with **0 errors, 0 warnings**
2. Publish both x64 and ARM64 to verify cross-compilation works
3. Stage specific files (not `git add .`) to avoid committing secrets or binaries

### Release Process
1. Update version in 3 places: `DesktopEarth.csproj`, `BlueMarbleDesktop-x64.iss`, `BlueMarbleDesktop-arm64.iss`
2. Build + publish x64 and ARM64
3. Build both installers with ISCC.exe
4. Commit, push, create GitHub release via API, upload both .exe installers
5. **No emoji in GitHub release notes. Ever.**

### Code Style
- C# namespace: `DesktopEarth` (internal, never changed)
- User-facing name: "Blue Marble Desktop" everywhere
- Settings JSON lives in `%AppData%/BlueMarbleDesktop/`
- When adding new settings: add to both `AppSettings` AND `DisplayConfig` (for per-monitor support)
- New enum values should be added at the end to preserve JSON serialization backward compatibility
- When removing features: migrate old settings gracefully in `SettingsManager.Load()`

### Working With Alex
- Alex is new to coding -- keep explanations clear and approachable
- ARM64 support is HIGH PRIORITY (test both platforms on every release)
- **Ask before making big architectural decisions** -- don't restructure without approval
- **Critically evaluate Alex's proposals** -- push back when ideas have better alternatives
- Don't be intentionally affirming -- give honest, direct feedback
- If a proposal is good, say why. If it's not, say why and suggest alternatives.

---

## Version History

- **v1.0** - Basic 3D globe renderer, desktop wallpaper setter
- **v2.0** - System tray, settings UI, flat map + moon views, multi-monitor
- **v2.1** - ARM64 support, Mesa3D fallback, build scripts
- **v3.0** - Inno Setup installers, auto-updater, rename to "Blue Marble Desktop"
- **v3.1** - NASA EPIC satellite imagery, per-monitor resolution fix
- **v3.2** - HD texture download (21600x10800), zoom slider
- **v4.0** - Multiple image sources (APOD, NPS, Smithsonian, Unsplash), thumbnail grids, favorites, random rotation
- **v4.1** - Remove Unsplash, combine still images into unified view with sub-dropdown, consolidate API keys to single api.data.gov key, search suggestion chips, cache improvements (30-day retention, protected favorites, offline fallback), UI polish
- **v4.2** - Resizable settings window, fix Smithsonian API (correct endpoint + Solr query syntax), fix NPS search (exact park codes for chips), image quality tiers (SD/HD/UD badges + filter), minimum 1080p enforcement, 28 curated national park chips

---

## Known Issues and Future Work

### Known Issues
- NPS photos often include people (park rangers, visitors, buildings) rather than nature landscapes -- no API-level filter exists for this
- Smithsonian search results depend on the art_design category -- some terms work better than others
- EPIC images are sometimes delayed 24-48 hours from NASA
- The SettingsForm is one large file (~1800 lines) -- could benefit from being split into partial classes or user controls if it grows further

### Potential Future Features
- Filter NPS results to exclude photos with people (would need image analysis or metadata heuristics)
- Pexels or Pixabay as image sources (free APIs, less restrictive than Unsplash was)
- Seasonal/holiday themes
- User-uploaded custom images as wallpaper source
- Wallpaper scheduling (different wallpapers at different times of day)
- Cloud sync for favorites
- Notification when new APOD is available
