namespace Aether.ThemeEngine;

public static class BuiltinPalettes
{
    public static AppPalette Midnight { get; } = new()
    {
        Id = "midnight", Name = "Midnight", Background = "#FF070B16", BackgroundGradient = "#FF151B3A",
        Surface = "#FF10162A", SurfaceElevated = "#FF182041", Border = "#26FFFFFF",
        TextPrimary = "#FFF2F5FF", TextSecondary = "#FFB4BDD8", TextMuted = "#FF6E7899",
        Accent = "#FF7C8CFF", AccentSecondary = "#FFB37CFF",
    };

    public static AppPalette Obsidian { get; } = new()
    {
        Id = "obsidian", Name = "Obsidian", Background = "#FF050505", BackgroundGradient = "#FF141414",
        Surface = "#FF111111", SurfaceElevated = "#FF1A1A1A", Border = "#1FFFFFFF",
        TextPrimary = "#FFF5F5F5", TextSecondary = "#FFB3B3B3", TextMuted = "#FF6B6B6B",
        Accent = "#FFE4E4E7", AccentSecondary = "#FF8E8E96", GlassOpacity = 0.75,
    };

    public static AppPalette Frost { get; } = new()
    {
        Id = "frost", Name = "Frost", Background = "#FF0A1620", BackgroundGradient = "#FF16324A",
        Surface = "#FF0F2230", SurfaceElevated = "#FF16303F", Border = "#33BFE9FF",
        TextPrimary = "#FFEFFAFF", TextSecondary = "#FFB2D3E3", TextMuted = "#FF6F95A8",
        Accent = "#FF5CD6FF", AccentSecondary = "#FFA6F0FF", GlassOpacity = 0.5,
    };

    public static AppPalette Aurora { get; } = new()
    {
        Id = "aurora", Name = "Aurora", Background = "#FF061412", BackgroundGradient = "#FF1B1240",
        Surface = "#FF0C1D1F", SurfaceElevated = "#FF132A2C", Border = "#2E7CFFD5",
        TextPrimary = "#FFF0FFF9", TextSecondary = "#FFB0D8CC", TextMuted = "#FF6A9488",
        Accent = "#FF3DF5B5", AccentSecondary = "#FF9B6BFF",
    };

    public static AppPalette Nord { get; } = new()
    {
        Id = "nord", Name = "Nord", Background = "#FF242933", BackgroundGradient = "#FF2E3440",
        Surface = "#FF2E3440", SurfaceElevated = "#FF3B4252", Border = "#33D8DEE9",
        TextPrimary = "#FFECEFF4", TextSecondary = "#FFD8DEE9", TextMuted = "#FF8892A6",
        Accent = "#FF88C0D0", AccentSecondary = "#FF81A1C1", Success = "#FFA3BE8C", Warning = "#FFEBCB8B", Danger = "#FFBF616A",
    };

    public static AppPalette Dracula { get; } = new()
    {
        Id = "dracula", Name = "Dracula", Background = "#FF1E1F29", BackgroundGradient = "#FF282A36",
        Surface = "#FF282A36", SurfaceElevated = "#FF343746", Border = "#336272A4",
        TextPrimary = "#FFF8F8F2", TextSecondary = "#FFCFCFE6", TextMuted = "#FF6272A4",
        Accent = "#FFBD93F9", AccentSecondary = "#FFFF79C6", Success = "#FF50FA7B", Warning = "#FFF1FA8C", Danger = "#FFFF5555",
    };

    public static AppPalette Cyberpunk { get; } = new()
    {
        Id = "cyberpunk", Name = "Cyberpunk", Background = "#FF0B0614", BackgroundGradient = "#FF2A0A3D",
        Surface = "#FF140A24", SurfaceElevated = "#FF1E0F36", Border = "#40FF2BD6",
        TextPrimary = "#FFFDF6FF", TextSecondary = "#FFE0B8F0", TextMuted = "#FF8D6AA3",
        Accent = "#FFFCEE0A", AccentSecondary = "#FFFF2BD6", Success = "#FF00FFC6", Danger = "#FFFF3864",
    };

    public static AppPalette MinimalLight { get; } = new()
    {
        Id = "minimal-light", Name = "Minimal Light", IsDark = false, Background = "#FFF4F6FB", BackgroundGradient = "#FFE6EAF5",
        Surface = "#FFFFFFFF", SurfaceElevated = "#FFF9FAFD", Border = "#1A0B1530",
        TextPrimary = "#FF0E1424", TextSecondary = "#FF4A5370", TextMuted = "#FF8A91A8",
        Accent = "#FF4F5BFF", AccentSecondary = "#FF8B5CF6", Success = "#FF0E9F6E", Warning = "#FFD97706", Danger = "#FFE11D48",
        GlassOpacity = 0.8,
    };

    public static IReadOnlyList<AppPalette> All { get; } = [Midnight, Obsidian, Frost, Aurora, Nord, Dracula, Cyberpunk, MinimalLight];
}

public static class BuiltinThemes
{
    private static ThemePack Make(AppPalette p, string description, WindowsPersonalization windows, string? cursor = null) => new()
    {
        Id = p.Id, Name = p.Name, Description = description, Author = "AETHER", BuiltIn = true, Palette = p, Windows = windows,
        CursorScheme = cursor,
        CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), UpdatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
    };

    private static WindowsPersonalization Dark(string accent) => new() { AppsMode = ThemeMode.Dark, SystemMode = ThemeMode.Dark, Transparency = true, AccentColor = accent };

    public static IReadOnlyList<ThemePack> All { get; } =
    [
        Make(BuiltinPalettes.Midnight, "Deep navy glass with indigo light. The signature AETHER look.", Dark("#FF7C8CFF")),
        Make(BuiltinPalettes.Obsidian, "Pure black, monochrome and quiet.", Dark("#FF8E8E96")),
        Make(BuiltinPalettes.Frost, "Glacial blues and crisp, airy glass.", Dark("#FF5CD6FF")),
        Make(BuiltinPalettes.Aurora, "Northern-lights greens drifting into violet.", Dark("#FF3DF5B5")),
        Make(BuiltinPalettes.Nord, "The arctic, north-bluish Nord palette.", Dark("#FF88C0D0")),
        Make(BuiltinPalettes.Dracula, "The beloved dark theme with vivid purple and pink.", Dark("#FFBD93F9")),
        Make(BuiltinPalettes.Cyberpunk, "Neon yellow and magenta on a night-city void.", Dark("#FFFCEE0A")),
        Make(BuiltinPalettes.MinimalLight, "Bright, calm and distraction free.",
            new WindowsPersonalization { AppsMode = ThemeMode.Light, SystemMode = ThemeMode.Light, Transparency = true, AccentColor = "#FF4F5BFF" }),
    ];
}
