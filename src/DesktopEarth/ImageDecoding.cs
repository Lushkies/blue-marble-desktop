using SixLabors.ImageSharp.Formats;

namespace DesktopEarth;

/// <summary>
/// Decoder options for every image the app reads. Images come from the network
/// (NASA, NPS, Smithsonian) and from the user, so they are untrusted input.
///
/// SkipMetadata: metadata (EXIF, ICC profiles) is never used for rendering, and
/// skipping it keeps ImageSharp 3.1's ICC-profile parser — which has a known
/// allocation flaw (GHSA-gwg2-r3hj-4w44, fixed only in 4.1.2) — off the path.
/// WORKAROUND: the proper fix is ImageSharp 4.1.3, whose Release builds need a
/// Six Labors license key (free for open source); see DesktopEarth.csproj.
/// </summary>
public static class ImageDecoding
{
    public static DecoderOptions Options => new() { SkipMetadata = true };

    /// <summary>Options that also downscale during decode (JPEG DCT scaling).</summary>
    public static DecoderOptions WithTargetSize(int maxWidth, int maxHeight) => new()
    {
        SkipMetadata = true,
        TargetSize = new SixLabors.ImageSharp.Size(maxWidth, maxHeight)
    };
}
