namespace WallpaperScheduler.Core;

/// <summary>Offline sunrise/sunset using the NOAA solar algorithm.</summary>
public static class SolarCalculator
{
    public static DateTime? Sunrise(DateOnly date, double lat, double lon, TimeZoneInfo tz) =>
        Compute(date, lat, lon, tz, rise: true);

    public static DateTime? Sunset(DateOnly date, double lat, double lon, TimeZoneInfo tz) =>
        Compute(date, lat, lon, tz, rise: false);

    private static DateTime? Compute(DateOnly date, double lat, double lon, TimeZoneInfo tz, bool rise)
    {
        double jd = JulianDay(date.Year, date.Month, date.Day);
        double minutes = EventMinutesUtc(jd, lat, lon, rise);
        if (double.IsNaN(minutes)) return null;

        // Refine using the estimated moment of the event.
        minutes = EventMinutesUtc(jd + minutes / 1440.0, lat, lon, rise);
        if (double.IsNaN(minutes)) return null;

        var utcMidnight = new DateTime(date.Year, date.Month, date.Day, 0, 0, 0, DateTimeKind.Utc);
        var utc = utcMidnight.AddMinutes(minutes);
        return TimeZoneInfo.ConvertTimeFromUtc(utc, tz);
    }

    private static double EventMinutesUtc(double jd, double lat, double lon, bool rise)
    {
        double t = (jd - 2451545.0) / 36525.0;
        double eqTime = EquationOfTime(t);
        double decl = Rad(SunDeclination(t));
        double latR = Rad(lat);

        double cosHa = Math.Cos(Rad(90.833)) / (Math.Cos(latR) * Math.Cos(decl)) - Math.Tan(latR) * Math.Tan(decl);
        if (cosHa < -1 || cosHa > 1) return double.NaN; // polar day / night

        double ha = Deg(Math.Acos(cosHa));
        if (!rise) ha = -ha;
        return 720.0 - 4.0 * (lon + ha) - eqTime;
    }

    private static double JulianDay(int y, int m, int d)
    {
        if (m <= 2) { y--; m += 12; }
        int a = y / 100;
        int b = 2 - a + a / 4;
        return Math.Floor(365.25 * (y + 4716)) + Math.Floor(30.6001 * (m + 1)) + d + b - 1524.5;
    }

    private static double EquationOfTime(double t)
    {
        double l0 = Rad(Mod(280.46646 + t * (36000.76983 + t * 0.0003032), 360));
        double m = Rad(357.52911 + t * (35999.05029 - 0.0001537 * t));
        double e = 0.016708634 - t * (0.000042037 + 0.0000001267 * t);
        double eps = Rad(ObliquityCorrected(t));
        double y = Math.Pow(Math.Tan(eps / 2), 2);
        double eq = y * Math.Sin(2 * l0) - 2 * e * Math.Sin(m) + 4 * e * y * Math.Sin(m) * Math.Cos(2 * l0)
                    - 0.5 * y * y * Math.Sin(4 * l0) - 1.25 * e * e * Math.Sin(2 * m);
        return Deg(eq) * 4.0;
    }

    private static double SunDeclination(double t)
    {
        double eps = Rad(ObliquityCorrected(t));
        double lambda = Rad(SunApparentLongitude(t));
        return Deg(Math.Asin(Math.Sin(eps) * Math.Sin(lambda)));
    }

    private static double SunApparentLongitude(double t)
    {
        double l0 = Mod(280.46646 + t * (36000.76983 + t * 0.0003032), 360);
        double m = Rad(357.52911 + t * (35999.05029 - 0.0001537 * t));
        double c = Math.Sin(m) * (1.914602 - t * (0.004817 + 0.000014 * t))
                   + Math.Sin(2 * m) * (0.019993 - 0.000101 * t) + Math.Sin(3 * m) * 0.000289;
        double trueLong = l0 + c;
        double omega = 125.04 - 1934.136 * t;
        return trueLong - 0.00569 - 0.00478 * Math.Sin(Rad(omega));
    }

    private static double ObliquityCorrected(double t)
    {
        double seconds = 21.448 - t * (46.815 + t * (0.00059 - t * 0.001813));
        double e0 = 23.0 + (26.0 + seconds / 60.0) / 60.0;
        double omega = 125.04 - 1934.136 * t;
        return e0 + 0.00256 * Math.Cos(Rad(omega));
    }

    private static double Mod(double x, double m) => x - m * Math.Floor(x / m);
    private static double Rad(double d) => d * Math.PI / 180.0;
    private static double Deg(double r) => r * 180.0 / Math.PI;
}
