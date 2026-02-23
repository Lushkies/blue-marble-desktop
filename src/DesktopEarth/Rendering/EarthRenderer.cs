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
    private ShaderProgram? _starsShader;
    private uint _starsVao;
    private uint _starsVbo;
    private uint _framebuffer;
    private uint _renderTexture;
    private uint _depthRenderbuffer;
    private int _fbWidth;
    private int _fbHeight;
    private byte[]? _pixelBuffer;

    public EarthRenderer(AppSettings settings)
    {
        _settings = settings;
    }

    public void Initialize(GL gl, string dayTexPath, string nightTexPath)
    {
        _gl = gl;
        _gl.Enable(EnableCap.DepthTest);
        _gl.Enable(EnableCap.CullFace);
        _gl.CullFace(TriangleFace.Back);

        // Create meshes — 128x256 for smooth edges
        _globe = new GlobeMesh(_gl, 128, 256);
        _atmosphereMesh = new GlobeMesh(_gl, 32, 64);

        // Load textures
        _textures = new TextureManager(_gl);
        _textures.LoadTexture(dayTexPath, "day");
        _textures.LoadTexture(nightTexPath, "night");

        // Create shaders
        _earthShader = new ShaderProgram(_gl, Shaders.EarthVertex, Shaders.EarthFragment);
        _atmosShader = new ShaderProgram(_gl, Shaders.AtmosphereVertex, Shaders.AtmosphereFragment);
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

    public void EnsureFramebuffer(int width, int height)
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

        var now = DateTime.UtcNow;

        // === Sun position ===
        // GetSubsolarPoint returns the lat/lon on Earth where the sun is directly overhead.
        // This already encodes the current time of day (via GMST/right ascension).
        var (sunLat, sunLon) = SunPosition.GetSubsolarPoint(now);
        float sunLatRad = (float)(sunLat * Math.PI / 180.0);
        float sunLonRad = (float)(sunLon * Math.PI / 180.0);

        // === Globe orientation ===
        // The model matrix rotates the globe so the user's chosen longitude faces the camera.
        // The mesh has lon=-90° at +Z (facing camera). System.Numerics row-major data is
        // read as column-major by OpenGL, effectively transposing (= inverting) the rotation.
        // To compensate, we negate: rotate by (-L - 90°) so the transpose gives (L + 90°).
        float lonOffsetRad = (-_settings.LongitudeOffset - 90.0f) * MathF.PI / 180.0f;
        float latitudeRad = _settings.CameraTilt * MathF.PI / 180.0f;

        var model = Matrix4x4.CreateRotationY(lonOffsetRad) *
                    Matrix4x4.CreateRotationX(-latitudeRad);

        // === Sun direction in model space ===
        // The mesh maps geographic point (lat, lon) to:
        //   x = -cos(lat)*cos(lon), y = sin(lat), z = -cos(lat)*sin(lon)
        // (because u=0 is lon=-180° at +X, so lon=0° maps to -X)
        // The sun shines FROM its subsolar point direction, so we need:
        var sunMeshSpace = new Vector3(
            -MathF.Cos(sunLatRad) * MathF.Cos(sunLonRad),
            MathF.Sin(sunLatRad),
            -MathF.Cos(sunLatRad) * MathF.Sin(sunLonRad)
        );
        // Transform by model matrix to account for globe rotation/tilt
        var sunDirection = Vector3.TransformNormal(sunMeshSpace, model);
        sunDirection = Vector3.Normalize(sunDirection);

        // Camera setup
        float fov = _settings.FieldOfView * MathF.PI / 180.0f;
        float aspect = (float)width / height;
        var projection = Matrix4x4.CreatePerspectiveFieldOfView(fov, aspect, 0.1f, 100.0f);

        // Image offset: shift camera laterally to slide the image on screen.
        // OffsetX/Y are -25..+25 (percentage). Convert to world units based on view size.
        float viewHeight = 2.0f * _settings.ZoomLevel * MathF.Tan(fov * 0.5f);
        float offsetX = (_settings.ImageOffsetX / 100.0f) * viewHeight * aspect;
        float offsetY = (_settings.ImageOffsetY / 100.0f) * viewHeight;
        var cameraPos = new Vector3(offsetX, offsetY, _settings.ZoomLevel);
        var cameraTarget = new Vector3(offsetX, offsetY, 0.0f);
        var view = Matrix4x4.CreateLookAt(cameraPos, cameraTarget, Vector3.UnitY);

        // Draw Earth
        _earthShader.Use();
        _earthShader.SetUniform("uModel", model);
        _earthShader.SetUniform("uView", view);
        _earthShader.SetUniform("uProjection", projection);
        _earthShader.SetUniform("uSunDirection", sunDirection);
        _earthShader.SetUniform("uAmbient", _settings.AmbientLight);

        float nightBrightness = _settings.NightLightsEnabled ? _settings.NightLightsBrightness : 0.0f;
        _earthShader.SetUniform("uNightBrightness", nightBrightness);

        int texUnit = 0;

        _gl.ActiveTexture(TextureUnit.Texture0 + texUnit);
        _gl.BindTexture(TextureTarget.Texture2D, _textures.GetTexture("day"));
        _earthShader.SetUniform("uDayTexture", texUnit++);

        _gl.ActiveTexture(TextureUnit.Texture0 + texUnit);
        _gl.BindTexture(TextureTarget.Texture2D, _textures.GetTexture("night"));
        _earthShader.SetUniform("uNightTexture", texUnit++);

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

        // Read pixels (reuse buffer to avoid LOH churn)
        int requiredSize = width * height * 4;
        if (_pixelBuffer == null || _pixelBuffer.Length != requiredSize)
            _pixelBuffer = new byte[requiredSize];

        unsafe
        {
            fixed (byte* ptr = _pixelBuffer)
            {
                _gl.ReadPixels(0, 0, (uint)width, (uint)height,
                    PixelFormat.Rgba, PixelType.UnsignedByte, ptr);
            }
        }

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);

        return _pixelBuffer;
    }

    public void Dispose()
    {
        _globe?.Dispose();
        _atmosphereMesh?.Dispose();
        _textures?.Dispose();
        _earthShader?.Dispose();
        _atmosShader?.Dispose();
        _starsShader?.Dispose();

        if (_starsVao != 0) _gl.DeleteVertexArray(_starsVao);
        if (_starsVbo != 0) _gl.DeleteBuffer(_starsVbo);
        if (_framebuffer != 0) _gl.DeleteFramebuffer(_framebuffer);
        if (_renderTexture != 0) _gl.DeleteTexture(_renderTexture);
        if (_depthRenderbuffer != 0) _gl.DeleteRenderbuffer(_depthRenderbuffer);
    }
}
