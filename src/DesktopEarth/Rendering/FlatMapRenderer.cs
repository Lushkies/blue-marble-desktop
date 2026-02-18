using Silk.NET.OpenGL;
using System.Numerics;

namespace DesktopEarth.Rendering;

public class FlatMapRenderer : IDisposable
{
    private readonly AppSettings _settings;
    private GL _gl = null!;
    private ShaderProgram? _shader;
    private TextureManager? _textures;
    private uint _vao;
    private uint _vbo;
    private uint _framebuffer;
    private uint _renderTexture;
    private int _fbWidth;
    private int _fbHeight;

    public FlatMapRenderer(AppSettings settings)
    {
        _settings = settings;
    }

    public void Initialize(GL gl, string dayTexPath, string nightTexPath)
    {
        _gl = gl;

        // Fullscreen quad: position (x,y) + texcoord (u,v)
        float[] quadVertices =
        [
            -1f, -1f,  0f, 0f,
             1f, -1f,  1f, 0f,
             1f,  1f,  1f, 1f,
            -1f, -1f,  0f, 0f,
             1f,  1f,  1f, 1f,
            -1f,  1f,  0f, 1f,
        ];

        _vao = _gl.GenVertexArray();
        _gl.BindVertexArray(_vao);

        _vbo = _gl.GenBuffer();
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        unsafe
        {
            fixed (float* ptr = quadVertices)
            {
                _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(quadVertices.Length * sizeof(float)),
                    ptr, BufferUsageARB.StaticDraw);
            }
        }

        uint stride = 4 * sizeof(float);
        _gl.EnableVertexAttribArray(0);
        unsafe { _gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, stride, (void*)0); }
        _gl.EnableVertexAttribArray(1);
        unsafe { _gl.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, stride, (void*)(2 * sizeof(float))); }

        _gl.BindVertexArray(0);

        _textures = new TextureManager(_gl);
        _textures.LoadTexture(dayTexPath, "day");
        _textures.LoadTexture(nightTexPath, "night");

        _shader = new ShaderProgram(_gl, Shaders.FlatMapVertex, Shaders.FlatMapFragment);
    }

    private void EnsureFramebuffer(int width, int height)
    {
        if (_fbWidth == width && _fbHeight == height && _framebuffer != 0)
            return;

        if (_framebuffer != 0) _gl.DeleteFramebuffer(_framebuffer);
        if (_renderTexture != 0) _gl.DeleteTexture(_renderTexture);

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

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    public byte[] Render(int width, int height)
    {
        if (_shader == null || _textures == null)
            throw new InvalidOperationException("FlatMapRenderer not initialized");

        EnsureFramebuffer(width, height);

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _framebuffer);
        _gl.Viewport(0, 0, (uint)width, (uint)height);
        _gl.ClearColor(0.0f, 0.0f, 0.02f, 1.0f);
        _gl.Clear(ClearBufferMask.ColorBufferBit);
        _gl.Disable(EnableCap.DepthTest);

        var sunDir = SunPosition.GetSunDirection(DateTime.UtcNow);

        _shader.Use();
        _shader.SetUniform("uSunDirection", new Vector3(sunDir.X, sunDir.Y, sunDir.Z));
        _shader.SetUniform("uAmbient", _settings.AmbientLight);

        float nightBrightness = _settings.NightLightsEnabled ? _settings.NightLightsBrightness : 0.0f;
        _shader.SetUniform("uNightBrightness", nightBrightness);

        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.BindTexture(TextureTarget.Texture2D, _textures.GetTexture("day"));
        _shader.SetUniform("uDayTexture", 0);

        _gl.ActiveTexture(TextureUnit.Texture1);
        _gl.BindTexture(TextureTarget.Texture2D, _textures.GetTexture("night"));
        _shader.SetUniform("uNightTexture", 1);

        _gl.BindVertexArray(_vao);
        _gl.DrawArrays(PrimitiveType.Triangles, 0, 6);
        _gl.BindVertexArray(0);

        _gl.Enable(EnableCap.DepthTest);

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
        _shader?.Dispose();
        _textures?.Dispose();
        if (_vao != 0) _gl.DeleteVertexArray(_vao);
        if (_vbo != 0) _gl.DeleteBuffer(_vbo);
        if (_framebuffer != 0) _gl.DeleteFramebuffer(_framebuffer);
        if (_renderTexture != 0) _gl.DeleteTexture(_renderTexture);
    }
}
