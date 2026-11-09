using System.Collections.ObjectModel;
using Aether.Core.Abstractions;
using Aether.Core.Configuration;
using Aether.ModEngine.Lifecycle;
using Aether.Presentation.Infrastructure;
using Aether.Presentation.Items;
using Aether.Services.Platform;
using Aether.Services.Profiles;
using Aether.ThemeEngine;
using Aether.WidgetEngine;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Aether.Presentation.ViewModels;

public sealed partial class WidgetInstanceItem(WidgetInstance instance, WidgetDefinition definition) : ObservableObject
{
    public WidgetInstance Instance { get; set; } = instance;
    public WidgetDefinition Definition { get; } = definition;
    public string Name => Definition.Name;
    public string Glyph => Definition.Glyph;
    public string Summary => $"{Instance.Width:0}×{Instance.Height:0} at {Instance.X:0},{Instance.Y:0}" + (Instance.AlwaysOnTop ? " · on top" : "") + (Instance.Visible ? "" : " · hidden");
}

public sealed partial class WidgetsViewModel : PageViewModel
{
    private readonly WidgetManager _widgets;
    private readonly IDialogService _dialogs;
    private readonly IWidgetHost? _host;
    private readonly ModInteraction _interaction;
    private readonly ModManager _mods;

    public WidgetsViewModel(WidgetManager widgets, IDialogService dialogs, ModManager mods, ModInteraction interaction, IUiDispatcher ui, INotificationSink notifications, IWidgetHost? host = null) : base(notifications)
    {
        _widgets = widgets; _dialogs = dialogs; _host = host; _mods = mods; _interaction = interaction;
        widgets.LayoutChanged += () => ui.Post(Reload);
    }

    public IReadOnlyList<WidgetDefinition> Catalog => WidgetEngine.WidgetCatalog.All;
    public ObservableCollection<WidgetInstanceItem> Widgets { get; } = [];
    public ObservableCollection<WidgetLayout> Layouts { get; } = [];

    [ObservableProperty] private WidgetLayout? _activeLayout;
    [ObservableProperty] private WidgetInstanceItem? _selected;
    [ObservableProperty] private SettingsFormViewModel? _widgetForm;
    [ObservableProperty] private string _background = "#FF10162A";
    [ObservableProperty] private double _opacity = 0.82;
    [ObservableProperty] private double _cornerRadius = 18;
    [ObservableProperty] private string _textColor = "#FFF2F5FF";
    [ObservableProperty] private string _accentColor = "#FF7C8CFF";
    [ObservableProperty] private string _fontFamily = "Segoe UI Variable";
    [ObservableProperty] private bool _blur = true;
    [ObservableProperty] private bool _alwaysOnTop;
    [ObservableProperty] private bool _visible = true;
    [ObservableProperty] private bool _locked;
    [ObservableProperty] private double _refreshSeconds = 2;
    [ObservableProperty] private double _width;
    [ObservableProperty] private double _height;
    [ObservableProperty] private bool _hostRunning;

    public bool HasSelection => Selected is not null;
    public bool IsEmpty => Widgets.Count == 0;
    public IReadOnlyList<string> Fonts { get; } = ["Segoe UI Variable", "Segoe UI", "Bahnschrift", "Cascadia Code", "Consolas", "Georgia", "Candara"];

    protected override Task LoadAsync() { Reload(); return Task.CompletedTask; }
    protected override Task RefreshAsync() { Reload(); return Task.CompletedTask; }

    private void Reload()
    {
        var selectedId = Selected?.Instance.Id;
        Layouts.Clear();
        foreach (var l in _widgets.GetLayouts()) Layouts.Add(l);
        ActiveLayout = Layouts.FirstOrDefault(l => l.Id == _widgets.ActiveLayoutId);
        Widgets.Clear();
        foreach (var w in _widgets.ActiveLayout.Widgets)
            if (WidgetEngine.WidgetCatalog.Find(w.TypeId) is { } d) Widgets.Add(new WidgetInstanceItem(w, d));
        Selected = Widgets.FirstOrDefault(w => w.Instance.Id == selectedId);
        HostRunning = _host?.IsRunning == true;
        OnPropertyChanged(nameof(IsEmpty));
    }

    partial void OnSelectedChanged(WidgetInstanceItem? value)
    {
        OnPropertyChanged(nameof(HasSelection));
        if (value is null) { WidgetForm = null; return; }
        var a = value.Instance.Appearance;
        Background = a.Background; Opacity = a.Opacity; CornerRadius = a.CornerRadius; TextColor = a.TextColor; AccentColor = a.AccentColor;
        FontFamily = a.FontFamily; Blur = a.Blur; AlwaysOnTop = value.Instance.AlwaysOnTop; Visible = value.Instance.Visible; Locked = value.Instance.Locked;
        RefreshSeconds = value.Instance.RefreshSeconds; Width = value.Instance.Width; Height = value.Instance.Height;
        WidgetForm = new SettingsFormViewModel(value.Definition.Settings, value.Instance.Settings);
    }

    partial void OnActiveLayoutChanged(WidgetLayout? value)
    {
        if (value is not null && value.Id != _widgets.ActiveLayoutId) _widgets.ActivateLayout(value.Id);
    }

    [RelayCommand]
    private void Add(WidgetDefinition? d)
    {
        if (d is null) return;
        if (d.RequiresNetwork) Toast(d.Name, $"Data source: {d.DataSource}.", NotificationSeverity.Info);
        var w = _widgets.AddWidget(d.TypeId);
        Reload();
        Selected = Widgets.FirstOrDefault(x => x.Instance.Id == w.Id);
        if (_host?.IsRunning != true) Toast("Widget added", "Turn on 'Show widgets on the desktop' to see it.", NotificationSeverity.Info);
    }

    [RelayCommand]
    private void SaveSelected()
    {
        if (Selected is null) return;
        if (!ColorValue.TryParse(Background, out _) || !ColorValue.TryParse(TextColor, out _) || !ColorValue.TryParse(AccentColor, out _))
        { Toast("Invalid color", "Use #RRGGBB or #AARRGGBB.", NotificationSeverity.Warning); return; }
        var updated = Selected.Instance with
        {
            Appearance = new WidgetAppearance { Background = Background, Opacity = Opacity, CornerRadius = CornerRadius, TextColor = TextColor, AccentColor = AccentColor, FontFamily = FontFamily, Blur = Blur },
            AlwaysOnTop = AlwaysOnTop, Visible = Visible, Locked = Locked, RefreshSeconds = (int)Math.Clamp(RefreshSeconds, 1, 3600),
            Width = Math.Max(Selected.Definition.MinWidth, Width), Height = Math.Max(Selected.Definition.MinHeight, Height),
            Settings = WidgetForm?.ToJson() ?? Selected.Instance.Settings,
        };
        _widgets.UpdateWidget(updated);
        Toast("Widget saved", $"{Selected.Name} updated.");
    }

    [RelayCommand]
    private void Remove(WidgetInstanceItem? item)
    {
        item ??= Selected;
        if (item is null) return;
        _widgets.RemoveWidget(item.Instance.Id);
        Selected = null;
    }

    [RelayCommand]
    private async Task NewLayoutAsync()
    {
        var name = await _dialogs.PromptAsync("New widget layout", "Name", "Work");
        if (string.IsNullOrWhiteSpace(name)) return;
        var copy = await _dialogs.ConfirmAsync("Start from current layout?", "Copy the widgets of the current layout into the new one?", "Copy widgets");
        var l = _widgets.CreateLayout(name, copy);
        _widgets.ActivateLayout(l.Id);
    }

    [RelayCommand]
    private async Task RenameLayoutAsync()
    {
        if (ActiveLayout is null) return;
        var name = await _dialogs.PromptAsync("Rename layout", "Name", ActiveLayout.Name);
        if (!string.IsNullOrWhiteSpace(name)) _widgets.RenameLayout(ActiveLayout.Id, name);
    }

    [RelayCommand]
    private async Task DeleteLayoutAsync()
    {
        if (ActiveLayout is null) return;
        if (!await _dialogs.ConfirmAsync($"Delete '{ActiveLayout.Name}'?", "Its widgets are removed.", "Delete", destructive: true)) return;
        if (!_widgets.DeleteLayout(ActiveLayout.Id)) Toast("Can't delete", "You need at least one layout.", NotificationSeverity.Warning);
    }

    [RelayCommand]
    private async Task ExportLayoutAsync()
    {
        if (ActiveLayout is null) return;
        var path = await _dialogs.SaveFileAsync($"{ActiveLayout.Name}.aetherwidgets.json", new FileFilter("Widget layout", ".json"));
        if (path is null) return;
        await File.WriteAllTextAsync(path, _widgets.ExportLayout(ActiveLayout.Id));
        Toast("Layout exported", Path.GetFileName(path));
    }

    [RelayCommand]
    private async Task ImportLayoutAsync()
    {
        var path = await _dialogs.PickFileAsync(new FileFilter("Widget layout", ".json"));
        if (path is null) return;
        try { var l = _widgets.ImportLayout(await File.ReadAllTextAsync(path)); Toast("Layout imported", $"'{l.Name}' with {l.Widgets.Count} widgets."); }
        catch (Exception ex) { Toast("Not a widget layout", ex.Message, NotificationSeverity.Error); }
    }

    [RelayCommand]
    private async Task ToggleHostAsync()
    {
        var mod = _mods.GetMod("aether.widget-manager");
        if (mod is null) return;
        await _interaction.SetEnabledAsync(mod.Id, !mod.IsEnabled);
        HostRunning = _host?.IsRunning == true;
    }
}

public sealed partial class ThemeItem(ThemePack theme, bool active) : ObservableObject
{
    public ThemePack Theme { get; } = theme;
    public string Name => Theme.Name;
    public string Description => Theme.Description;
    public string Author => Theme.Author;
    public bool BuiltIn => Theme.BuiltIn;
    public string Background => Theme.Palette.Background;
    public string Gradient => Theme.Palette.BackgroundGradient;
    public string Surface => Theme.Palette.Surface;
    public string Accent => Theme.Palette.Accent;
    public string Accent2 => Theme.Palette.AccentSecondary;
    public string Text => Theme.Palette.TextPrimary;
    public string Muted => Theme.Palette.TextMuted;
    public string ModeLabel => Theme.Palette.IsDark ? "Dark" : "Light";
    [ObservableProperty] private bool _isActive = active;
}

public sealed partial class ThemesViewModel : PageViewModel
{
    private readonly ThemeManager _themes;
    private readonly IDialogService _dialogs;
    private readonly IAppPaths _paths;
    private readonly ShellProfileService _shell;
    private readonly WidgetManager _widgets;
    private readonly ICursorService _cursors;
    private readonly Aether.Services.Settings.ISettingsService _settings;

    public ThemesViewModel(ThemeManager themes, IDialogService dialogs, IAppPaths paths, ShellProfileService shell, WidgetManager widgets, ICursorService cursors,
        Aether.Services.Settings.ISettingsService settings, IUiDispatcher ui, INotificationSink notifications) : base(notifications)
    {
        _themes = themes; _dialogs = dialogs; _paths = paths; _shell = shell; _widgets = widgets; _cursors = cursors; _settings = settings;
        themes.ThemesChanged += () => ui.Post(Reload);
        themes.ThemeApplied += _ => ui.Post(Reload);
    }

    public ObservableCollection<ThemeItem> Themes { get; } = [];
    public ObservableCollection<string> TaskbarProfiles { get; } = [];
    public ObservableCollection<string> StartProfiles { get; } = [];
    public ObservableCollection<string> WidgetLayouts { get; } = [];
    public ObservableCollection<string> CursorSchemes { get; } = [];
    public IReadOnlyList<ThemeMode> Modes { get; } = Enum.GetValues<ThemeMode>();
    public IReadOnlyList<WallpaperFit> Fits { get; } = Enum.GetValues<WallpaperFit>();

    [ObservableProperty] private ThemeItem? _selected;
    public bool CanDelete => Selected is { BuiltIn: false };
    partial void OnSelectedChanged(ThemeItem? value) => OnPropertyChanged(nameof(CanDelete));
    [ObservableProperty] private bool _isEditing;
    [ObservableProperty] private bool _canRollback;
    // Editor fields
    [ObservableProperty] private string _editName = "";
    [ObservableProperty] private string _editDescription = "";
    [ObservableProperty] private string _editBackground = "";
    [ObservableProperty] private string _editGradient = "";
    [ObservableProperty] private string _editSurface = "";
    [ObservableProperty] private string _editAccent = "";
    [ObservableProperty] private string _editAccent2 = "";
    [ObservableProperty] private string _editText = "";
    [ObservableProperty] private bool _editIsDark = true;
    [ObservableProperty] private double _editGlass = 0.62;
    [ObservableProperty] private ThemeMode _editAppsMode;
    [ObservableProperty] private ThemeMode _editSystemMode;
    [ObservableProperty] private bool _editTransparency = true;
    [ObservableProperty] private bool _editApplyAccent = true;
    [ObservableProperty] private string? _editWallpaper;
    [ObservableProperty] private WallpaperFit _editFit;
    [ObservableProperty] private string? _editCursor;
    [ObservableProperty] private string? _editTaskbarProfile;
    [ObservableProperty] private string? _editStartProfile;
    [ObservableProperty] private string? _editWidgetLayout;
    [ObservableProperty] private string _editFont = "Segoe UI Variable";
    // Schedule
    [ObservableProperty] private bool _scheduleEnabled;
    [ObservableProperty] private bool _scheduleSun;
    [ObservableProperty] private string _lightAt = "07:00";
    [ObservableProperty] private string _darkAt = "19:00";
    [ObservableProperty] private ThemeItem? _lightTheme;
    [ObservableProperty] private ThemeItem? _darkTheme;

    protected override Task LoadAsync()
    {
        Reload();
        foreach (var p in _shell.GetTaskbarProfiles()) TaskbarProfiles.Add(p.Id);
        foreach (var p in _shell.GetStartProfiles()) StartProfiles.Add(p.Id);
        foreach (var l in _widgets.GetLayouts()) WidgetLayouts.Add(l.Id);
        try { foreach (var c in _cursors.GetSchemes()) CursorSchemes.Add(c.Name); } catch (Exception) { }
        var s = _themes.Schedule;
        ScheduleEnabled = s.Enabled; ScheduleSun = s.Mode == ScheduleMode.SunriseSunset; LightAt = s.LightAt; DarkAt = s.DarkAt;
        LightTheme = Themes.FirstOrDefault(t => t.Theme.Id == s.LightThemeId);
        DarkTheme = Themes.FirstOrDefault(t => t.Theme.Id == s.DarkThemeId);
        return Task.CompletedTask;
    }

    private void Reload()
    {
        var sel = Selected?.Theme.Id;
        Themes.Clear();
        foreach (var t in _themes.GetThemes()) Themes.Add(new ThemeItem(t, t.Id == _themes.ActiveThemeId));
        Selected = Themes.FirstOrDefault(t => t.Theme.Id == sel) ?? Themes.FirstOrDefault(t => t.IsActive);
        CanRollback = _themes.CanRollback;
    }

    [RelayCommand]
    private Task ApplyAsync(ThemeItem? item) => (item ?? Selected) is not { } t ? Task.CompletedTask : RunAsync(async () =>
    {
        var applyWindows = await _dialogs.ConfirmAsync($"Apply '{t.Name}'?",
            "AETHER will restyle itself and apply the theme's Windows settings (light/dark mode, accent, transparency" +
            (t.Theme.Wallpaper is not null ? ", wallpaper" : "") + (t.Theme.CursorScheme is not null ? ", cursors" : "") +
            "). A rollback snapshot is taken first.", "Apply theme");
        if (!applyWindows) return;
        Report(await _themes.ApplyAsync(t.Theme.Id), "Theme");
        Reload();
    });

    [RelayCommand]
    private void PreviewInApp(ThemeItem? item)
    {
        if ((item ?? Selected) is not { } t) return;
        _themes.SetActiveAppTheme(t.Theme.Id);
        Toast("AETHER appearance", $"AETHER now uses '{t.Name}'. Windows settings were not changed.");
    }

    [RelayCommand]
    private void Rollback() { Report(_themes.Rollback(), "Theme rollback"); Reload(); }

    [RelayCommand]
    private async Task DuplicateAsync(ThemeItem? item)
    {
        if ((item ?? Selected) is not { } t) return;
        var name = await _dialogs.PromptAsync("Duplicate theme", "Name", t.Name + " custom");
        if (string.IsNullOrWhiteSpace(name)) return;
        var copy = _themes.Duplicate(t.Theme.Id, name);
        Reload();
        Selected = Themes.FirstOrDefault(x => x.Theme.Id == copy.Id);
        BeginEdit();
    }

    [RelayCommand]
    private void BeginEdit()
    {
        if (Selected is null) return;
        if (Selected.BuiltIn) { Toast("Built-in themes are read-only", "Duplicate it to make your own version.", NotificationSeverity.Info); return; }
        var t = Selected.Theme;
        EditName = t.Name; EditDescription = t.Description; EditBackground = t.Palette.Background; EditGradient = t.Palette.BackgroundGradient;
        EditSurface = t.Palette.Surface; EditAccent = t.Palette.Accent; EditAccent2 = t.Palette.AccentSecondary; EditText = t.Palette.TextPrimary;
        EditIsDark = t.Palette.IsDark; EditGlass = t.Palette.GlassOpacity; EditAppsMode = t.Windows.AppsMode; EditSystemMode = t.Windows.SystemMode;
        EditTransparency = t.Windows.Transparency ?? true; EditApplyAccent = t.Windows.AccentColor is not null; EditWallpaper = t.Wallpaper?.Path;
        EditFit = t.Wallpaper?.Fit ?? WallpaperFit.Fill; EditCursor = t.CursorScheme; EditTaskbarProfile = t.TaskbarProfileId; EditStartProfile = t.StartProfileId;
        EditWidgetLayout = t.WidgetLayoutId; EditFont = t.Typography.FontFamily;
        IsEditing = true;
    }

    [RelayCommand] private void CancelEdit() => IsEditing = false;

    [RelayCommand]
    private void SaveEdit()
    {
        if (Selected is null) return;
        foreach (var c in new[] { EditBackground, EditGradient, EditSurface, EditAccent, EditAccent2, EditText })
            if (!ColorValue.TryParse(c, out _)) { Toast("Invalid color", $"'{c}' is not a color.", NotificationSeverity.Warning); return; }
        var t = Selected.Theme;
        var palette = t.Palette with
        {
            Background = EditBackground, BackgroundGradient = EditGradient, Surface = EditSurface,
            SurfaceElevated = ColorValue.Parse(EditSurface).Lerp(new ColorValue(255, 255, 255, 255), EditIsDark ? 0.05 : 0).ToHex(),
            Accent = EditAccent, AccentSecondary = EditAccent2, TextPrimary = EditText, IsDark = EditIsDark, GlassOpacity = EditGlass,
        };
        var saved = _themes.Save(t with
        {
            Name = EditName, Description = EditDescription, Palette = palette, Typography = t.Typography with { FontFamily = EditFont },
            Windows = new WindowsPersonalization { AppsMode = EditAppsMode, SystemMode = EditSystemMode, Transparency = EditTransparency, AccentColor = EditApplyAccent ? EditAccent : null },
            Wallpaper = string.IsNullOrWhiteSpace(EditWallpaper) ? null : new ThemeWallpaper { Path = EditWallpaper, Fit = EditFit },
            CursorScheme = string.IsNullOrWhiteSpace(EditCursor) ? null : EditCursor,
            TaskbarProfileId = EditTaskbarProfile, StartProfileId = EditStartProfile, WidgetLayoutId = EditWidgetLayout,
        });
        IsEditing = false;
        Reload();
        Selected = Themes.FirstOrDefault(x => x.Theme.Id == saved.Id);
        Toast("Theme saved", $"'{saved.Name}' updated.");
    }

    [RelayCommand]
    private async Task PickWallpaperAsync()
    {
        var p = await _dialogs.PickFileAsync(new FileFilter("Images", ".jpg", ".jpeg", ".png", ".bmp", ".webp"));
        if (p is not null) EditWallpaper = p;
    }

    [RelayCommand]
    private async Task DeleteAsync(ThemeItem? item)
    {
        if ((item ?? Selected) is not { BuiltIn: false } t) return;
        if (!await _dialogs.ConfirmAsync($"Delete '{t.Name}'?", "This can't be undone (unless you have a backup).", "Delete", destructive: true)) return;
        _themes.Delete(t.Theme.Id);
    }

    [RelayCommand]
    private async Task ExportAsync(ThemeItem? item)
    {
        if ((item ?? Selected) is not { } t) return;
        var path = await _dialogs.SaveFileAsync(t.Name + ThemePack.FileExtension, new FileFilter("AETHER theme", ThemePack.FileExtension));
        if (path is null) return;
        await RunAsync(async () =>
        {
            await using var fs = File.Create(path);
            _themes.Export(t.Theme.Id, fs);
            Toast("Theme exported", Path.GetFileName(path));
        });
    }

    [RelayCommand]
    private async Task ImportAsync()
    {
        var path = await _dialogs.PickFileAsync(new FileFilter("AETHER theme", ThemePack.FileExtension, ".zip"));
        if (path is null) return;
        await using var fs = File.OpenRead(path);
        var r = _themes.Import(fs, _paths.Themes);
        Report(r.ToResult(), "Theme import");
    }

    [RelayCommand]
    private void SaveSchedule()
    {
        if (!TimeOnly.TryParse(LightAt, out _) || !TimeOnly.TryParse(DarkAt, out _)) { Toast("Invalid time", "Use HH:mm.", NotificationSeverity.Warning); return; }
        _themes.Schedule = new ThemeSchedule
        {
            Enabled = ScheduleEnabled, Mode = ScheduleSun ? ScheduleMode.SunriseSunset : ScheduleMode.FixedTimes, LightAt = LightAt, DarkAt = DarkAt,
            LightThemeId = LightTheme?.Theme.Id ?? "minimal-light", DarkThemeId = DarkTheme?.Theme.Id ?? "midnight",
            Latitude = _settings.Current.Latitude, Longitude = _settings.Current.Longitude,
        };
        var (now, next) = ThemeScheduleCalculator.Evaluate(_themes.Schedule, DateTimeOffset.Now);
        Toast("Theme schedule saved", ScheduleEnabled ? $"Now: {_themes.GetTheme(now)?.Name}. Next change {next:ddd HH:mm}." : "Scheduling is off.");
    }
}
