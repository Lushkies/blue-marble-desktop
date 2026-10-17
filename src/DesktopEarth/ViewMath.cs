namespace DesktopEarth;

/// <summary>
/// Maps the 1–100 zoom slider to the camera (distance from the globe's centre
/// in globe radii, and vertical field of view in degrees) and back.
///
/// The slider is uniform in how big the globe LOOKS: every step enlarges it by
/// the same ratio. The old mapping interpolated distance and field of view
/// separately, so the first quarter of the slider barely zoomed while the last
/// fifth zoomed 4.5x. The end points are unchanged (slider 1 = 6.0 / 60°,
/// slider 100 = 1.15 / 10°), so saved views at either extreme look identical.
/// </summary>
public static class ViewMath
{
    public const int ZoomMin = 1;
    public const int ZoomMax = 100;

    // Globe radius as a fraction of half the screen height at each end
    private static readonly double ScaleAtMin = ApparentScale(6.0, 60.0);
    private static readonly double ScaleAtMax = ApparentScale(1.15, 10.0);

    public static (float Distance, float FieldOfView) SliderToCamera(int slider)
    {
        double t = (Math.Clamp(slider, ZoomMin, ZoomMax) - ZoomMin) / (double)(ZoomMax - ZoomMin);
        double fov = 60.0 - 50.0 * t;
        double scale = ScaleAtMin * Math.Pow(ScaleAtMax / ScaleAtMin, t);

        // Invert ApparentScale for the distance: tan(asin(1/d)) = scale * tan(fov/2)
        double k = scale * Math.Tan(fov * Math.PI / 360.0);
        double distance = Math.Sqrt(1.0 + 1.0 / (k * k));
        return ((float)distance, (float)fov);
    }

    public static int CameraToSlider(float distance, float fieldOfView)
    {
        double scale = ApparentScale(distance, fieldOfView);
        if (double.IsNaN(scale) || scale <= ScaleAtMin) return ZoomMin;
        if (scale >= ScaleAtMax) return ZoomMax;
        double t = Math.Log(scale / ScaleAtMin) / Math.Log(ScaleAtMax / ScaleAtMin);
        return Math.Clamp((int)Math.Round(ZoomMin + t * (ZoomMax - ZoomMin)), ZoomMin, ZoomMax);
    }

    /// <summary>How much bigger the globe looks than at the widest zoom, e.g. 3.4.</summary>
    public static double Magnification(float distance, float fieldOfView) =>
        ApparentScale(distance, fieldOfView) / ScaleAtMin;

    /// <summary>
    /// Globe radius on screen as a fraction of half the screen height, for a
    /// camera at <paramref name="distance"/> radii with the given vertical FOV.
    /// </summary>
    private static double ApparentScale(double distance, double fovDegrees)
    {
        if (distance <= 1.0) return double.PositiveInfinity;
        double angularRadius = Math.Asin(1.0 / distance);
        return Math.Tan(angularRadius) / Math.Tan(fovDegrees * Math.PI / 360.0);
    }
}
