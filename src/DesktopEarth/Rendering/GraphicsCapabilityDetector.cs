using System.Runtime.InteropServices;

namespace DesktopEarth.Rendering;

/// <summary>
/// Detects graphics capabilities and whether Mesa3D fallback is needed.
/// On ARM64 devices, OpenGL drivers may not be available natively,
/// so we bundle Mesa3D (software renderer) as a fallback.
/// </summary>
public static class GraphicsCapabilityDetector
{
    public static bool IsArm64 =>
        RuntimeInformation.ProcessArchitecture == Architecture.Arm64;

    /// <summary>
    /// Checks if Mesa3D opengl32.dll is present alongside the exe.
    /// </summary>
    public static bool IsMesaAvailable()
    {
        string mesaPath = Path.Combine(AppContext.BaseDirectory, "opengl32.dll");
        return File.Exists(mesaPath);
    }

    /// <summary>
    /// Logs the current graphics environment for diagnostics.
    /// </summary>
    public static string GetDiagnostics()
    {
        var lines = new List<string>
        {
            $"Architecture: {RuntimeInformation.ProcessArchitecture}",
            $"OS: {RuntimeInformation.OSDescription}",
            $"Is ARM64: {IsArm64}",
            $"Mesa3D available: {IsMesaAvailable()}"
        };

        if (IsArm64 && !IsMesaAvailable())
        {
            lines.Add("WARNING: Running on ARM64 without Mesa3D fallback.");
            lines.Add("  If rendering fails, place opengl32.dll (Mesa3D) next to BlueMarbleDesktop.exe");
        }

        return string.Join(Environment.NewLine, lines);
    }
}
