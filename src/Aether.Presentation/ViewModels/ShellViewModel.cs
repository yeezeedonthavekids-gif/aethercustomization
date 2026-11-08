using System.Collections.ObjectModel;
using Aether.Core.Abstractions;
using Aether.Presentation.Infrastructure;
using Aether.Services.Commands;
using Aether.Services.Settings;
using Aether.ThemeEngine;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Aether.Presentation.ViewModels;

public sealed partial class NavItemViewModel(NavigationItem item) : ObservableObject
{
    public NavigationItem Item { get; } = item;
    public PageKey Key => Item.Key;
    public string Title => Item.Title;
    public string Glyph => Item.Glyph;
    public string Description => Item.Description;
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private string? _badge;
    public bool HasBadge => !string.IsNullOrEmpty(Badge);
    partial void OnBadgeChanged(string? value) => OnPropertyChanged(nameof(HasBadge));
}

public sealed partial class ToastViewModel(AppNotification n) : ObservableObject
{
    public AppNotification Notification => n;
    public string Title => n.Title;
    public string Message => n.Message;
    public NotificationSeverity Severity => n.Severity;
    public string Glyph => n.Severity switch { NotificationSeverity.Success => "", NotificationSeverity.Warning => "", NotificationSeverity.Error => "", _ => "" };
    public DateTimeOffset At { get; } = DateTimeOffset.Now;
    public bool HasAction => n.Action is not null;
    public string ActionLabel => n.ActionLabel ?? "";
}

/// <summary>Sidebar, page title/breadcrumb, toasts, notification history and command palette host.</summary>
public sealed partial class ShellViewModel : ObservableObject
{
    private readonly INavigationService _navigation;
    private readonly ISettingsService _settings;
    private readonly IUiDispatcher _ui;
    private List<NavItemViewModel> _all = [];

    public ShellViewModel(INavigationService navigation, ISettingsService settings, NotificationHub hub, IUiDispatcher ui,
        ThemeManager themes, ISystemEnvironment environment, CommandPaletteViewModel palette)
    {
        _navigation = navigation; _settings = settings; _ui = ui;
        Palette = palette;
        Themes = themes;
        IsAdministrator = environment.IsAdministrator;
        WindowsLabel = environment.IsWindows ? $"{environment.Version.FriendlyName} · {environment.Version.DisplayVersion} · build {environment.Version.BuildString}" : "Not running on Windows";
        IsCompact = settings.Current.Sidebar == SidebarMode.Compact;
        BuildNavigation();
        navigation.Navigated += (key, _) => OnNavigated(key);
        hub.Attach(n => _ui.Post(() => Push(n)));
        settings.Changed += (b, a) => _ui.Post(() =>
        {
            if (!b.SidebarOrder.SequenceEqual(a.SidebarOrder) || !b.HiddenPages.SequenceEqual(a.HiddenPages)) BuildNavigation();
        });
    }

    public CommandPaletteViewModel Palette { get; }
    public ThemeManager Themes { get; }
    public ObservableCollection<NavItemViewModel> NavItems { get; } = [];
    public ObservableCollection<ToastViewModel> Toasts { get; } = [];
    public ObservableCollection<ToastViewModel> NotificationHistory { get; } = [];
    public bool IsAdministrator { get; }
    public string WindowsLabel { get; }

    [ObservableProperty] private bool _isCompact;
    [ObservableProperty] private string _navFilter = "";
    [ObservableProperty] private string _pageTitle = "Overview";
    [ObservableProperty] private string _pageDescription = "";
    [ObservableProperty] private string _pageGlyph = "";
    [ObservableProperty] private int _unreadNotifications;

    public bool CanGoBack => _navigation.CanGoBack;

    partial void OnIsCompactChanged(bool value) =>
        _settings.Update(s => s with { Sidebar = value ? SidebarMode.Compact : SidebarMode.Expanded });

    partial void OnNavFilterChanged(string value) => ApplyFilter();

    private void BuildNavigation()
    {
        var order = _settings.Current.SidebarOrder;
        var hidden = _settings.Current.HiddenPages.ToHashSet();
        _all = NavigationCatalog.All
            .Where(i => !hidden.Contains(i.Key.ToString()) || i.Key is PageKey.Overview or PageKey.Settings)
            .OrderBy(i => order.IndexOf(i.Key.ToString()) is var ix && ix >= 0 ? ix : 100 + (int)i.Key)
            .Select(i => new NavItemViewModel(i)).ToList();
        ApplyFilter();
        OnNavigated(_navigation.Current);
    }

    private void ApplyFilter()
    {
        NavItems.Clear();
        var q = NavFilter.Trim();
        foreach (var i in _all.Where(i => q.Length == 0 || i.Title.Contains(q, StringComparison.OrdinalIgnoreCase) || i.Description.Contains(q, StringComparison.OrdinalIgnoreCase)))
            NavItems.Add(i);
    }

    private void OnNavigated(PageKey key)
    {
        foreach (var i in _all) i.IsSelected = i.Key == key;
        var item = NavigationCatalog.Get(key);
        PageTitle = item.Title;
        PageDescription = item.Description;
        PageGlyph = item.Glyph;
        OnPropertyChanged(nameof(CanGoBack));
        _settings.Update(s => s with { LastPage = key.ToString() });
    }

    public void SetBadge(PageKey key, string? badge)
    {
        if (_all.FirstOrDefault(i => i.Key == key) is { } item) item.Badge = badge;
    }

    [RelayCommand] private void Navigate(PageKey key) => _navigation.Navigate(key);
    [RelayCommand] private void GoBack() => _navigation.GoBack();
    [RelayCommand] private void ToggleSidebar() => IsCompact = !IsCompact;
    [RelayCommand] private void OpenPalette() => Palette.Open();
    [RelayCommand] private void DismissToast(ToastViewModel? t) { if (t is not null) Toasts.Remove(t); }
    [RelayCommand] private void ClearNotifications() { NotificationHistory.Clear(); UnreadNotifications = 0; }
    [RelayCommand] private void MarkNotificationsRead() => UnreadNotifications = 0;

    public void MoveNavItem(PageKey key, int delta)
    {
        var order = _all.Select(i => i.Key.ToString()).ToList();
        var idx = order.IndexOf(key.ToString());
        var target = Math.Clamp(idx + delta, 0, order.Count - 1);
        if (idx < 0 || target == idx) return;
        order.RemoveAt(idx);
        order.Insert(target, key.ToString());
        _settings.Update(s => s with { SidebarOrder = order });
    }

    private void Push(AppNotification n)
    {
        var t = new ToastViewModel(n);
        NotificationHistory.Insert(0, t);
        while (NotificationHistory.Count > 100) NotificationHistory.RemoveAt(NotificationHistory.Count - 1);
        UnreadNotifications++;
        if (!_settings.Current.ShowToasts && n.Severity != NotificationSeverity.Error) return;
        Toasts.Add(t);
        while (Toasts.Count > 4) Toasts.RemoveAt(0);
        var delay = n.Severity == NotificationSeverity.Error ? 9000 : n.Message.Length > 120 ? 7000 : 4500;
        _ = Task.Delay(delay).ContinueWith(_ => _ui.Post(() => Toasts.Remove(t)), TaskScheduler.Default);
    }
}
