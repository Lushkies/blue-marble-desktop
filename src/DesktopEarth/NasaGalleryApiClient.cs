using System.Text.Json;

namespace DesktopEarth;

/// <summary>
/// Client for the NASA Image and Video Library API.
/// https://images.nasa.gov/docs/images.nasa.gov_api_docs.pdf
/// Free, no API key required. All images are public domain.
/// </summary>
public class NasaGalleryApiClient
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    private const string ApiBase = "https://images-api.nasa.gov";

    /// <summary>
    /// Curated search queries for rotation pool building and suggestion chips.
    /// Focused on space/astronomy topics that produce stunning wallpaper images.
    /// </summary>
    public static readonly string[] SuggestedQueries =
    [
        "Nebula", "Galaxy", "James Webb", "Hubble", "Earth from Space",
        "Jupiter", "Saturn", "Mars", "Aurora", "Supernova",
        "Milky Way", "Solar System", "Astronaut", "Moon Landing", "Space Station"
    ];

    /// <summary>
    /// Search for images matching a query. Returns null on error.
    /// </summary>
    public async Task<List<ImageSourceInfo>?> SearchImagesAsync(string query, int pageSize = 30)
    {
        try
        {
            var url = $"{ApiBase}/search?q={Uri.EscapeDataString(query)}&media_type=image&page_size={pageSize}";
            using var response = await Http.GetAsync(url);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync();

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("collection", out var collection) ||
                !collection.TryGetProperty("items", out var items))
                return null;

            var images = new List<ImageSourceInfo>();

            foreach (var item in items.EnumerateArray())
            {
                try
                {
                    if (!item.TryGetProperty("data", out var dataArr)) continue;
                    var data = dataArr[0];

                    string nasaId = "";
                    string title = "";
                    string description = "";
                    string dateCreated = "";
                    string center = "";

                    if (data.TryGetProperty("nasa_id", out var idEl))
                        nasaId = idEl.GetString() ?? "";
                    if (data.TryGetProperty("title", out var titleEl))
                        title = titleEl.GetString() ?? "";
                    if (data.TryGetProperty("description", out var descEl))
                        description = descEl.GetString() ?? "";
                    if (data.TryGetProperty("date_created", out var dateEl))
                        dateCreated = dateEl.GetString() ?? "";
                    if (data.TryGetProperty("center", out var centerEl))
                        center = centerEl.GetString() ?? "";

                    if (string.IsNullOrEmpty(nasaId)) continue;

                    // Get thumbnail from links array
                    string thumbUrl = "";
                    if (item.TryGetProperty("links", out var links))
                    {
                        foreach (var link in links.EnumerateArray())
                        {
                            if (link.TryGetProperty("rel", out var relEl) &&
                                relEl.GetString() == "preview" &&
                                link.TryGetProperty("href", out var hrefEl))
                            {
                                thumbUrl = hrefEl.GetString() ?? "";
                                break;
                            }
                        }
                    }

                    if (string.IsNullOrEmpty(thumbUrl)) continue;

                    // Parse date for display
                    string displayDate = "";
                    if (DateTime.TryParse(dateCreated, System.Globalization.CultureInfo.InvariantCulture,
                            System.Globalization.DateTimeStyles.None, out var dt))
                        displayDate = dt.ToString("yyyy-MM-dd");

                    images.Add(new ImageSourceInfo
                    {
                        Source = ImageSource.NasaGallery,
                        Id = nasaId,
                        Title = title,
                        Description = description.Length > 200 ? description[..200] + "..." : description,
                        Date = displayDate,
                        ThumbnailUrl = thumbUrl,
                        FullImageUrl = thumbUrl, // Placeholder — resolved via GetBestImageUrlAsync on download
                        HdImageUrl = "",
                        SourceAttribution = string.IsNullOrEmpty(center)
                            ? "NASA Image and Video Library"
                            : $"NASA {center}"
                    });
                }
                catch { /* Skip malformed entries */ }
            }

            return images;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"NASA Gallery API error: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Get the best (highest resolution) image URL for a NASA ID.
    /// Calls the /asset/{nasa_id} endpoint to get the manifest.
    /// Prefers ~orig.jpg > ~large.jpg > ~medium.jpg.
    /// Returns URL string, or null on error.
    /// </summary>
    public async Task<string?> GetBestImageUrlAsync(string nasaId)
    {
        try
        {
            var url = $"{ApiBase}/asset/{Uri.EscapeDataString(nasaId)}";
            using var response = await Http.GetAsync(url);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync();

            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("collection", out var collection) ||
                !collection.TryGetProperty("items", out var items))
                return null;

            string? origUrl = null, largeUrl = null, mediumUrl = null;
            foreach (var item in items.EnumerateArray())
            {
                if (!item.TryGetProperty("href", out var hrefEl)) continue;
                var href = hrefEl.GetString() ?? "";
                if (string.IsNullOrEmpty(href)) continue;

                // Skip non-image files (metadata.json, etc.)
                if (!href.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) &&
                    !href.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) &&
                    !href.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (href.Contains("~orig", StringComparison.OrdinalIgnoreCase))
                    origUrl = href;
                else if (href.Contains("~large", StringComparison.OrdinalIgnoreCase))
                    largeUrl = href;
                else if (href.Contains("~medium", StringComparison.OrdinalIgnoreCase))
                    mediumUrl = href;
            }

            // Prefer highest resolution available
            var bestUrl = origUrl ?? largeUrl ?? mediumUrl;

            // Ensure HTTPS
            if (bestUrl != null && bestUrl.StartsWith("http://"))
                bestUrl = "https://" + bestUrl[7..];

            return bestUrl;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"NASA Gallery asset error ({nasaId}): {ex.Message}");
            return null;
        }
    }
}
