using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DesktopEarth;

/// <summary>
/// Client for NASA's EPIC (Earth Polychromatic Imaging Camera) API.
/// DSCOVR satellite photographs of Earth from the L1 Lagrange point.
/// No API key required. https://epic.gsfc.nasa.gov/about/api
/// </summary>
public class EpicApiClient
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    private const string ApiBase = "https://epic.gsfc.nasa.gov/api";
    private const string ArchiveBase = "https://epic.gsfc.nasa.gov/archive";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Get the most recent EPIC images (natural or enhanced color).
    /// Returns null on any error (no internet, timeout, etc.).
    /// </summary>
    public async Task<List<EpicImageInfo>?> GetLatestImagesAsync(EpicImageType type)
    {
        try
        {
            var collection = type == EpicImageType.Enhanced ? "enhanced" : "natural";
            var url = $"{ApiBase}/{collection}";
            var response = await Http.GetAsync(url);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<List<EpicImageInfo>>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"EPIC API error (latest): {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Get EPIC images for a specific date (YYYY-MM-DD format).
    /// Returns null on any error.
    /// </summary>
    public async Task<List<EpicImageInfo>?> GetImagesByDateAsync(EpicImageType type, string date)
    {
        try
        {
            var collection = type == EpicImageType.Enhanced ? "enhanced" : "natural";
            var url = $"{ApiBase}/{collection}/date/{date}";
            var response = await Http.GetAsync(url);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<List<EpicImageInfo>>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"EPIC API error (date {date}): {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Get all available dates with EPIC images.
    /// Returns null on any error.
    /// </summary>
    public async Task<List<EpicDateInfo>?> GetAvailableDatesAsync(EpicImageType type)
    {
        try
        {
            var collection = type == EpicImageType.Enhanced ? "enhanced" : "natural";
            var url = $"{ApiBase}/{collection}/all";
            var response = await Http.GetAsync(url);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<List<EpicDateInfo>>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"EPIC API error (available dates): {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Build the archive URL for downloading an EPIC image.
    /// Uses JPG format (half resolution, ~0.5MB) for reasonable file sizes.
    /// </summary>
    public string GetImageUrl(EpicImageInfo image, EpicImageType type)
    {
        var collection = type == EpicImageType.Enhanced ? "enhanced" : "natural";

        // Parse the date from the image's Date field (format: "2026-02-18 00:13:03")
        if (!DateTime.TryParse(image.Date, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            return "";

        var year = dt.Year.ToString("D4");
        var month = dt.Month.ToString("D2");
        var day = dt.Day.ToString("D2");

        return $"{ArchiveBase}/{collection}/{year}/{month}/{day}/jpg/{image.Image}.jpg";
    }

    /// <summary>
    /// Download an EPIC image to the local cache directory.
    /// Returns the local file path on success, or null on failure.
    /// </summary>
    public async Task<string?> DownloadImageAsync(
        EpicImageInfo image, EpicImageType type, EpicImageCache cache,
        CancellationToken ct = default)
    {
        string? tempPath = null;
        try
        {
            // Check if already cached
            var cachePath = cache.GetCachePath(image, type);
            if (cache.IsCached(image, type))
                return cachePath;

            // Download
            var url = GetImageUrl(image, type);
            if (string.IsNullOrEmpty(url))
                return null;

            Console.WriteLine($"EPIC: Downloading {image.Image}...");
            using var response = await Http.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();

            // Ensure directory exists
            var dir = Path.GetDirectoryName(cachePath)!;
            Directory.CreateDirectory(dir);

            // Write to temp file first, then move (atomic)
            tempPath = cachePath + ".tmp";
            await using (var fileStream = File.Create(tempPath))
            {
                await response.Content.CopyToAsync(fileStream, ct);
            }

            File.Move(tempPath, cachePath, overwrite: true);
            Console.WriteLine($"EPIC: Downloaded {image.Image} ({new FileInfo(cachePath).Length / 1024}KB)");
            return cachePath;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"EPIC download error: {ex.Message}");
            try { if (tempPath != null) File.Delete(tempPath); } catch { }
            return null;
        }
    }
}

/// <summary>
/// Metadata for a single EPIC image from the API.
/// </summary>
public class EpicImageInfo
{
    [JsonPropertyName("identifier")]
    public string Identifier { get; set; } = "";

    [JsonPropertyName("image")]
    public string Image { get; set; } = "";

    [JsonPropertyName("caption")]
    public string Caption { get; set; } = "";

    [JsonPropertyName("date")]
    public string Date { get; set; } = "";

    [JsonPropertyName("centroid_coordinates")]
    public EpicCentroid? CentroidCoordinates { get; set; }

    /// <summary>
    /// Display-friendly string: time + brief description.
    /// </summary>
    public override string ToString()
    {
        if (DateTime.TryParse(Date, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            return $"{dt:HH:mm UTC} — {Image}";
        return Image;
    }
}

/// <summary>
/// Centroid (center point) coordinates for an EPIC image.
/// </summary>
public class EpicCentroid
{
    [JsonPropertyName("lat")]
    public double Lat { get; set; }

    [JsonPropertyName("lon")]
    public double Lon { get; set; }
}

/// <summary>
/// Date entry from the EPIC /all endpoint.
/// </summary>
public class EpicDateInfo
{
    [JsonPropertyName("date")]
    public string Date { get; set; } = "";
}
