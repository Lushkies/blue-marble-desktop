using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Bmp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;

namespace DesktopEarth;

/// <summary>
/// Decoder options for every image the app reads with ImageSharp (grid thumbnails
/// are decoded by GDI+ instead, which these ImageSharp advisories don't affect).
/// Images come from the network (NASA, NPS, Smithsonian) and from the user, so
/// they are untrusted input.
///
/// Formats: only JPEG, PNG, GIF, BMP and WebP can be decoded. TIFF (and the other
/// formats ImageSharp registers by default) is left out: no source the app uses
/// needs it, and ImageSharp 3.1's TIFF decoder has known flaws — a heap overwrite
/// from tiled fax TIFFs (GHSA-v76p-62qx-wwq2) and a BigTIFF loop
/// (GHSA-wmxv-xphr-5c9g). A TIFF now fails to load like any unreadable file.
///
/// SkipMetadata: metadata (EXIF, ICC profiles) is never used for rendering, and
/// skipping it keeps ImageSharp 3.1's ICC-profile parser — which has a known
/// allocation flaw (GHSA-gwg2-r3hj-4w44) — off the path.
///
/// WORKAROUND: the proper fix is ImageSharp 3.2.0+/4.1.2+, whose Release builds
/// need a Six Labors license key (free for open source); see DesktopEarth.csproj.
/// </summary>
public static class ImageDecoding
{
    private static readonly Configuration SafeFormats = new(
        new JpegConfigurationModule(),
        new PngConfigurationModule(),
        new GifConfigurationModule(),
        new BmpConfigurationModule(),
        new WebpConfigurationModule());

    public static DecoderOptions Options => new() { Configuration = SafeFormats, SkipMetadata = true };

    /// <summary>Options that also downscale during decode (JPEG DCT scaling).</summary>
    public static DecoderOptions WithTargetSize(int maxWidth, int maxHeight) => new()
    {
        Configuration = SafeFormats,
        SkipMetadata = true,
        TargetSize = new SixLabors.ImageSharp.Size(maxWidth, maxHeight)
    };
}
