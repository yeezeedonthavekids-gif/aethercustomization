namespace Aether.WidgetEngine;

public sealed record MemoryInfo(ulong UsedBytes, ulong TotalBytes)
{
    public double Percent => TotalBytes == 0 ? 0 : 100.0 * UsedBytes / TotalBytes;
}

public sealed record NetworkThroughput(double BytesSentPerSecond, double BytesReceivedPerSecond);

public sealed record DriveUsage(string Name, string Label, ulong TotalBytes, ulong FreeBytes)
{
    public double PercentUsed => TotalBytes == 0 ? 0 : 100.0 * (TotalBytes - FreeBytes) / TotalBytes;
}

public sealed record BatteryInfo(bool Present, int Percent, bool Charging, bool OnAcPower, TimeSpan? Remaining);

/// <summary>Live system metrics. Implementations sample on demand — there is no background polling.</summary>
public interface ISystemMetricsProvider
{
    /// <summary>CPU usage 0–100 since the previous call.</summary>
    double GetCpuUsage();
    MemoryInfo GetMemory();
    /// <summary>GPU 3D utilization 0–100, or null when the counters are unavailable.</summary>
    double? GetGpuUsage();
    NetworkThroughput GetNetwork();
    IReadOnlyList<DriveUsage> GetDrives();
    BatteryInfo GetBattery();
}

public enum MediaPlaybackState { None, Playing, Paused, Stopped }

public sealed record MediaInfo(string Title, string Artist, string Album, string SourceApp, MediaPlaybackState State, byte[]? Thumbnail);

public interface IMediaSessionProvider
{
    bool IsSupported { get; }
    Task<MediaInfo?> GetCurrentAsync(CancellationToken ct = default);
    Task<bool> PlayPauseAsync(CancellationToken ct = default);
    Task<bool> NextAsync(CancellationToken ct = default);
    Task<bool> PreviousAsync(CancellationToken ct = default);
}

public sealed record WeatherInfo(string Location, double Temperature, double High, double Low, int WeatherCode, string Condition, string Glyph, double WindSpeed, int Humidity, DateTimeOffset ObservedAt);

public sealed record GeoLocation(string Name, string Country, double Latitude, double Longitude);

public interface IWeatherProvider
{
    string Attribution { get; }
    Task<GeoLocation?> FindLocationAsync(string query, CancellationToken ct = default);
    Task<WeatherInfo> GetCurrentAsync(GeoLocation location, bool fahrenheit, CancellationToken ct = default);
}
