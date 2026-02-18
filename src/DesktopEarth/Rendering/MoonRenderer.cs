using Silk.NET.OpenGL;
using System.Numerics;

namespace DesktopEarth.Rendering;

public class MoonRenderer : IDisposable
{
    private readonly AppSettings _settings;
    private GL _gl = null!;
    private GlobeMesh? _globe;
    private TextureManager? _textures;
    private ShaderProgram? _shader;
    private uint _framebuffer;
    private uint _renderTexture;
    private uint _depthRenderbuffer;
    private int _fbWidth;
    private int _fbHeight;

    public MoonRenderer(AppSettings settings)
    {
        _settings = settings;
    }

    public void Initialize(GL gl, string moonTexPath)
    {
        _gl = gl;
        _gl.Enable(EnableCap.DepthTest);
        _gl.Enable(EnableCap.CullFace);
        _gl.CullFace(TriangleFace.Back);

        _globe = new GlobeMesh(_gl, 64, 128);

        _textures = new TextureManager(_gl);
        _textures.LoadTexture(moonTexPath, "moon");

        _shader = new ShaderProgram(_gl, Shaders.MoonVertex, Shaders.MoonFragment);
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
        _gl.ClearColor(0.0f, 0.0f, 0.02f, 1.0f);
        _gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        float fov = _settings.FieldOfView * MathF.PI / 180.0f;
        float aspect = (float)width / height;
        var projection = Matrix4x4.CreatePerspectiveFieldOfView(fov, aspect, 0.1f, 100.0f);
        var cameraPos = new Vector3(0.0f, 0.0f, _settings.ZoomLevel);
        var view = Matrix4x4.CreateLookAt(cameraPos, Vector3.Zero, Vector3.UnitY);

        var sunDir = SunPosition.GetSunDirection(DateTime.UtcNow);
        var sunDirection = new Vector3(sunDir.X, sunDir.Y, sunDir.Z);

        // Moon rotates much slower than earth (synchronous rotation)
        float tiltAngle = _settings.CameraTilt * MathF.PI / 180.0f;
        var model = Matrix4x4.CreateRotationX(-tiltAngle);

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
        _textures?.Dispose();
        _shader?.Dispose();
        if (_framebuffer != 0) _gl.DeleteFramebuffer(_framebuffer);
        if (_renderTexture != 0) _gl.DeleteTexture(_renderTexture);
        if (_depthRenderbuffer != 0) _gl.DeleteRenderbuffer(_depthRenderbuffer);
    }
}
