namespace Aether.Presentation.Infrastructure;

public enum PageKey
{
    Overview, Personalization, Taskbar, StartMenu, FileExplorer, Mods, Desktop, Widgets,
    Themes, Marketplace, Applications, System, Automation, Backups, Settings,
}

public sealed record NavigationItem(PageKey Key, string Title, string Glyph, string Description);

public static class NavigationCatalog
{
    public static IReadOnlyList<NavigationItem> All { get; } =
    [
        new(PageKey.Overview, "Overview", "", "Your desktop at a glance"),
        new(PageKey.Personalization, "Personalization", "", "Colors, accent, wallpaper and cursors"),
        new(PageKey.Taskbar, "Taskbar", "", "Native taskbar settings, profiles and the AETHER dock"),
        new(PageKey.StartMenu, "Start Menu", "", "Start settings and the AETHER launcher"),
        new(PageKey.FileExplorer, "File Explorer", "", "Explorer behavior, appearance and context menu"),
        new(PageKey.Mods, "Windows Mods", "", "Install, configure and manage modifications"),
        new(PageKey.Desktop, "Desktop", "", "Desktop icons, focus and window management"),
        new(PageKey.Widgets, "Widgets", "", "Desktop widgets and layouts"),
        new(PageKey.Themes, "Themes", "", "Theme packs, editing and scheduling"),
        new(PageKey.Marketplace, "Marketplace", "", "Discover mods, themes, widgets and plugins"),
        new(PageKey.Applications, "Applications", "", "Apps, startup, launch profiles and groups"),
        new(PageKey.System, "System", "", "Performance, privacy, notifications and more"),
        new(PageKey.Automation, "Automation", "", "Rules that customize Windows for you"),
        new(PageKey.Backups, "Backups", "", "Backups, restore points and change history"),
        new(PageKey.Settings, "Settings", "", "Preferences for AETHER itself"),
    ];

    public static NavigationItem Get(PageKey key) => All.First(i => i.Key == key);
}

public interface INavigationService
{
    PageKey Current { get; }
    void Navigate(PageKey key, object? parameter = null);
    bool CanGoBack { get; }
    void GoBack();
    event Action<PageKey, object?>? Navigated;
}

/// <summary>Navigation state independent of the UI frame; the shell listens to <see cref="Navigated"/>.</summary>
public sealed class NavigationService : INavigationService
{
    private readonly Stack<(PageKey, object?)> _history = new();
    public PageKey Current { get; private set; } = PageKey.Overview;
    public event Action<PageKey, object?>? Navigated;
    public bool CanGoBack => _history.Count > 0;

    public void Navigate(PageKey key, object? parameter = null)
    {
        if (key == Current && parameter is null) return;
        _history.Push((Current, null));
        Current = key;
        Navigated?.Invoke(key, parameter);
    }

    public void GoBack()
    {
        if (!_history.TryPop(out var prev)) return;
        Current = prev.Item1;
        Navigated?.Invoke(prev.Item1, prev.Item2);
    }
}
