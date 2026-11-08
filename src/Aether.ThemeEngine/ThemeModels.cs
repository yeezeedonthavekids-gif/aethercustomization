using Aether.Core.Abstractions;
using Aether.Core.Configuration;

namespace Aether.ThemeEngine;

/// <summary>Colors used by the AETHER interface itself. Hex strings (#AARRGGBB or #RRGGBB).</summary>
public sealed record AppPalette
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public bool IsDark { get; init; } = true;
    public required string Background { get; init; }
    public required string BackgroundGradient { get; init; }
    public required string Surface { get; init; }
    public required string SurfaceElevated { get; init; }
    public required string Border { get; init; }
    public required string TextPrimary { get; init; }
    public required string TextSecondary { get; init; }
    public required string TextMuted { get; init; }
    public required string Accent { get; init; }
    public required string AccentSecondary { get; init; }
    public string Success { get; init; } = "#FF3DDC97";
    public string Warning { get; init; } = "#FFFFB547";
    public string Danger { get; init; } = "#FFFF5C7A";
    /// <summary>0–1 opacity of glass surfaces over the backdrop.</summary>
    public double GlassOpacity { get; init; } = 0.62;

    public AppPalette WithAccent(string accent)
    {
        var a = ColorValue.Parse(accent);
        var secondary = a.Lerp(IsDark ? new ColorValue(255, 255, 255, 255) : new ColorValue(255, 0, 0, 0), 0.25);
        return this with { Accent = a.ToHex(), AccentSecondary = secondary.ToHex() };
    }
}

public enum ThemeMode { Unchanged, Light, Dark }

/// <summary>Windows personalization a theme may set. Null members are left untouched.</summary>
public sealed record WindowsPersonalization
{
    public ThemeMode AppsMode { get; init; } = ThemeMode.Unchanged;
    public ThemeMode SystemMode { get; init; } = ThemeMode.Unchanged;
    public bool? Transparency { get; init; }
    public string? AccentColor { get; init; }
    public bool? AccentOnTaskbarAndStart { get; init; }
    public bool? AccentOnTitleBars { get; init; }
}

public sealed record ThemeWallpaper
{
    public string? Path { get; init; }
    public WallpaperFit Fit { get; init; } = WallpaperFit.Fill;
    /// <summary>Optional per-monitor wallpapers keyed by monitor device path.</summary>
    public Dictionary<string, string>? PerMonitor { get; init; }
}

public sealed record ThemeTypography
{
    public string FontFamily { get; init; } = "Segoe UI Variable";
    public double Scale { get; init; } = 1.0;
}

/// <summary>Mod configuration bundled with a theme (applied only when the user agrees).</summary>
public sealed record ThemeModConfiguration(string ModId, bool Enabled, System.Text.Json.Nodes.JsonObject Settings);

/// <summary>A complete desktop look: interface palette, Windows settings, wallpaper, cursor and profiles.</summary>
public sealed record ThemePack
{
    public const string FileExtension = ".aethertheme";
    public int Format { get; init; } = 1;
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string Description { get; init; } = "";
    public string Author { get; init; } = "You";
    public string Version { get; init; } = "1.0.0";
    public bool BuiltIn { get; init; }
    public required AppPalette Palette { get; init; }
    public ThemeTypography Typography { get; init; } = new();
    public WindowsPersonalization Windows { get; init; } = new();
    public ThemeWallpaper? Wallpaper { get; init; }
    public string? CursorScheme { get; init; }
    public string? TaskbarProfileId { get; init; }
    public string? StartProfileId { get; init; }
    public string? WidgetLayoutId { get; init; }
    public List<ThemeModConfiguration> ModConfigurations { get; init; } = [];
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;
}

public enum ScheduleMode { FixedTimes, SunriseSunset }

public sealed record ThemeSchedule
{
    public bool Enabled { get; init; }
    public ScheduleMode Mode { get; init; } = ScheduleMode.FixedTimes;
    public string LightThemeId { get; init; } = "minimal-light";
    public string DarkThemeId { get; init; } = "midnight";
    public string LightAt { get; init; } = "07:00";
    public string DarkAt { get; init; } = "19:00";
    public double Latitude { get; init; }
    public double Longitude { get; init; }
}

public sealed record ThemeAppliedEvent(ThemePack Theme);
