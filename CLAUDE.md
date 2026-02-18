# Desktop Earth - Windows 11 Revival Project

## Overview
Reviving Desktop Earth (v3.2.42), a 3D Earth visualization and screensaver app originally by Marton Anka (anka.me). The app is long unsupported and needs to be updated for modern Windows.

## Original App Details
- **Original MSI:** `DesktopEarthSetup3.2.42.msi` (reference installer)
- **Framework:** .NET Framework 4.0 (Client Profile)
- **Language:** Compiled .NET (likely C# or VB.NET)
- **Graphics:** OpenGL (bundled opengl32.dll, ~15 MB)
- **Update lib:** WinSparkle.dll (no longer needed)
- **Assets:** 24 high-res JPEG Earth textures (8192x4096), moon texture, night lights
- **Components:** Main app (DesktopEarth.exe) + Screensaver (DEarth.scr)

## Project Goals
1. **Primary:** Get Desktop Earth working on Windows 11 (x64)
2. **Future:** Add ARM64 (Windows on ARM) support
3. Modernize the codebase where necessary

## User Context
- The user is new to coding — explain concepts, avoid jargon, and keep things approachable
- Prefer clear step-by-step guidance over dense technical explanations
- Ask before making big architectural decisions

## Technical Notes
- The original app targets .NET 4.0 — will likely need migration to .NET 8+ for modern support
- OpenGL rendering — evaluate whether to keep OpenGL or move to a modern alternative
- Screensaver (.scr) support on Windows 11 still works but is less common
- Original MSI installer can be extracted to recover assets and inspect binaries
- No source code available — this is a reverse-engineering / rebuild project

## Conventions (to establish as project grows)
- Target: .NET 8+ (or latest LTS) for Windows 11 compatibility
- Language: C# (matching the likely original language)
- Keep earth texture assets in an `assets/` folder
- Use Git for version control once code exists
