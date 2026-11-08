using System.Text.RegularExpressions;
using Aether.Core.Diagnostics;
using Aether.Services.Platform;
using Aether.Storage;
using Microsoft.Extensions.Logging;

namespace Aether.Services.Windows;

/// <summary>A zone in fractions (0–1) of a monitor's work area.</summary>
public sealed record Zone(double X, double Y, double Width, double Height);

public sealed record WindowLayout
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public List<Zone> Zones { get; init; } = [];
    public int Gap { get; init; } = 8;
    public bool BuiltIn { get; init; }
}

public sealed record WindowRule
{
    public required string Id { get; init; }
    public bool Enabled { get; init; } = true;
    public required string ProcessName { get; init; }
    public string? TitleContains { get; init; }
    public string? LayoutId { get; init; }
    public int? ZoneIndex { get; init; }
    public int MonitorIndex { get; init; }
    public bool? AlwaysOnTop { get; init; }
    public byte? Opacity { get; init; }
}

public sealed record WindowPlacement(string ProcessName, string Title, int Left, int Top, int Width, int Height);

public sealed record Workspace
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public List<WindowPlacement> Windows { get; init; } = [];
    public int MonitorCount { get; init; }
    public DateTimeOffset SavedAt { get; init; } = DateTimeOffset.UtcNow;
}

public static class BuiltinLayouts
{
    public static IReadOnlyList<WindowLayout> All { get; } =
    [
        new() { Id = "halves", Name = "Halves", BuiltIn = true, Zones = [new(0, 0, .5, 1), new(.5, 0, .5, 1)] },
        new() { Id = "thirds", Name = "Thirds", BuiltIn = true, Zones = [new(0, 0, 1 / 3d, 1), new(1 / 3d, 0, 1 / 3d, 1), new(2 / 3d, 0, 1 / 3d, 1)] },
        new() { Id = "grid", Name = "Grid 2×2", BuiltIn = true, Zones = [new(0, 0, .5, .5), new(.5, 0, .5, .5), new(0, .5, .5, .5), new(.5, .5, .5, .5)] },
        new() { Id = "priority", Name = "Priority", BuiltIn = true, Zones = [new(0, 0, 2 / 3d, 1), new(2 / 3d, 0, 1 / 3d, .5), new(2 / 3d, .5, 1 / 3d, .5)] },
        new() { Id = "focus", Name = "Focus", BuiltIn = true, Zones = [new(.15, .08, .7, .84)] },
        new() { Id = "wide-center", Name = "Wide center", BuiltIn = true, Zones = [new(0, 0, .25, 1), new(.25, 0, .5, 1), new(.75, 0, .25, 1)] },
        new() { Id = "rows", Name = "Rows", BuiltIn = true, Zones = [new(0, 0, 1, .5), new(0, .5, 1, .5)] },
    ];
}

/// <summary>Snapping presets, custom layouts, per-app rules and saved workspaces built on <see cref="IWindowManager"/>.</summary>
public sealed class WindowLayoutService : IDisposable
{
    private readonly IWindowManager _windows;
    private readonly IDocumentStore _store;
    private readonly ILogger<WindowLayoutService> _logger;
    private bool _rulesActive;

    public WindowLayoutService(IWindowManager windows, IDocumentStore store, ILogger<WindowLayoutService> logger)
    {
        _windows = windows; _store = store; _logger = logger;
    }

    public event Action? Changed;

    public IReadOnlyList<WindowLayout> GetLayouts() => BuiltinLayouts.All.Concat(_store.GetAll<WindowLayout>("window-layouts")).ToList();
    public WindowLayout? GetLayout(string id) => GetLayouts().FirstOrDefault(l => l.Id == id);

    public string ActiveLayoutId
    {
        get => _store.Get<Active>("window-state", "active")?.Id ?? "halves";
        set { _store.Put("window-state", "active", new Active(value)); Changed?.Invoke(); }
    }

    public void SaveLayout(WindowLayout layout)
    {
        if (layout.BuiltIn) throw new InvalidOperationException("Built-in layouts are read-only.");
        if (layout.Zones.Count == 0) throw new ArgumentException("A layout needs at least one zone.");
        if (layout.Zones.Any(z => z.X < 0 || z.Y < 0 || z.Width <= 0 || z.Height <= 0 || z.X + z.Width > 1.0001 || z.Y + z.Height > 1.0001))
            throw new ArgumentException("Zones must fit inside the screen.");
        _store.Put("window-layouts", layout.Id, layout);
        Changed?.Invoke();
    }

    public void DeleteLayout(string id) { _store.Delete("window-layouts", id); Changed?.Invoke(); }

    public static (int Left, int Top, int Width, int Height) ZoneRect(WorkArea area, Zone zone, int gap)
    {
        var left = area.Left + (int)Math.Round(zone.X * area.Width) + gap / 2;
        var top = area.Top + (int)Math.Round(zone.Y * area.Height) + gap / 2;
        var width = (int)Math.Round(zone.Width * area.Width) - gap;
        var height = (int)Math.Round(zone.Height * area.Height) - gap;
        return (left, top, Math.Max(100, width), Math.Max(80, height));
    }

    private WorkArea AreaFor(int monitorIndex)
    {
        var areas = _windows.GetWorkAreas().OrderByDescending(a => a.IsPrimary).ThenBy(a => a.Left).ToList();
        if (areas.Count == 0) throw new InvalidOperationException("No monitors were found.");
        return areas[Math.Clamp(monitorIndex, 0, areas.Count - 1)];
    }

    private WorkArea AreaContaining(nint handle)
    {
        var w = _windows.GetWindows().FirstOrDefault(x => x.Handle == handle);
        var areas = _windows.GetWorkAreas();
        if (w is null || areas.Count == 0) return AreaFor(0);
        var cx = w.Left + w.Width / 2;
        var cy = w.Top + w.Height / 2;
        return areas.FirstOrDefault(a => cx >= a.Left && cx < a.Left + a.Width && cy >= a.Top && cy < a.Top + a.Height) ?? AreaFor(0);
    }

    public OperationResult SnapForeground(int zoneIndex)
    {
        var handle = _windows.GetForegroundWindow();
        if (handle == 0) return OperationResult.Fail("No active window.");
        var layout = GetLayout(ActiveLayoutId) ?? BuiltinLayouts.All[0];
        if (zoneIndex < 0 || zoneIndex >= layout.Zones.Count) return OperationResult.Fail($"Layout '{layout.Name}' has {layout.Zones.Count} zones.");
        var r = ZoneRect(AreaContaining(handle), layout.Zones[zoneIndex], layout.Gap);
        _windows.Restore(handle);
        _windows.MoveWindow(handle, r.Left, r.Top, r.Width, r.Height);
        return OperationResult.Ok($"Snapped to zone {zoneIndex + 1}.");
    }

    /// <summary>Places the visible windows on the primary monitor into the layout's zones, most recent first.</summary>
    public OperationResult Arrange(string layoutId, int monitorIndex = 0)
    {
        var layout = GetLayout(layoutId);
        if (layout is null) return OperationResult.Fail("Layout not found.");
        var area = AreaFor(monitorIndex);
        var windows = _windows.GetWindows().Where(w => !w.IsMinimized).Take(layout.Zones.Count).ToList();
        for (var i = 0; i < windows.Count; i++)
        {
            var r = ZoneRect(area, layout.Zones[i], layout.Gap);
            _windows.Restore(windows[i].Handle);
            _windows.MoveWindow(windows[i].Handle, r.Left, r.Top, r.Width, r.Height);
        }
        ActiveLayoutId = layoutId;
        return OperationResult.Ok($"Arranged {windows.Count} window(s) in '{layout.Name}'.");
    }

    public OperationResult ToggleTopmostForeground()
    {
        var h = _windows.GetForegroundWindow();
        var w = _windows.GetWindows().FirstOrDefault(x => x.Handle == h);
        if (w is null) return OperationResult.Fail("No active window.");
        _windows.SetTopmost(h, !w.IsTopmost);
        return OperationResult.Ok(w.IsTopmost ? $"'{w.Title}' is no longer always on top." : $"'{w.Title}' is now always on top.");
    }

    public OperationResult AdjustForegroundOpacity(int delta)
    {
        var h = _windows.GetForegroundWindow();
        var w = _windows.GetWindows().FirstOrDefault(x => x.Handle == h);
        if (w is null) return OperationResult.Fail("No active window.");
        var value = (byte)Math.Clamp(w.Opacity + delta, 40, 255);
        _windows.SetOpacity(h, value);
        return OperationResult.Ok($"Opacity {value * 100 / 255}%");
    }

    // ── Rules ──
    public IReadOnlyList<WindowRule> GetRules() => _store.GetAll<WindowRule>("window-rules");
    public void SaveRule(WindowRule rule) { _store.Put("window-rules", rule.Id, rule); Changed?.Invoke(); }
    public void DeleteRule(string id) { _store.Delete("window-rules", id); Changed?.Invoke(); }

    public void StartRules()
    {
        if (_rulesActive) return;
        _windows.WindowShown += OnWindowShown;
        _rulesActive = true;
    }

    public void StopRules()
    {
        if (!_rulesActive) return;
        _windows.WindowShown -= OnWindowShown;
        _rulesActive = false;
    }

    private void OnWindowShown(WindowInfo w)
    {
        foreach (var rule in GetRules().Where(r => r.Enabled && Matches(r, w)))
        {
            try
            {
                if (rule.LayoutId is { } lid && rule.ZoneIndex is { } zi && GetLayout(lid) is { } layout && zi < layout.Zones.Count)
                {
                    var r = ZoneRect(AreaFor(rule.MonitorIndex), layout.Zones[zi], layout.Gap);
                    _windows.MoveWindow(w.Handle, r.Left, r.Top, r.Width, r.Height);
                }
                if (rule.AlwaysOnTop is { } top) _windows.SetTopmost(w.Handle, top);
                if (rule.Opacity is { } o) _windows.SetOpacity(w.Handle, o);
            }
            catch (Exception ex) { _logger.LogWarning(ex, "Window rule {Rule} failed", rule.Id); }
        }
    }

    public static bool Matches(WindowRule r, WindowInfo w)
    {
        var name = r.ProcessName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? r.ProcessName[..^4] : r.ProcessName;
        if (!w.ProcessName.Equals(name, StringComparison.OrdinalIgnoreCase)) return false;
        return r.TitleContains is not { Length: > 0 } t || w.Title.Contains(t, StringComparison.OrdinalIgnoreCase);
    }

    // ── Workspaces ──
    public IReadOnlyList<Workspace> GetWorkspaces() => _store.GetAll<Workspace>("workspaces");

    public Workspace CaptureWorkspace(string name)
    {
        var ws = new Workspace
        {
            Id = "ws-" + Guid.NewGuid().ToString("N")[..8], Name = name, MonitorCount = _windows.GetWorkAreas().Count,
            Windows = _windows.GetWindows().Where(w => !w.IsMinimized).Select(w => new WindowPlacement(w.ProcessName, w.Title, w.Left, w.Top, w.Width, w.Height)).ToList(),
        };
        _store.Put("workspaces", ws.Id, ws);
        Changed?.Invoke();
        return ws;
    }

    public OperationResult RestoreWorkspace(string id)
    {
        var ws = _store.Get<Workspace>("workspaces", id);
        if (ws is null) return OperationResult.Fail("Workspace not found.");
        var open = _windows.GetWindows().ToList();
        var used = new HashSet<nint>();
        var placed = 0;
        foreach (var p in ws.Windows)
        {
            var match = open.FirstOrDefault(w => !used.Contains(w.Handle) && w.ProcessName.Equals(p.ProcessName, StringComparison.OrdinalIgnoreCase) && w.Title == p.Title)
                        ?? open.FirstOrDefault(w => !used.Contains(w.Handle) && w.ProcessName.Equals(p.ProcessName, StringComparison.OrdinalIgnoreCase));
            if (match is null) continue;
            used.Add(match.Handle);
            _windows.Restore(match.Handle);
            _windows.MoveWindow(match.Handle, p.Left, p.Top, p.Width, p.Height);
            placed++;
        }
        var note = ws.MonitorCount != _windows.GetWorkAreas().Count ? " (monitor setup differs from when it was saved)" : "";
        return OperationResult.Ok($"Restored {placed} of {ws.Windows.Count} windows{note}.");
    }

    public void DeleteWorkspace(string id) { _store.Delete("workspaces", id); Changed?.Invoke(); }

    public void Dispose() => StopRules();

    private sealed record Active(string Id);
}
