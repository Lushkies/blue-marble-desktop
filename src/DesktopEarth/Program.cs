using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.Numerics;
using DesktopEarth;

// ─── Configuration ───
const int RenderWidth = 1920;
const int RenderHeight = 1080;
const int UpdateIntervalMinutes = 10;

// ─── Locate assets ───
string exeDir = AppContext.BaseDirectory;
string assetsDir = Path.Combine(exeDir, "assets", "textures");

// If running from build output, try to find assets relative to project
if (!Directory.Exists(assetsDir))
{
    string? dir = exeDir;
    while (dir != null)
    {
        string candidate = Path.Combine(dir, "assets", "textures");
        if (Directory.Exists(candidate))
        {
            assetsDir = candidate;
            break;
        }
        dir = Directory.GetParent(dir)?.FullName;
    }
}

if (!Directory.Exists(assetsDir))
{
    Console.WriteLine($"ERROR: Could not find assets/textures directory.");
    Console.WriteLine($"Searched from: {exeDir}");
    Console.WriteLine("Please ensure the assets folder is in the project root.");
    return;
}

Console.WriteLine($"Using textures from: {assetsDir}");

// ─── Pick the right month's texture ───
string GetDayTexturePath()
{
    int month = DateTime.UtcNow.Month;
    string monthStr = month.ToString("D2");
    string pattern = $"world.topo.2004{monthStr}*";
    var matches = Directory.GetFiles(assetsDir, pattern);
    if (matches.Length > 0) return matches[0];

    pattern = $"world.topo.bathy.2004{monthStr}*";
    matches = Directory.GetFiles(assetsDir, pattern);
    if (matches.Length > 0) return matches[0];

    matches = Directory.GetFiles(assetsDir, "world.topo.*");
    return matches.Length > 0 ? matches[0] : throw new FileNotFoundException("No earth day texture found");
}

string GetNightTexturePath()
{
    string[] candidates = ["land_ocean_ice_lights_8192.jpg", "land_lights_8192.jpg", "nightearth_8192.jpg"];
    foreach (var name in candidates)
    {
        string path = Path.Combine(assetsDir, name);
        if (File.Exists(path)) return path;
    }
    throw new FileNotFoundException("No night lights texture found");
}

string dayTexPath = GetDayTexturePath();
string nightTexPath = GetNightTexturePath();
Console.WriteLine($"Day texture: {Path.GetFileName(dayTexPath)}");
Console.WriteLine($"Night texture: {Path.GetFileName(nightTexPath)}");

// ─── State ───
GL? gl = null;
GlobeMesh? globe = null;
GlobeMesh? atmosphereMesh = null;
TextureManager? textures = null;
ShaderProgram? earthShader = null;
ShaderProgram? atmosShader = null;
uint framebuffer = 0;
uint renderTexture = 0;
uint depthRenderbuffer = 0;
string wallpaperPath = Path.Combine(Path.GetTempPath(), "DesktopEarth_wallpaper.bmp");
DateTime lastWallpaperUpdate = DateTime.MinValue;
bool firstRender = true;

// ─── Create hidden window for offscreen rendering ───
var options = WindowOptions.Default;
options.Size = new Vector2D<int>(RenderWidth, RenderHeight);
options.Title = "Desktop Earth";
options.IsVisible = false;
options.VSync = false;
options.API = new GraphicsAPI(ContextAPI.OpenGL, ContextProfile.Core, ContextFlags.Default, new APIVersion(3, 3));

var window = Window.Create(options);
window.Load += OnLoad;
window.Render += OnRender;
window.Closing += OnClose;

Console.WriteLine("Starting Desktop Earth renderer...");
Console.WriteLine($"Wallpaper will update every {UpdateIntervalMinutes} minutes.");
Console.WriteLine($"Wallpaper saved to: {wallpaperPath}");
Console.WriteLine("Press Ctrl+C to stop.");

window.Run();

// ─── Callbacks ───

void OnLoad()
{
    gl = GL.GetApi(window);
    gl.Enable(EnableCap.DepthTest);
    gl.Enable(EnableCap.CullFace);
    gl.CullFace(TriangleFace.Back);

    // Create offscreen framebuffer
    framebuffer = gl.GenFramebuffer();
    gl.BindFramebuffer(FramebufferTarget.Framebuffer, framebuffer);

    renderTexture = gl.GenTexture();
    gl.BindTexture(TextureTarget.Texture2D, renderTexture);
    unsafe
    {
        gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba8,
            RenderWidth, RenderHeight, 0, PixelFormat.Rgba, PixelType.UnsignedByte, null);
    }
    gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
    gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
    gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
        TextureTarget.Texture2D, renderTexture, 0);

    depthRenderbuffer = gl.GenRenderbuffer();
    gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, depthRenderbuffer);
    gl.RenderbufferStorage(RenderbufferTarget.Renderbuffer, InternalFormat.DepthComponent24,
        (uint)RenderWidth, (uint)RenderHeight);
    gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment,
        RenderbufferTarget.Renderbuffer, depthRenderbuffer);

    var fbStatus = gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
    if (fbStatus != GLEnum.FramebufferComplete)
    {
        Console.WriteLine($"ERROR: Framebuffer not complete: {fbStatus}");
        return;
    }

    gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);

    // Create meshes
    globe = new GlobeMesh(gl, 64, 128);
    atmosphereMesh = new GlobeMesh(gl, 32, 64);

    // Load textures
    textures = new TextureManager(gl);
    Console.WriteLine("Loading day texture...");
    textures.LoadTexture(dayTexPath, "day");
    Console.WriteLine("Loading night texture...");
    textures.LoadTexture(nightTexPath, "night");
    Console.WriteLine("Textures loaded.");

    // Create shaders
    earthShader = new ShaderProgram(gl, Shaders.EarthVertex, Shaders.EarthFragment);
    atmosShader = new ShaderProgram(gl, Shaders.AtmosphereVertex, Shaders.AtmosphereFragment);
}

void OnRender(double deltaTime)
{
    if (gl == null || globe == null || earthShader == null || textures == null) return;

    var now = DateTime.UtcNow;
    if (!firstRender && (now - lastWallpaperUpdate).TotalMinutes < UpdateIntervalMinutes)
    {
        Thread.Sleep(1000);
        return;
    }

    firstRender = false;
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Rendering earth...");

    // Render to framebuffer
    gl.BindFramebuffer(FramebufferTarget.Framebuffer, framebuffer);
    gl.Viewport(0, 0, RenderWidth, RenderHeight);
    gl.ClearColor(0.0f, 0.0f, 0.02f, 1.0f);
    gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

    // Camera
    float fov = 45.0f * MathF.PI / 180.0f;
    float aspect = (float)RenderWidth / RenderHeight;
    var projection = Matrix4x4.CreatePerspectiveFieldOfView(fov, aspect, 0.1f, 100.0f);
    var cameraPos = new Vector3(0.0f, 0.0f, 2.8f);
    var view = Matrix4x4.CreateLookAt(cameraPos, Vector3.Zero, Vector3.UnitY);

    // Sun direction
    var sunDir3 = SunPosition.GetSunDirection(now);
    var sunDirection = new Vector3(sunDir3.X, sunDir3.Y, sunDir3.Z);

    // Earth rotation: one full rotation per 24h
    float hoursUtc = (float)now.TimeOfDay.TotalHours;
    float earthRotation = -(hoursUtc / 24.0f) * 2.0f * MathF.PI;

    // Slight tilt for aesthetics
    float tiltAngle = 20.0f * MathF.PI / 180.0f;

    var model = Matrix4x4.CreateRotationY(earthRotation) *
                Matrix4x4.CreateRotationX(-tiltAngle);

    // Draw Earth
    earthShader.Use();
    earthShader.SetUniform("uModel", model);
    earthShader.SetUniform("uView", view);
    earthShader.SetUniform("uProjection", projection);
    earthShader.SetUniform("uSunDirection", sunDirection);
    earthShader.SetUniform("uAmbient", 0.15f);

    gl.ActiveTexture(TextureUnit.Texture0);
    gl.BindTexture(TextureTarget.Texture2D, textures.GetTexture("day"));
    earthShader.SetUniform("uDayTexture", 0);

    gl.ActiveTexture(TextureUnit.Texture1);
    gl.BindTexture(TextureTarget.Texture2D, textures.GetTexture("night"));
    earthShader.SetUniform("uNightTexture", 1);

    globe.Draw();

    // Draw Atmosphere glow
    if (atmosShader != null && atmosphereMesh != null)
    {
        gl.Enable(EnableCap.Blend);
        gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        gl.CullFace(TriangleFace.Front);

        var atmosModel = model * Matrix4x4.CreateScale(1.025f);
        atmosShader.Use();
        atmosShader.SetUniform("uModel", atmosModel);
        atmosShader.SetUniform("uView", view);
        atmosShader.SetUniform("uProjection", projection);
        atmosShader.SetUniform("uSunDirection", sunDirection);
        atmosShader.SetUniform("uCameraPos", cameraPos);

        atmosphereMesh.Draw();

        gl.CullFace(TriangleFace.Back);
        gl.Disable(EnableCap.Blend);
    }

    // Read pixels
    var pixels = new byte[RenderWidth * RenderHeight * 4];
    unsafe
    {
        fixed (byte* ptr = pixels)
        {
            gl.ReadPixels(0, 0, (uint)RenderWidth, (uint)RenderHeight,
                PixelFormat.Rgba, PixelType.UnsignedByte, ptr);
        }
    }

    gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);

    // Save as BMP and set wallpaper
    SaveAsBmp(pixels, RenderWidth, RenderHeight, wallpaperPath);
    WallpaperSetter.SetWallpaper(wallpaperPath);
    lastWallpaperUpdate = now;
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Wallpaper updated. Next update in {UpdateIntervalMinutes} minutes.");
}

void OnClose()
{
    globe?.Dispose();
    atmosphereMesh?.Dispose();
    textures?.Dispose();
    earthShader?.Dispose();
    atmosShader?.Dispose();

    if (gl != null)
    {
        if (framebuffer != 0) gl.DeleteFramebuffer(framebuffer);
        if (renderTexture != 0) gl.DeleteTexture(renderTexture);
        if (depthRenderbuffer != 0) gl.DeleteRenderbuffer(depthRenderbuffer);
    }

    Console.WriteLine("Desktop Earth stopped.");
}

void SaveAsBmp(byte[] rgbaPixels, int width, int height, string path)
{
    using var image = new Image<Rgba32>(width, height);
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
