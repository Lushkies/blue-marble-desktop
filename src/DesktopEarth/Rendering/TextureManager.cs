using Silk.NET.OpenGL;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace DesktopEarth.Rendering;

public class TextureManager : IDisposable
{
    private readonly GL _gl;
    private readonly Dictionary<string, uint> _textures = new();

    /// <summary>
    /// Maximum texture dimension (width or height) for memory-efficient loading.
    /// Set to 2x the highest monitor dimension (minimum 4096) so textures are
    /// always a 2x oversample of the display — visually identical but uses far
    /// less memory than decoding 21600x10800 HD textures at full resolution.
    /// </summary>
    private readonly int _maxTextureDimension;

    public TextureManager(GL gl)
    {
        _gl = gl;

        // Cap textures at 2x the highest monitor dimension for quality headroom.
        // Floor of 4096 ensures decent quality even if monitor detection fails.
        var (monW, monH) = MonitorManager.GetHighestMonitorResolution();
        _maxTextureDimension = Math.Max(4096, Math.Max(monW, monH) * 2);
    }

    public uint LoadTexture(string path, string name)
    {
        if (_textures.TryGetValue(name, out uint existing))
            return existing;

        // Use DecoderOptions.TargetSize to leverage JPEG DCT block scaling.
        // The JPEG decoder produces a reduced-resolution image directly from
        // DCT coefficients — it never allocates the full-resolution pixel buffer.
        // For a 21600x10800 HD texture capped to ~5120, this reduces peak memory
        // from ~933 MB to ~100 MB. Images already below the cap pass through unchanged.
        var decoderOptions = new DecoderOptions
        {
            TargetSize = new SixLabors.ImageSharp.Size(_maxTextureDimension, _maxTextureDimension)
        };

        using var image = SixLabors.ImageSharp.Image.Load<Rgba32>(decoderOptions, path);
        image.Mutate(x => x.Flip(FlipMode.Vertical));

        uint texture = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, texture);

        var pixelData = new byte[image.Width * image.Height * 4];
        image.CopyPixelDataTo(pixelData);

        unsafe
        {
            fixed (byte* ptr = pixelData)
            {
                _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba,
                    (uint)image.Width, (uint)image.Height, 0,
                    PixelFormat.Rgba, PixelType.UnsignedByte, ptr);
            }
        }

        // Release the large pixel array immediately after GPU upload.
        // Without this, the LOH allocation lingers until GC runs — on systems
        // with lots of RAM, .NET may not collect it for a long time.
        pixelData = null;

        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        _gl.GenerateMipmap(TextureTarget.Texture2D);

        _textures[name] = texture;

        // Prompt GC to collect the dead LOH allocation from pixelData + ImageSharp buffers.
        // This is critical during PerDisplay rendering where multiple renderers are created
        // in sequence — without this, dead 100+ MB arrays pile up between iterations.
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Default, false);

        return texture;
    }

    public bool HasTexture(string name) => _textures.ContainsKey(name);

    public uint GetTexture(string name) => _textures[name];

    public void Dispose()
    {
        foreach (var tex in _textures.Values)
            _gl.DeleteTexture(tex);
        _textures.Clear();
    }
}
