using Aether.Presentation.Infrastructure;
using Aether.Presentation.ViewModels;
using Aether.Services.Settings;
using Aether.ThemeEngine;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Aether.App.Controls.Ui;

namespace Aether.App.Views;

/// <summary>Settings for AETHER itself, generated per section from <see cref="AppSettings"/>.</summary>
public sealed class SettingsPage : UserControl
{
    private readonly SettingsViewModel _vm;
    private readonly StackPanel _content = new() { Spacing = 12, Padding = new Thickness(0, 0, 12, 24) };

    public SettingsPage(SettingsViewModel vm)
    {
        _vm = vm;
        var grid = new Grid { Padding = new Thickness(36, 8, 24, 24), ColumnSpacing = 20 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        var list = new ListView { ItemsSource = vm.Sections, SelectedItem = vm.Section };
        list.SelectionChanged += (_, _) => { if (list.SelectedItem is SettingsSection s) { vm.Section = s; Build(); } };
        var scroll = new ScrollViewer { Content = _content };
        Grid.SetColumn(scroll, 1);
        grid.Children.Add(list);
        grid.Children.Add(scroll);
        Content = grid;
        vm.PropertyChanged += (_, e) => { if (e.PropertyName is nameof(SettingsViewModel.Current) && !_building) DispatcherQueue.TryEnqueue(Build); };
        Loaded += (_, _) => Build();
    }

    private bool _building;
    private AppSettings S => _vm.Current;

    private void Add(UIElement e) => _content.Children.Add(e);
    private void Header(string title, string? caption = null)
    {
        Add(new TextBlock { Text = title, Style = Style("AeSectionTitle") });
        if (caption is not null) Add(Text(caption, "AeCaption"));
    }

    private void Toggle(string title, string description, bool value, Func<AppSettings, bool, AppSettings> set)
    {
        var t = new ToggleSwitch { IsOn = value, OnContent = "", OffContent = "", MinWidth = 0 };
        t.Toggled += (_, _) => _vm.Update(s => set(s, t.IsOn));
        Add(Card(Row(Stack(2, Title(title, 14), Text(description, "AeCaption")), t)));
    }

    private void Choice<T>(string title, string description, IEnumerable<T> options, T value, Func<AppSettings, T, AppSettings> set)
    {
        var c = new ComboBox { ItemsSource = options.ToList(), SelectedItem = value, MinWidth = 200 };
        c.SelectionChanged += (_, _) => { if (c.SelectedItem is T v) _vm.Update(s => set(s, v)); };
        Add(Card(Row(Stack(2, Title(title, 14), Text(description, "AeCaption")), c)));
    }

    private void Number(string title, string description, double value, double min, double max, Func<AppSettings, double, AppSettings> set)
    {
        var n = new NumberBox { Value = value, Minimum = min, Maximum = max, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact, Width = 150 };
        n.ValueChanged += (_, e) => { if (!double.IsNaN(e.NewValue)) _vm.Update(s => set(s, e.NewValue)); };
        Add(Card(Row(Stack(2, Title(title, 14), Text(description, "AeCaption")), n)));
    }

    private void TextSetting(string title, string description, string value, Func<AppSettings, string, AppSettings> set)
    {
        var box = new TextBox { Text = value, Width = 240 };
        box.LostFocus += (_, _) => _vm.Update(s => set(s, box.Text.Trim()));
        Add(Card(Row(Stack(2, Title(title, 14), Text(description, "AeCaption")), box)));
    }

    private void Build()
    {
        _building = true;
        _content.Children.Clear();
        switch (_vm.Section)
        {
            case SettingsSection.General:
                Header("General");
                Choice("Customization level", "Controls which pages are shown and how much detail is visible.", _vm.Levels, S.Level, (s, v) => s with { Level = v });
                Toggle("Confirm high-impact changes", "Show what will change before enabling mods that restart Explorer or touch shell components.", S.ConfirmRiskyChanges, (s, v) => s with { ConfirmRiskyChanges = v });
                TextSetting("Location name", "Used for weather defaults and sunrise/sunset.", S.LocationName, (s, v) => s with { LocationName = v });
                Number("Latitude", "Decimal degrees.", S.Latitude, -90, 90, (s, v) => s with { Latitude = v });
                Number("Longitude", "Decimal degrees.", S.Longitude, -180, 180, (s, v) => s with { Longitude = v });
                break;
            case SettingsSection.Appearance:
                Header("Appearance", "Changes AETHER's own look. Use Themes to restyle Windows too.");
                var palettes = new ComboBox { ItemsSource = _vm.Palettes, DisplayMemberPath = "Name", SelectedItem = _vm.SelectedPalette, MinWidth = 200 };
                palettes.SelectionChanged += (_, _) => _vm.SelectedPalette = palettes.SelectedItem as AppPalette;
                Add(Card(Row(Stack(2, Title("Interface appearance", 14), Text("Midnight, Obsidian, Frost, Aurora, Nord, Dracula, Cyberpunk or Minimal Light.", "AeCaption")), palettes)));
                TextSetting("Custom accent", "Override the accent with #RRGGBB, or leave empty.", S.AccentOverride ?? "", (s, v) => s with { AccentOverride = v.Length == 0 ? null : v });
                Toggle("Mica backdrop", "Use the Windows 11 Mica material behind the window (restart AETHER to apply).", S.UseMicaBackdrop, (s, v) => s with { UseMicaBackdrop = v });
                Toggle("Reduce motion", "Minimize animations in AETHER.", S.ReduceMotion, (s, v) => s with { ReduceMotion = v });
                break;
            case SettingsSection.Navigation:
                Header("Navigation");
                Choice("Sidebar", "Expanded shows labels; compact shows icons only.", Enum.GetValues<SidebarMode>(), S.Sidebar, (s, v) => s with { Sidebar = v });
                Choice("Start on", "The page AETHER opens with.", Enum.GetValues<StartupPage>(), S.StartupPage, (s, v) => s with { StartupPage = v });
                TextSetting("Command palette shortcut", "Global shortcut, e.g. Ctrl+Space or Ctrl+Alt+P.", S.CommandPaletteHotkey, (s, v) => s with { CommandPaletteHotkey = v });
                TextSetting("Launcher shortcut", "Opens the AETHER launcher.", S.LauncherHotkey, (s, v) => s with { LauncherHotkey = v });
                Header("Pages", "Hide pages you don't use and reorder the sidebar.");
                var shell = App.Get<ShellViewModel>();
                foreach (var item in _vm.HiddenCandidates)
                {
                    var key = item.Key;
                    var t = new ToggleSwitch { IsOn = !_vm.IsHidden(key), OnContent = "", OffContent = "", MinWidth = 0 };
                    t.Toggled += (_, _) => _vm.SetHidden(key, !t.IsOn);
                    var up = Button("↑", () => shell.MoveNavItem(key, -1));
                    var down = Button("↓", () => shell.MoveNavItem(key, 1));
                    Add(Card(Row(Stack(2, Title(item.Title, 14), Text(item.Description, "AeCaption")), HStack(6, up, down, t))));
                }
                break;
            case SettingsSection.Notifications:
                Header("Notifications");
                Toggle("Show toasts", "Errors are always shown.", S.ShowToasts, (s, v) => s with { ShowToasts = v });
                Toggle("Automation notifications", "Notify when a rule runs.", S.NotifyAutomationRuns, (s, v) => s with { NotifyAutomationRuns = v });
                Toggle("Update notifications", "Notify when marketplace updates are available.", S.NotifyUpdates, (s, v) => s with { NotifyUpdates = v });
                break;
            case SettingsSection.Startup:
                Header("Startup");
                var launch = new ToggleSwitch { IsOn = _vm.LaunchAtSignIn, OnContent = "", OffContent = "", MinWidth = 0 };
                launch.Toggled += (_, _) => _vm.LaunchAtSignIn = launch.IsOn;
                Add(Card(Row(Stack(2, Title("Start with Windows", 14), Text("Adds AETHER to your sign-in programs (HKCU Run), starting in the background.", "AeCaption")), launch)));
                Toggle("Keep running in the notification area", "Closing the window hides AETHER instead of exiting, so mods with live effects keep working.", S.MinimizeToTray, (s, v) => s with { MinimizeToTray = v });
                Toggle("Re-apply mods on startup", "Repairs mod settings that Windows updates reset.", S.ReapplyModsOnStartup, (s, v) => s with { ReapplyModsOnStartup = v });
                break;
            case SettingsSection.Background:
                Header("Background services", "Each component can be turned off individually. Nothing polls unless it's needed.");
                Toggle("Automation engine", "Evaluates your rules (every 15 s while rules exist).", S.AutomationEnabled, (s, v) => s with { AutomationEnabled = v });
                Toggle("Theme scheduler", "Switches themes on the schedule set in Themes.", S.ThemeSchedulerEnabled, (s, v) => s with { ThemeSchedulerEnabled = v });
                Toggle("Widgets", "Allow the widget host to run.", S.WidgetsEnabled, (s, v) => s with { WidgetsEnabled = v });
                Toggle("Scheduled backups", "Configure frequency in Backups.", S.ScheduledBackups, (s, v) => s with { ScheduledBackups = v });
                break;
            case SettingsSection.Updates:
                Header("Updates");
                Toggle("Check sources for mod updates", "Compares installed versions with your marketplace sources when the Marketplace opens.", S.CheckForModUpdates, (s, v) => s with { CheckForModUpdates = v });
                Add(Text("AETHER itself has no auto-updater yet; new versions are published as releases.", "AeCaption"));
                break;
            case SettingsSection.Marketplace:
                Header("Marketplace sources", "Manage sources on the Marketplace page. Every package is verified before installation regardless of source.");
                Add(Button("Open Marketplace", () => App.Get<INavigationService>().Navigate(PageKey.Marketplace), accent: true));
                break;
            case SettingsSection.Security:
                Header("Security");
                Toggle("Allow unsigned packages", "Unsigned mods can be installed after review. Tampered packages are always refused.", S.AllowUnsignedPackages, (s, v) => s with { AllowUnsignedPackages = v });
                Toggle("Back up before high-impact changes", "Creates an AETHER backup automatically before risky mods are enabled.", S.CreateBackupBeforeRiskyChanges, (s, v) => s with { CreateBackupBeforeRiskyChanges = v });
                Header("Trusted publishers");
                foreach (var p in _vm.Publishers)
                {
                    var pub = p;
                    Add(Card(Row(Stack(2, Title(p.Name, 14), Text(p.Id, "AeMuted")), Button("Remove", () => _vm.RemovePublisherCommand.Execute(pub)))));
                }
                if (_vm.Publishers.Count == 0) Add(Text("No trusted publishers yet.", "AeMuted"));
                Add(Button("Trust a publisher…", () => _vm.AddPublisherCommand.Execute(null)));
                break;
            case SettingsSection.Privacy:
                Header("Privacy", "AETHER sends no telemetry. Network access happens only for the features below.");
                Toggle("Online widgets", "Allow the Weather widget to contact Open-Meteo.", S.AllowNetworkWidgets, (s, v) => s with { AllowNetworkWidgets = v });
                Toggle("Online wallpapers", "Allow loading Bing's daily images.", S.AllowOnlineWallpapers, (s, v) => s with { AllowOnlineWallpapers = v });
                break;
            case SettingsSection.Advanced:
                Header("Advanced");
                Add(Card(Row(Stack(2, Title("Data folder", 14), Text(_vm.DataFolder, "AeMuted")), Button("Open", () => _vm.OpenDataFolderCommand.Execute(null)))));
                Add(Button("Reset AETHER settings…", () => _vm.ResetCommand.Execute(null)));
                break;
            case SettingsSection.Developer:
                Header("Developer mode", "Load plugins that aren't signed by a trusted publisher. Plugins are .NET code; only use plugins you trust.");
                Toggle("Developer mode", "Allows installing and loading unsigned plugins.", S.DeveloperMode, (s, v) => s with { DeveloperMode = v });
                Header("Plugins");
                foreach (var p in _vm.Plugins)
                {
                    var plugin = p;
                    Add(Card(Row(Stack(2, Title($"{p.Manifest.Name} {p.Manifest.Version}", 14), Text($"{p.State} · {p.Verification} · {p.Manifest.Author}", "AeMuted"), Text(p.Error ?? "", "AeCaption")),
                        HStack(6, Button(p.State == Aether.PluginSystem.PluginState.Loaded ? "Disable" : "Enable", () => _vm.TogglePluginCommand.Execute(plugin)), Button("Remove", () => _vm.UninstallPluginCommand.Execute(plugin))))));
                }
                if (_vm.Plugins.Count == 0) Add(Text("No plugins installed. See docs/plugin-sdk.md to build one.", "AeMuted"));
                Add(Button("Install plugin…", () => _vm.InstallPluginCommand.Execute(null)));
                break;
            case SettingsSection.Logging:
                Header("Logging");
                Choice("Log level", "Debug logs are verbose.", _vm.LogLevels, S.LogLevel, (s, v) => s with { LogLevel = v });
                Number("Keep logs for (days)", "Older log files are deleted at startup.", S.LogRetentionDays, 1, 365, (s, v) => s with { LogRetentionDays = (int)v });
                Add(HStack(8, Button("Open logs folder", () => _vm.OpenLogsFolderCommand.Execute(null)), Button("Refresh", () => { _vm.RefreshLogsCommand.Execute(null); Build(); })));
                var logs = new StackPanel { Spacing = 2 };
                foreach (var l in _vm.RecentLogs.Take(150))
                    logs.Children.Add(new TextBlock { Text = $"{l.Timestamp:HH:mm:ss} {l.Level,-11} {l.Category}: {l.Message}", FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Cascadia Mono, Consolas"), FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = Brush("AeTextSecondary") });
                Add(Card(logs));
                break;
            case SettingsSection.Diagnostics:
                Header("Diagnostics");
                var diag = new StackPanel { Spacing = 6 };
                foreach (var (label, value) in _vm.Diagnostics) diag.Children.Add(Row(Text(label, "AeCaption"), Text(value)));
                Add(Card(diag));
                Add(Button("Run self-check", () => _vm.RunSelfCheckCommand.Execute(null), accent: true));
                break;
            case SettingsSection.ImportExport:
                Header("Import and export", "Move AETHER preferences between PCs. For everything (themes, widgets, mods) use Backups.");
                Add(HStack(8, Button("Export settings…", () => _vm.ExportSettingsCommand.Execute(null)), Button("Import settings…", () => _vm.ImportSettingsCommand.Execute(null))));
                break;
        }
        Add(Text($"AETHER {_vm.Version} · Your Windows. Your Rules.", "AeMuted"));
        _building = false;
    }
}
