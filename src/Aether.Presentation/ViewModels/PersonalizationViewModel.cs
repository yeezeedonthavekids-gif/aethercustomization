using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using Aether.Core.Abstractions;
using Aether.Core.Configuration;
using Aether.ModEngine.Lifecycle;
using Aether.Presentation.Infrastructure;
using Aether.Presentation.Items;
using Aether.Services.Platform;
using Aether.Services.Settings;
using Aether.Services.Tweaks;
using Aether.Services.Wallpapers;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Aether.Presentation.ViewModels;

public sealed partial class MonitorWallpaperViewModel(MonitorInfo monitor) : ObservableObject
{
    public MonitorInfo Monitor { get; } = monitor;
    public string Name => Monitor.DisplayName;
    public string Resolution => Monitor.Resolution;
    [ObservableProperty] private string? _currentPath;
    [ObservableProperty] private string? _assignedPath;
    public double AspectWidth => 160;
    public double AspectHeight => Monitor.Width == 0 ? 90 : 160.0 * Monitor.Height / Monitor.Width;
}

public sealed record Swatch(string Hex);

public sealed record WallpaperThumb(string Path)
{
    public string Name => System.IO.Path.GetFileNameWithoutExtension(Path);
}

public sealed partial class PersonalizationViewModel : PageViewModel
{
    private readonly TweakService _tweaks;
    private readonly IWallpaperService _wallpaper;
    private readonly ICursorService _cursors;
    private readonly WallpaperLibrary _library;
    private readonly WallpaperAssignments _assignments;
    private readonly ModManager _mods;
    private readonly IDialogService _dialogs;
    private readonly IShellLauncher _launcher;
    private readonly ISettingsService _settings;

    public PersonalizationViewModel(TweakService tweaks, IWallpaperService wallpaper, ICursorService cursors, WallpaperLibrary library, WallpaperAssignments assignments,
        ModManager mods, ModInteraction interaction, IDialogService dialogs, IShellLauncher launcher, ISettingsService settings, INavigationService navigation,
        IUiDispatcher ui, INotificationSink notifications) : base(notifications)
    {
        _tweaks = tweaks; _wallpaper = wallpaper; _cursors = cursors; _library = library; _assignments = assignments; _mods = mods;
        _dialogs = dialogs; _launcher = launcher; _settings = settings;
        Appearance = new TweakGroupViewModel(tweaks, TweakArea.Personalization, r => Report(r));
        Related = new RelatedMods(mods, interaction, navigation, ui, "aether.window-animations", "aether.cursor-profiles", "aether.wallpaper-scheduler", "aether.multimonitor-wallpaper", "aether.taskbar-accent");
    }

    public TweakGroupViewModel Appearance { get; }
    public RelatedMods Related { get; }
    public IReadOnlyList<Swatch> AccentPresets { get; } =
        new[] { "#FF7C8CFF", "#FF0078D4", "#FF00B7C3", "#FF3DF5B5", "#FF10893E", "#FFFFB900", "#FFF7630C", "#FFE81123", "#FFEA005E", "#FFB146C2", "#FF8E8CD8", "#FF767676" }
            .Select(h => new Swatch(h)).ToList();
    public ObservableCollection<MonitorWallpaperViewModel> Monitors { get; } = [];
    public ObservableCollection<WallpaperThumb> Library { get; } = [];
    public ObservableCollection<OnlineWallpaper> Online { get; } = [];
    public ObservableCollection<string> Folders { get; } = [];
    public ObservableCollection<CursorScheme> CursorSchemes { get; } = [];
    public IReadOnlyList<WallpaperFit> Fits { get; } = Enum.GetValues<WallpaperFit>();

    [ObservableProperty] private string _customAccent = "#FF7C8CFF";
    [ObservableProperty] private bool _accentCustomized;
    [ObservableProperty] private WallpaperFit _selectedFit;
    [ObservableProperty] private MonitorWallpaperViewModel? _selectedMonitor;
    [ObservableProperty] private CursorScheme? _selectedCursorScheme;
    [ObservableProperty] private string? _activeCursorScheme;
    [ObservableProperty] private bool _onlineLoading;
    [ObservableProperty] private string? _onlineError;

    public bool WallpaperSupported => _wallpaper.IsSupported;
    public bool OnlineAllowed => _settings.Current.AllowOnlineWallpapers;
    public string OnlineAttribution => _library.Providers.FirstOrDefault()?.Description ?? "";

    protected override async Task LoadAsync()
    {
        AccentCustomized = _tweaks.IsAccentCustomized;
        await LoadWallpapersAsync();
        try
        {
            foreach (var s in _cursors.GetSchemes()) CursorSchemes.Add(s);
            ActiveCursorScheme = _cursors.GetActiveSchemeName();
        }
        catch (Exception ex) { Toast("Cursor schemes unavailable", ex.Message, NotificationSeverity.Warning); }
    }

    protected override async Task RefreshAsync()
    {
        Appearance.Refresh();
        AccentCustomized = _tweaks.IsAccentCustomized;
        await LoadWallpapersAsync();
    }

    protected override void OnActivated(object? parameter) { }

    private async Task LoadWallpapersAsync()
    {
        if (!_wallpaper.IsSupported) return;
        var (monitors, fit, files) = await Task.Run(() =>
        {
            var ms = _wallpaper.GetMonitors().Select(m => (m, _wallpaper.GetWallpaper(m.Id))).ToList();
            return (ms, _wallpaper.GetFit(), _library.Scan(240));
        });
        var assigned = _assignments.Get();
        Monitors.Clear();
        foreach (var (m, path) in monitors)
            Monitors.Add(new MonitorWallpaperViewModel(m) { CurrentPath = path is not null && File.Exists(path) ? path : null, AssignedPath = assigned.PerMonitor.GetValueOrDefault(m.Id) });
        SelectedMonitor ??= Monitors.FirstOrDefault();
        SelectedFit = fit;
        Library.Clear();
        foreach (var f in files) Library.Add(new WallpaperThumb(f));
        Folders.Clear();
        foreach (var f in _library.GetFolders()) Folders.Add(f);
    }

    partial void OnSelectedFitChanged(WallpaperFit value)
    {
        if (!_wallpaper.IsSupported || IsLoading) return;
        try { _wallpaper.SetFit(value); } catch (Exception ex) { Toast("Could not change fit", ex.Message, NotificationSeverity.Error); }
    }

    [RelayCommand]
    private Task ApplyAccentAsync(string? hex) => RunAsync(async () =>
    {
        hex ??= CustomAccent;
        if (!ColorValue.TryParse(hex, out var c)) { Toast("Invalid color", "Use #RRGGBB.", NotificationSeverity.Warning); return; }
        CustomAccent = c.ToHex();
        Report(await _tweaks.ApplyAccentAsync(c), "Accent color");
        AccentCustomized = _tweaks.IsAccentCustomized;
    });

    [RelayCommand]
    private Task RestoreAccentAsync() => RunAsync(async () =>
    {
        Report(await _tweaks.RestoreAccentAsync(), "Accent color");
        AccentCustomized = _tweaks.IsAccentCustomized;
    });

    [RelayCommand]
    private Task SetWallpaperAsync(WallpaperThumb? thumb) => thumb is null ? Task.CompletedTask : SetWallpaperFileAsync(thumb.Path, SelectedMonitor);

    [RelayCommand]
    private Task SetWallpaperAllAsync(WallpaperThumb? thumb) => thumb is null ? Task.CompletedTask : SetWallpaperFileAsync(thumb.Path, null);

    private Task SetWallpaperFileAsync(string path, MonitorWallpaperViewModel? monitor) => RunAsync(async () =>
    {
        await Task.Run(() => _wallpaper.SetWallpaper(monitor?.Monitor.Id, path));
        var set = _assignments.Get();
        if (monitor is not null) { set.PerMonitor[monitor.Monitor.Id] = path; _assignments.Save(set); }
        Toast("Wallpaper changed", monitor is null ? "Applied to all monitors." : $"Applied to {monitor.Name}.");
        await LoadWallpapersAsync();
    }, "Could not set the wallpaper");

    [RelayCommand]
    private async Task BrowseWallpaperAsync()
    {
        var path = await _dialogs.PickFileAsync(new FileFilter("Images", ".jpg", ".jpeg", ".png", ".bmp", ".webp"));
        if (path is not null) await SetWallpaperFileAsync(path, SelectedMonitor);
    }

    [RelayCommand]
    private async Task AddFolderAsync()
    {
        var folder = await _dialogs.PickFolderAsync();
        if (folder is null) return;
        _library.AddFolder(folder);
        await LoadWallpapersAsync();
    }

    [RelayCommand]
    private async Task RemoveFolderAsync(string? folder)
    {
        if (folder is null) return;
        _library.RemoveFolder(folder);
        await LoadWallpapersAsync();
    }

    [RelayCommand]
    private async Task SaveCollectionAsync()
    {
        var name = await _dialogs.PromptAsync("New collection", "Collection name", "Favorites");
        if (string.IsNullOrWhiteSpace(name)) return;
        _library.SaveCollection(new WallpaperCollection("col-" + Guid.NewGuid().ToString("N")[..8], name, Monitors.Select(m => m.CurrentPath).OfType<string>().ToList()));
        Toast("Collection saved", $"'{name}' contains your current wallpapers.");
    }

    [RelayCommand]
    private Task LoadOnlineAsync() => RunAsync(async () =>
    {
        if (!OnlineAllowed) { OnlineError = "Online wallpapers are turned off in Settings ▸ Privacy."; return; }
        OnlineLoading = true; OnlineError = null;
        try
        {
            Online.Clear();
            foreach (var provider in _library.Providers)
                foreach (var w in await provider.GetLatestAsync()) Online.Add(w);
        }
        catch (Exception ex) { OnlineError = $"Couldn't reach the provider: {ex.Message}"; }
        finally { OnlineLoading = false; }
    });

    [RelayCommand]
    private Task UseOnlineAsync(OnlineWallpaper? w) => w is null ? Task.CompletedTask : RunAsync(async () =>
    {
        var file = await _library.DownloadAsync(w);
        await SetWallpaperFileAsync(file, SelectedMonitor);
    }, "Download failed");

    [RelayCommand]
    private async Task EnablePerMonitorKeeperAsync()
    {
        var mod = _mods.GetMod("aether.multimonitor-wallpaper");
        if (mod is null) return;
        var item = Related.Items.First(i => i.Id == mod.Id);
        await Related.ToggleAsync(item, true);
    }

    [RelayCommand]
    private async Task ApplyCursorSchemeAsync()
    {
        if (SelectedCursorScheme is null) return;
        // Cursor changes go through the Cursor Profile mod so they are snapshotted and reversible.
        await _mods.UpdateSettingsAsync("aether.cursor-profiles", new JsonObject { ["scheme"] = SelectedCursorScheme.Name, ["shadow"] = true });
        var item = Related.Items.FirstOrDefault(i => i.Id == "aether.cursor-profiles");
        if (item is not null && !item.IsEnabled) await Related.ToggleAsync(item, true);
        else Toast("Cursor scheme", $"'{SelectedCursorScheme.Name}' applied.");
        ActiveCursorScheme = _cursors.GetActiveSchemeName();
    }

    [RelayCommand]
    private void OpenWindowsSettings(string? uri)
    {
        try { _launcher.Open(uri ?? "ms-settings:personalization"); }
        catch (Exception ex) { Toast("Couldn't open Settings", ex.Message, NotificationSeverity.Error); }
    }
}
