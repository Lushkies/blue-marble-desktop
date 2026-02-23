namespace DesktopEarth;

/// <summary>
/// Unified file cache for all image sources (APOD, NPS, Smithsonian).
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

    private const int MaxCachedDays = 30;

    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(60)
    };

    /// <summary>
    /// Get the cache path for a full-size image.
    /// </summary>
    public string GetCachePath(ImageSource source, string imageId, string extension = ".jpg")
    {
        string sourceDir = SourceDirName(source);
        string safeId = SanitizeFileName(imageId);
        return Path.Combine(CacheDir, sourceDir, safeId + extension);
    }

    /// <summary>
    /// Get the cache path for a thumbnail image.
    /// </summary>
    public string GetThumbCachePath(ImageSource source, string imageId)
    {
        string sourceDir = SourceDirName(source);
        string safeId = SanitizeFileName(imageId);
        return Path.Combine(ThumbCacheDir, sourceDir, safeId + ".jpg");
    }

    /// <summary>
    /// Check if a full-size image is cached (exists and > 50KB).
    /// </summary>
    public bool IsCached(ImageSource source, string imageId, string extension = ".jpg")
    {
        var path = GetCachePath(source, imageId, extension);
        if (!File.Exists(path)) return false;
        return new FileInfo(path).Length > 50 * 1024;
    }

    /// <summary>
    /// Check if a thumbnail is cached.
    /// </summary>
    public bool IsThumbCached(ImageSource source, string imageId)
    {
        var path = GetThumbCachePath(source, imageId);
        return File.Exists(path) && new FileInfo(path).Length > 1024;
    }

    /// <summary>
    /// Check if an image ID matches any blacklisted prefix (case-insensitive).
    /// </summary>
    public static bool IsBlacklisted(string imageId, List<string>? prefixes)
    {
        if (prefixes == null || prefixes.Count == 0) return false;
        return prefixes.Any(p => imageId.StartsWith(p, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Download a URL to the full-size image cache.
    /// Uses temp-file-then-rename for atomic writes.
    /// Returns local file path on success, null on failure.
    /// </summary>
    public async Task<string?> DownloadToCache(
        ImageSource source, string imageId, string url,
        string extension = ".jpg", CancellationToken ct = default)
    {
        string? tempPath = null;
        try
        {
            var cachePath = GetCachePath(source, imageId, extension);

            // Already cached?
            if (File.Exists(cachePath) && new FileInfo(cachePath).Length > 50 * 1024)
                return cachePath;

            var dir = Path.GetDirectoryName(cachePath)!;
            Directory.CreateDirectory(dir);

            url = EnsureHttps(url);
            if (!IsAllowedImageUrl(url))
            {
                Console.WriteLine($"ImageCache: Blocked download from untrusted URL for {source}/{imageId}");
                return null;
            }

            Console.WriteLine($"ImageCache: Downloading {source}/{imageId}...");
            using var response = await Http.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();

            tempPath = cachePath + ".tmp";
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
            try { if (tempPath != null) File.Delete(tempPath); } catch { }
            return null;
        }
    }

    /// <summary>
    /// Download a thumbnail to the thumbnail cache.
    /// Returns local file path on success, null on failure.
    /// </summary>
    public async Task<string?> DownloadThumbnail(
        ImageSource source, string imageId, string thumbnailUrl,
        CancellationToken ct = default)
    {
        string? tempPath = null;
        try
        {
            var cachePath = GetThumbCachePath(source, imageId);

            if (File.Exists(cachePath) && new FileInfo(cachePath).Length > 1024)
                return cachePath;

            var dir = Path.GetDirectoryName(cachePath)!;
            Directory.CreateDirectory(dir);

            thumbnailUrl = EnsureHttps(thumbnailUrl);
            if (!IsAllowedImageUrl(thumbnailUrl))
                return null;

            using var response = await Http.GetAsync(thumbnailUrl, ct);
            response.EnsureSuccessStatusCode();

            tempPath = cachePath + ".tmp";
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
            try { if (tempPath != null) File.Delete(tempPath); } catch { }
            return null;
        }
    }

    /// <summary>
    /// Get the most recent cached image for a source (offline fallback).
    /// </summary>
    public string? GetLatestCachedImagePath(ImageSource source)
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
    public List<string> GetAllCachedImagePaths(ImageSource source)
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
    /// Delete cached files older than the specified number of days.
    /// Protects favorited images and always keeps the most recent image per source.
    /// </summary>
    public void CleanOldCache(HashSet<string>? protectedIds = null, int maxDays = 30)
    {
        try
        {
            if (!Directory.Exists(CacheDir)) return;
            if (maxDays <= 0) return; // 0 = keep forever

            var cutoff = DateTime.Now.AddDays(-maxDays);

            foreach (var dir in Directory.GetDirectories(CacheDir))
            {
                if (Path.GetFileName(dir) == "thumbnails") continue; // Skip thumb dir

                var files = Directory.GetFiles(dir)
                    .Where(f => !f.EndsWith(".tmp"))
                    .OrderByDescending(f => File.GetLastWriteTime(f))
                    .ToList();

                // Always keep at least the most recent file (offline safety)
                bool keptOne = false;

                foreach (var file in files)
                {
                    try
                    {
                        // Always keep the most recent file per source
                        if (!keptOne)
                        {
                            keptOne = true;
                            continue;
                        }

                        // Never delete favorited images
                        var fileId = Path.GetFileNameWithoutExtension(file);
                        if (protectedIds != null && protectedIds.Contains(fileId))
                            continue;

                        if (File.GetLastWriteTime(file) < cutoff)
                            File.Delete(file);
                    }
                    catch { }
                }
            }

            // Clean old thumbnails too (but don't need to protect them)
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

    private static string SourceDirName(ImageSource source) => source switch
    {
        ImageSource.NasaApod => "nasaapod",
        ImageSource.NationalParks => "nationalparks",
        ImageSource.Smithsonian => "smithsonian",
        ImageSource.UserImages => "userimages",
        ImageSource.NasaGallery => "nasagallery",
        _ => "other"
    };

    /// <summary>
    /// Get cached full-image path for a favorite, if it exists locally.
    /// Checks LocalCachePath first, then the standard cache directory.
    /// </summary>
    public string? GetCachedPathForFavorite(FavoriteImage fav)
    {
        // Check if LocalCachePath is set and exists
        if (!string.IsNullOrEmpty(fav.LocalCachePath) && File.Exists(fav.LocalCachePath))
            return fav.LocalCachePath;

        // For user images, check user_images directory
        if (fav.Source == ImageSource.UserImages)
        {
            var userMgr = new UserImageManager();
            return userMgr.GetImagePath(fav.ImageId);
        }

        // Check standard image_cache
        var path = GetCachePath(fav.Source, fav.ImageId);
        if (File.Exists(path) && new FileInfo(path).Length > 50 * 1024)
            return path;

        return null;
    }

    /// <summary>
    /// Delete cached images (full-size + thumbnails) whose IDs match blacklisted prefixes.
    /// Called on startup to clean up previously cached unwanted images.
    /// </summary>
    public void CleanBlacklistedImages(List<string>? prefixes)
    {
        if (prefixes == null || prefixes.Count == 0) return;

        try
        {
            if (!Directory.Exists(CacheDir)) return;

            int deleted = 0;
            foreach (var dir in Directory.GetDirectories(CacheDir))
            {
                if (Path.GetFileName(dir) == "thumbnails") continue;

                foreach (var file in Directory.GetFiles(dir))
                {
                    var fileId = Path.GetFileNameWithoutExtension(file);
                    if (IsBlacklisted(fileId, prefixes))
                    {
                        try { File.Delete(file); deleted++; } catch { }
                    }
                }
            }

            // Clean matching thumbnails too
            if (Directory.Exists(ThumbCacheDir))
            {
                foreach (var dir in Directory.GetDirectories(ThumbCacheDir))
                {
                    foreach (var file in Directory.GetFiles(dir))
                    {
                        var fileId = Path.GetFileNameWithoutExtension(file);
                        if (IsBlacklisted(fileId, prefixes))
                        {
                            try { File.Delete(file); } catch { }
                        }
                    }
                }
            }

            if (deleted > 0)
                Console.WriteLine($"ImageCache: Cleaned {deleted} blacklisted images");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ImageCache blacklist cleanup error: {ex.Message}");
        }
    }

    public static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        // Limit length
        if (sanitized.Length > 100)
            sanitized = sanitized[..100];
        return sanitized;
    }

    /// <summary>
    /// Upgrade http:// URLs to https:// for transport security.
    /// Returns the URL unchanged if already https or other scheme.
    /// </summary>
    public static string EnsureHttps(string url)
    {
        if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            return "https://" + url[7..];
        return url;
    }

    /// <summary>
    /// Validate that a URL uses an allowed scheme and points to a trusted image host.
    /// Returns true for valid image URLs, false for suspicious ones (internal IPs, file://, etc.).
    /// </summary>
    public static bool IsAllowedImageUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;

        // Only allow http/https schemes
        if (uri.Scheme != "https" && uri.Scheme != "http")
            return false;

        // Block private/internal IP ranges and localhost
        var host = uri.Host;
        if (host == "localhost" || host == "127.0.0.1" || host == "::1" ||
            host.StartsWith("10.") || host.StartsWith("192.168.") ||
            host == "169.254.169.254" || host.StartsWith("172."))
            return false;

        return true;
    }
}
