# Blue Marble Desktop

A Windows desktop wallpaper app that renders a real-time 3D Earth globe with day/night illumination using OpenGL, and sets it as your wallpaper.

## Features

**Live 3D Globe** - A rotating Earth showing real-time sunlight and shadow based on your local time. City lights appear on the night side. Zoom in, adjust lighting, and toggle between topographic and bathymetric textures.

**Multiple Views** - Switch between a 3D globe, flat map projection, moon view, or still images from several sources.

**Still Image Sources:**
- **NASA EPIC** - Real satellite photos of Earth from the DSCOVR spacecraft
- **NASA APOD** - Astronomy Picture of the Day
- **NASA Gallery** - 140,000+ public domain images from NASA's Image and Video Library
- **National Parks** - Photos from the National Park Service (28 curated parks)
- **Smithsonian** - Art and design from the Smithsonian Open Access collection
- **My Images** - Import your own images as wallpaper

**Auto-Rotate Wallpaper** - Automatically cycle through images from any source (or all sources at once) on a configurable timer.

**Multi-Monitor Support** - Same wallpaper on all displays, span across monitors, or configure each display independently.

**HD Textures** - Download ultra-high-resolution Earth textures (21600x10800) for crisp detail at any zoom level.

**Favorites & Collections** - Favorite any image, browse your collection across all sources, and export images.

**Settings Presets** - Save and load named appearance configurations.

## Download

Download the latest installer from [Releases](https://github.com/Lushkies/blue-marble-desktop/releases):

- **BlueMarbleDesktop-x64-setup.exe** - Standard Windows (x64)
- **BlueMarbleDesktop-arm64-setup.exe** - Windows on ARM (ARM64)

Both installers are self-contained and include the .NET 8 runtime.

## Usage

After installation, Blue Marble Desktop runs as a system tray application:

- **Double-click** the tray icon to open settings
- **Right-click** for quick access to update wallpaper, favorite the current image, or open settings
- The app starts automatically with Windows (configurable in settings)

### API Key (Optional)

NASA EPIC and NASA Gallery work without any API key. For NASA APOD, National Parks, and Smithsonian, the app uses a default demo key (50 requests/day). For heavier usage, get a free API key at [api.data.gov/signup](https://api.data.gov/signup/) and enter it in Settings > System.

## Build from Source

Requires [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```bash
cd src/DesktopEarth
dotnet build
dotnet run
```

### Publish (Self-contained)

```bash
# x64
dotnet publish src/DesktopEarth/DesktopEarth.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish/x64

# ARM64
dotnet publish src/DesktopEarth/DesktopEarth.csproj -c Release -r win-arm64 --self-contained -p:PublishSingleFile=true -o publish/arm64
```

## Tech Stack

- **.NET 8** with WinForms
- **Silk.NET** (OpenGL 3.3 Core, GLFW)
- **SixLabors.ImageSharp** (texture loading, BMP export)
- **GLSL 3.3** shaders
- **Inno Setup 6** (Windows installers)

## Credits

Created by Alex and Claude (Anthropic).

Image sources and data:
- **NASA** -- Earth textures, EPIC satellite imagery (DSCOVR), Astronomy Picture of the Day, and the NASA Image and Video Library. All public domain.
- **National Park Service** -- Park photography via the NPS API.
- **Smithsonian Institution** -- Art and design from the Smithsonian Open Access collection (CC0 public domain).
- **NASA/GSFC Scientific Visualization Studio** -- Moon texture.
- **api.data.gov** -- API access provided by the U.S. General Services Administration.

## License

[MIT License](LICENSE)
