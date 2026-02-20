using System.Text.Json;
using System.Text.Json.Serialization;

namespace DesktopEarth;

/// <summary>
/// Client for the Smithsonian Open Access API.
/// https://api.si.edu/openaccess/api/v1.0/
/// Requires a free API key from api.data.gov.
/// All images are CC0 public domain.
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
    /// Filters results to only include entries with online media (images).
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

            // Search with online_media_type filter for images
            var url = $"{ApiBase}/search?q={Uri.EscapeDataString(query)}" +
                      $"&online_media_type=Images&start={start}&rows={rows}" +
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

                            // Get image URL from online_media.media[0].content
                            if (dnr.TryGetProperty("online_media", out var onlineMedia) &&
                                onlineMedia.TryGetProperty("media", out var mediaArr))
                            {
                                foreach (var media in mediaArr.EnumerateArray())
                                {
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

                                            break;
                                        }
                                    }
                                }
                            }
                        }
                    }

                    if (string.IsNullOrEmpty(imageUrl) || string.IsNullOrEmpty(id))
                        continue;

                    // Use the image URL as thumbnail if no dedicated thumbnail
                    if (string.IsNullOrEmpty(thumbUrl))
                        thumbUrl = imageUrl;

                    images.Add(new ImageSourceInfo
                    {
                        Source = ImageSource.Smithsonian,
                        Id = id,
                        Title = title,
                        ThumbnailUrl = thumbUrl,
                        FullImageUrl = imageUrl,
                        HdImageUrl = imageUrl,
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
}
