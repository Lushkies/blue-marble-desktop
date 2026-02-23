using System.Text.Json;
using System.Text.Json.Serialization;

namespace DesktopEarth;

/// <summary>
/// Client for the Smithsonian Open Access API.
/// https://api.si.edu/openaccess/api/v1.0/
/// Requires a free API key from api.data.gov.
/// All images are CC0 public domain.
///
/// Uses the /category/art_design/search endpoint which returns full media data
/// including image URLs and dimensions. The base /search endpoint does NOT return
/// media data and should not be used.
/// </summary>
public class SmithsonianApiClient
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    private const string ApiBase = "https://api.si.edu/openaccess/api/v1.0";

    /// <summary>
    /// Search for images matching a query. Returns null on error.
    /// Uses the category endpoint for art_design which returns full media data.
    /// Filters for images using Solr field syntax inside the query parameter.
    /// </summary>
    public async Task<List<ImageSourceInfo>?> SearchImagesAsync(
        string apiKey, string query, int start = 0, int rows = 30)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                Console.WriteLine("Smithsonian: No API key configured");
                return null;
            }

            // Use category endpoint (art_design) which returns full online_media data.
            // Filter for images using Solr field syntax INSIDE the q parameter.
            // Wrap user query in quotes to prevent Solr operator injection.
            var escapedQuery = query.Replace("\\", "\\\\").Replace("\"", "\\\"");
            var solrQuery = $"online_media_type:Images AND \"{escapedQuery}\"";
            var url = $"{ApiBase}/category/art_design/search" +
                      $"?q={Uri.EscapeDataString(solrQuery)}" +
                      $"&start={start}&rows={rows}&sort=random" +
                      $"&api_key={apiKey}";

            var response = await Http.GetAsync(url);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync();

            // Parse the deeply nested Smithsonian response
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("response", out var responseEl) ||
                !responseEl.TryGetProperty("rows", out var rowsEl))
                return null;

            var images = new List<ImageSourceInfo>();

            foreach (var row in rowsEl.EnumerateArray())
            {
                try
                {
                    string title = "";
                    string id = "";
                    string imageUrl = "";
                    string thumbUrl = "";
                    string hdUrl = "";
                    int imgWidth = 0;
                    int imgHeight = 0;

                    // Get ID
                    if (row.TryGetProperty("id", out var idEl))
                        id = idEl.GetString() ?? "";

                    // Get title from content.descriptiveNonRepeating.title.content
                    if (row.TryGetProperty("content", out var contentEl))
                    {
                        if (contentEl.TryGetProperty("descriptiveNonRepeating", out var dnr))
                        {
                            if (dnr.TryGetProperty("title", out var titleEl))
                            {
                                if (titleEl.TryGetProperty("content", out var titleContent))
                                    title = titleContent.GetString() ?? "";
                                else if (titleEl.ValueKind == JsonValueKind.String)
                                    title = titleEl.GetString() ?? "";
                            }

                            // Get image URL from online_media.media[0]
                            if (dnr.TryGetProperty("online_media", out var onlineMedia) &&
                                onlineMedia.TryGetProperty("media", out var mediaArr))
                            {
                                foreach (var media in mediaArr.EnumerateArray())
                                {
                                    // Get the base content URL
                                    if (media.TryGetProperty("content", out var mediaContent))
                                    {
                                        var mediaUrl = mediaContent.GetString() ?? "";
                                        if (!string.IsNullOrEmpty(mediaUrl) &&
                                            (mediaUrl.Contains(".jpg", StringComparison.OrdinalIgnoreCase) ||
                                             mediaUrl.Contains(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                                             mediaUrl.Contains(".png", StringComparison.OrdinalIgnoreCase) ||
                                             mediaUrl.Contains("ids.si.edu", StringComparison.OrdinalIgnoreCase)))
                                        {
                                            imageUrl = mediaUrl;

                                            // Try to get thumbnail
                                            if (media.TryGetProperty("thumbnail", out var thumbEl))
                                                thumbUrl = thumbEl.GetString() ?? "";

                                            // Parse resources array for HD URLs and dimensions
                                            if (media.TryGetProperty("resources", out var resources))
                                            {
                                                ParseResources(resources, ref hdUrl, ref thumbUrl,
                                                    ref imgWidth, ref imgHeight);
                                            }

                                            break;
                                        }
                                    }
                                }
                            }
                        }
                    }

                    if (string.IsNullOrEmpty(imageUrl) || string.IsNullOrEmpty(id))
                        continue;

                    // Skip images below 1080p when dimensions are known
                    if (imgWidth > 0 && imgHeight > 0 &&
                        Math.Max(imgWidth, imgHeight) < 1080)
                        continue;

                    // Use the image URL as thumbnail if no dedicated thumbnail
                    if (string.IsNullOrEmpty(thumbUrl))
                        thumbUrl = imageUrl;

                    // Use HD URL if found, otherwise fall back to content URL
                    if (string.IsNullOrEmpty(hdUrl))
                        hdUrl = imageUrl;

                    var qualityTier = imgWidth > 0 && imgHeight > 0
                        ? ImageSourceInfo.GetQualityTier(imgWidth, imgHeight)
                        : ImageQualityTier.Unknown;

                    images.Add(new ImageSourceInfo
                    {
                        Source = ImageSource.Smithsonian,
                        Id = id,
                        Title = title,
                        ThumbnailUrl = thumbUrl,
                        FullImageUrl = imageUrl,
                        HdImageUrl = hdUrl,
                        ImageWidth = imgWidth,
                        ImageHeight = imgHeight,
                        QualityTier = qualityTier,
                        SourceAttribution = "Smithsonian Open Access (CC0 Public Domain)"
                    });
                }
                catch
                {
                    // Skip malformed entries
                }
            }

            return images;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Smithsonian API error: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Parse the resources array from a media entry to extract HD URLs and dimensions.
    /// Resources contain labeled downloads like "High-resolution JPEG", "Screen Image", "Thumbnail Image".
    /// </summary>
    private static void ParseResources(JsonElement resources,
        ref string hdUrl, ref string thumbUrl, ref int width, ref int height)
    {
        int bestWidth = 0;

        foreach (var res in resources.EnumerateArray())
        {
            string label = "";
            string url = "";
            int w = 0, h = 0;

            if (res.TryGetProperty("label", out var labelEl))
                label = labelEl.GetString() ?? "";
            if (res.TryGetProperty("url", out var urlEl))
                url = urlEl.GetString() ?? "";
            if (res.TryGetProperty("width", out var widthEl) && widthEl.TryGetInt32(out var wVal))
                w = wVal;
            if (res.TryGetProperty("height", out var heightEl) && heightEl.TryGetInt32(out var hVal))
                h = hVal;

            if (string.IsNullOrEmpty(url)) continue;

            // Use the thumbnail resource for thumbnails
            if (label.Contains("Thumbnail", StringComparison.OrdinalIgnoreCase))
            {
                thumbUrl = url;
                continue;
            }

            // Track the highest-resolution JPEG/PNG resource for HD
            bool isImage = label.Contains("JPEG", StringComparison.OrdinalIgnoreCase) ||
                           label.Contains("PNG", StringComparison.OrdinalIgnoreCase) ||
                           url.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                           url.EndsWith(".png", StringComparison.OrdinalIgnoreCase);

            if (isImage && w > bestWidth)
            {
                bestWidth = w;
                hdUrl = url;
                width = w;
                height = h;
            }
        }
    }
}
