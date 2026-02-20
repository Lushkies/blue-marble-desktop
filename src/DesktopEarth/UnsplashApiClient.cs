using System.Text.Json;
using System.Text.Json.Serialization;

namespace DesktopEarth;

/// <summary>
/// Client for the Unsplash API.
/// https://api.unsplash.com/
/// Requires a free access key from unsplash.com/developers.
/// IMPORTANT: Must display photographer attribution per API terms.
/// </summary>
public class UnsplashApiClient
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    private const string ApiBase = "https://api.unsplash.com";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Search photos by keyword. Returns null on error.
    /// </summary>
    public async Task<List<ImageSourceInfo>?> SearchPhotosAsync(
        string accessKey, string query, int page = 1, int perPage = 30)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(accessKey))
            {
                Console.WriteLine("Unsplash: No access key configured");
                return null;
            }

            var url = $"{ApiBase}/search/photos?query={Uri.EscapeDataString(query)}&page={page}&per_page={perPage}&orientation=landscape";

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("Authorization", $"Client-ID {accessKey}");

            var response = await Http.SendAsync(request);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<UnsplashSearchResponse>(json, JsonOptions);

            return result?.Results?.Select(MapToInfo).ToList();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Unsplash API error: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Get photos from a topic/collection. Returns null on error.
    /// Topic slugs: "nature", "wallpapers", "travel", "animals", "architecture"
    /// </summary>
    public async Task<List<ImageSourceInfo>?> GetTopicPhotosAsync(
        string accessKey, string topicSlug, int page = 1, int perPage = 30)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(accessKey))
            {
                Console.WriteLine("Unsplash: No access key configured");
                return null;
            }

            var url = $"{ApiBase}/topics/{Uri.EscapeDataString(topicSlug)}/photos?page={page}&per_page={perPage}";

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("Authorization", $"Client-ID {accessKey}");

            var response = await Http.SendAsync(request);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync();
            var photos = JsonSerializer.Deserialize<List<UnsplashPhoto>>(json, JsonOptions);

            return photos?.Select(MapToInfo).ToList();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Unsplash API error (topic {topicSlug}): {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Trigger a download event (REQUIRED by Unsplash API terms when setting as wallpaper).
    /// </summary>
    public async Task TriggerDownloadAsync(string accessKey, string downloadLocation)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(accessKey) || string.IsNullOrEmpty(downloadLocation))
                return;

            using var request = new HttpRequestMessage(HttpMethod.Get, downloadLocation);
            request.Headers.Add("Authorization", $"Client-ID {accessKey}");
            await Http.SendAsync(request);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Unsplash download trigger error: {ex.Message}");
        }
    }

    private static ImageSourceInfo MapToInfo(UnsplashPhoto photo)
    {
        string photographerName = photo.User?.Name ?? "Unknown";
        return new ImageSourceInfo
        {
            Source = DisplayMode.Unsplash,
            Id = photo.Id ?? "",
            Title = photo.Description ?? photo.AltDescription ?? $"Photo by {photographerName}",
            Description = photo.Description ?? photo.AltDescription ?? "",
            Date = photo.CreatedAt ?? "",
            ThumbnailUrl = photo.Urls?.Small ?? photo.Urls?.Thumb ?? "",
            FullImageUrl = photo.Urls?.Full ?? photo.Urls?.Regular ?? "",
            // Request 4K via raw URL with width parameter
            HdImageUrl = !string.IsNullOrEmpty(photo.Urls?.Raw)
                ? $"{photo.Urls.Raw}&w=3840&q=85"
                : photo.Urls?.Full ?? "",
            PhotographerName = photographerName,
            PhotographerUrl = photo.User?.Links?.Html ?? "",
            SourceAttribution = $"Photo by {photographerName} on Unsplash",
            DownloadLocationUrl = photo.Links?.DownloadLocation ?? ""
        };
    }
}

// Unsplash API response models

internal class UnsplashSearchResponse
{
    [JsonPropertyName("results")]
    public List<UnsplashPhoto>? Results { get; set; }

    [JsonPropertyName("total")]
    public int Total { get; set; }

    [JsonPropertyName("total_pages")]
    public int TotalPages { get; set; }
}

internal class UnsplashPhoto
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("alt_description")]
    public string? AltDescription { get; set; }

    [JsonPropertyName("created_at")]
    public string? CreatedAt { get; set; }

    [JsonPropertyName("width")]
    public int Width { get; set; }

    [JsonPropertyName("height")]
    public int Height { get; set; }

    [JsonPropertyName("urls")]
    public UnsplashUrls? Urls { get; set; }

    [JsonPropertyName("user")]
    public UnsplashUser? User { get; set; }

    [JsonPropertyName("links")]
    public UnsplashPhotoLinks? Links { get; set; }
}

internal class UnsplashUrls
{
    [JsonPropertyName("raw")]
    public string? Raw { get; set; }

    [JsonPropertyName("full")]
    public string? Full { get; set; }

    [JsonPropertyName("regular")]
    public string? Regular { get; set; }

    [JsonPropertyName("small")]
    public string? Small { get; set; }

    [JsonPropertyName("thumb")]
    public string? Thumb { get; set; }
}

internal class UnsplashUser
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("links")]
    public UnsplashUserLinks? Links { get; set; }
}

internal class UnsplashUserLinks
{
    [JsonPropertyName("html")]
    public string? Html { get; set; }
}

internal class UnsplashPhotoLinks
{
    [JsonPropertyName("download_location")]
    public string? DownloadLocation { get; set; }
}
