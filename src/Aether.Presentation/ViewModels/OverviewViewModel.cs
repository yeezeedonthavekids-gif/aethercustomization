using System.Collections.ObjectModel;
using Aether.Core.Abstractions;
using Aether.ModEngine.Lifecycle;
using Aether.PluginSystem;
using Aether.Presentation.Infrastructure;
using Aether.Presentation.Items;
using Aether.Services.Backup;
using Aether.Services.Platform;
using Aether.Services.Tweaks;
using Aether.Storage;
using Aether.ThemeEngine;
using Aether.WidgetEngine;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Aether.Presentation.ViewModels;

public sealed record QuickAction(string Title, string Description, string Glyph, IAsyncRelayCommand Command);
public sealed record StatusRow(string Label, string Value, string Glyph, bool Good);

public sealed partial class OverviewViewModel : PageViewModel
{
    private readonly ModManager _mods;
    private readonly ThemeManager _themes;
    private readonly PluginHost _plugins;
    private readonly WidgetManager _widgets;
    private readonly BackupService _backups;
    private readonly TweakService _tweaks;
    private readonly ISystemInfoProvider _info;
    private readonly IWallpaperService _wallpaper;
    private readonly INavigationService _navigation;
    private readonly ModInteraction _interaction;

    public OverviewViewModel(ModManager mods, ThemeManager themes, PluginHost plugins, WidgetManager widgets, BackupService backups, TweakService tweaks,
        ISystemInfoProvider info, IWallpaperService wallpaper, INavigationService navigation, ModInteraction interaction, INotificationSink notifications) : base(notifications)
    {
        _mods = mods; _themes = themes; _plugins = plugins; _widgets = widgets; _backups = backups; _tweaks = tweaks;
        _info = info; _wallpaper = wallpaper; _navigation = navigation; _interaction = interaction;
        QuickActions =
        [
            new("Restart Explorer", "Reload the taskbar and shell", "", new AsyncRelayCommand(() => _interaction.InvokeActionAsync("aether.explorer-restart", _mods.GetMod("aether.explorer-restart")!.Manifest.Actions[0]))),
            new("Back up now", "Save your AETHER setup", "", new AsyncRelayCommand(BackupNowAsync)),
            new("Focus mode", "Hide distractions for 25 minutes", "", new AsyncRelayCommand(() => ToggleModAsync("aether.focus-mode"))),
            new("Show file extensions", "Always see .exe, .pdf…", "", new AsyncRelayCommand(() => ToggleModAsync("aether.explorer-file-extensions"))),
            new("Change wallpaper", "Pick a new background", "", new AsyncRelayCommand(() => { _navigation.Navigate(PageKey.Personalization, "wallpaper"); return Task.CompletedTask; })),
            new("Browse themes", "Switch your whole look", "", new AsyncRelayCommand(() => { _navigation.Navigate(PageKey.Themes); return Task.CompletedTask; })),
        ];
    }

    public IReadOnlyList<QuickAction> QuickActions { get; }
    public ObservableCollection<ModItemViewModel> ActiveMods { get; } = [];
    public ObservableCollection<ModItemViewModel> Recommended { get; } = [];
    public ObservableCollection<ModHistoryRecord> RecentActivity { get; } = [];
    public ObservableCollection<StatusRow> Status { get; } = [];

    [ObservableProperty] private ThemePack? _activeTheme;
    [ObservableProperty] private SystemSummary? _system;
    [ObservableProperty] private string? _wallpaperPath;
    [ObservableProperty] private int _activeModCount;
    [ObservableProperty] private int _modCount;
    [ObservableProperty] private int _pluginCount;
    [ObservableProperty] private int _widgetCount;
    [ObservableProperty] private int _tweaksChanged;
    [ObservableProperty] private string _greeting = "";
    [ObservableProperty] private string _lastBackup = "Never";

    public string MemoryText => System is null ? "" : $"{System.TotalMemoryBytes / 1024d / 1024 / 1024:0.#} GB";
    public string UptimeText => System is null ? "" : System.Uptime.TotalDays >= 1 ? $"{(int)System.Uptime.TotalDays}d {System.Uptime.Hours}h" : $"{System.Uptime.Hours}h {System.Uptime.Minutes}m";
    public bool HasNoActiveMods => ActiveMods.Count == 0;

    protected override async Task LoadAsync() => await RefreshAsync();

    protected override async Task RefreshAsync()
    {
        var hour = DateTime.Now.Hour;
        Greeting = hour < 5 ? "Working late" : hour < 12 ? "Good morning" : hour < 18 ? "Good afternoon" : "Good evening";
        ActiveTheme = _themes.ActiveTheme;
        var mods = _mods.GetMods();
        ModCount = mods.Count;
        ActiveModCount = mods.Count(m => m.IsEnabled);
        Replace(ActiveMods, mods.Where(m => m.IsEnabled).Take(6).Select(m => new ModItemViewModel(m)));
        Replace(Recommended, mods.Where(m => !m.IsEnabled && m.Compatibility.IsCompatible && m.Manifest.Risk == ModEngine.Manifest.RiskLevel.Low).Take(4).Select(m => new ModItemViewModel(m)));
        Replace(RecentActivity, _mods.GetHistory(limit: 6));
        PluginCount = _plugins.GetPlugins().Count;
        WidgetCount = _widgets.ActiveLayout.Widgets.Count;
        TweaksChanged = _tweaks.GetStates().Count(s => s.IsModified);
        OnPropertyChanged(nameof(HasNoActiveMods));

        var (system, wallpaper, last) = await Task.Run(() =>
        {
            SystemSummary? s = null;
            try { s = _info.GetSummary(); } catch (Exception) { }
            string? w = null;
            try { w = _wallpaper.IsSupported ? _wallpaper.GetMonitors().Select(m => _wallpaper.GetWallpaper(m.Id)).FirstOrDefault(p => p is not null) : null; } catch (Exception) { }
            var b = _backups.List().FirstOrDefault();
            return (s, w, b);
        });
        System = system;
        WallpaperPath = wallpaper is not null && File.Exists(wallpaper) ? wallpaper : null;
        LastBackup = last is null ? "Never" : last.Manifest.CreatedAt.ToLocalTime().ToString("g");
        OnPropertyChanged(nameof(MemoryText)); OnPropertyChanged(nameof(UptimeText));

        Status.Clear();
        Status.Add(new("Mods", ActiveModCount == 0 ? "None active" : $"{ActiveModCount} active", "", true));
        var errors = mods.Count(m => m.Status == ModStatus.Error);
        Status.Add(new("Health", errors == 0 ? "All changes applied" : $"{errors} mod(s) need attention", errors == 0 ? "" : "", errors == 0));
        Status.Add(new("Native settings changed", TweaksChanged.ToString(), "", true));
        Status.Add(new("Last backup", LastBackup, "", last is not null));
        Status.Add(new("Privileges", system?.IsAdministrator == true ? "Administrator" : "Standard user", "", true));
    }

    private static void Replace<T>(ObservableCollection<T> c, IEnumerable<T> items) { c.Clear(); foreach (var i in items) c.Add(i); }

    private async Task ToggleModAsync(string id)
    {
        var mod = _mods.GetMod(id);
        if (mod is null) return;
        await _interaction.SetEnabledAsync(id, !mod.IsEnabled);
        await RefreshAsync();
    }

    [RelayCommand]
    private Task EnableRecommendedAsync(ModItemViewModel? item) => item is null ? Task.CompletedTask : ToggleModAsync(item.Id);

    [RelayCommand]
    private void OpenMod(ModItemViewModel? item) { if (item is not null) _navigation.Navigate(PageKey.Mods, item.Id); }

    [RelayCommand]
    private void Go(PageKey key) => _navigation.Navigate(key);

    private Task BackupNowAsync() => RunAsync(async () =>
    {
        var r = await _backups.CreateAsync(BackupKind.Full, "Manual backup from Overview");
        Report(r.ToResult(), "Backup");
        await RefreshAsync();
    });
}
