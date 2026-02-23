using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace DesktopEarth;

public static partial class WallpaperSetter
{
    private const int SPI_SETDESKWALLPAPER = 0x0014;
    private const int SPIF_UPDATEINIFILE = 0x01;
    private const int SPIF_SENDCHANGE = 0x02;

    [LibraryImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SystemParametersInfo(int uAction, int uParam, string lpvParam, int fuWinIni);

    public static void SetWallpaper(string imagePath,
        MultiMonitorMode mode = MultiMonitorMode.SameForAll,
        WallpaperFitMode fitMode = WallpaperFitMode.Fill)
    {
        // Set wallpaper style in registry before applying
        SetWallpaperStyle(mode, fitMode);

        bool result = SystemParametersInfo(
            SPI_SETDESKWALLPAPER,
            0,
            imagePath,
            SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);

        if (!result)
        {
            int error = Marshal.GetLastWin32Error();
            Console.WriteLine($"Warning: Failed to set wallpaper (error {error}). Path: {imagePath}");
        }
    }

    private static void SetWallpaperStyle(MultiMonitorMode mode, WallpaperFitMode fitMode)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop", true);
            if (key == null) return;

            // Span across monitors always uses style 22 regardless of fit mode
            if (mode == MultiMonitorMode.SpanAcross)
            {
                key.SetValue("WallpaperStyle", "22");
                key.SetValue("TileWallpaper", "0");
                return;
            }

            // Map fit mode to Windows registry values
            switch (fitMode)
            {
                case WallpaperFitMode.Fit:
                    key.SetValue("WallpaperStyle", "6");
                    key.SetValue("TileWallpaper", "0");
                    break;
                case WallpaperFitMode.Stretch:
                    key.SetValue("WallpaperStyle", "2");
                    key.SetValue("TileWallpaper", "0");
                    break;
                case WallpaperFitMode.Tile:
                    key.SetValue("WallpaperStyle", "0");
                    key.SetValue("TileWallpaper", "1");
                    break;
                case WallpaperFitMode.Center:
                    key.SetValue("WallpaperStyle", "0");
                    key.SetValue("TileWallpaper", "0");
                    break;
                default: // Fill
                    key.SetValue("WallpaperStyle", "10");
                    key.SetValue("TileWallpaper", "0");
                    break;
            }
        }
        catch { /* Non-critical if style setting fails */ }
    }
}
