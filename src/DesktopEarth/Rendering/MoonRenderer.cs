using Silk.NET.OpenGL;
using System.Numerics;

namespace DesktopEarth.Rendering;

public class MoonRenderer : IDisposable
{
    private readonly AppSettings _settings;
    private GL _gl = null!;
    private GlobeMesh? _globe;
    private GlobeMesh? _miniEarthGlobe;
    private TextureManager? _textures;
    private ShaderProgram? _shader;
    private ShaderProgram? _earthShader;
    private ShaderProgram? _starsShader;
    private uint _starsVao;
    private uint _starsVbo;
    private uint _framebuffer;
    private uint _renderTexture;
    private uint _depthRenderbuffer;
    private int _fbWidth;
    private int _fbHeight;
    private bool _hasEarthTextures;

    public MoonRenderer(AppSettings settings)
    {
        _settings = settings;
    }

    public void Initialize(GL gl, string moonTexPath, string? earthDayTexPath = null, string? earthNightTexPath = null)
    {
        _gl = gl;
        _gl.Enable(EnableCap.DepthTest);
        _gl.Enable(EnableCap.CullFace);
        _gl.CullFace(TriangleFace.Back);

        // Moon globe: 128x256 for smooth edges
        _globe = new GlobeMesh(_gl, 128, 256);

        _textures = new TextureManager(_gl);
        _textures.LoadTexture(moonTexPath, "moon");

        // Load earth textures for mini earth in background
        if (earthDayTexPath != null && earthNightTexPath != null)
        {
            try
            {
                _textures.LoadTexture(earthDayTexPath, "earthDay");
                _textures.LoadTexture(earthNightTexPath, "earthNight");
                _miniEarthGlobe = new GlobeMesh(_gl, 64, 128);
                _earthShader = new ShaderProgram(_gl, Shaders.EarthVertex, Shaders.EarthFragment);
                _hasEarthTextures = true;
            }
            catch
            {
                // Graceful fallback — just skip mini earth
                _hasEarthTextures = false;
            }
        }

        _shader = new ShaderProgram(_gl, Shaders.MoonVertex, Shaders.MoonFragment);
        _starsShader = new ShaderProgram(_gl, Shaders.StarsVertex, Shaders.StarsFragment);

        // Create fullscreen quad for stars
        float[] quadVerts =
        [
            -1f, -1f,
             1f, -1f,
             1f,  1f,
            -1f, -1f,
             1f,  1f,
            -1f,  1f,
        ];

        _starsVao = _gl.GenVertexArray();
        _gl.BindVertexArray(_starsVao);

        _starsVbo = _gl.GenBuffer();
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _starsVbo);
        unsafe
        {
            fixed (float* ptr = quadVerts)
            {
                _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(quadVerts.Length * sizeof(float)),
                    ptr, BufferUsageARB.StaticDraw);
            }
        }

        _gl.EnableVertexAttribArray(0);
        unsafe { _gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 2 * sizeof(float), (void*)0); }

        _gl.BindVertexArray(0);
    }

    private void EnsureFramebuffer(int width, int height)
    {
        if (_fbWidth == width && _fbHeight == height && _framebuffer != 0)
            return;

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

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    public byte[] Render(int width, int height)
    {
        if (_globe == null || _shader == null || _textures == null)
            throw new InvalidOperationException("MoonRenderer not initialized");

        EnsureFramebuffer(width, height);

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _framebuffer);
        _gl.Viewport(0, 0, (uint)width, (uint)height);
        _gl.ClearColor(0.0f, 0.0f, 0.0f, 1.0f);
        _gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        // --- Draw stars background ---
        if (_starsShader != null)
        {
            _gl.Disable(EnableCap.DepthTest);
            _starsShader.Use();
            _starsShader.SetUniform("uResolution", new Vector3(width, height, 0.0f));
            _gl.BindVertexArray(_starsVao);
            _gl.DrawArrays(PrimitiveType.Triangles, 0, 6);
            _gl.BindVertexArray(0);
            _gl.Enable(EnableCap.DepthTest);
        }

        float fov = _settings.FieldOfView * MathF.PI / 180.0f;
        float aspect = (float)width / height;
        var projection = Matrix4x4.CreatePerspectiveFieldOfView(fov, aspect, 0.1f, 100.0f);

        // Image offset (same approach as EarthRenderer)
        float viewHeight = 2.0f * _settings.ZoomLevel * MathF.Tan(fov * 0.5f);
        float offsetX = (_settings.ImageOffsetX / 100.0f) * viewHeight * aspect;
        float offsetY = (_settings.ImageOffsetY / 100.0f) * viewHeight;
        var cameraPos = new Vector3(offsetX, offsetY, _settings.ZoomLevel);
        var cameraTarget = new Vector3(offsetX, offsetY, 0.0f);
        var view = Matrix4x4.CreateLookAt(cameraPos, cameraTarget, Vector3.UnitY);

        var now = DateTime.UtcNow;
        var sunDir = SunPosition.GetSunDirection(now);
        var sunDirection = new Vector3(sunDir.X, sunDir.Y, sunDir.Z);

        // --- Draw mini Earth in background (upper-right of the moon) ---
        if (_hasEarthTextures && _earthShader != null && _miniEarthGlobe != null)
        {
            float earthScale = 0.55f; // Large enough to be clearly visible
            // Position further right and back so the full globe is visible beside the moon
            var earthTranslation = new Vector3(2.2f, 0.5f, -3.0f);

            // Show the night side of Earth by default (rotate 180° from sun-facing side).
            // Uses the same negated longitude convention as EarthRenderer for consistency.
            float earthLonOffset = (180.0f) * MathF.PI / 180.0f;

            var earthRotationMatrix = Matrix4x4.CreateRotationY(earthLonOffset);
            var earthModel = Matrix4x4.CreateScale(earthScale) *
                             earthRotationMatrix *
                             Matrix4x4.CreateTranslation(earthTranslation);

            // Sun direction in mesh coordinates (same approach as EarthRenderer)
            var (sunLat, sunLon) = SunPosition.GetSubsolarPoint(now);
            float sunLatRad = (float)(sunLat * Math.PI / 180.0);
            float sunLonRad = (float)(sunLon * Math.PI / 180.0);
            // Mesh convention: lon 0° maps to -X, so negate x and z
            var sunMeshSpace = new Vector3(
                -MathF.Cos(sunLatRad) * MathF.Cos(sunLonRad),
                MathF.Sin(sunLatRad),
                -MathF.Cos(sunLatRad) * MathF.Sin(sunLonRad)
            );
            var earthSunDir = Vector3.TransformNormal(sunMeshSpace, earthRotationMatrix);
            earthSunDir = Vector3.Normalize(earthSunDir);

            _earthShader.Use();
            _earthShader.SetUniform("uModel", earthModel);
            _earthShader.SetUniform("uView", view);
            _earthShader.SetUniform("uProjection", projection);
            _earthShader.SetUniform("uSunDirection", earthSunDir);
            _earthShader.SetUniform("uCameraPos", cameraPos);
            _earthShader.SetUniform("uAmbient", 0.08f);
            _earthShader.SetUniform("uNightBrightness", 1.5f);

            _gl.ActiveTexture(TextureUnit.Texture0);
            _gl.BindTexture(TextureTarget.Texture2D, _textures.GetTexture("earthDay"));
            _earthShader.SetUniform("uDayTexture", 0);

            _gl.ActiveTexture(TextureUnit.Texture1);
            _gl.BindTexture(TextureTarget.Texture2D, _textures.GetTexture("earthNight"));
            _earthShader.SetUniform("uNightTexture", 1);

            _miniEarthGlobe.Draw();
        }

        // --- Draw Moon ---
        // Apply both latitude (tilt) and longitude rotation so the slider works
        float latitudeRad = _settings.CameraTilt * MathF.PI / 180.0f;
        float lonOffsetRad = (-_settings.LongitudeOffset - 90.0f) * MathF.PI / 180.0f;
        var model = Matrix4x4.CreateRotationX(-latitudeRad) *
                    Matrix4x4.CreateRotationY(lonOffsetRad);

        _shader.Use();
        _shader.SetUniform("uModel", model);
        _shader.SetUniform("uView", view);
        _shader.SetUniform("uProjection", projection);
        _shader.SetUniform("uSunDirection", sunDirection);
        _shader.SetUniform("uAmbient", _settings.AmbientLight);

        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.BindTexture(TextureTarget.Texture2D, _textures.GetTexture("moon"));
        _shader.SetUniform("uMoonTexture", 0);

        _globe.Draw();

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
        _miniEarthGlobe?.Dispose();
        _textures?.Dispose();
        _shader?.Dispose();
        _earthShader?.Dispose();
        _starsShader?.Dispose();
        if (_starsVao != 0) _gl.DeleteVertexArray(_starsVao);
        if (_starsVbo != 0) _gl.DeleteBuffer(_starsVbo);
        if (_framebuffer != 0) _gl.DeleteFramebuffer(_framebuffer);
        if (_renderTexture != 0) _gl.DeleteTexture(_renderTexture);
        if (_depthRenderbuffer != 0) _gl.DeleteRenderbuffer(_depthRenderbuffer);
    }
}
