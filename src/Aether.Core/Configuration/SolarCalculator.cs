namespace Aether.Core.Configuration;

/// <summary>
/// Sunrise/sunset times using the NOAA simplified solar position algorithm (accurate to about a minute
/// for latitudes between ±65°). Returns null on polar day/night.
/// </summary>
public static class SolarCalculator
{
    public static (DateTimeOffset? Sunrise, DateTimeOffset? Sunset) Calculate(DateOnly date, double latitude, double longitude, TimeSpan utcOffset)
    {
        var dayOfYear = date.DayOfYear;
        var gamma = 2 * Math.PI / 365 * (dayOfYear - 1);
        var eqTime = 229.18 * (0.000075 + 0.001868 * Math.Cos(gamma) - 0.032077 * Math.Sin(gamma)
                     - 0.014615 * Math.Cos(2 * gamma) - 0.040849 * Math.Sin(2 * gamma));
        var decl = 0.006918 - 0.399912 * Math.Cos(gamma) + 0.070257 * Math.Sin(gamma) - 0.006758 * Math.Cos(2 * gamma)
                   + 0.000907 * Math.Sin(2 * gamma) - 0.002697 * Math.Cos(3 * gamma) + 0.00148 * Math.Sin(3 * gamma);
        var latRad = latitude * Math.PI / 180;
        var zenith = 90.833 * Math.PI / 180;
        var cosHa = Math.Cos(zenith) / (Math.Cos(latRad) * Math.Cos(decl)) - Math.Tan(latRad) * Math.Tan(decl);
        if (cosHa is > 1 or < -1) return (null, null);
        var ha = Math.Acos(cosHa) * 180 / Math.PI;

        DateTimeOffset At(double minutesUtc)
        {
            var midnightUtc = new DateTimeOffset(date.Year, date.Month, date.Day, 0, 0, 0, TimeSpan.Zero);
            return midnightUtc.AddMinutes(minutesUtc).ToOffset(utcOffset);
        }
        var sunrise = 720 - 4 * (longitude + ha) - eqTime;
        var sunset = 720 - 4 * (longitude - ha) - eqTime;
        return (At(sunrise), At(sunset));
    }
}
