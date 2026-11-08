namespace Aether.Core.Abstractions;

public enum WallpaperFit { Fill, Fit, Stretch, Tile, Center, Span }

public sealed record MonitorInfo(string Id, string DisplayName, int Left, int Top, int Width, int Height, bool IsPrimary, double Scale)
{
    public string Resolution => $"{Width} × {Height}";
}

/// <summary>Desktop wallpaper control. Implemented with the documented IDesktopWallpaper COM interface.</summary>
public interface IWallpaperService
{
    bool IsSupported { get; }
    IReadOnlyList<MonitorInfo> GetMonitors();
    /// <summary>Returns the wallpaper file of a monitor (or the shared wallpaper when <paramref name="monitorId"/> is null).</summary>
    string? GetWallpaper(string? monitorId);
    /// <summary>Sets the wallpaper on one monitor, or on all monitors when <paramref name="monitorId"/> is null.</summary>
    void SetWallpaper(string? monitorId, string imagePath);
    WallpaperFit GetFit();
    void SetFit(WallpaperFit fit);
    /// <summary>Solid background color shown behind letterboxed wallpapers.</summary>
    void SetBackgroundColor(Configuration.ColorValue color);
}

public sealed record CursorScheme(string Name, bool IsSystem, IReadOnlyDictionary<string, string> Cursors);

public interface ICursorService
{
    IReadOnlyList<CursorScheme> GetSchemes();
    string? GetActiveSchemeName();
    /// <summary>Writes the scheme to HKCU\Control Panel\Cursors and reloads it (SPI_SETCURSORS).</summary>
    void ApplyScheme(CursorScheme scheme);
    int GetCursorSize();
    void SetCursorSize(int size);
}
