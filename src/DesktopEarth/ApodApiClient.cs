using System.Text.Json;
using System.Text.Json.Serialization;

namespace DesktopEarth;

/// <summary>
/// Client for NASA's Astronomy Picture of the Day (APOD) API.
/// https://api.nasa.gov/planetary/apod
/// </summary>
public class ApodApiClient
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    private const string ApiBase = "https://api.nasa.gov/planetary/apod";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Get the last N days of APOD images (defaults to 7).
    /// Filters out video entries. Returns null on error.
    /// </summary>
    public async Task<List<ImageSourceInfo>?> GetRecentAsync(string apiKey, int days = 7)
    {
        try
        {
            var endDate = DateTime.UtcNow.ToString("yyyy-MM-dd");
            var startDate = DateTime.UtcNow.AddDays(-(days - 1)).ToString("yyyy-MM-dd");
            var url = $"{ApiBase}?api_key={apiKey}&start_date={startDate}&end_date={endDate}&thumbs=true";

            var response = await Http.GetAsync(url);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync();
            var items = JsonSerializer.Deserialize<List<ApodItem>>(json, JsonOptions);

            if (items == null) return null;

            return items
                .Where(i => i.MediaType == "image") // Skip video entries
                .OrderByDescending(i => i.Date)
                .Select(i => new ImageSourceInfo
                {
                    Source = ImageSource.NasaApod,
                    Id = i.Date ?? "",
                    Title = i.Title ?? "Untitled",
                    Description = i.Explanation ?? "",
                    Date = i.Date ?? "",
                    ThumbnailUrl = i.Url ?? "",       // Standard size for thumbnail
                    FullImageUrl = i.Url ?? "",        // Standard quality
                    HdImageUrl = i.HdUrl ?? i.Url ?? "", // Prefer HD for wallpaper
                    SourceAttribution = "NASA Astronomy Picture of the Day"
                })
                .ToList();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"APOD API error: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Get the APOD for a specific date. Returns null on error.
    /// </summary>
    public async Task<ImageSourceInfo?> GetByDateAsync(string apiKey, string date)
    {
        try
        {
            var url = $"{ApiBase}?api_key={apiKey}&date={date}&thumbs=true";
            var response = await Http.GetAsync(url);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync();
            var item = JsonSerializer.Deserialize<ApodItem>(json, JsonOptions);

            if (item == null || item.MediaType != "image") return null;

            return new ImageSourceInfo
            {
                Source = ImageSource.NasaApod,
                Id = item.Date ?? "",
                Title = item.Title ?? "Untitled",
                Description = item.Explanation ?? "",
                Date = item.Date ?? "",
                ThumbnailUrl = item.Url ?? "",
                FullImageUrl = item.Url ?? "",
                HdImageUrl = item.HdUrl ?? item.Url ?? "",
                SourceAttribution = "NASA Astronomy Picture of the Day"
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"APOD API error (date {date}): {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Get the best URL for wallpaper use (prefer HD).
    /// </summary>
    public static string GetBestUrl(ImageSourceInfo info)
    {
        return !string.IsNullOrEmpty(info.HdImageUrl) ? info.HdImageUrl : info.FullImageUrl;
    }
}

/// <summary>
/// Raw APOD API response item.
/// </summary>
internal class ApodItem
{
    [JsonPropertyName("date")]
    public string? Date { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("explanation")]
    public string? Explanation { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("hdurl")]
    public string? HdUrl { get; set; }

    [JsonPropertyName("media_type")]
    public string? MediaType { get; set; }

    [JsonPropertyName("thumbnail_url")]
    public string? ThumbnailUrl { get; set; }
}
