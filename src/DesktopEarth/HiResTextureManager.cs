namespace DesktopEarth;

/// <summary>
/// Manages downloading and storing high-resolution NASA textures.
/// Textures are stored in %AppData%/BlueMarbleDesktop/textures_hd/.
/// The app ships with standard 8192x4096 textures and can optionally
/// download 21600x10800 (day) and 13500x6750 (night) versions.
/// </summary>
public class HiResTextureManager
{
    private static readonly string HdTextureDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "BlueMarbleDesktop", "textures_hd");

    // NASA Blue Marble Next Generation - 21600x10800 topo+bathymetry (12 months)
    private static readonly string[] DayBathyUrls = Enumerable.Range(1, 12)
        .Select(m => $"https://eoimages.gsfc.nasa.gov/images/imagerecords/73000/73776/world.topo.bathy.{2004}{m:D2}.3x21600x10800.jpg")
        .ToArray();

    // NASA Blue Marble Next Generation - 21600x10800 topo only (12 months)
    private static readonly string[] DayTopoUrls = Enumerable.Range(1, 12)
        .Select(m => $"https://eoimages.gsfc.nasa.gov/images/imagerecords/73000/73751/world.topo.{2004}{m:D2}.3x21600x10800.jpg")
        .ToArray();

    // NASA Black Marble 2016 - VIIRS nighttime lights (3km resolution, 13500x6750)
    private const string NightLightsUrl =
        "https://eoimages.gsfc.nasa.gov/images/imagerecords/144000/144898/BlackMarble_2016_3km.jpg";

    private CancellationTokenSource? _cts;
    private Task? _downloadTask;

    /// <summary>Progress 0.0 to 1.0, and status message.</summary>
    public event Action<float, string>? ProgressChanged;

    /// <summary>Fired when all downloads complete (or fail).</summary>
    public event Action<bool, string>? DownloadCompleted;

    /// <summary>Whether hi-res textures have been fully downloaded.</summary>
    public static bool AreHiResTexturesAvailable()
    {
        if (!Directory.Exists(HdTextureDir)) return false;

        // Check that at least the night texture and one month of day textures exist
        string nightPath = Path.Combine(HdTextureDir, "BlackMarble_2016_3km.jpg");
        string dayPath = Path.Combine(HdTextureDir, "world.topo.bathy.200401.3x21600x10800.jpg");
        return File.Exists(nightPath) && File.Exists(dayPath);
    }

    /// <summary>Returns the hi-res texture directory, or null if not available.</summary>
    public static string? GetHiResTextureDir()
    {
        return AreHiResTexturesAvailable() ? HdTextureDir : null;
    }

    /// <summary>Get the total download size estimate in MB.</summary>
    public static int GetEstimatedDownloadSizeMB()
    {
        // 24 day textures × ~40 MB each + 1 night texture × ~15 MB ≈ 975 MB
        return 975;
    }

    /// <summary>Start downloading hi-res textures in the background.</summary>
    public void StartDownload()
    {
        if (_downloadTask != null && !_downloadTask.IsCompleted)
            return; // Already downloading

        _cts = new CancellationTokenSource();
        _downloadTask = Task.Run(() => DownloadAllAsync(_cts.Token));
    }

    /// <summary>Cancel an in-progress download.</summary>
    public void CancelDownload()
    {
        _cts?.Cancel();
    }

    /// <summary>Whether a download is currently in progress.</summary>
    public bool IsDownloading => _downloadTask != null && !_downloadTask.IsCompleted;

    private async Task DownloadAllAsync(CancellationToken ct)
    {
        try
        {
            Directory.CreateDirectory(HdTextureDir);

            // Total files: 12 topo + 12 topo-bathy + 1 night = 25
            int totalFiles = DayTopoUrls.Length + DayBathyUrls.Length + 1;
            int completed = 0;

            using var http = new HttpClient();
            http.Timeout = TimeSpan.FromMinutes(30); // Large files

            // Download night texture first (most impactful visual upgrade)
            await DownloadFileAsync(http, NightLightsUrl,
                Path.Combine(HdTextureDir, "BlackMarble_2016_3km.jpg"),
                "Night lights (Black Marble 2016)", ct);
            completed++;
            ReportProgress(completed, totalFiles, "Night lights downloaded");

            // Download topo+bathy day textures (12 months)
            for (int i = 0; i < DayBathyUrls.Length; i++)
            {
                ct.ThrowIfCancellationRequested();
                string fileName = $"world.topo.bathy.{2004}{(i + 1):D2}.3x21600x10800.jpg";
                string monthName = new DateTime(2004, i + 1, 1).ToString("MMMM");
                await DownloadFileAsync(http, DayBathyUrls[i],
                    Path.Combine(HdTextureDir, fileName),
                    $"Topo+Bathymetry {monthName}", ct);
                completed++;
                ReportProgress(completed, totalFiles, $"Topo+Bathymetry {monthName} downloaded");
            }

            // Download topo-only day textures (12 months)
            for (int i = 0; i < DayTopoUrls.Length; i++)
            {
                ct.ThrowIfCancellationRequested();
                string fileName = $"world.topo.{2004}{(i + 1):D2}.3x21600x10800.jpg";
                string monthName = new DateTime(2004, i + 1, 1).ToString("MMMM");
                await DownloadFileAsync(http, DayTopoUrls[i],
                    Path.Combine(HdTextureDir, fileName),
                    $"Topographic {monthName}", ct);
                completed++;
                ReportProgress(completed, totalFiles, $"Topographic {monthName} downloaded");
            }

            DownloadCompleted?.Invoke(true, "All HD textures downloaded successfully!");
        }
        catch (OperationCanceledException)
        {
            DownloadCompleted?.Invoke(false, "Download cancelled.");
        }
        catch (Exception ex)
        {
            DownloadCompleted?.Invoke(false, $"Download failed: {ex.Message}");
        }
    }

    private async Task DownloadFileAsync(HttpClient http, string url, string destPath,
        string description, CancellationToken ct)
    {
        // Skip if already downloaded
        if (File.Exists(destPath))
        {
            var fi = new FileInfo(destPath);
            if (fi.Length > 1_000_000) // At least 1 MB = valid file
                return;
        }

        ReportProgress(-1, -1, $"Downloading {description}...");

        string tempPath = destPath + ".tmp";
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        await using var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None);

        var buffer = new byte[81920]; // 80KB buffer
        int bytesRead;
        while ((bytesRead = await stream.ReadAsync(buffer, ct)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
        }

        fileStream.Close();

        // Atomic rename
        if (File.Exists(destPath)) File.Delete(destPath);
        File.Move(tempPath, destPath);
    }

    private void ReportProgress(int completed, int total, string message)
    {
        float progress = total > 0 ? (float)completed / total : 0;
        ProgressChanged?.Invoke(progress, message);
    }
}
