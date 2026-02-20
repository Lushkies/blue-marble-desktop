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

    public bool IsFavorited { get; set; }

    public override string ToString() => !string.IsNullOrEmpty(Title) ? Title : Id;
}
