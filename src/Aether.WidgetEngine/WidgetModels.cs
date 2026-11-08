using System.Text.Json.Nodes;
using Aether.Core.Configuration;

namespace Aether.WidgetEngine;

public sealed record WidgetDefinition
{
    public required string TypeId { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    /// <summary>Segoe Fluent Icons glyph.</summary>
    public required string Glyph { get; init; }
    public string Category { get; init; } = "General";
    public double DefaultWidth { get; init; } = 280;
    public double DefaultHeight { get; init; } = 160;
    public double MinWidth { get; init; } = 160;
    public double MinHeight { get; init; } = 100;
    public int DefaultRefreshSeconds { get; init; } = 2;
    public List<SettingDefinition> Settings { get; init; } = [];
    /// <summary>Where the data comes from — shown to the user for transparency.</summary>
    public string DataSource { get; init; } = "Local";
    public bool RequiresNetwork { get; init; }
}

public sealed record WidgetAppearance
{
    public string Background { get; init; } = "#FF10162A";
    public double Opacity { get; init; } = 0.82;
    public bool Blur { get; init; } = true;
    public double CornerRadius { get; init; } = 18;
    public string FontFamily { get; init; } = "Segoe UI Variable";
    public string TextColor { get; init; } = "#FFF2F5FF";
    public string AccentColor { get; init; } = "#FF7C8CFF";
    public bool ShowBorder { get; init; } = true;
}

public sealed record WidgetInstance
{
    public required string Id { get; init; }
    public required string TypeId { get; init; }
    public double X { get; init; }
    public double Y { get; init; }
    public double Width { get; init; }
    public double Height { get; init; }
    /// <summary>Monitor device id the position is relative to; null = primary.</summary>
    public string? MonitorId { get; init; }
    public WidgetAppearance Appearance { get; init; } = new();
    public int RefreshSeconds { get; init; } = 2;
    public bool AlwaysOnTop { get; init; }
    public bool Visible { get; init; } = true;
    public bool Locked { get; init; }
    public JsonObject Settings { get; init; } = [];
}

/// <summary>A named arrangement of widgets (a "workspace" or "profile").</summary>
public sealed record WidgetLayout
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public List<WidgetInstance> Widgets { get; init; } = [];
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;
}
