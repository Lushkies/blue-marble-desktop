using System.Globalization;

namespace DesktopEarth;

/// <summary>
/// Manages cached EPIC images in %AppData%/BlueMarbleDesktop/epic_images/.
/// Images are organized by type and date:
///   epic_images/natural/2026-02-18/epic_1b_20260218001751.jpg
/// Old images are cleaned up after 14 days (configurable).
/// </summary>
public class EpicImageCache
{
    private static readonly string CacheDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "BlueMarbleDesktop", "epic_images");

    private const int MaxCachedDays = 14;

    /// <summary>
    /// Get the expected local file path for an EPIC image.
    /// </summary>
    public string GetCachePath(EpicImageInfo image, EpicImageType type)
    {
        var collection = type == EpicImageType.Enhanced ? "enhanced" : "natural";

        // Parse date from image's Date field
        string dateFolder;
        if (DateTime.TryParse(image.Date, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            dateFolder = dt.ToString("yyyy-MM-dd");
        else
            dateFolder = "unknown";

        return Path.Combine(CacheDir, collection, dateFolder, $"{image.Image}.jpg");
    }

    /// <summary>
    /// Check if an EPIC image is already cached (file exists and is reasonably sized).
    /// </summary>
    public bool IsCached(EpicImageInfo image, EpicImageType type)
    {
        var path = GetCachePath(image, type);
        if (!File.Exists(path))
            return false;

        // Verify it's a real file (not a failed partial download)
        var info = new FileInfo(path);
        return info.Length > 100 * 1024; // > 100KB
    }

    /// <summary>
    /// Delete cached images older than the specified number of days.
    /// Safe to call periodically — skips errors silently.
    /// </summary>
    public void CleanOldCache(int maxDays = 14)
    {
        try
        {
            if (!Directory.Exists(CacheDir))
                return;
            if (maxDays <= 0) return; // 0 = keep forever

            var cutoff = DateTime.Now.AddDays(-maxDays);

            foreach (var collectionDir in Directory.GetDirectories(CacheDir))
            {
                foreach (var dateDir in Directory.GetDirectories(collectionDir))
                {
                    var dirName = Path.GetFileName(dateDir);
                    if (DateTime.TryParse(dirName, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dirDate) && dirDate < cutoff)
                    {
                        try
                        {
                            Directory.Delete(dateDir, recursive: true);
                            Console.WriteLine($"EPIC cache: Cleaned old directory {dirName}");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"EPIC cache cleanup error: {ex.Message}");
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"EPIC cache cleanup error: {ex.Message}");
        }
    }

    /// <summary>
    /// Delete cached EPIC images whose IDs match blacklisted prefixes.
    /// </summary>
    public void CleanBlacklistedImages(List<string>? prefixes)
    {
        if (prefixes == null || prefixes.Count == 0) return;

        try
        {
            if (!Directory.Exists(CacheDir)) return;

            int deleted = 0;
            foreach (var collectionDir in Directory.GetDirectories(CacheDir))
            {
                foreach (var dateDir in Directory.GetDirectories(collectionDir))
                {
                    foreach (var file in Directory.GetFiles(dateDir, "*.jpg"))
                    {
                        var fileId = Path.GetFileNameWithoutExtension(file);
                        if (ImageCache.IsBlacklisted(fileId, prefixes))
                        {
                            try { File.Delete(file); deleted++; } catch { }
                        }
                    }
                }
            }

            if (deleted > 0)
                Console.WriteLine($"EPIC cache: Cleaned {deleted} blacklisted images");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"EPIC cache blacklist cleanup error: {ex.Message}");
        }
    }

    /// <summary>
    /// Get all cached image paths for a given EPIC image type.
    /// Used by the rotation system to build a pool of available images.
    /// </summary>
    public List<string> GetAllCachedImagePaths(EpicImageType type)
    {
        try
        {
            var collection = type == EpicImageType.Enhanced ? "enhanced" : "natural";
            var collectionDir = Path.Combine(CacheDir, collection);

            if (!Directory.Exists(collectionDir))
                return new List<string>();

            return Directory.GetDirectories(collectionDir)
                .SelectMany(dateDir => Directory.GetFiles(dateDir, "*.jpg"))
                .Where(f => new FileInfo(f).Length > 100 * 1024)
                .OrderByDescending(f => File.GetLastWriteTime(f))
                .ToList();
        }
        catch
        {
            return new List<string>();
        }
    }

    /// <summary>
    /// Find the most recent cached image for offline fallback.
    /// Returns the file path or null if no cached images exist.
    /// </summary>
    public string? GetLatestCachedImagePath(EpicImageType type)
    {
        try
        {
            var collection = type == EpicImageType.Enhanced ? "enhanced" : "natural";
            var collectionDir = Path.Combine(CacheDir, collection);

            if (!Directory.Exists(collectionDir))
                return null;

            // Get date directories sorted newest first
            var dateDirs = Directory.GetDirectories(collectionDir)
                .Select(d => new { Path = d, Name = Path.GetFileName(d) })
                .Where(d => DateTime.TryParse(d.Name, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                .OrderByDescending(d => d.Name)
                .ToList();

            foreach (var dateDir in dateDirs)
            {
                // Get the first JPG in this date directory
                var images = Directory.GetFiles(dateDir.Path, "*.jpg")
                    .Where(f => new FileInfo(f).Length > 100 * 1024) // Valid files only
                    .OrderBy(f => f) // Consistent ordering
                    .ToList();

                if (images.Count > 0)
                    return images[0];
            }

            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"EPIC cache search error: {ex.Message}");
            return null;
        }
    }
}
