using Silk.NET.OpenGL;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace DesktopEarth;

public class TextureManager : IDisposable
{
    private readonly GL _gl;
    private readonly Dictionary<string, uint> _textures = new();

    public TextureManager(GL gl)
    {
        _gl = gl;
    }

    public uint LoadTexture(string path, string name)
    {
        if (_textures.TryGetValue(name, out uint existing))
            return existing;

        using var image = Image.Load<Rgba32>(path);
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

        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        _gl.GenerateMipmap(TextureTarget.Texture2D);

        _textures[name] = texture;
        return texture;
    }

    public uint GetTexture(string name) => _textures[name];

    public void Dispose()
    {
        foreach (var tex in _textures.Values)
            _gl.DeleteTexture(tex);
        _textures.Clear();
    }
}
