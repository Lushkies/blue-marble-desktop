namespace DesktopEarth;

/// <summary>
/// Manages cached EPIC images in %AppData%/BlueMarbleDesktop/epic_images/.
/// Images are organized by type and date:
///   epic_images/natural/2026-02-18/epic_1b_20260218001751.jpg
/// Old images are cleaned up after 7 days.
/// </summary>
public class EpicImageCache
{
    private static readonly string CacheDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "BlueMarbleDesktop", "epic_images");

    private const int MaxCachedDays = 7;

    /// <summary>
    /// Get the expected local file path for an EPIC image.
    /// </summary>
    public string GetCachePath(EpicImageInfo image, EpicImageType type)
    {
        var collection = type == EpicImageType.Enhanced ? "enhanced" : "natural";

        // Parse date from image's Date field
        string dateFolder;
        if (DateTime.TryParse(image.Date, out var dt))
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
    /// Delete cached images older than MaxCachedDays.
    /// Safe to call periodically — skips errors silently.
    /// </summary>
    public void CleanOldCache()
    {
        try
        {
            if (!Directory.Exists(CacheDir))
                return;

            var cutoff = DateTime.Now.AddDays(-MaxCachedDays);

            foreach (var collectionDir in Directory.GetDirectories(CacheDir))
            {
                foreach (var dateDir in Directory.GetDirectories(collectionDir))
                {
                    var dirName = Path.GetFileName(dateDir);
                    if (DateTime.TryParse(dirName, out var dirDate) && dirDate < cutoff)
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
                .Where(d => DateTime.TryParse(d.Name, out _))
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
