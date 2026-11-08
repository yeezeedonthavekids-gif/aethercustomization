using Aether.Core.Abstractions;
using Aether.Core.Configuration;
using Aether.Core.Diagnostics;
using Aether.ModEngine.Execution;
using Aether.ModEngine.Manifest;
using Aether.Services.Platform;
using Aether.Services.Wallpapers;
using Aether.Storage;

namespace Aether.Services.Mods.Builtin.Components;

/// <summary>Remembers wallpapers before a mod changes them so they can be put back exactly.</summary>
internal sealed class WallpaperMemento(IDocumentStore store, string owner)
{
    private sealed record Saved(Dictionary<string, string> PerMonitor, WallpaperFit Fit);

    public void CaptureIfMissing(IWallpaperService wallpaper)
    {
        if (store.Get<Saved>("wallpaper-memento", owner) is not null) return;
        var map = new Dictionary<string, string>();
        foreach (var m in wallpaper.GetMonitors())
            if (wallpaper.GetWallpaper(m.Id) is { Length: > 0 } path) map[m.Id] = path;
        store.Put("wallpaper-memento", owner, new Saved(map, wallpaper.GetFit()));
    }

    public void Restore(IWallpaperService wallpaper)
    {
        var saved = store.Get<Saved>("wallpaper-memento", owner);
        if (saved is null) return;
        var monitors = wallpaper.GetMonitors().Select(m => m.Id).ToHashSet();
        foreach (var (monitor, path) in saved.PerMonitor)
            if (monitors.Contains(monitor) && File.Exists(path)) wallpaper.SetWallpaper(monitor, path);
        wallpaper.SetFit(saved.Fit);
        store.Delete("wallpaper-memento", owner);
    }
}

public static class WallpaperFiles
{
    public static readonly string[] Extensions = [".jpg", ".jpeg", ".png", ".bmp", ".webp", ".jfif"];

    public static IReadOnlyList<string> Enumerate(string folder) =>
        Directory.Exists(folder)
            ? Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                .Where(f => Extensions.Contains(Path.GetExtension(f).ToLowerInvariant())).Order(StringComparer.OrdinalIgnoreCase).ToList()
            : [];

    public static WallpaperFit ParseFit(string value) => value switch
    {
        "fit" => WallpaperFit.Fit, "stretch" => WallpaperFit.Stretch, "center" => WallpaperFit.Center,
        "span" => WallpaperFit.Span, "tile" => WallpaperFit.Tile, _ => WallpaperFit.Fill,
    };
}

/// <summary>Mod 12 — rotates wallpapers from a folder.</summary>
public sealed class WallpaperSchedulerComponent(IDocumentStore store) : LoopingComponent
{
    private readonly WallpaperMemento _memento = new(store, "aether.wallpaper-scheduler");
    private readonly Random _random = new();
    private IWallpaperService? _wallpaper;
    private int _index;
    private string? _status;

    public override string? Status => _status;

    protected override TimeSpan Interval(ModComponentContext context) => TimeSpan.FromMinutes(Math.Max(1, context.Settings.GetDouble("intervalMinutes", 30)));

    public override Task StartAsync(ModComponentContext context, CancellationToken ct)
    {
        _wallpaper = context.GetService<IWallpaperService>(ModPermissions.Wallpaper);
        if (!_wallpaper.IsSupported) throw new PlatformNotSupportedException("Wallpaper control is not available.");
        var folder = context.Settings.GetString("folder");
        if (WallpaperFiles.Enumerate(folder).Count == 0)
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(folder) ? "Choose a wallpaper folder in the mod settings first." : $"No images were found in '{folder}'.");
        _memento.CaptureIfMissing(_wallpaper);
        Next(context);
        StartLoop(context);
        return Task.CompletedTask;
    }

    public override Task OnSettingsChangedAsync(ModComponentContext context, CancellationToken ct) { Next(context); return Task.CompletedTask; }
    protected override Task TickAsync(ModComponentContext context, CancellationToken ct) { Next(context); return Task.CompletedTask; }

    public override Task<OperationResult> InvokeActionAsync(ModComponentContext context, string actionId, CancellationToken ct)
    {
        if (_wallpaper is null) return Task.FromResult(OperationResult.Fail("Enable the scheduler first."));
        Next(context);
        return Task.FromResult(OperationResult.Ok(_status ?? "Wallpaper changed."));
    }

    private void Next(ModComponentContext context)
    {
        var files = WallpaperFiles.Enumerate(context.Settings.GetString("folder"));
        if (files.Count == 0 || _wallpaper is null) { _status = "No images found in the folder"; return; }
        _wallpaper.SetFit(WallpaperFiles.ParseFit(context.Settings.GetString("fit")));
        var shuffle = context.Settings.GetBool("shuffle", true);
        string Pick() => shuffle ? files[_random.Next(files.Count)] : files[_index++ % files.Count];
        var monitors = _wallpaper.GetMonitors();
        if (context.Settings.GetBool("perMonitor", true) && monitors.Count > 1)
            foreach (var m in monitors) _wallpaper.SetWallpaper(m.Id, Pick());
        else
            _wallpaper.SetWallpaper(null, Pick());
        _status = $"Changed at {DateTime.Now:HH:mm} · {files.Count} images";
    }

    public override async Task StopAsync(ModComponentContext context, CancellationToken ct)
    {
        await StopLoopAsync().ConfigureAwait(false);
        if (_wallpaper is not null) _memento.Restore(_wallpaper);
    }
}

/// <summary>Mod 18 — applies saved per-monitor wallpaper assignments and re-applies them when displays change.</summary>
public sealed class MultiMonitorWallpaperComponent(IDocumentStore store, WallpaperAssignments assignments) : LoopingComponent
{
    private readonly WallpaperMemento _memento = new(store, "aether.multimonitor-wallpaper");
    private IWallpaperService? _wallpaper;
    private string _lastTopology = "";
    private string? _status;
    public override string? Status => _status;

    protected override TimeSpan Interval(ModComponentContext context) => TimeSpan.FromSeconds(Math.Max(5, context.Settings.GetDouble("checkSeconds", 15)));

    public override Task StartAsync(ModComponentContext context, CancellationToken ct)
    {
        _wallpaper = context.GetService<IWallpaperService>(ModPermissions.Wallpaper);
        if (!_wallpaper.IsSupported) throw new PlatformNotSupportedException("Wallpaper control is not available.");
        if (assignments.Get().PerMonitor.Count == 0) throw new InvalidOperationException("Assign wallpapers to your monitors in Personalization ▸ Wallpaper first.");
        _memento.CaptureIfMissing(_wallpaper);
        ApplyAssignments();
        StartLoop(context);
        return Task.CompletedTask;
    }

    protected override Task TickAsync(ModComponentContext context, CancellationToken ct)
    {
        if (!context.Settings.GetBool("watchDisplays", true) || _wallpaper is null) return Task.CompletedTask;
        var topology = string.Join('|', _wallpaper.GetMonitors().Select(m => m.Id + m.Resolution));
        if (topology != _lastTopology) ApplyAssignments();
        return Task.CompletedTask;
    }

    public override Task<OperationResult> InvokeActionAsync(ModComponentContext context, string actionId, CancellationToken ct)
    {
        _wallpaper ??= context.GetService<IWallpaperService>(ModPermissions.Wallpaper);
        return Task.FromResult(ApplyAssignments());
    }

    private OperationResult ApplyAssignments()
    {
        var set = assignments.Get();
        var monitors = _wallpaper!.GetMonitors();
        var applied = 0;
        var missing = new List<string>();
        foreach (var m in monitors)
        {
            if (!set.PerMonitor.TryGetValue(m.Id, out var path)) continue;
            if (File.Exists(path)) { _wallpaper.SetWallpaper(m.Id, path); applied++; }
            else missing.Add(path);
        }
        _wallpaper.SetFit(set.Fit);
        _lastTopology = string.Join('|', monitors.Select(m => m.Id + m.Resolution));
        _status = $"{applied} of {monitors.Count} monitors assigned";
        return missing.Count == 0 ? OperationResult.Ok(_status) : OperationResult.Fail("Some wallpaper files are missing.", missing);
    }

    public override async Task StopAsync(ModComponentContext context, CancellationToken ct)
    {
        await StopLoopAsync().ConfigureAwait(false);
        if (_wallpaper is not null) _memento.Restore(_wallpaper);
    }
}
