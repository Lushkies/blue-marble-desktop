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
/// formats ImageSharp registers by default) is left out on purpose: no source the
/// app uses needs it, and the TIFF decoder is where most of ImageSharp's recent
/// security advisories were (GHSA-v76p-62qx-wwq2, GHSA-wmxv-xphr-5c9g — fixed in
/// 3.2.0, which the app now uses). Less untrusted-input surface, nothing lost.
/// A TIFF fails to load like any unreadable file.
///
/// SkipMetadata: metadata (EXIF, ICC profiles) is never used for rendering, so
/// skipping it keeps the metadata parsers off the untrusted-input path too.
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
