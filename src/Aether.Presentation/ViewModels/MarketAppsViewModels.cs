using System.Collections.ObjectModel;
using Aether.Core.Abstractions;
using Aether.ModEngine.Manifest;
using Aether.Presentation.Infrastructure;
using Aether.Services.Applications;
using Aether.Services.Marketplace;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Aether.Presentation.ViewModels;

public sealed partial class ListingItem(MarketplaceEntry entry) : ObservableObject
{
    public MarketplaceEntry Entry { get; private set; } = entry;
    public MarketplaceListing Listing => Entry.Listing;
    public string Name => Listing.Name;
    public string Author => Listing.Author;
    public string Description => Listing.Description;
    public string Version => Listing.Version;
    public string Kind => Listing.Kind switch { ListingKind.WidgetLayout => "Widget layout", var k => k.ToString() };
    public string Glyph => Listing.Glyph ?? Listing.Kind switch { ListingKind.Theme => "", ListingKind.WidgetLayout => "", ListingKind.Plugin => "", _ => "" };
    public string Accent => Listing.AccentColor ?? "#FF7C8CFF";
    public string? Preview => Listing.Previews.FirstOrDefault();
    public bool HasPreview => Preview is not null;
    // Never fabricated: shows the provider's numbers or says they are unavailable.
    public string RatingText => Listing.Rating is { } r ? $"★ {r:0.0} ({Listing.RatingCount ?? 0})" : "Not rated";
    public string InstallsText => Listing.InstallCount is { } c ? $"{c:N0} installs" : "Install count not available";
    public string StateText => Entry.State switch { InstallState.Installed => "Installed", InstallState.UpdateAvailable => $"Update to {Version}", _ => "Install" };
    public bool CanInstall => Entry.State != InstallState.Installed;
    public bool IsInstalled => Entry.State != InstallState.NotInstalled;
    public bool HasUpdate => Entry.State == InstallState.UpdateAvailable;
    public string Source => Listing.SourceName;
    public string Updated => Listing.UpdatedAt == default ? "—" : Listing.UpdatedAt.ToLocalTime().ToString("d");
    public IReadOnlyList<string> PermissionTexts => Listing.Permissions.Select(p => ModPermissions.Describe(p).Title).ToList();
    [ObservableProperty] private bool _isFavorite = entry.IsFavorite;
    [ObservableProperty] private bool _isWorking;
    public void Update(MarketplaceEntry e) { Entry = e; IsFavorite = e.IsFavorite; OnPropertyChanged(string.Empty); }
}

public enum MarketSection { Featured, All, Mods, Themes, Widgets, Plugins, Updates, Favorites, RecentlyUpdated, NewReleases, Trending, Popular }
public enum MarketSort { Name, RecentlyUpdated, Newest, Rating }

public sealed partial class MarketplaceViewModel : PageViewModel
{
    private readonly MarketplaceService _market;
    private readonly IDialogService _dialogs;
    private List<ListingItem> _all = [];

    public MarketplaceViewModel(MarketplaceService market, IDialogService dialogs, INotificationSink notifications) : base(notifications)
    {
        _market = market; _dialogs = dialogs;
    }

    public ObservableCollection<ListingItem> Listings { get; } = [];
    public ObservableCollection<ListingItem> Featured { get; } = [];
    public ObservableCollection<PublisherProfile> Publishers { get; } = [];
    public ObservableCollection<MarketplaceSourceView> Sources { get; } = [];
    public ObservableCollection<string> SourceErrors { get; } = [];
    public IReadOnlyList<MarketSection> Sections { get; } = Enum.GetValues<MarketSection>();
    public IReadOnlyList<MarketSort> Sorts { get; } = Enum.GetValues<MarketSort>();

    [ObservableProperty] private MarketSection _section = MarketSection.Featured;
    [ObservableProperty] private MarketSort _sort = MarketSort.Name;
    [ObservableProperty] private string _search = "";
    [ObservableProperty] private ListingItem? _selected;
    [ObservableProperty] private int _updatesAvailable;
    [ObservableProperty] private string? _sectionNote;

    public bool IsEmpty => Listings.Count == 0;
    public bool HasSelection => Selected is not null;

    partial void OnSectionChanged(MarketSection value) => Apply();
    partial void OnSortChanged(MarketSort value) => Apply();
    partial void OnSearchChanged(string value) => Apply();
    partial void OnSelectedChanged(ListingItem? value) => OnPropertyChanged(nameof(HasSelection));

    protected override Task LoadAsync() => ReloadAsync();
    protected override Task RefreshAsync() => ReloadAsync();

    [RelayCommand]
    private async Task ReloadAsync()
    {
        IsBusy = true;
        try
        {
            var entries = await _market.RefreshAsync();
            _all = entries.Select(e => new ListingItem(e)).ToList();
            SourceErrors.Clear();
            foreach (var e in _market.LastErrors) SourceErrors.Add(e);
            Publishers.Clear();
            foreach (var p in _market.GetPublishers()) Publishers.Add(p);
            Sources.Clear();
            foreach (var s in _market.GetSources()) Sources.Add(new MarketplaceSourceView(s));
            Featured.Clear();
            foreach (var f in _all.Where(l => l.Listing.Featured).Take(6)) Featured.Add(f);
            UpdatesAvailable = _all.Count(l => l.HasUpdate);
            Apply();
        }
        finally { IsBusy = false; }
    }

    private void Apply()
    {
        SectionNote = null;
        IEnumerable<ListingItem> q = _all;
        q = Section switch
        {
            MarketSection.Featured => q.Where(l => l.Listing.Featured),
            MarketSection.Mods => q.Where(l => l.Listing.Kind == ListingKind.Mod),
            MarketSection.Themes => q.Where(l => l.Listing.Kind == ListingKind.Theme),
            MarketSection.Widgets => q.Where(l => l.Listing.Kind == ListingKind.WidgetLayout),
            MarketSection.Plugins => q.Where(l => l.Listing.Kind == ListingKind.Plugin),
            MarketSection.Updates => q.Where(l => l.HasUpdate),
            MarketSection.Favorites => q.Where(l => l.IsFavorite),
            MarketSection.RecentlyUpdated => q.OrderByDescending(l => l.Listing.UpdatedAt).Take(20),
            MarketSection.NewReleases => q.OrderByDescending(l => l.Listing.ReleasedAt).Take(20),
            MarketSection.Trending or MarketSection.Popular => q.Where(l => l.Listing.InstallCount is not null).OrderByDescending(l => l.Listing.InstallCount),
            _ => q,
        };
        if (Section is MarketSection.Trending or MarketSection.Popular && !q.Any())
            SectionNote = "Your sources don't publish install statistics, so AETHER can't rank by popularity. It never invents numbers.";
        var s = Search.Trim();
        if (s.Length > 0) q = q.Where(l => l.Name.Contains(s, StringComparison.OrdinalIgnoreCase) || l.Description.Contains(s, StringComparison.OrdinalIgnoreCase)
                                           || l.Author.Contains(s, StringComparison.OrdinalIgnoreCase) || l.Listing.Tags.Any(t => t.Contains(s, StringComparison.OrdinalIgnoreCase)));
        if (Section is not (MarketSection.RecentlyUpdated or MarketSection.NewReleases))
            q = Sort switch
            {
                MarketSort.RecentlyUpdated => q.OrderByDescending(l => l.Listing.UpdatedAt),
                MarketSort.Newest => q.OrderByDescending(l => l.Listing.ReleasedAt),
                MarketSort.Rating => q.OrderByDescending(l => l.Listing.Rating ?? -1),
                _ => q.OrderBy(l => l.Name),
            };
        Listings.Clear();
        foreach (var l in q) Listings.Add(l);
        OnPropertyChanged(nameof(IsEmpty));
    }

    [RelayCommand]
    private async Task InstallAsync(ListingItem? item)
    {
        item ??= Selected;
        if (item is null || item.IsWorking) return;
        item.IsWorking = true;
        try
        {
            if (item.Listing.Kind == ListingKind.Mod)
            {
                var inspection = await _market.InspectModAsync(item.Listing);
                if (!inspection.Success) { Report(inspection.ToResult()); return; }
                var i = inspection.Value!;
                var text = $"{i.Manifest.Name} {i.Manifest.Version} by {i.Manifest.Author}\n\n{i.Verification.Message}\n" +
                           $"Permissions: {(i.Manifest.Permissions.Count == 0 ? "none" : string.Join(", ", i.Manifest.Permissions.Select(p => ModPermissions.Describe(p).Title)))}\n\n{i.Summary}" +
                           (i.ValidationErrors.Count > 0 ? "\n• " + string.Join("\n• ", i.ValidationErrors) : "");
                if (!i.CanInstall) { await _dialogs.ShowTextAsync("Can't install", text); return; }
                if (!await _dialogs.ConfirmAsync(i.InstalledVersion is null ? "Install mod?" : "Update mod?", text + "\n\nInstalling doesn't enable it — you choose when it runs.", "Install")) return;
            }
            else if (item.Listing.Kind == ListingKind.Plugin &&
                     !await _dialogs.ConfirmAsync("Install plugin?", $"{item.Name} is .NET code that runs inside AETHER with the capabilities you grant. Only install plugins from publishers you trust.", "Install"))
                return;
            Report(await _market.InstallAsync(item.Listing), item.Name);
            await ReloadAsync();
        }
        finally { item.IsWorking = false; }
    }

    [RelayCommand]
    private void ToggleFavorite(ListingItem? item)
    {
        if (item is null) return;
        _market.ToggleFavorite(item.Listing);
        item.IsFavorite = !item.IsFavorite;
        if (Section == MarketSection.Favorites) Apply();
    }

    [RelayCommand]
    private void ShowPublisher(PublisherProfile? p)
    {
        if (p is null) return;
        Section = MarketSection.All;
        Search = p.Name;
    }

    [RelayCommand]
    private async Task AddSourceAsync()
    {
        var location = await _dialogs.PromptAsync("Add a source", "A folder containing catalog.json, or an HTTPS index URL");
        if (string.IsNullOrWhiteSpace(location)) return;
        var name = await _dialogs.PromptAsync("Name this source", "Name", location.StartsWith("http") ? new Uri(location).Host : Path.GetFileName(location.TrimEnd('\\', '/')));
        Report(_market.AddSource(name ?? location, location.Trim()));
        await ReloadAsync();
    }

    [RelayCommand]
    private async Task ToggleSourceAsync(MarketplaceSourceView? s)
    {
        if (s is null) return;
        _market.SetSourceEnabled(s.Source.Id, !s.Source.Enabled);
        await ReloadAsync();
    }

    [RelayCommand]
    private async Task RemoveSourceAsync(MarketplaceSourceView? s)
    {
        if (s is null || s.Source.IsBuiltIn) return;
        _market.RemoveSource(s.Source.Id);
        await ReloadAsync();
    }
}

public sealed record MarketplaceSourceView(Aether.Services.Settings.MarketplaceSource Source)
{
    public string Name => Source.Name;
    public string Location => Source.Location;
    public bool Enabled => Source.Enabled;
    public bool IsOnline => Source.Location.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
    public string Kind => IsOnline ? "Online (HTTPS)" : "Local folder";
    public bool CanRemove => !Source.IsBuiltIn;
}

public sealed partial class AppItem(InstalledApp app, bool favorite) : ObservableObject
{
    public InstalledApp App { get; } = app;
    public string Name => App.Name;
    public string Category => App.Category;
    public string SourceLabel => App.SourceLabel;
    public string Initial => App.Name.Length > 0 ? App.Name[..1].ToUpperInvariant() : "?";
    [ObservableProperty] private bool _isFavorite = favorite;
}

public sealed partial class StartupItem(StartupEntry entry) : ObservableObject
{
    public StartupEntry Entry { get; } = entry;
    public string Name => Entry.Name;
    public string Command => Entry.Command;
    public string Source => Entry.SourceLabel;
    public bool RequiresAdmin => Entry.RequiresAdmin;
    public bool CanRemove => Entry.Source == StartupSource.UserRun;
    [ObservableProperty] private bool _isEnabled = entry.Enabled;
}

public sealed partial class ApplicationsViewModel : PageViewModel
{
    private readonly AppCatalog _apps;
    private readonly StartupManager _startup;
    private readonly IDialogService _dialogs;
    private bool _suppress;

    public ApplicationsViewModel(AppCatalog apps, StartupManager startup, IDialogService dialogs, INotificationSink notifications) : base(notifications)
    {
        _apps = apps; _startup = startup; _dialogs = dialogs;
    }

    public ObservableCollection<AppItem> Apps { get; } = [];
    public ObservableCollection<AppItem> Favorites { get; } = [];
    public ObservableCollection<AppItem> Recent { get; } = [];
    public ObservableCollection<string> Categories { get; } = [];
    public ObservableCollection<StartupItem> Startup { get; } = [];
    public ObservableCollection<LaunchProfile> Profiles { get; } = [];
    public ObservableCollection<QuickLaunchGroup> Groups { get; } = [];
    private List<AppItem> _all = [];

    [ObservableProperty] private string _search = "";
    [ObservableProperty] private string _category = "All";
    [ObservableProperty] private AppItem? _selected;
    [ObservableProperty] private int _startupEnabledCount;

    public bool IsEmpty => Apps.Count == 0;

    partial void OnSearchChanged(string value) => Apply();
    partial void OnCategoryChanged(string value) => Apply();

    protected override async Task LoadAsync() => await ReloadAsync(false);
    protected override async Task RefreshAsync() => await ReloadAsync(false);

    [RelayCommand]
    private Task RescanAsync() => RunAsync(() => ReloadAsync(true));

    private async Task ReloadAsync(bool rescan)
    {
        var apps = await _apps.GetAppsAsync(rescan);
        var favs = _apps.Favorites;
        _all = apps.Select(a => new AppItem(a, favs.Contains(a.Id))).ToList();
        var selectedCategory = Category;
        Categories.Clear();
        Categories.Add("All");
        foreach (var c in _all.Select(a => a.Category).Distinct().Order()) Categories.Add(c);
        Category = Categories.Contains(selectedCategory) ? selectedCategory : "All";
        Favorites.Clear();
        foreach (var f in _all.Where(a => a.IsFavorite)) Favorites.Add(f);
        Recent.Clear();
        foreach (var id in _apps.Recent.Take(8)) if (_all.FirstOrDefault(a => a.App.Id == id) is { } r) Recent.Add(r);
        Apply();
        LoadStartup();
        Profiles.Clear();
        foreach (var p in _apps.GetProfiles()) Profiles.Add(p);
        Groups.Clear();
        foreach (var g in _apps.GetGroups()) Groups.Add(g);
    }

    private void Apply()
    {
        var q = Search.Trim();
        Apps.Clear();
        foreach (var a in _all.Where(a => (Category == "All" || a.Category == Category) && (q.Length == 0 || a.Name.Contains(q, StringComparison.OrdinalIgnoreCase))))
            Apps.Add(a);
        OnPropertyChanged(nameof(IsEmpty));
    }

    private void LoadStartup()
    {
        _suppress = true;
        Startup.Clear();
        try
        {
            foreach (var e in _startup.GetEntries())
            {
                var item = new StartupItem(e);
                item.PropertyChanged += (s, ev) => { if (ev.PropertyName == nameof(StartupItem.IsEnabled) && !_suppress) _ = ToggleStartupAsync((StartupItem)s!); };
                Startup.Add(item);
            }
        }
        catch (Exception ex) { Toast("Couldn't read startup apps", ex.Message, NotificationSeverity.Warning); }
        StartupEnabledCount = Startup.Count(s => s.IsEnabled);
        _suppress = false;
    }

    private Task ToggleStartupAsync(StartupItem item)
    {
        var r = _startup.SetEnabled(item.Entry, item.IsEnabled);
        Report(r, item.Name);
        LoadStartup();
        return Task.CompletedTask;
    }

    [RelayCommand]
    private void Launch(AppItem? item) { if ((item ?? Selected) is { } a) { var r = _apps.Launch(a.App); if (!r.Success) Report(r); } }

    [RelayCommand]
    private void LaunchElevated(AppItem? item) { if ((item ?? Selected) is { } a) { var r = _apps.Launch(a.App, elevated: true); if (!r.Success) Report(r); } }

    [RelayCommand]
    private void ToggleFavorite(AppItem? item)
    {
        if (item is null) return;
        _apps.ToggleFavorite(item.App.Id);
        item.IsFavorite = !item.IsFavorite;
        if (item.IsFavorite) Favorites.Add(item); else Favorites.Remove(item);
    }

    [RelayCommand]
    private async Task SetCategoryAsync(AppItem? item)
    {
        if (item is null) return;
        var c = await _dialogs.PromptAsync("Category", $"Category for {item.Name}", item.Category);
        if (string.IsNullOrWhiteSpace(c)) return;
        _apps.Customize(item.App.Id, _apps.GetCustomization(item.App.Id) with { Category = c.Trim() });
        await ReloadAsync(false);
    }

    [RelayCommand]
    private async Task SetIconAsync(AppItem? item)
    {
        if (item is null) return;
        var icon = await _dialogs.PickFileAsync(new FileFilter("Icons and images", ".ico", ".png", ".exe"));
        if (icon is null) return;
        _apps.Customize(item.App.Id, _apps.GetCustomization(item.App.Id) with { IconPath = icon });
        await ReloadAsync(false);
    }

    [RelayCommand]
    private async Task HideAsync(AppItem? item)
    {
        if (item is null) return;
        _apps.Customize(item.App.Id, _apps.GetCustomization(item.App.Id) with { Hidden = true });
        await ReloadAsync(false);
        Toast("Hidden", $"{item.Name} is hidden from AETHER's launcher. Rescan resets nothing — unhide it from Settings ▸ Import/export if needed.", NotificationSeverity.Info);
    }

    [RelayCommand]
    private async Task AddCustomAppAsync()
    {
        var path = await _dialogs.PickFileAsync(new FileFilter("Programs", ".exe", ".lnk", ".bat", ".cmd"));
        if (path is null) return;
        var name = await _dialogs.PromptAsync("Add application", "Display name", Path.GetFileNameWithoutExtension(path));
        if (string.IsNullOrWhiteSpace(name)) return;
        _apps.AddCustomApp(name, path, null, "Custom");
        await ReloadAsync(true);
    }

    [RelayCommand]
    private async Task AddStartupAsync()
    {
        var path = await _dialogs.PickFileAsync(new FileFilter("Programs", ".exe"));
        if (path is null) return;
        var name = await _dialogs.PromptAsync("Start at sign-in", "Entry name", Path.GetFileNameWithoutExtension(path));
        if (string.IsNullOrWhiteSpace(name)) return;
        Report(_startup.AddUserEntry(name, $"\"{path}\""));
        LoadStartup();
    }

    [RelayCommand]
    private async Task RemoveStartupAsync(StartupItem? item)
    {
        if (item is null) return;
        if (!await _dialogs.ConfirmAsync($"Remove {item.Name} from startup?", "The entry is deleted. To keep it but stop it from starting, just switch it off instead.", "Remove", destructive: true)) return;
        Report(_startup.RemoveUserEntry(item.Entry));
        LoadStartup();
    }

    [RelayCommand]
    private async Task NewProfileAsync(AppItem? item)
    {
        var target = item?.App.LaunchTarget ?? await _dialogs.PickFileAsync(new FileFilter("Programs", ".exe", ".lnk"));
        if (target is null) return;
        var name = await _dialogs.PromptAsync("New launch profile", "Name", item?.Name ?? Path.GetFileNameWithoutExtension(target));
        if (string.IsNullOrWhiteSpace(name)) return;
        var args = await _dialogs.PromptAsync("Launch arguments", "Arguments (optional)", "");
        var admin = await _dialogs.ConfirmAsync("Run as administrator?", "Should this profile start the app with administrator rights?", "Yes, elevate");
        _apps.SaveProfile(new LaunchProfile { Id = "lp-" + Guid.NewGuid().ToString("N")[..8], Name = name, Target = target, Arguments = string.IsNullOrWhiteSpace(args) ? null : args, RunAsAdministrator = admin });
        await ReloadAsync(false);
    }

    [RelayCommand] private void LaunchProfile(LaunchProfile? p) { if (p is not null) Report(_apps.Launch(p)); }
    [RelayCommand] private async Task DeleteProfileAsync(LaunchProfile? p) { if (p is not null) { _apps.DeleteProfile(p.Id); await ReloadAsync(false); } }

    [RelayCommand]
    private async Task NewGroupFromFavoritesAsync()
    {
        if (Favorites.Count == 0) { Toast("No favorites yet", "Star a few apps first; a group launches them together.", NotificationSeverity.Info); return; }
        var name = await _dialogs.PromptAsync("New quick launch group", "Name", "Morning setup");
        if (string.IsNullOrWhiteSpace(name)) return;
        _apps.SaveGroup(new QuickLaunchGroup { Id = "grp-" + Guid.NewGuid().ToString("N")[..8], Name = name, Items = Favorites.Select(f => f.App.Id).ToList() });
        await ReloadAsync(false);
    }

    [RelayCommand] private Task LaunchGroupAsync(QuickLaunchGroup? g) => g is null ? Task.CompletedTask : RunAsync(async () => Report(await _apps.LaunchGroupAsync(g)));
    [RelayCommand] private async Task DeleteGroupAsync(QuickLaunchGroup? g) { if (g is not null) { _apps.DeleteGroup(g.Id); await ReloadAsync(false); } }
}
