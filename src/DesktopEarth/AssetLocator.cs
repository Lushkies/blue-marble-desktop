namespace DesktopEarth;

public class AssetLocator
{
    public string TexturesDir { get; }
    public string AssetsDir { get; }

    public AssetLocator()
    {
        string exeDir = AppContext.BaseDirectory;
        string candidate = Path.Combine(exeDir, "assets", "textures");

        if (!Directory.Exists(candidate))
        {
            string? dir = exeDir;
            while (dir != null)
            {
                candidate = Path.Combine(dir, "assets", "textures");
                if (Directory.Exists(candidate))
                    break;
                dir = Directory.GetParent(dir)?.FullName;
            }
        }

        if (!Directory.Exists(candidate))
            throw new DirectoryNotFoundException(
                $"Could not find assets/textures directory. Searched from: {exeDir}");

        TexturesDir = candidate;
        AssetsDir = Directory.GetParent(candidate)!.FullName;
    }

    public string GetDayTexturePath(ImageStyle style = ImageStyle.Topo)
    {
        int month = DateTime.UtcNow.Month;
        string monthStr = month.ToString("D2");

        if (style == ImageStyle.TopoBathy)
        {
            string bathyPattern = $"world.topo.bathy.2004{monthStr}*";
            var bathyMatches = Directory.GetFiles(TexturesDir, bathyPattern);
            if (bathyMatches.Length > 0) return bathyMatches[0];
        }

        string topoPattern = $"world.topo.2004{monthStr}*";
        var topoMatches = Directory.GetFiles(TexturesDir, topoPattern);
        if (topoMatches.Length > 0) return topoMatches[0];

        // Fallback: try bathy for current month
        string fallbackPattern = $"world.topo.bathy.2004{monthStr}*";
        var fallbackMatches = Directory.GetFiles(TexturesDir, fallbackPattern);
        if (fallbackMatches.Length > 0) return fallbackMatches[0];

        // Last resort: any topo texture
        var anyMatches = Directory.GetFiles(TexturesDir, "world.topo.*");
        return anyMatches.Length > 0
            ? anyMatches[0]
            : throw new FileNotFoundException("No earth day texture found");
    }

    public string GetNightTexturePath()
    {
        string[] candidates = ["land_ocean_ice_lights_8192.jpg", "land_lights_8192.jpg", "nightearth_8192.jpg"];
        foreach (var name in candidates)
        {
            string path = Path.Combine(TexturesDir, name);
            if (File.Exists(path)) return path;
        }
        throw new FileNotFoundException("No night lights texture found");
    }

    public string GetMoonTexturePath()
    {
        string path = Path.Combine(TexturesDir, "moon_8192.jpg");
        return File.Exists(path) ? path : throw new FileNotFoundException("No moon texture found");
    }

    public string? GetBathyMaskPath()
    {
        string path = Path.Combine(TexturesDir, "bathy_mask_8192.jpg");
        return File.Exists(path) ? path : null;
    }
}
