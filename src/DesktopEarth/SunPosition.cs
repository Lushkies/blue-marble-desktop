namespace DesktopEarth;

/// <summary>
/// Calculates the sun's position (latitude/longitude of the subsolar point)
/// based on the current UTC time, using a simplified astronomical algorithm.
/// </summary>
public static class SunPosition
{
    public static (double Latitude, double Longitude) GetSubsolarPoint(DateTime utcNow)
    {
        // Julian date calculation
        double jd = ToJulianDate(utcNow);
        double n = jd - 2451545.0; // Days since J2000.0 epoch

        // Mean longitude of the sun (degrees)
        double L = (280.460 + 0.9856474 * n) % 360.0;
        if (L < 0) L += 360.0;

        // Mean anomaly of the sun (degrees)
        double g = (357.528 + 0.9856003 * n) % 360.0;
        if (g < 0) g += 360.0;

        double gRad = g * Math.PI / 180.0;

        // Ecliptic longitude (degrees)
        double lambda = L + 1.915 * Math.Sin(gRad) + 0.020 * Math.Sin(2.0 * gRad);

        // Obliquity of the ecliptic (degrees)
        double epsilon = 23.439 - 0.0000004 * n;

        double lambdaRad = lambda * Math.PI / 180.0;
        double epsilonRad = epsilon * Math.PI / 180.0;

        // Sun's declination (latitude of subsolar point)
        double sinDec = Math.Sin(epsilonRad) * Math.Sin(lambdaRad);
        double declination = Math.Asin(sinDec) * 180.0 / Math.PI;

        // Right ascension
        double ra = Math.Atan2(Math.Cos(epsilonRad) * Math.Sin(lambdaRad), Math.Cos(lambdaRad));
        ra = ra * 180.0 / Math.PI;
        if (ra < 0) ra += 360.0;

        // Greenwich Mean Sidereal Time (degrees)
        double gmst = (280.46061837 + 360.98564736629 * n) % 360.0;
        if (gmst < 0) gmst += 360.0;

        // Subsolar longitude
        double longitude = ra - gmst;
        if (longitude > 180.0) longitude -= 360.0;
        if (longitude < -180.0) longitude += 360.0;

        return (declination, longitude);
    }

    /// <summary>
    /// Returns a normalized direction vector from the center of the earth toward the sun.
    /// X = toward 0 longitude, Y = toward north pole, Z = toward 90E longitude.
    /// </summary>
    public static (float X, float Y, float Z) GetSunDirection(DateTime utcNow)
    {
        var (lat, lon) = GetSubsolarPoint(utcNow);
        double latRad = lat * Math.PI / 180.0;
        double lonRad = lon * Math.PI / 180.0;

        float x = (float)(Math.Cos(latRad) * Math.Cos(lonRad));
        float y = (float)(Math.Sin(latRad));
        float z = (float)(Math.Cos(latRad) * Math.Sin(lonRad));

        return (x, y, z);
    }

    private static double ToJulianDate(DateTime utc)
    {
        int y = utc.Year;
        int m = utc.Month;
        double d = utc.Day + utc.Hour / 24.0 + utc.Minute / 1440.0 + utc.Second / 86400.0;

        if (m <= 2)
        {
            y -= 1;
            m += 12;
        }

        int A = y / 100;
        int B = 2 - A + A / 4;

        return (int)(365.25 * (y + 4716)) + (int)(30.6001 * (m + 1)) + d + B - 1524.5;
    }
}
