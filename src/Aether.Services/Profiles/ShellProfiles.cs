using Aether.Core.Diagnostics;
using Aether.Services.Platform;
using Aether.Services.Tweaks;
using Aether.Storage;

namespace Aether.Services.Profiles;

public enum DockStyle { Dock, Floating, Minimal, Compact, Launcher }
public enum DockEdge { Bottom, Top, Left, Right }

/// <summary>Settings of the AETHER-rendered dock/taskbar (an independent window — not a modified Windows taskbar).</summary>
public sealed record DockSettings
{
    public DockStyle Style { get; init; } = DockStyle.Dock;
    public DockEdge Edge { get; init; } = DockEdge.Bottom;
    public double IconSize { get; init; } = 44;
    public bool Magnify { get; init; } = true;
    public bool AutoHide { get; init; }
    public double Opacity { get; init; } = 0.78;
    public double CornerRadius { get; init; } = 22;
    public double Margin { get; init; } = 10;
    public bool ShowRunningApps { get; init; } = true;
    public bool ShowClock { get; init; }
    public bool ShowStartButton { get; init; } = true;
    public List<string> PinnedApps { get; init; } = [];
}

public sealed record TaskbarProfile
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string Description { get; init; } = "";
    public string Glyph { get; init; } = "";
    public bool BuiltIn { get; init; }
    /// <summary>Native Windows settings applied through the tweak engine (tweak id → option).</summary>
    public Dictionary<string, string> Native { get; init; } = [];
    /// <summary>Auto-hide the Windows taskbar (used when an AETHER dock replaces it visually).</summary>
    public bool? AutoHideWindowsTaskbar { get; init; }
    /// <summary>Null means no AETHER dock.</summary>
    public DockSettings? Dock { get; init; }
}

public enum LauncherStyle { Minimal, Classic, Modern, Productivity, Gaming, Spotlight }

/// <summary>Layout of the AETHER launcher — an independent Start-menu alternative (Windows exposes no API to restyle Start itself).</summary>
public sealed record StartProfile
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string Description { get; init; } = "";
    public bool BuiltIn { get; init; }
    public LauncherStyle Style { get; init; } = LauncherStyle.Modern;
    public int Columns { get; init; } = 6;
    public double IconSize { get; init; } = 40;
    public double Spacing { get; init; } = 12;
    public double Width { get; init; } = 720;
    public double Height { get; init; } = 640;
    public bool ShowSearch { get; init; } = true;
    public bool ShowRecent { get; init; } = true;
    public bool ShowCategories { get; init; } = true;
    public bool ShowPowerButtons { get; init; } = true;
    public bool ShowAllApps { get; init; } = true;
    /// <summary>"palette", "accent", or a file path to an image.</summary>
    public string Background { get; init; } = "palette";
    public double Opacity { get; init; } = 0.86;
    public bool Blur { get; init; } = true;
    public double CornerRadius { get; init; } = 20;
    public List<string> PinnedApps { get; init; } = [];
    public Dictionary<string, List<string>> CustomCategories { get; init; } = [];
    /// <summary>Quick shortcuts: "documents", "downloads", "pictures", "music", "videos", "settings", "explorer", "terminal".</summary>
    public List<string> QuickShortcuts { get; init; } = ["documents", "downloads", "pictures", "settings"];
}

public interface IDockHost
{
    bool IsRunning { get; }
    Task ShowAsync(DockSettings settings);
    Task HideAsync();
}

public interface ILauncherHost
{
    void Toggle();
    void Apply(StartProfile profile);
}

public static class BuiltinShellProfiles
{
    public static IReadOnlyList<TaskbarProfile> Taskbars { get; } =
    [
        new() { Id = "windows", Name = "Windows default", BuiltIn = true, Glyph = "", Description = "The standard Windows taskbar, centered, with search and Task View.",
            Native = new() { ["taskbar.alignment"] = "1", ["taskbar.search"] = "2", ["taskbar.taskview"] = "on" }, AutoHideWindowsTaskbar = false },
        new() { Id = "centered-clean", Name = "Centered & clean", BuiltIn = true, Glyph = "", Description = "Native taskbar, centered, search as an icon and no Task View button.",
            Native = new() { ["taskbar.alignment"] = "1", ["taskbar.search"] = "1", ["taskbar.taskview"] = "off" }, AutoHideWindowsTaskbar = false },
        new() { Id = "classic-left", Name = "Classic left", BuiltIn = true, Glyph = "", Description = "Start button in the corner, labels shown when the taskbar is full.",
            Native = new() { ["taskbar.alignment"] = "0", ["taskbar.search"] = "1", ["taskbar.combine"] = "1" }, AutoHideWindowsTaskbar = false },
        new() { Id = "minimal", Name = "Minimal", BuiltIn = true, Glyph = "", Description = "Native taskbar stripped to apps only, auto-hidden until you need it.",
            Native = new() { ["taskbar.search"] = "0", ["taskbar.taskview"] = "off", ["taskbar.badges"] = "off" }, AutoHideWindowsTaskbar = true },
        new() { Id = "dock", Name = "macOS-inspired dock", BuiltIn = true, Glyph = "", Description = "A magnifying AETHER dock at the bottom; the Windows taskbar auto-hides.",
            AutoHideWindowsTaskbar = true, Dock = new DockSettings { Style = DockStyle.Dock, Magnify = true, ShowRunningApps = true } },
        new() { Id = "floating", Name = "Floating taskbar", BuiltIn = true, Glyph = "", Description = "A rounded, floating AETHER bar with a clock; the Windows taskbar auto-hides.",
            AutoHideWindowsTaskbar = true, Dock = new DockSettings { Style = DockStyle.Floating, Magnify = false, IconSize = 36, ShowClock = true, CornerRadius = 16 } },
        new() { Id = "compact", Name = "Compact dock", BuiltIn = true, Glyph = "", Description = "A small AETHER dock alongside the native taskbar for extra launchers.",
            AutoHideWindowsTaskbar = false, Dock = new DockSettings { Style = DockStyle.Compact, Magnify = false, IconSize = 30, Edge = DockEdge.Top, ShowRunningApps = false } },
        new() { Id = "vertical", Name = "Vertical dock", BuiltIn = true, Glyph = "", Description = "An AETHER dock on the left edge — Windows 11 does not support a vertical native taskbar.",
            AutoHideWindowsTaskbar = true, Dock = new DockSettings { Style = DockStyle.Dock, Edge = DockEdge.Left, Magnify = true } },
        new() { Id = "launcher", Name = "Launcher dock", BuiltIn = true, Glyph = "", Description = "Pinned launchers only, auto-hiding, beside the native taskbar.",
            AutoHideWindowsTaskbar = false, Dock = new DockSettings { Style = DockStyle.Launcher, AutoHide = true, ShowRunningApps = false, Edge = DockEdge.Right } },
    ];

    public static IReadOnlyList<StartProfile> Starts { get; } =
    [
        new() { Id = "modern", Name = "Modern", BuiltIn = true, Description = "Pinned grid, recent apps and quick folders.", Style = LauncherStyle.Modern },
        new() { Id = "minimal", Name = "Minimal", BuiltIn = true, Description = "Just search and your pins.", Style = LauncherStyle.Minimal, Columns = 5, ShowRecent = false, ShowCategories = false, Width = 560, Height = 460 },
        new() { Id = "classic", Name = "Classic", BuiltIn = true, Description = "A two-pane list of all apps with folders on the side.", Style = LauncherStyle.Classic, Columns = 1, IconSize = 24, Width = 560, Height = 680 },
        new() { Id = "productivity", Name = "Productivity", BuiltIn = true, Description = "Categories, launch groups and quick folders up front.", Style = LauncherStyle.Productivity, Columns = 7, Width = 860, Height = 680,
            QuickShortcuts = ["documents", "downloads", "explorer", "terminal", "settings"] },
        new() { Id = "gaming", Name = "Gaming", BuiltIn = true, Description = "Large tiles for your games and launchers.", Style = LauncherStyle.Gaming, Columns = 4, IconSize = 64, Spacing = 18, Width = 820, Height = 620, Background = "accent" },
        new() { Id = "spotlight", Name = "macOS-inspired launcher", BuiltIn = true, Description = "A centered full-screen app grid with search at the top.", Style = LauncherStyle.Spotlight, Columns = 8, IconSize = 56, Width = 1100, Height = 760, ShowRecent = false },
    ];
}

/// <summary>Switches taskbar and launcher profiles, distinguishing native Windows settings from AETHER components.</summary>
public sealed class ShellProfileService(TweakService tweaks, IDesktopController desktop, IDocumentStore store, IDockHost? dock = null, ILauncherHost? launcher = null)
{
    public event Action? Changed;

    public IReadOnlyList<TaskbarProfile> GetTaskbarProfiles() => BuiltinShellProfiles.Taskbars.Concat(store.GetAll<TaskbarProfile>("taskbar-profiles")).ToList();
    public IReadOnlyList<StartProfile> GetStartProfiles() => BuiltinShellProfiles.Starts.Concat(store.GetAll<StartProfile>("start-profiles")).ToList();

    public string? ActiveTaskbarProfileId => store.Get<Active>("shell-state", "taskbar")?.Id;
    public string ActiveStartProfileId => store.Get<Active>("shell-state", "start")?.Id ?? "modern";
    public StartProfile ActiveStartProfile => GetStartProfiles().FirstOrDefault(p => p.Id == ActiveStartProfileId) ?? BuiltinShellProfiles.Starts[0];
    public bool DockAvailable => dock is not null;

    public void SaveTaskbarProfile(TaskbarProfile p) { if (p.BuiltIn) throw new InvalidOperationException("Duplicate built-in profiles to edit them."); store.Put("taskbar-profiles", p.Id, p); Changed?.Invoke(); }
    public void SaveStartProfile(StartProfile p) { if (p.BuiltIn) throw new InvalidOperationException("Duplicate built-in profiles to edit them."); store.Put("start-profiles", p.Id, p); Changed?.Invoke(); if (p.Id == ActiveStartProfileId) launcher?.Apply(p); }
    public void DeleteTaskbarProfile(string id) { store.Delete("taskbar-profiles", id); Changed?.Invoke(); }
    public void DeleteStartProfile(string id) { store.Delete("start-profiles", id); Changed?.Invoke(); }

    public async Task<OperationResult> ApplyTaskbarProfileAsync(string id, CancellationToken ct = default)
    {
        var profile = GetTaskbarProfiles().FirstOrDefault(p => p.Id == id);
        if (profile is null) return OperationResult.Fail("Profile not found.");
        var notes = new List<string>();

        foreach (var (tweakId, option) in profile.Native)
        {
            var state = tweaks.GetStates().FirstOrDefault(s => s.Definition.Id == tweakId);
            if (state is null || !state.IsCompatible) { notes.Add($"{state?.Definition.Title ?? tweakId}: not available on this Windows build"); continue; }
            var r = await tweaks.SetAsync(tweakId, option, ct).ConfigureAwait(false);
            if (!r.Success) notes.Add(r.Describe());
        }

        if (profile.AutoHideWindowsTaskbar is { } hide)
        {
            if (store.Get<SavedAutoHide>("shell-state", "autohide-before") is null)
                store.Put("shell-state", "autohide-before", new SavedAutoHide(desktop.IsTaskbarAutoHide()));
            desktop.SetTaskbarAutoHide(hide);
        }

        if (profile.Dock is { } dockSettings)
        {
            if (dock is null) notes.Add("The AETHER dock is not available in this session.");
            else await dock.ShowAsync(dockSettings).ConfigureAwait(false);
        }
        else if (dock?.IsRunning == true) await dock.HideAsync().ConfigureAwait(false);

        store.Put("shell-state", "taskbar", new Active(id));
        Changed?.Invoke();
        return notes.Count == 0 ? OperationResult.Ok($"Taskbar profile '{profile.Name}' applied.") : new OperationResult(true, $"'{profile.Name}' applied with notes.", notes);
    }

    /// <summary>Restores every native taskbar setting AETHER changed, stops the dock and restores auto-hide.</summary>
    public async Task<OperationResult> RestoreWindowsTaskbarAsync(CancellationToken ct = default)
    {
        var r = await tweaks.RestoreAreaAsync(TweakArea.Taskbar, ct).ConfigureAwait(false);
        if (store.Get<SavedAutoHide>("shell-state", "autohide-before") is { } saved)
        {
            desktop.SetTaskbarAutoHide(saved.AutoHide);
            store.Delete("shell-state", "autohide-before");
        }
        if (dock?.IsRunning == true) await dock.HideAsync().ConfigureAwait(false);
        store.Delete("shell-state", "taskbar");
        Changed?.Invoke();
        return r.Success ? OperationResult.Ok("The Windows taskbar is back to how it was before AETHER.") : r;
    }

    public void ActivateStartProfile(string id)
    {
        var p = GetStartProfiles().FirstOrDefault(x => x.Id == id) ?? throw new KeyNotFoundException(id);
        store.Put("shell-state", "start", new Active(id));
        launcher?.Apply(p);
        Changed?.Invoke();
    }

    private sealed record Active(string Id);
    private sealed record SavedAutoHide(bool AutoHide);
}
