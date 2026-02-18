using Silk.NET.OpenGL;
using System.Numerics;

namespace DesktopEarth.Rendering;

public class EarthRenderer : IDisposable
{
    private readonly AppSettings _settings;
    private GL _gl = null!;
    private GlobeMesh? _globe;
    private GlobeMesh? _atmosphereMesh;
    private TextureManager? _textures;
    private ShaderProgram? _earthShader;
    private ShaderProgram? _atmosShader;
    private uint _framebuffer;
    private uint _renderTexture;
    private uint _depthRenderbuffer;
    private int _fbWidth;
    private int _fbHeight;
    private bool _hasBathyMask;

    public EarthRenderer(AppSettings settings)
    {
        _settings = settings;
    }

    public void Initialize(GL gl, string dayTexPath, string nightTexPath, string? bathyMaskPath)
    {
        _gl = gl;
        _gl.Enable(EnableCap.DepthTest);
        _gl.Enable(EnableCap.CullFace);
        _gl.CullFace(TriangleFace.Back);

        // Create meshes
        _globe = new GlobeMesh(_gl, 64, 128);
        _atmosphereMesh = new GlobeMesh(_gl, 32, 64);

        // Load textures
        _textures = new TextureManager(_gl);
        _textures.LoadTexture(dayTexPath, "day");
        _textures.LoadTexture(nightTexPath, "night");

        if (bathyMaskPath != null)
        {
            _textures.LoadTexture(bathyMaskPath, "bathyMask");
            _hasBathyMask = true;
        }

        // Create shaders
        _earthShader = new ShaderProgram(_gl, Shaders.EarthVertex, Shaders.EarthFragment);
        _atmosShader = new ShaderProgram(_gl, Shaders.AtmosphereVertex, Shaders.AtmosphereFragment);
    }

    public void EnsureFramebuffer(int width, int height)
    {
        if (_fbWidth == width && _fbHeight == height && _framebuffer != 0)
            return;

        // Clean up old framebuffer
        if (_framebuffer != 0) _gl.DeleteFramebuffer(_framebuffer);
        if (_renderTexture != 0) _gl.DeleteTexture(_renderTexture);
        if (_depthRenderbuffer != 0) _gl.DeleteRenderbuffer(_depthRenderbuffer);

        _fbWidth = width;
        _fbHeight = height;

        _framebuffer = _gl.GenFramebuffer();
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _framebuffer);

        _renderTexture = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, _renderTexture);
        unsafe
        {
            _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba8,
                (uint)width, (uint)height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, null);
        }
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        _gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
            TextureTarget.Texture2D, _renderTexture, 0);

        _depthRenderbuffer = _gl.GenRenderbuffer();
        _gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _depthRenderbuffer);
        _gl.RenderbufferStorage(RenderbufferTarget.Renderbuffer, InternalFormat.DepthComponent24,
            (uint)width, (uint)height);
        _gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment,
            RenderbufferTarget.Renderbuffer, _depthRenderbuffer);

        var fbStatus = _gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
        if (fbStatus != GLEnum.FramebufferComplete)
            throw new Exception($"Framebuffer not complete: {fbStatus}");

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    public byte[] Render(int width, int height)
    {
        if (_globe == null || _earthShader == null || _textures == null)
            throw new InvalidOperationException("Renderer not initialized");

        EnsureFramebuffer(width, height);

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _framebuffer);
        _gl.Viewport(0, 0, (uint)width, (uint)height);
        _gl.ClearColor(0.0f, 0.0f, 0.02f, 1.0f);
        _gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        var now = DateTime.UtcNow;

        // Camera from settings
        float fov = _settings.FieldOfView * MathF.PI / 180.0f;
        float aspect = (float)width / height;
        var projection = Matrix4x4.CreatePerspectiveFieldOfView(fov, aspect, 0.1f, 100.0f);
        var cameraPos = new Vector3(0.0f, 0.0f, _settings.ZoomLevel);
        var view = Matrix4x4.CreateLookAt(cameraPos, Vector3.Zero, Vector3.UnitY);

        // Sun direction
        var sunDir3 = SunPosition.GetSunDirection(now);
        var sunDirection = new Vector3(sunDir3.X, sunDir3.Y, sunDir3.Z);

        // Earth rotation
        float hoursUtc = (float)now.TimeOfDay.TotalHours;
        float earthRotation = -(hoursUtc / 24.0f) * 2.0f * MathF.PI;
        float tiltAngle = _settings.CameraTilt * MathF.PI / 180.0f;

        var model = Matrix4x4.CreateRotationY(earthRotation) *
                    Matrix4x4.CreateRotationX(-tiltAngle);

        // Draw Earth
        _earthShader.Use();
        _earthShader.SetUniform("uModel", model);
        _earthShader.SetUniform("uView", view);
        _earthShader.SetUniform("uProjection", projection);
        _earthShader.SetUniform("uSunDirection", sunDirection);
        _earthShader.SetUniform("uCameraPos", cameraPos);
        _earthShader.SetUniform("uAmbient", _settings.AmbientLight);

        float nightBrightness = _settings.NightLightsEnabled ? _settings.NightLightsBrightness : 0.0f;
        _earthShader.SetUniform("uNightBrightness", nightBrightness);
        _earthShader.SetUniform("uSpecularIntensity", _settings.SunSpecularIntensity);
        _earthShader.SetUniform("uSpecularPower", _settings.SunSpecularPower);
        _earthShader.SetUniform("uHasBathyMask", _hasBathyMask ? 1 : 0);

        int texUnit = 0;

        _gl.ActiveTexture(TextureUnit.Texture0 + texUnit);
        _gl.BindTexture(TextureTarget.Texture2D, _textures.GetTexture("day"));
        _earthShader.SetUniform("uDayTexture", texUnit++);

        _gl.ActiveTexture(TextureUnit.Texture0 + texUnit);
        _gl.BindTexture(TextureTarget.Texture2D, _textures.GetTexture("night"));
        _earthShader.SetUniform("uNightTexture", texUnit++);

        if (_hasBathyMask)
        {
            _gl.ActiveTexture(TextureUnit.Texture0 + texUnit);
            _gl.BindTexture(TextureTarget.Texture2D, _textures.GetTexture("bathyMask"));
            _earthShader.SetUniform("uBathyMask", texUnit++);
        }

        _globe.Draw();

        // Draw Atmosphere glow
        if (_atmosShader != null && _atmosphereMesh != null)
        {
            _gl.Enable(EnableCap.Blend);
            _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            _gl.CullFace(TriangleFace.Front);

            var atmosModel = model * Matrix4x4.CreateScale(1.025f);
            _atmosShader.Use();
            _atmosShader.SetUniform("uModel", atmosModel);
            _atmosShader.SetUniform("uView", view);
            _atmosShader.SetUniform("uProjection", projection);
            _atmosShader.SetUniform("uSunDirection", sunDirection);
            _atmosShader.SetUniform("uCameraPos", cameraPos);

            _atmosphereMesh.Draw();

            _gl.CullFace(TriangleFace.Back);
            _gl.Disable(EnableCap.Blend);
        }

        // Read pixels
        var pixels = new byte[width * height * 4];
        unsafe
        {
            fixed (byte* ptr = pixels)
            {
                _gl.ReadPixels(0, 0, (uint)width, (uint)height,
                    PixelFormat.Rgba, PixelType.UnsignedByte, ptr);
            }
        }

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);

        return pixels;
    }

    public void Dispose()
    {
        _globe?.Dispose();
        _atmosphereMesh?.Dispose();
        _textures?.Dispose();
        _earthShader?.Dispose();
        _atmosShader?.Dispose();

        if (_framebuffer != 0) _gl.DeleteFramebuffer(_framebuffer);
        if (_renderTexture != 0) _gl.DeleteTexture(_renderTexture);
        if (_depthRenderbuffer != 0) _gl.DeleteRenderbuffer(_depthRenderbuffer);
    }
}
