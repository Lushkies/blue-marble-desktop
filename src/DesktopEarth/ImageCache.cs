namespace DesktopEarth;

/// <summary>
/// Unified file cache for all image sources (APOD, NPS, Unsplash, Smithsonian).
/// Structure: %AppData%/BlueMarbleDesktop/image_cache/{source}/{imageId}.jpg
/// Thumbnails: %AppData%/BlueMarbleDesktop/image_cache/thumbnails/{source}/{imageId}.jpg
/// EPIC images continue to use the separate EpicImageCache for backward compatibility.
/// </summary>
public class ImageCache
{
    private static readonly string CacheDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "BlueMarbleDesktop", "image_cache");

    private static readonly string ThumbCacheDir = Path.Combine(CacheDir, "thumbnails");

    private const int MaxCachedDays = 14;

    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(60)
    };

    /// <summary>
    /// Get the cache path for a full-size image.
    /// </summary>
    public string GetCachePath(DisplayMode source, string imageId, string extension = ".jpg")
    {
        string sourceDir = SourceDirName(source);
        string safeId = SanitizeFileName(imageId);
        return Path.Combine(CacheDir, sourceDir, safeId + extension);
    }

    /// <summary>
    /// Get the cache path for a thumbnail image.
    /// </summary>
    public string GetThumbCachePath(DisplayMode source, string imageId)
    {
        string sourceDir = SourceDirName(source);
        string safeId = SanitizeFileName(imageId);
        return Path.Combine(ThumbCacheDir, sourceDir, safeId + ".jpg");
    }

    /// <summary>
    /// Check if a full-size image is cached (exists and > 50KB).
    /// </summary>
    public bool IsCached(DisplayMode source, string imageId, string extension = ".jpg")
    {
        var path = GetCachePath(source, imageId, extension);
        if (!File.Exists(path)) return false;
        return new FileInfo(path).Length > 50 * 1024;
    }

    /// <summary>
    /// Check if a thumbnail is cached.
    /// </summary>
    public bool IsThumbCached(DisplayMode source, string imageId)
    {
        var path = GetThumbCachePath(source, imageId);
        return File.Exists(path) && new FileInfo(path).Length > 1024;
    }

    /// <summary>
    /// Download a URL to the full-size image cache.
    /// Uses temp-file-then-rename for atomic writes.
    /// Returns local file path on success, null on failure.
    /// </summary>
    public async Task<string?> DownloadToCache(
        DisplayMode source, string imageId, string url,
        string extension = ".jpg", CancellationToken ct = default)
    {
        try
        {
            var cachePath = GetCachePath(source, imageId, extension);

            // Already cached?
            if (File.Exists(cachePath) && new FileInfo(cachePath).Length > 50 * 1024)
                return cachePath;

            var dir = Path.GetDirectoryName(cachePath)!;
            Directory.CreateDirectory(dir);

            Console.WriteLine($"ImageCache: Downloading {source}/{imageId}...");
            var response = await Http.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();

            var tempPath = cachePath + ".tmp";
            await using (var fs = File.Create(tempPath))
            {
                await response.Content.CopyToAsync(fs, ct);
            }

            File.Move(tempPath, cachePath, overwrite: true);
            Console.WriteLine($"ImageCache: Downloaded {source}/{imageId} ({new FileInfo(cachePath).Length / 1024}KB)");
            return cachePath;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ImageCache download error ({source}/{imageId}): {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Download a thumbnail to the thumbnail cache.
    /// Returns local file path on success, null on failure.
    /// </summary>
    public async Task<string?> DownloadThumbnail(
        DisplayMode source, string imageId, string thumbnailUrl,
        CancellationToken ct = default)
    {
        try
        {
            var cachePath = GetThumbCachePath(source, imageId);

            if (File.Exists(cachePath) && new FileInfo(cachePath).Length > 1024)
                return cachePath;

            var dir = Path.GetDirectoryName(cachePath)!;
            Directory.CreateDirectory(dir);

            var response = await Http.GetAsync(thumbnailUrl, ct);
            response.EnsureSuccessStatusCode();

            var tempPath = cachePath + ".tmp";
            await using (var fs = File.Create(tempPath))
            {
                await response.Content.CopyToAsync(fs, ct);
            }

            File.Move(tempPath, cachePath, overwrite: true);
            return cachePath;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ImageCache thumb error ({source}/{imageId}): {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Get the most recent cached image for a source (offline fallback).
    /// </summary>
    public string? GetLatestCachedImagePath(DisplayMode source)
    {
        try
        {
            var sourceDir = Path.Combine(CacheDir, SourceDirName(source));
            if (!Directory.Exists(sourceDir)) return null;

            return Directory.GetFiles(sourceDir, "*.*")
                .Where(f => !f.EndsWith(".tmp"))
                .Where(f => new FileInfo(f).Length > 50 * 1024)
                .OrderByDescending(f => File.GetLastWriteTime(f))
                .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Get all cached image paths for a source (for random rotation).
    /// </summary>
    public List<string> GetAllCachedImagePaths(DisplayMode source)
    {
        try
        {
            var sourceDir = Path.Combine(CacheDir, SourceDirName(source));
            if (!Directory.Exists(sourceDir)) return new List<string>();

            return Directory.GetFiles(sourceDir, "*.*")
                .Where(f => !f.EndsWith(".tmp"))
                .Where(f => new FileInfo(f).Length > 50 * 1024)
                .ToList();
        }
        catch
        {
            return new List<string>();
        }
    }

    /// <summary>
    /// Delete cached files older than MaxCachedDays.
    /// </summary>
    public void CleanOldCache()
    {
        try
        {
            if (!Directory.Exists(CacheDir)) return;

            var cutoff = DateTime.Now.AddDays(-MaxCachedDays);

            foreach (var dir in Directory.GetDirectories(CacheDir))
            {
                if (Path.GetFileName(dir) == "thumbnails") continue; // Skip thumb dir

                foreach (var file in Directory.GetFiles(dir))
                {
                    try
                    {
                        if (File.GetLastWriteTime(file) < cutoff)
                            File.Delete(file);
                    }
                    catch { }
                }
            }

            // Clean old thumbnails too
            if (Directory.Exists(ThumbCacheDir))
            {
                foreach (var dir in Directory.GetDirectories(ThumbCacheDir))
                {
                    foreach (var file in Directory.GetFiles(dir))
                    {
                        try
                        {
                            if (File.GetLastWriteTime(file) < cutoff)
                                File.Delete(file);
                        }
                        catch { }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ImageCache cleanup error: {ex.Message}");
        }
    }

    private static string SourceDirName(DisplayMode source) => source switch
    {
        DisplayMode.NasaApod => "nasaapod",
        DisplayMode.NationalParks => "nationalparks",
        DisplayMode.Unsplash => "unsplash",
        DisplayMode.Smithsonian => "smithsonian",
        _ => "other"
    };

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        // Limit length
        if (sanitized.Length > 100)
            sanitized = sanitized[..100];
        return sanitized;
    }
}
