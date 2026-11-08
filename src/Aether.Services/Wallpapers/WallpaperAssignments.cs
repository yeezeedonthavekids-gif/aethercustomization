using Aether.Core.Abstractions;
using Aether.Storage;

namespace Aether.Services.Wallpapers;

public sealed record WallpaperAssignmentSet
{
    public Dictionary<string, string> PerMonitor { get; init; } = new();
    public WallpaperFit Fit { get; init; } = WallpaperFit.Fill;
}

/// <summary>Per-monitor wallpaper choices made in Personalization ▸ Wallpaper.</summary>
public sealed class WallpaperAssignments(IDocumentStore store)
{
    public WallpaperAssignmentSet Get() => store.Get<WallpaperAssignmentSet>("wallpaper", "assignments") ?? new();
    public void Save(WallpaperAssignmentSet set) => store.Put("wallpaper", "assignments", set);
}
