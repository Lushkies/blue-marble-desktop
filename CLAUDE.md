# Desktop Earth - Windows 11 Revival Project

## Overview
Reviving Desktop Earth (v3.2.42), a 3D Earth visualization wallpaper app originally by Marton Anka (anka.me). Rebuilt from scratch for Windows 11.

## Current Status: Working MVP
The app builds, runs, and sets the desktop wallpaper with a 3D globe.

## Build & Run
```
cd src/DesktopEarth
dotnet build
dotnet run
```
- Requires .NET 8 SDK (installed at `C:\Program Files\dotnet`)
- Renders a 1920x1080 globe image and sets it as the Windows wallpaper
- Updates every 10 minutes; press Ctrl+C to stop

## Project Structure
```
src/DesktopEarth/          # C# source code
  Program.cs               # Entry point, render loop, wallpaper update
  GlobeMesh.cs             # UV sphere mesh generation for OpenGL
  ShaderProgram.cs         # OpenGL shader compilation/uniform helpers
  Shaders.cs               # GLSL vertex/fragment shaders (earth + atmosphere)
  SunPosition.cs           # Astronomical sun position calculator
  TextureManager.cs        # Texture loading via ImageSharp -> OpenGL
  WallpaperSetter.cs       # Windows API (SystemParametersInfo) wallpaper setter
assets/
  textures/                # 30 JPEG earth textures (monthly topo, night lights, moon)
  original_binaries/       # Original .exe, .scr, .dll from the MSI (reference only)
  license.rtf              # Original license
```

## Tech Stack
- **.NET 8** (net8.0-windows)
- **Silk.NET** 2.23.0 — OpenGL 3.3 Core, GLFW windowing
- **SixLabors.ImageSharp** 3.1.12 — texture loading, BMP export
- **GLSL 3.3** shaders with day/night blending and atmospheric glow

## Key Features
- 3D OpenGL globe with high-res satellite textures
- Day/night lighting with smooth terminator, based on real sun position
- City lights visible on the night side
- Atmospheric blue glow at globe edges
- Auto-selects monthly texture (12 months of topography data)
- Earth rotation matches actual UTC time

## User Context
- The user is new to coding — explain concepts, avoid jargon, keep things approachable
- Prefer clear step-by-step guidance over dense technical explanations
- Ask before making big architectural decisions

## Future Work
- ARM64 (Windows on ARM) support
- System tray icon with settings UI
- Configurable update interval and render resolution
- Multi-monitor support
- Cloud overlay from weather data
- Stars background
