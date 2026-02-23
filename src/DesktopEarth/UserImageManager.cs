using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Formats.Jpeg;

namespace DesktopEarth;

/// <summary>
/// Manages user-imported images in %AppData%/BlueMarbleDesktop/user_images/.
/// Thumbnails are written to the ImageCache thumbnail directory so ThumbnailGridPanel
/// can load them without modification.
/// </summary>
public class UserImageManager
{
    private static readonly string UserImagesDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "BlueMarbleDesktop", "user_images");

    // Thumbnails go into ImageCache's thumbnail directory for seamless grid loading
    private static readonly string ThumbDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "BlueMarbleDesktop", "image_cache", "thumbnails", "userimages");

    private static readonly string[] SupportedExtensions =
        [".jpg", ".jpeg", ".png", ".bmp", ".webp"];

    private const int ThumbnailMaxDimension = 200;

    /// <summary>Full path to the user images directory.</summary>
    public static string ImagesDirectory => UserImagesDir;

    /// <summary>
    /// Import images from user-selected file paths. Copies to user_images/ with sanitized names.
    /// Returns the count of successfully imported images.
    /// </summary>
    public int ImportImages(string[] sourcePaths)
    {
        Directory.CreateDirectory(UserImagesDir);
        int imported = 0;

        foreach (var sourcePath in sourcePaths)
        {
            try
            {
                string ext = Path.GetExtension(sourcePath).ToLowerInvariant();
                if (!SupportedExtensions.Contains(ext))
                    continue;

                string baseName = ImageCache.SanitizeFileName(
                    Path.GetFileNameWithoutExtension(sourcePath));
                string destPath = Path.Combine(UserImagesDir, baseName + ext);

                // Handle duplicate names by appending a counter
                int counter = 1;
                while (File.Exists(destPath))
                {
                    destPath = Path.Combine(UserImagesDir, $"{baseName}_{counter}{ext}");
                    counter++;
                }

                File.Copy(sourcePath, destPath);
                imported++;

                // Pre-generate thumbnail
                EnsureThumbnail(destPath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"UserImageManager: Failed to import {sourcePath}: {ex.Message}");
            }
        }

        return imported;
    }

    /// <summary>
    /// Get all user images as ImageSourceInfo list (for ThumbnailGridPanel).
    /// Scans the directory at runtime, generates thumbnails as needed.
    /// </summary>
    public List<ImageSourceInfo> GetAllImages()
    {
        var images = new List<ImageSourceInfo>();

        if (!Directory.Exists(UserImagesDir))
            return images;

        try
        {
            var files = Directory.GetFiles(UserImagesDir)
                .Where(f => SupportedExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .OrderByDescending(f => File.GetLastWriteTime(f));

            foreach (var filePath in files)
            {
                try
                {
                    string id = Path.GetFileNameWithoutExtension(filePath);
                    string fileName = Path.GetFileName(filePath);

                    // Get dimensions for quality tier
                    int width = 0, height = 0;
                    var qualityTier = ImageQualityTier.Unknown;
                    try
                    {
                        var info = SixLabors.ImageSharp.Image.Identify(filePath);
                        if (info != null)
                        {
                            width = info.Width;
                            height = info.Height;
                            qualityTier = ImageSourceInfo.GetQualityTier(width, height);
                        }
                    }
                    catch { /* dimension detection is best-effort */ }

                    // Ensure thumbnail exists
                    EnsureThumbnail(filePath);

                    images.Add(new ImageSourceInfo
                    {
                        Source = ImageSource.UserImages,
                        Id = id,
                        Title = fileName,
                        ThumbnailUrl = "", // Local thumbnails — loaded via IsThumbCached path
                        FullImageUrl = filePath,
                        ImageWidth = width,
                        ImageHeight = height,
                        QualityTier = qualityTier
                    });
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"UserImageManager: Error reading {filePath}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"UserImageManager: Error scanning directory: {ex.Message}");
        }

        return images;
    }

    /// <summary>
    /// Delete a single user image and its thumbnail.
    /// </summary>
    public bool DeleteImage(string imageId)
    {
        try
        {
            // Find the actual file (could be any supported extension)
            var filePath = GetImagePath(imageId);
            if (filePath != null)
            {
                File.Delete(filePath);
            }

            // Delete thumbnail
            string safeId = ImageCache.SanitizeFileName(imageId);
            string thumbPath = Path.Combine(ThumbDir, safeId + ".jpg");
            if (File.Exists(thumbPath))
                File.Delete(thumbPath);

            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"UserImageManager: Error deleting {imageId}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Delete all user images and thumbnails.
    /// </summary>
    public void DeleteAllImages()
    {
        try
        {
            if (Directory.Exists(UserImagesDir))
            {
                foreach (var file in Directory.GetFiles(UserImagesDir))
                {
                    try { File.Delete(file); } catch { }
                }
            }

            if (Directory.Exists(ThumbDir))
            {
                foreach (var file in Directory.GetFiles(ThumbDir))
                {
                    try { File.Delete(file); } catch { }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"UserImageManager: Error deleting all images: {ex.Message}");
        }
    }

    /// <summary>
    /// Get the full file path for a user image by its ID (filename without extension).
    /// Searches for any supported extension.
    /// </summary>
    public string? GetImagePath(string imageId)
    {
        if (!Directory.Exists(UserImagesDir))
            return null;

        // Always sanitize first to prevent path traversal via tampered settings
        string safeId = ImageCache.SanitizeFileName(imageId);

        // Try sanitized ID first (covers both already-safe and needs-sanitization cases)
        foreach (var ext in SupportedExtensions)
        {
            string path = Path.Combine(UserImagesDir, safeId + ext);
            if (File.Exists(path))
                return path;
        }

        // Also try the original ID for backward compatibility with existing files
        if (safeId != imageId)
        {
            foreach (var ext in SupportedExtensions)
            {
                string path = Path.Combine(UserImagesDir, imageId + ext);
                // Verify the resolved path is within the user images directory
                string fullPath = Path.GetFullPath(path);
                if (fullPath.StartsWith(UserImagesDir, StringComparison.OrdinalIgnoreCase) &&
                    File.Exists(path))
                    return path;
            }
        }

        return null;
    }

    /// <summary>
    /// Get all image file paths (for random rotation).
    /// </summary>
    public List<string> GetAllImagePaths()
    {
        if (!Directory.Exists(UserImagesDir))
            return new List<string>();

        try
        {
            return Directory.GetFiles(UserImagesDir)
                .Where(f => SupportedExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .Where(f => new FileInfo(f).Length > 50 * 1024) // Skip tiny files
                .ToList();
        }
        catch
        {
            return new List<string>();
        }
    }

    /// <summary>
    /// Ensure a thumbnail exists for the given image file. Generates a ~200px thumbnail
    /// into the ImageCache thumbnail directory so ThumbnailGridPanel can find it.
    /// </summary>
    public void EnsureThumbnail(string imagePath)
    {
        try
        {
            string imageId = Path.GetFileNameWithoutExtension(imagePath);
            string safeId = ImageCache.SanitizeFileName(imageId);
            string thumbPath = Path.Combine(ThumbDir, safeId + ".jpg");

            if (File.Exists(thumbPath) && new FileInfo(thumbPath).Length > 1024)
                return; // Already exists

            Directory.CreateDirectory(ThumbDir);

            using var image = SixLabors.ImageSharp.Image.Load<Rgba32>(imagePath);

            // Scale down to max 200px on longest side
            int maxDim = Math.Max(image.Width, image.Height);
            if (maxDim > ThumbnailMaxDimension)
            {
                float scale = (float)ThumbnailMaxDimension / maxDim;
                int newWidth = (int)(image.Width * scale);
                int newHeight = (int)(image.Height * scale);
                image.Mutate(x => x.Resize(newWidth, newHeight));
            }

            image.SaveAsJpeg(thumbPath, new JpegEncoder { Quality = 80 });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"UserImageManager: Thumbnail error for {imagePath}: {ex.Message}");
        }
    }
}
