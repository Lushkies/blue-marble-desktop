namespace DesktopEarth;

/// <summary>
/// Unified image metadata from any source. Used by thumbnail grid, cache, and favorites.
/// </summary>
public class ImageSourceInfo
{
    public ImageSource Source { get; set; }
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Date { get; set; } = "";
    public string ThumbnailUrl { get; set; } = "";
    public string FullImageUrl { get; set; } = "";
    public string HdImageUrl { get; set; } = "";

    // Source attribution (photographer, credit)
    public string PhotographerName { get; set; } = "";
    public string PhotographerUrl { get; set; } = "";
    public string SourceAttribution { get; set; } = "";

    // Source-specific tracking URL
    public string DownloadLocationUrl { get; set; } = "";

    // Image dimensions (0 = unknown)
    public int ImageWidth { get; set; }
    public int ImageHeight { get; set; }
    public ImageQualityTier QualityTier { get; set; } = ImageQualityTier.Unknown;

    public bool IsFavorited { get; set; }

    public override string ToString() => !string.IsNullOrEmpty(Title) ? Title : Id;

    /// <summary>
    /// Calculate quality tier from image dimensions.
    /// </summary>
    public static ImageQualityTier GetQualityTier(int width, int height)
    {
        int maxDim = Math.Max(width, height);
        if (maxDim >= 3840) return ImageQualityTier.UD;
        if (maxDim >= 2160) return ImageQualityTier.HD;
        if (maxDim >= 1080) return ImageQualityTier.SD;
        return ImageQualityTier.Unknown; // Below minimum
    }
}
