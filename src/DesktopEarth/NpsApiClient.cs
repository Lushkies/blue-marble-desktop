using System.Text.Json;
using System.Text.Json.Serialization;

namespace DesktopEarth;

/// <summary>
/// Client for the National Park Service API.
/// https://developer.nps.gov/api/v1/
/// Requires a free API key from developer.nps.gov.
/// </summary>
public class NpsApiClient
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    private const string ApiBase = "https://developer.nps.gov/api/v1";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Search parks by keyword and return their images.
    /// Returns null on error.
    /// </summary>
    public async Task<List<ImageSourceInfo>?> SearchParksAsync(string apiKey, string query, int limit = 10)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                Console.WriteLine("NPS: No API key configured");
                return null;
            }

            var url = $"{ApiBase}/parks?q={Uri.EscapeDataString(query)}&limit={limit}&api_key={apiKey}";
            var response = await Http.GetAsync(url);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<NpsParksResponse>(json, JsonOptions);

            if (result?.Data == null) return null;

            var images = new List<ImageSourceInfo>();

            foreach (var park in result.Data)
            {
                if (park.Images == null) continue;

                foreach (var img in park.Images)
                {
                    if (string.IsNullOrEmpty(img.Url)) continue;

                    images.Add(new ImageSourceInfo
                    {
                        Source = ImageSource.NationalParks,
                        Id = $"{park.ParkCode}_{images.Count}",
                        Title = $"{park.FullName}: {img.Caption ?? img.AltText ?? "Photo"}",
                        Description = img.Caption ?? "",
                        ThumbnailUrl = img.Url,   // NPS doesn't provide separate thumbnails
                        FullImageUrl = img.Url,
                        HdImageUrl = img.Url,
                        SourceAttribution = $"National Park Service - {park.FullName}"
                    });
                }
            }

            return images;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"NPS API error: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Get images for a specific park by park code (e.g., "yell" for Yellowstone).
    /// Returns null on error.
    /// </summary>
    public async Task<List<ImageSourceInfo>?> GetParkImagesAsync(string apiKey, string parkCode)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                Console.WriteLine("NPS: No API key configured");
                return null;
            }

            var url = $"{ApiBase}/parks?parkCode={Uri.EscapeDataString(parkCode)}&api_key={apiKey}";
            var response = await Http.GetAsync(url);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<NpsParksResponse>(json, JsonOptions);

            if (result?.Data == null || result.Data.Count == 0) return null;

            var park = result.Data[0];
            var images = new List<ImageSourceInfo>();

            if (park.Images != null)
            {
                int idx = 0;
                foreach (var img in park.Images)
                {
                    if (string.IsNullOrEmpty(img.Url)) continue;

                    images.Add(new ImageSourceInfo
                    {
                        Source = ImageSource.NationalParks,
                        Id = $"{parkCode}_{idx++}",
                        Title = img.Caption ?? img.AltText ?? $"{park.FullName} Photo",
                        Description = img.Caption ?? "",
                        ThumbnailUrl = img.Url,
                        FullImageUrl = img.Url,
                        HdImageUrl = img.Url,
                        SourceAttribution = $"National Park Service - {park.FullName}"
                    });
                }
            }

            return images;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"NPS API error (park {parkCode}): {ex.Message}");
            return null;
        }
    }
}

// NPS API response models

internal class NpsParksResponse
{
    [JsonPropertyName("data")]
    public List<NpsPark>? Data { get; set; }
}

internal class NpsPark
{
    [JsonPropertyName("parkCode")]
    public string ParkCode { get; set; } = "";

    [JsonPropertyName("fullName")]
    public string FullName { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("images")]
    public List<NpsParkImage>? Images { get; set; }
}

internal class NpsParkImage
{
    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("caption")]
    public string? Caption { get; set; }

    [JsonPropertyName("altText")]
    public string? AltText { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }
}
