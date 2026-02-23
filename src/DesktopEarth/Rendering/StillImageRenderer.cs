using Silk.NET.OpenGL;

namespace DesktopEarth.Rendering;

/// <summary>
/// Renders a still image (like EPIC satellite photos) as a fullscreen textured quad.
/// Maintains the image's aspect ratio with black letterbox/pillarbox bars.
/// Follows the same pattern as FlatMapRenderer.
/// </summary>
public class StillImageRenderer : IDisposable
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
    private byte[]? _pixelBuffer;
    private string? _currentImagePath;
    private int _imageWidth;
    private int _imageHeight;

    public StillImageRenderer(AppSettings settings)
    {
        _settings = settings;
    }

    /// <summary>True if an image has been loaded and is ready to render.</summary>
    public bool HasImage => _currentImagePath != null && _textures != null;

    /// <summary>
    /// True if the loaded image is below the minimum 1080p threshold.
    /// Callers should skip rendering and try a different image when this is true.
    /// </summary>
    public bool IsBelowMinimumQuality => _imageWidth > 0 && _imageHeight > 0 &&
        Math.Max(_imageWidth, _imageHeight) < 1080;

    public void Initialize(GL gl)
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

        _shader = new ShaderProgram(_gl, Shaders.StillImageVertex, Shaders.StillImageFragment);
    }

    /// <summary>
    /// Load (or reload) an image from disk as the texture to display.
    /// Skips reload if the same path is already loaded.
    /// </summary>
    public void LoadImage(GL gl, string imagePath)
    {
        if (imagePath == _currentImagePath && _textures != null)
            return; // Already loaded

        // Dispose old textures if reloading a different image
        _textures?.Dispose();
        _textures = new TextureManager(gl);

        // Get image dimensions before loading as texture
        try
        {
            var imageInfo = SixLabors.ImageSharp.Image.Identify(imagePath);
            if (imageInfo != null)
            {
                _imageWidth = imageInfo.Width;
                _imageHeight = imageInfo.Height;
            }
            else
            {
                _imageWidth = 1024;
                _imageHeight = 1024;
            }
        }
        catch
        {
            _imageWidth = 1024;
            _imageHeight = 1024;
        }

        // Enforce minimum 1080p quality -- warn but still load (caller decides whether to skip)
        int maxDim = Math.Max(_imageWidth, _imageHeight);
        if (maxDim > 0 && maxDim < 1080)
        {
            Console.WriteLine($"StillImageRenderer: WARNING - {Path.GetFileName(imagePath)} " +
                $"is below minimum quality ({_imageWidth}x{_imageHeight}, max dimension {maxDim}px < 1080px)");
        }

        _textures.LoadTexture(imagePath, "image");
        _currentImagePath = imagePath;

        Console.WriteLine($"StillImageRenderer: Loaded {Path.GetFileName(imagePath)} ({_imageWidth}x{_imageHeight})");
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
            throw new InvalidOperationException("StillImageRenderer not initialized or no image loaded");

        EnsureFramebuffer(width, height);

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _framebuffer);
        _gl.Viewport(0, 0, (uint)width, (uint)height);
        _gl.ClearColor(0.0f, 0.0f, 0.0f, 1.0f);
        _gl.Clear(ClearBufferMask.ColorBufferBit);
        _gl.Disable(EnableCap.DepthTest);

        _shader.Use();

        // Pass aspect ratios so the shader can letterbox/pillarbox correctly
        float imageAspect = _imageWidth / (float)_imageHeight;
        float screenAspect = width / (float)height;
        _shader.SetUniform("uImageAspect", imageAspect);
        _shader.SetUniform("uScreenAspect", screenAspect);

        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.BindTexture(TextureTarget.Texture2D, _textures.GetTexture("image"));
        _shader.SetUniform("uImage", 0);

        _gl.BindVertexArray(_vao);
        _gl.DrawArrays(PrimitiveType.Triangles, 0, 6);
        _gl.BindVertexArray(0);

        _gl.Enable(EnableCap.DepthTest);

        // Reuse buffer to avoid LOH churn
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
        _shader?.Dispose();
        _textures?.Dispose();
        if (_vao != 0) _gl.DeleteVertexArray(_vao);
        if (_vbo != 0) _gl.DeleteBuffer(_vbo);
        if (_framebuffer != 0) _gl.DeleteFramebuffer(_framebuffer);
        if (_renderTexture != 0) _gl.DeleteTexture(_renderTexture);
    }
}
