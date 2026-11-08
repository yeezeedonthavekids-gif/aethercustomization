using System.Globalization;
using System.Text.Json;
using Aether.WidgetEngine;

namespace Aether.Services.Wallpapers;

/// <summary>Weather from Open-Meteo (https://open-meteo.com) — free, no API key, CC BY 4.0 data.</summary>
public sealed class OpenMeteoWeatherProvider(HttpClient http) : IWeatherProvider
{
    public string Attribution => "Weather data by Open-Meteo.com";

    public async Task<GeoLocation?> FindLocationAsync(string query, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query)) return null;
        var json = await http.GetStringAsync($"https://geocoding-api.open-meteo.com/v1/search?count=1&language=en&format=json&name={Uri.EscapeDataString(query.Trim())}", ct).ConfigureAwait(false);
        return ParseLocation(json);
    }

    public static GeoLocation? ParseLocation(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("results", out var results) || results.GetArrayLength() == 0) return null;
        var r = results[0];
        return new GeoLocation(r.GetProperty("name").GetString() ?? "", r.TryGetProperty("country", out var c) ? c.GetString() ?? "" : "",
            r.GetProperty("latitude").GetDouble(), r.GetProperty("longitude").GetDouble());
    }

    public async Task<WeatherInfo> GetCurrentAsync(GeoLocation location, bool fahrenheit, CancellationToken ct = default)
    {
        var url = string.Create(CultureInfo.InvariantCulture,
            $"https://api.open-meteo.com/v1/forecast?latitude={location.Latitude}&longitude={location.Longitude}&current=temperature_2m,relative_humidity_2m,weather_code,wind_speed_10m&daily=temperature_2m_max,temperature_2m_min&timezone=auto&forecast_days=1{(fahrenheit ? "&temperature_unit=fahrenheit&wind_speed_unit=mph" : "")}");
        return ParseForecast(await http.GetStringAsync(url, ct).ConfigureAwait(false), location.Name);
    }

    public static WeatherInfo ParseForecast(string json, string locationName)
    {
        using var doc = JsonDocument.Parse(json);
        var current = doc.RootElement.GetProperty("current");
        var daily = doc.RootElement.GetProperty("daily");
        var code = current.GetProperty("weather_code").GetInt32();
        var (condition, glyph) = Describe(code);
        return new WeatherInfo(locationName, current.GetProperty("temperature_2m").GetDouble(),
            daily.GetProperty("temperature_2m_max")[0].GetDouble(), daily.GetProperty("temperature_2m_min")[0].GetDouble(),
            code, condition, glyph, current.GetProperty("wind_speed_10m").GetDouble(), current.GetProperty("relative_humidity_2m").GetInt32(),
            DateTimeOffset.Now);
    }

    /// <summary>WMO weather interpretation codes.</summary>
    public static (string Condition, string Glyph) Describe(int code) => code switch
    {
        0 => ("Clear", "\uE706"),
        1 or 2 => ("Partly cloudy", "\uE9A9"),
        3 => ("Overcast", "\uE753"),
        45 or 48 => ("Fog", "\uE9CB"),
        51 or 53 or 55 or 56 or 57 => ("Drizzle", "\uE9C6"),
        61 or 63 or 65 or 66 or 67 or 80 or 81 or 82 => ("Rain", "\uE9C4"),
        71 or 73 or 75 or 77 or 85 or 86 => ("Snow", "\uE9C8"),
        95 or 96 or 99 => ("Thunderstorm", "\uE9C6"),
        _ => ("Unknown", "\uE753"),
    };
}
