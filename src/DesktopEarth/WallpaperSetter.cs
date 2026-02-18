using System.Runtime.InteropServices;

namespace DesktopEarth;

public static partial class WallpaperSetter
{
    private const int SPI_SETDESKWALLPAPER = 0x0014;
    private const int SPIF_UPDATEINIFILE = 0x01;
    private const int SPIF_SENDCHANGE = 0x02;

    [LibraryImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SystemParametersInfo(int uAction, int uParam, string lpvParam, int fuWinIni);

    public static void SetWallpaper(string imagePath)
    {
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
}
