# Desktop Earth - Windows 11 Revival Project

## Overview
Reviving Desktop Earth (v3.2.42), a 3D Earth visualization wallpaper app originally by Marton Anka (anka.me). Rebuilt from scratch for Windows 11.

## Current Status: System Tray App with Settings UI
Full-featured tray application with settings dialog, multiple renderers, and multi-monitor support.

## Build & Run
```
cd src/DesktopEarth
dotnet build
dotnet run
```
- Requires .NET 8 SDK (installed at `C:\Program Files\dotnet`)
- Runs as a system tray app (no console window)
- Double-click tray icon to open settings
- Right-click tray icon for context menu

## Publish (Self-contained)
```powershell
# x64
dotnet publish src/DesktopEarth/DesktopEarth.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish/x64

# ARM64
dotnet publish src/DesktopEarth/DesktopEarth.csproj -c Release -r win-arm64 --self-contained -p:PublishSingleFile=true -o publish/arm64
```
Or use `.\build.ps1 -Target all`

## Project Structure
```
src/DesktopEarth/
  Program.cs               # WinForms entry point, single-instance mutex
  AppSettings.cs            # Settings data model + enums
  SettingsManager.cs        # JSON load/save to %AppData%/DesktopEarth/
  AssetLocator.cs           # Texture path discovery
  RenderScheduler.cs        # Background thread: GLFW/OpenGL render loop
  MonitorManager.cs         # Multi-monitor detection
  WallpaperSetter.cs        # Windows API wallpaper setter with style support
  StartupManager.cs         # Windows startup registry management
  Resources/
    desktopearth.ico        # Multi-size app icon (16/32/48/256)
  Rendering/
    EarthRenderer.cs        # 3D globe renderer (specular, night lights, bathy mask)
    FlatMapRenderer.cs      # 2D equirectangular projection
    MoonRenderer.cs         # 3D moon globe
    GlobeMesh.cs            # UV sphere mesh generation
    ShaderProgram.cs        # OpenGL shader helper
    Shaders.cs              # All GLSL shaders
    TextureManager.cs       # ImageSharp -> OpenGL texture loading
    SunPosition.cs          # Astronomical sun position
    GraphicsCapabilityDetector.cs  # ARM64/Mesa3D detection
  UI/
    TrayApplicationContext.cs  # System tray icon + context menu
    SettingsForm.cs            # Tabbed settings dialog (5 tabs)
    AboutForm.cs               # Version/credits dialog
assets/
  textures/                # 30 JPEG earth textures
  original_binaries/       # Original .exe, .scr, .dll (reference only)
tools/
  GenIcon/                 # Icon generator tool
build.ps1                  # PowerShell build script (x64 + ARM64)
lib/
  mesa3d/                  # Mesa3D opengl32.dll for ARM64 fallback
```

## Tech Stack
- **.NET 8** (net8.0-windows) with WinForms
- **Silk.NET** 2.23.0 — OpenGL 3.3 Core, GLFW windowing
- **SixLabors.ImageSharp** 3.1.12 — texture loading, BMP export
- **GLSL 3.3** shaders

## Key Features
- 3D OpenGL globe with high-res satellite textures
- Day/night lighting with smooth terminator, real sun position
- City lights on night side (configurable brightness)
- Atmospheric glow, specular reflection on oceans
- Flat map and moon view modes
- System tray with settings dialog
- Multi-monitor support (fill / span)
- Auto-selects monthly texture (12 months of topo data)
- Single-instance enforcement
- Settings persisted as JSON in %AppData%

## User Context
- The user is new to coding — keep things approachable
- ARM64 support is HIGH PRIORITY
- Ask before making big architectural decisions
