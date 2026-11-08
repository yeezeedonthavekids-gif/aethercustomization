using System.Collections.ObjectModel;
using Aether.Core.Abstractions;
using Aether.ModEngine.Lifecycle;
using Aether.Presentation.Infrastructure;
using Aether.Presentation.Items;
using Aether.Services.Applications;
using Aether.Services.Platform;
using Aether.Services.Profiles;
using Aether.Services.Settings;
using Aether.Services.Tweaks;
using Aether.Services.Windows;
using Aether.Services.Explorer;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Aether.Presentation.ViewModels;

public sealed partial class TaskbarProfileItem(TaskbarProfile profile, bool active) : ObservableObject
{
    public TaskbarProfile Profile { get; } = profile;
    public string Name => Profile.Name;
    public string Description => Profile.Description;
    public string Glyph => Profile.Glyph;
    public bool UsesDock => Profile.Dock is not null;
    public string Kind => UsesDock ? "AETHER dock + native settings" : "Native Windows settings";
    [ObservableProperty] private bool _isActive = active;
}

public sealed partial class TaskbarViewModel : PageViewModel
{
    private readonly ShellProfileService _shell;
    private readonly ISettingsService _settings;

    public TaskbarViewModel(TweakService tweaks, ShellProfileService shell, ModManager mods, ModInteraction interaction, INavigationService navigation,
        ISettingsService settings, IUiDispatcher ui, INotificationSink notifications) : base(notifications)
    {
        _shell = shell; _settings = settings;
        Native = new TweakGroupViewModel(tweaks, TweakArea.Taskbar, r => Report(r));
        Related = new RelatedMods(mods, interaction, navigation, ui, "aether.taskbar-transparency", "aether.taskbar-accent", "aether.clock-format", "aether.clock-seconds", "aether.explorer-restart");
    }

    public TweakGroupViewModel Native { get; }
    public RelatedMods Related { get; }
    public ObservableCollection<TaskbarProfileItem> Profiles { get; } = [];
    public bool DockAvailable => _shell.DockAvailable;

    [ObservableProperty] private double _dockIconSize = 44;
    [ObservableProperty] private bool _dockMagnify = true;
    [ObservableProperty] private bool _dockAutoHide;
    [ObservableProperty] private double _dockOpacity = 0.78;
    [ObservableProperty] private DockEdge _dockEdge = DockEdge.Bottom;
    public IReadOnlyList<DockEdge> DockEdges { get; } = Enum.GetValues<DockEdge>();

    protected override Task LoadAsync() { LoadProfiles(); return Task.CompletedTask; }
    protected override Task RefreshAsync() { Native.Refresh(); LoadProfiles(); return Task.CompletedTask; }

    private void LoadProfiles()
    {
        Profiles.Clear();
        foreach (var p in _shell.GetTaskbarProfiles()) Profiles.Add(new TaskbarProfileItem(p, p.Id == _shell.ActiveTaskbarProfileId));
        var active = _shell.GetTaskbarProfiles().FirstOrDefault(p => p.Id == _shell.ActiveTaskbarProfileId)?.Dock;
        if (active is not null) { DockIconSize = active.IconSize; DockMagnify = active.Magnify; DockAutoHide = active.AutoHide; DockOpacity = active.Opacity; DockEdge = active.Edge; }
    }

    [RelayCommand]
    private Task ApplyProfileAsync(TaskbarProfileItem? item) => item is null ? Task.CompletedTask : RunAsync(async () =>
    {
        Report(await _shell.ApplyTaskbarProfileAsync(item.Profile.Id), item.Name);
        Native.Refresh();
        LoadProfiles();
    });

    [RelayCommand]
    private Task SaveDockAsync() => RunAsync(async () =>
    {
        var baseProfile = _shell.GetTaskbarProfiles().FirstOrDefault(p => p.Id == _shell.ActiveTaskbarProfileId && p.Dock is not null)
                          ?? _shell.GetTaskbarProfiles().First(p => p.Id == "dock");
        var custom = baseProfile with
        {
            Id = baseProfile.BuiltIn ? "custom-dock" : baseProfile.Id, Name = baseProfile.BuiltIn ? "My dock" : baseProfile.Name, BuiltIn = false,
            Description = "Your customized AETHER dock.",
            Dock = baseProfile.Dock! with { IconSize = DockIconSize, Magnify = DockMagnify, AutoHide = DockAutoHide, Opacity = DockOpacity, Edge = DockEdge },
        };
        _shell.SaveTaskbarProfile(custom);
        Report(await _shell.ApplyTaskbarProfileAsync(custom.Id), "Dock");
        LoadProfiles();
    });

    [RelayCommand]
    private Task RestoreWindowsAsync() => RunAsync(async () =>
    {
        Report(await _shell.RestoreWindowsTaskbarAsync(), "Taskbar");
        Native.Refresh();
        LoadProfiles();
    });
}

public sealed partial class StartProfileItem(StartProfile profile, bool active) : ObservableObject
{
    public StartProfile Profile { get; } = profile;
    public string Name => Profile.Name;
    public string Description => Profile.Description;
    public string StyleLabel => Profile.Style.ToString();
    [ObservableProperty] private bool _isActive = active;
}

public sealed partial class StartMenuViewModel : PageViewModel
{
    private readonly ShellProfileService _shell;
    private readonly AppCatalog _apps;
    private readonly ISettingsService _settings;
    private readonly ILauncherHost? _launcher;

    public StartMenuViewModel(TweakService tweaks, ShellProfileService shell, AppCatalog apps, ModManager mods, ModInteraction interaction, INavigationService navigation,
        ISettingsService settings, IUiDispatcher ui, INotificationSink notifications, ILauncherHost? launcher = null) : base(notifications)
    {
        _shell = shell; _apps = apps; _settings = settings; _launcher = launcher;
        Native = new TweakGroupViewModel(tweaks, TweakArea.StartMenu, r => Report(r));
        Related = new RelatedMods(mods, interaction, navigation, ui, "aether.power-shortcuts");
    }

    public TweakGroupViewModel Native { get; }
    public RelatedMods Related { get; }
    public ObservableCollection<StartProfileItem> Profiles { get; } = [];
    public ObservableCollection<InstalledApp> PinnedApps { get; } = [];
    public ObservableCollection<InstalledApp> AllApps { get; } = [];

    [ObservableProperty] private StartProfileItem? _editing;
    [ObservableProperty] private int _columns;
    [ObservableProperty] private double _iconSize;
    [ObservableProperty] private double _spacing;
    [ObservableProperty] private double _opacity;
    [ObservableProperty] private bool _showRecent;
    [ObservableProperty] private bool _showCategories;
    [ObservableProperty] private bool _showSearch;
    [ObservableProperty] private bool _blur;
    [ObservableProperty] private string _background = "palette";
    [ObservableProperty] private InstalledApp? _appToPin;

    public string LauncherHotkey => _settings.Current.LauncherHotkey;
    public bool LauncherAvailable => _launcher is not null;
    public IReadOnlyList<string> Backgrounds { get; } = ["palette", "accent"];

    protected override async Task LoadAsync()
    {
        LoadProfiles();
        foreach (var a in await _apps.GetAppsAsync()) AllApps.Add(a);
        LoadEditor();
    }

    protected override Task RefreshAsync() { Native.Refresh(); LoadProfiles(); return Task.CompletedTask; }

    private void LoadProfiles()
    {
        Profiles.Clear();
        foreach (var p in _shell.GetStartProfiles()) Profiles.Add(new StartProfileItem(p, p.Id == _shell.ActiveStartProfileId));
    }

    private void LoadEditor()
    {
        var p = _shell.ActiveStartProfile;
        Editing = Profiles.FirstOrDefault(x => x.Profile.Id == p.Id);
        Columns = p.Columns; IconSize = p.IconSize; Spacing = p.Spacing; Opacity = p.Opacity; ShowRecent = p.ShowRecent;
        ShowCategories = p.ShowCategories; ShowSearch = p.ShowSearch; Blur = p.Blur; Background = p.Background;
        PinnedApps.Clear();
        foreach (var id in p.PinnedApps) if (AllApps.FirstOrDefault(a => a.Id == id) is { } a) PinnedApps.Add(a);
    }

    [RelayCommand]
    private void ActivateProfile(StartProfileItem? item)
    {
        if (item is null) return;
        _shell.ActivateStartProfile(item.Profile.Id);
        LoadProfiles();
        LoadEditor();
        Toast("Launcher", $"'{item.Name}' is now your AETHER launcher layout. Press {LauncherHotkey} to open it.");
    }

    [RelayCommand]
    private void SaveLauncher()
    {
        var p = _shell.ActiveStartProfile;
        var saved = p with
        {
            Id = p.BuiltIn ? "custom-" + p.Id : p.Id, Name = p.BuiltIn ? $"My {p.Name}" : p.Name, BuiltIn = false,
            Columns = Math.Clamp(Columns, 1, 12), IconSize = IconSize, Spacing = Spacing, Opacity = Opacity, ShowRecent = ShowRecent,
            ShowCategories = ShowCategories, ShowSearch = ShowSearch, Blur = Blur, Background = Background,
            PinnedApps = PinnedApps.Select(a => a.Id).ToList(),
        };
        _shell.SaveStartProfile(saved);
        _shell.ActivateStartProfile(saved.Id);
        LoadProfiles();
        LoadEditor();
        Toast("Launcher saved", $"'{saved.Name}' updated.");
    }

    [RelayCommand]
    private void Pin()
    {
        if (AppToPin is null || PinnedApps.Contains(AppToPin)) return;
        PinnedApps.Add(AppToPin);
    }

    [RelayCommand]
    private void Unpin(InstalledApp? app) { if (app is not null) PinnedApps.Remove(app); }

    [RelayCommand]
    private void OpenLauncher() => _launcher?.Toggle();
}

public sealed partial class FileExplorerViewModel : PageViewModel
{
    private readonly IDialogService _dialogs;
    private readonly FolderCustomizer _folders;

    public FileExplorerViewModel(TweakService tweaks, ModManager mods, ModInteraction interaction, INavigationService navigation, IDialogService dialogs,
        FolderCustomizer folders, IUiDispatcher ui, INotificationSink notifications) : base(notifications)
    {
        _dialogs = dialogs; _folders = folders;
        Native = new TweakGroupViewModel(tweaks, TweakArea.FileExplorer, r => Report(r));
        Related = new RelatedMods(mods, interaction, navigation, ui, "aether.explorer-hidden-files", "aether.explorer-file-extensions", "aether.context-menu", "aether.compact-desktop", "aether.explorer-restart");
    }

    public TweakGroupViewModel Native { get; }
    public RelatedMods Related { get; }
    public ObservableCollection<CustomizedFolder> CustomFolders { get; } = [];

    protected override Task LoadAsync() { Reload(); return Task.CompletedTask; }
    protected override Task RefreshAsync() { Native.Refresh(); Reload(); return Task.CompletedTask; }

    private void Reload()
    {
        CustomFolders.Clear();
        foreach (var f in _folders.GetCustomized()) CustomFolders.Add(f);
    }

    [RelayCommand]
    private async Task SetFolderIconAsync()
    {
        var folder = await _dialogs.PickFolderAsync();
        if (folder is null) return;
        var icon = await _dialogs.PickFileAsync(new FileFilter("Icons", ".ico", ".dll", ".exe"));
        if (icon is null) return;
        Report(_folders.SetIcon(folder, icon, 0), "Folder icon");
        Reload();
    }

    [RelayCommand]
    private void ResetFolder(CustomizedFolder? f)
    {
        if (f is null) return;
        Report(_folders.Reset(f.Path), "Folder icon");
        Reload();
    }

    [RelayCommand]
    private Task RestoreAllAsync() => RunAsync(Native.RestoreAllAsync);
}

public sealed partial class LayoutItem(WindowLayout layout, bool active) : ObservableObject
{
    public WindowLayout Layout { get; } = layout;
    public string Name => Layout.Name;
    public IReadOnlyList<Zone> Zones => Layout.Zones;
    [ObservableProperty] private bool _isActive = active;
}

public sealed partial class DesktopViewModel : PageViewModel
{
    private readonly WindowLayoutService _layouts;
    private readonly IDialogService _dialogs;
    private readonly IWindowManager _windows;

    public DesktopViewModel(WindowLayoutService layouts, IWindowManager windows, ModManager mods, ModInteraction interaction, INavigationService navigation, IDialogService dialogs,
        IUiDispatcher ui, INotificationSink notifications) : base(notifications)
    {
        _layouts = layouts; _dialogs = dialogs; _windows = windows;
        Related = new RelatedMods(mods, interaction, navigation, ui, "aether.desktop-icons", "aether.focus-mode", "aether.compact-desktop", "aether.gaming-profile", "aether.widget-manager");
    }

    public RelatedMods Related { get; }
    public ObservableCollection<LayoutItem> Layouts { get; } = [];
    public ObservableCollection<WindowRule> Rules { get; } = [];
    public ObservableCollection<Workspace> Workspaces { get; } = [];
    public ObservableCollection<WindowInfo> OpenWindows { get; } = [];

    [ObservableProperty] private string _ruleProcess = "";
    [ObservableProperty] private string _ruleTitle = "";
    [ObservableProperty] private LayoutItem? _ruleLayout;
    [ObservableProperty] private double _ruleZone = 1;
    [ObservableProperty] private bool _ruleTopmost;
    [ObservableProperty] private double _ruleOpacity = 100;

    protected override Task LoadAsync() { Reload(); return Task.CompletedTask; }
    protected override Task RefreshAsync() { Reload(); return Task.CompletedTask; }

    private void Reload()
    {
        Layouts.Clear();
        foreach (var l in _layouts.GetLayouts()) Layouts.Add(new LayoutItem(l, l.Id == _layouts.ActiveLayoutId));
        Rules.Clear();
        foreach (var r in _layouts.GetRules()) Rules.Add(r);
        Workspaces.Clear();
        foreach (var w in _layouts.GetWorkspaces()) Workspaces.Add(w);
        RefreshWindows();
    }

    [RelayCommand]
    private void RefreshWindows()
    {
        OpenWindows.Clear();
        try { foreach (var w in _windows.GetWindows()) OpenWindows.Add(w); }
        catch (Exception ex) { Toast("Couldn't list windows", ex.Message, NotificationSeverity.Warning); }
    }

    [RelayCommand]
    private void ToggleWindowTopmost(WindowInfo? w)
    {
        if (w is null) return;
        _windows.SetTopmost(w.Handle, !w.IsTopmost);
        RefreshWindows();
    }

    [RelayCommand]
    private void SetWindowOpacity(WindowInfo? w)
    {
        if (w is null) return;
        _windows.SetOpacity(w.Handle, w.Opacity == 255 ? (byte)204 : (byte)255);
        RefreshWindows();
    }

    [RelayCommand]
    private void Arrange(LayoutItem? item)
    {
        if (item is null) return;
        Report(_layouts.Arrange(item.Layout.Id), item.Name);
        foreach (var l in Layouts) l.IsActive = l == item;
    }

    [RelayCommand]
    private void SetActiveLayout(LayoutItem? item)
    {
        if (item is null) return;
        _layouts.ActiveLayoutId = item.Layout.Id;
        foreach (var l in Layouts) l.IsActive = l == item;
        Toast("Snap layout", $"Ctrl+Alt+1…{item.Zones.Count} now snap the active window into '{item.Name}'.");
    }

    [RelayCommand]
    private async Task CreateLayoutAsync()
    {
        var spec = await _dialogs.PromptAsync("New layout", "Columns and rows, e.g. 3x1 or 2x2", "3x2");
        if (spec is null) return;
        var parts = spec.ToLowerInvariant().Split('x');
        if (parts.Length != 2 || !int.TryParse(parts[0], out var cols) || !int.TryParse(parts[1], out var rows) || cols is < 1 or > 6 || rows is < 1 or > 4)
        {
            Toast("Invalid layout", "Use columns x rows, up to 6x4.", NotificationSeverity.Warning);
            return;
        }
        var zones = new List<Zone>();
        for (var r = 0; r < rows; r++) for (var c = 0; c < cols; c++) zones.Add(new Zone((double)c / cols, (double)r / rows, 1.0 / cols, 1.0 / rows));
        _layouts.SaveLayout(new WindowLayout { Id = "layout-" + Guid.NewGuid().ToString("N")[..6], Name = $"Grid {cols}×{rows}", Zones = zones });
        Reload();
    }

    [RelayCommand]
    private void DeleteLayout(LayoutItem? item)
    {
        if (item is null || item.Layout.BuiltIn) return;
        _layouts.DeleteLayout(item.Layout.Id);
        Reload();
    }

    [RelayCommand]
    private void AddRule()
    {
        if (string.IsNullOrWhiteSpace(RuleProcess)) { Toast("Process name required", "For example: code.exe", NotificationSeverity.Warning); return; }
        _layouts.SaveRule(new WindowRule
        {
            Id = "rule-" + Guid.NewGuid().ToString("N")[..6], ProcessName = RuleProcess.Trim(), TitleContains = string.IsNullOrWhiteSpace(RuleTitle) ? null : RuleTitle,
            LayoutId = RuleLayout?.Layout.Id, ZoneIndex = RuleLayout is null ? null : (int)RuleZone - 1,
            AlwaysOnTop = RuleTopmost ? true : null, Opacity = RuleOpacity < 100 ? (byte)(RuleOpacity * 255 / 100) : null,
        });
        RuleProcess = ""; RuleTitle = "";
        Reload();
        Toast("Window rule added", "It applies whenever a matching window opens.");
    }

    [RelayCommand]
    private void DeleteRule(WindowRule? rule) { if (rule is not null) { _layouts.DeleteRule(rule.Id); Reload(); } }

    [RelayCommand]
    private void ToggleRule(WindowRule? rule) { if (rule is not null) { _layouts.SaveRule(rule with { Enabled = !rule.Enabled }); Reload(); } }

    [RelayCommand]
    private async Task SaveWorkspaceAsync()
    {
        var name = await _dialogs.PromptAsync("Save workspace", "Name", $"Workspace {Workspaces.Count + 1}");
        if (string.IsNullOrWhiteSpace(name)) return;
        var ws = _layouts.CaptureWorkspace(name);
        Toast("Workspace saved", $"{ws.Windows.Count} window positions remembered.");
        Reload();
    }

    [RelayCommand]
    private void RestoreWorkspace(Workspace? ws) { if (ws is not null) Report(_layouts.RestoreWorkspace(ws.Id), ws.Name); }

    [RelayCommand]
    private void DeleteWorkspace(Workspace? ws) { if (ws is not null) { _layouts.DeleteWorkspace(ws.Id); Reload(); } }

    [RelayCommand] private void ToggleTopmost() => Report(_layouts.ToggleTopmostForeground());
}
