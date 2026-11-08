using System.Text.Json.Nodes;
using Aether.Core.Configuration;

namespace Aether.WidgetEngine;

/// <summary>The built-in widget types. Each is backed by a real data source.</summary>
public static class WidgetCatalog
{
    private static SettingDefinition Bool(string key, string label, bool def) => new() { Key = key, Label = label, Type = SettingKind.Boolean, Default = def };
    private static SettingDefinition Text(string key, string label, string def, string? placeholder = null) => new() { Key = key, Label = label, Type = SettingKind.Text, Default = def, Placeholder = placeholder };
    private static SettingDefinition Choice(string key, string label, string def, params (string V, string L)[] options) =>
        new() { Key = key, Label = label, Type = SettingKind.Choice, Default = def, Options = options.Select(o => new SettingOption { Value = o.V, Label = o.L }).ToList() };
    private static SettingDefinition Number(string key, string label, double def, double min, double max, string? unit = null) =>
        new() { Key = key, Label = label, Type = SettingKind.Number, Default = def, Min = min, Max = max, Unit = unit };

    public static IReadOnlyList<WidgetDefinition> All { get; } =
    [
        new() { TypeId = "clock", Name = "Clock", Description = "Large time and date.", Glyph = "", Category = "Time", DefaultHeight = 150, DefaultRefreshSeconds = 1,
            Settings = [Bool("seconds", "Show seconds", false), Bool("hour24", "24-hour clock", true), Bool("showDate", "Show date", true)] },
        new() { TypeId = "calendar", Name = "Calendar", Description = "Month view with today highlighted.", Glyph = "", Category = "Time", DefaultWidth = 300, DefaultHeight = 300, DefaultRefreshSeconds = 60,
            Settings = [Choice("firstDay", "First day of week", "monday", ("monday", "Monday"), ("sunday", "Sunday"))] },
        new() { TypeId = "worldclocks", Name = "World clocks", Description = "Several time zones at a glance.", Glyph = "", Category = "Time", DefaultWidth = 300, DefaultHeight = 220, DefaultRefreshSeconds = 1,
            Settings = [Text("zones", "Time zones (comma separated IANA or Windows ids)", "UTC,America/New_York,Europe/London,Asia/Tokyo")] },
        new() { TypeId = "countdown", Name = "Countdown", Description = "Days, hours and minutes until an event.", Glyph = "", Category = "Time", DefaultRefreshSeconds = 1,
            Settings = [Text("title", "Event", "Launch day"), Text("target", "Date and time (yyyy-MM-dd HH:mm)", "2027-01-01 00:00")] },
        new() { TypeId = "focus", Name = "Focus timer", Description = "Pomodoro-style work and break cycles.", Glyph = "", Category = "Productivity", DefaultHeight = 200, DefaultRefreshSeconds = 1,
            Settings = [Number("work", "Focus length", 25, 1, 180, "min"), Number("break", "Break length", 5, 1, 60, "min")] },
        new() { TypeId = "weather", Name = "Weather", Description = "Current conditions and today's range.", Glyph = "", Category = "Information", DefaultHeight = 170, DefaultRefreshSeconds = 900,
            DataSource = "Open-Meteo (open-meteo.com) — no account required", RequiresNetwork = true,
            Settings = [Text("location", "City", "London"), Choice("units", "Units", "celsius", ("celsius", "Celsius"), ("fahrenheit", "Fahrenheit"))] },
        new() { TypeId = "system", Name = "System monitor", Description = "CPU, memory, GPU and network together.", Glyph = "", Category = "System", DefaultWidth = 320, DefaultHeight = 220,
            DataSource = "Windows performance counters" },
        new() { TypeId = "cpu", Name = "CPU usage", Description = "Processor load with a live sparkline.", Glyph = "", Category = "System", DataSource = "GetSystemTimes" },
        new() { TypeId = "ram", Name = "Memory usage", Description = "Physical memory in use.", Glyph = "", Category = "System", DataSource = "GlobalMemoryStatusEx" },
        new() { TypeId = "gpu", Name = "GPU usage", Description = "3D engine utilization where the driver exposes it.", Glyph = "", Category = "System", DataSource = "PDH 'GPU Engine' counters (Windows 10 1709+)" },
        new() { TypeId = "network", Name = "Network", Description = "Upload and download throughput.", Glyph = "", Category = "System", DataSource = "Network interface statistics" },
        new() { TypeId = "storage", Name = "Storage", Description = "Free space on each drive.", Glyph = "", Category = "System", DefaultHeight = 200, DefaultRefreshSeconds = 30, DataSource = "DriveInfo" },
        new() { TypeId = "battery", Name = "Battery", Description = "Charge level and power source.", Glyph = "", Category = "System", DefaultRefreshSeconds = 15, DataSource = "GetSystemPowerStatus" },
        new() { TypeId = "media", Name = "Media controls", Description = "Play, pause and skip whatever is playing.", Glyph = "", Category = "Media", DefaultWidth = 320, DefaultHeight = 150,
            DataSource = "Windows media session (GlobalSystemMediaTransportControls)" },
        new() { TypeId = "nowplaying", Name = "Music information", Description = "Track, artist and album of the current media session.", Glyph = "", Category = "Media", DefaultWidth = 320, DefaultHeight = 120,
            DataSource = "Windows media session (GlobalSystemMediaTransportControls)" },
        new() { TypeId = "launcher", Name = "App launcher", Description = "A grid of favorite applications.", Glyph = "", Category = "Productivity", DefaultWidth = 300, DefaultHeight = 180, DefaultRefreshSeconds = 3600,
            Settings = [Text("apps", "Application paths (one per line)", "notepad.exe\ncalc.exe\nexplorer.exe")] },
        new() { TypeId = "notes", Name = "Notes", Description = "A sticky note that saves as you type.", Glyph = "", Category = "Productivity", DefaultHeight = 220, DefaultRefreshSeconds = 3600,
            Settings = [Text("text", "Note", "")] },
        new() { TypeId = "todo", Name = "To-do list", Description = "Quick tasks with check-off.", Glyph = "", Category = "Productivity", DefaultHeight = 260, DefaultRefreshSeconds = 3600 },
        new() { TypeId = "clipboard", Name = "Clipboard history", Description = "Opens the Windows clipboard history (Win+V).", Glyph = "", Category = "Productivity", DefaultWidth = 220, DefaultHeight = 110, DefaultRefreshSeconds = 3600,
            DataSource = "Windows clipboard history (must be enabled in Settings ▸ System ▸ Clipboard)" },
    ];

    public static WidgetDefinition? Find(string typeId) => All.FirstOrDefault(d => d.TypeId == typeId);

    public static JsonObject DefaultSettings(WidgetDefinition d) => SettingValues.ResolveDefaults(d.Settings);
}
