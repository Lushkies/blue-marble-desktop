namespace DesktopEarth;

/// <summary>
/// Unified image metadata from any source. Used by thumbnail grid, cache, and favorites.
/// </summary>
public class ImageSourceInfo
{
    public DisplayMode Source { get; set; }
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Date { get; set; } = "";
    public string ThumbnailUrl { get; set; } = "";
    public string FullImageUrl { get; set; } = "";
    public string HdImageUrl { get; set; } = "";

    // Unsplash attribution (required by their API terms)
    public string PhotographerName { get; set; } = "";
    public string PhotographerUrl { get; set; } = "";
    public string SourceAttribution { get; set; } = "";

    // Unsplash download tracking URL
    public string DownloadLocationUrl { get; set; } = "";

    public bool IsFavorited { get; set; }

    public override string ToString() => !string.IsNullOrEmpty(Title) ? Title : Id;
}
