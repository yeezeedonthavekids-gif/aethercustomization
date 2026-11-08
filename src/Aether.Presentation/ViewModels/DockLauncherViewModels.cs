using System.Collections.ObjectModel;
using Aether.Presentation.Infrastructure;
using Aether.Services.Applications;
using Aether.Services.Platform;
using Aether.Services.Profiles;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Aether.Presentation.ViewModels;

public sealed partial class DockItem(string id, string name, string target, bool running, nint window = 0) : ObservableObject
{
    public string Id { get; } = id;
    public string Name { get; } = name;
    public string Target { get; } = target;
    public nint Window { get; } = window;
    public string Initial => Name.Length > 0 ? Name[..1].ToUpperInvariant() : "?";
    [ObservableProperty] private bool _isRunning = running;
}

/// <summary>State of the AETHER-rendered dock (an independent window, not a modified Windows taskbar).</summary>
public sealed partial class DockViewModel(AppCatalog apps, IWindowManager windows, IShellLauncher launcher, ILauncherHost? launcherHost = null) : ObservableObject
{
    public ObservableCollection<DockItem> Items { get; } = [];
    [ObservableProperty] private DockSettings _settings = new();
    [ObservableProperty] private string _clock = "";

    public async Task RefreshAsync()
    {
        Clock = DateTime.Now.ToString("HH:mm");
        var all = await apps.GetAppsAsync();
        var pinned = Settings.PinnedApps.Count > 0 ? Settings.PinnedApps : apps.Favorites.ToList();
        var open = Settings.ShowRunningApps ? windows.GetWindows() : [];
        var list = new List<DockItem>();
        foreach (var id in pinned)
            if (all.FirstOrDefault(a => a.Id == id) is { } a)
                list.Add(new DockItem(a.Id, a.Name, a.LaunchTarget, open.Any(w => a.LaunchTarget.Contains(w.ProcessName, StringComparison.OrdinalIgnoreCase))));
        foreach (var w in open.Where(w => list.All(i => !i.Target.Contains(w.ProcessName, StringComparison.OrdinalIgnoreCase))).GroupBy(w => w.ProcessName).Select(g => g.First()).Take(12))
            list.Add(new DockItem("win:" + w.Handle, w.ProcessName, "", true, w.Handle));
        if (list.Select(i => i.Id).SequenceEqual(Items.Select(i => i.Id)))
        {
            for (var i = 0; i < list.Count; i++) Items[i].IsRunning = list[i].IsRunning;
            return;
        }
        Items.Clear();
        foreach (var i in list) Items.Add(i);
    }

    [RelayCommand]
    private void Activate(DockItem? item)
    {
        if (item is null) return;
        if (item.Window != 0) { windows.Restore(item.Window); return; }
        if (!string.IsNullOrEmpty(item.Target)) launcher.Open(item.Target);
    }

    [RelayCommand] private void OpenLauncher() => launcherHost?.Toggle();
}

public sealed partial class LauncherViewModel(AppCatalog apps, ShellProfileService shell, IShellLauncher launcher) : ObservableObject
{
    private List<InstalledApp> _all = [];
    public ObservableCollection<InstalledApp> Results { get; } = [];
    public ObservableCollection<InstalledApp> Pinned { get; } = [];
    public ObservableCollection<InstalledApp> Recent { get; } = [];
    public ObservableCollection<string> Categories { get; } = [];
    public IReadOnlyList<(string Key, string Label, string Glyph)> Shortcuts => shell.ActiveStartProfile.QuickShortcuts.Select(Shortcut).ToList();

    [ObservableProperty] private StartProfile _profile = shell.ActiveStartProfile;
    [ObservableProperty] private string _query = "";
    [ObservableProperty] private string? _category;
    [ObservableProperty] private int _selectedIndex;

    partial void OnQueryChanged(string value) => Filter();
    partial void OnCategoryChanged(string? value) => Filter();

    public async Task ReloadAsync()
    {
        Profile = shell.ActiveStartProfile;
        _all = (await apps.GetAppsAsync()).ToList();
        Pinned.Clear();
        var pins = Profile.PinnedApps.Count > 0 ? Profile.PinnedApps : apps.Favorites.ToList();
        foreach (var id in pins) if (_all.FirstOrDefault(a => a.Id == id) is { } a) Pinned.Add(a);
        Recent.Clear();
        foreach (var id in apps.Recent.Take(6)) if (_all.FirstOrDefault(a => a.Id == id) is { } a) Recent.Add(a);
        Categories.Clear();
        foreach (var c in _all.Select(a => a.Category).Distinct().Order()) Categories.Add(c);
        foreach (var (name, ids) in Profile.CustomCategories) if (!Categories.Contains(name)) Categories.Add(name);
        Query = "";
        Filter();
        OnPropertyChanged(nameof(Shortcuts));
    }

    private void Filter()
    {
        var q = Query.Trim();
        IEnumerable<InstalledApp> list = _all;
        if (Category is { } c)
            list = Profile.CustomCategories.TryGetValue(c, out var ids) ? list.Where(a => ids.Contains(a.Id)) : list.Where(a => a.Category == c);
        if (q.Length > 0)
            list = list.Select(a => (a, Aether.Services.Commands.FuzzyMatcher.Match(q, a.Name)))
                .Where(x => x.Item2 is not null).OrderByDescending(x => x.Item2!.Value.Score).Select(x => x.a);
        Results.Clear();
        foreach (var a in list.Take(200)) Results.Add(a);
        SelectedIndex = Results.Count > 0 ? 0 : -1;
    }

    [RelayCommand]
    public void Launch(InstalledApp? app)
    {
        app ??= SelectedIndex >= 0 && SelectedIndex < Results.Count ? Results[SelectedIndex] : null;
        if (app is not null) apps.Launch(app);
    }

    [RelayCommand]
    private void OpenShortcut(string? key)
    {
        string? Folder(Environment.SpecialFolder f) => Environment.GetFolderPath(f);
        var target = key switch
        {
            "documents" => Folder(Environment.SpecialFolder.MyDocuments),
            "downloads" => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
            "pictures" => Folder(Environment.SpecialFolder.MyPictures),
            "music" => Folder(Environment.SpecialFolder.MyMusic),
            "videos" => Folder(Environment.SpecialFolder.MyVideos),
            "settings" => "ms-settings:",
            "explorer" => "explorer.exe",
            "terminal" => "wt.exe",
            _ => null,
        };
        if (target is not null) launcher.Open(target);
    }

    [RelayCommand]
    private void Power(string? action)
    {
        var (exe, args) = action switch
        {
            "lock" => ("rundll32.exe", "user32.dll,LockWorkStation"),
            "signout" => ("shutdown.exe", "/l"),
            "restart" => ("shutdown.exe", "/r /t 0"),
            "shutdown" => ("shutdown.exe", "/s /t 0"),
            _ => ("", ""),
        };
        if (exe.Length > 0) launcher.Open(exe, args);
    }

    private static (string Key, string Label, string Glyph) Shortcut(string key) => key switch
    {
        "documents" => (key, "Documents", ""),
        "downloads" => (key, "Downloads", ""),
        "pictures" => (key, "Pictures", ""),
        "music" => (key, "Music", ""),
        "videos" => (key, "Videos", ""),
        "settings" => (key, "Settings", ""),
        "explorer" => (key, "File Explorer", ""),
        "terminal" => (key, "Terminal", ""),
        _ => (key, key, ""),
    };
}
