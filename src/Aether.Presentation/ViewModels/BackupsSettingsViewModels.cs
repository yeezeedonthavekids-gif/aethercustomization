using System.Collections.ObjectModel;
using Aether.Core.Abstractions;
using Aether.Core.Diagnostics;
using Aether.ModEngine.Security;
using Aether.PluginSystem;
using Aether.Presentation.Infrastructure;
using Aether.Services.Backup;
using Aether.Services.Logging;
using Aether.Services.Platform;
using Aether.Services.Settings;
using Aether.Storage;
using Aether.ThemeEngine;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Aether.Presentation.ViewModels;

public sealed record BackupItem(BackupInfo Info)
{
    public string Description => Info.Manifest.Description;
    public string Kind => Info.Manifest.Kind.ToString();
    public string Created => Info.Manifest.CreatedAt.ToLocalTime().ToString("g");
    public string Size => Info.SizeBytes < 1024 * 1024 ? $"{Info.SizeBytes / 1024d:0} KB" : $"{Info.SizeBytes / 1024d / 1024:0.0} MB";
    public bool Automatic => Info.Manifest.Automatic;
    public string Origin => Automatic ? "Automatic" : "Manual";
}

public sealed partial class BackupsViewModel : PageViewModel
{
    private readonly BackupService _backups;
    private readonly IRestorePointService _restorePoints;
    private readonly IDialogService _dialogs;
    private readonly IAuditLog _audit;
    private readonly ISettingsService _settings;

    public BackupsViewModel(BackupService backups, IRestorePointService restorePoints, IDialogService dialogs, IAuditLog audit, ISettingsService settings, IUiDispatcher ui, INotificationSink notifications) : base(notifications)
    {
        _backups = backups; _restorePoints = restorePoints; _dialogs = dialogs; _audit = audit; _settings = settings;
        backups.BackupsChanged += () => ui.Post(Reload);
        var s = settings.Current;
        _scheduled = s.ScheduledBackups; _intervalHours = s.BackupIntervalHours; _keep = s.BackupsToKeep; _backupBeforeRisky = s.CreateBackupBeforeRiskyChanges;
    }

    public ObservableCollection<BackupItem> Backups { get; } = [];
    public ObservableCollection<AuditEntry> History { get; } = [];
    public IReadOnlyList<BackupKind> Kinds { get; } = Enum.GetValues<BackupKind>();

    [ObservableProperty] private BackupKind _newKind = BackupKind.Full;
    [ObservableProperty] private string _newDescription = "";
    [ObservableProperty] private bool _scheduled;
    [ObservableProperty] private double _intervalHours;
    [ObservableProperty] private double _keep;
    [ObservableProperty] private bool _backupBeforeRisky;
    [ObservableProperty] private string _historyFilter = "";

    public bool RestorePointsSupported => _restorePoints.IsSupported;
    public bool IsEmpty => Backups.Count == 0;

    partial void OnScheduledChanged(bool value) => _settings.Update(s => s with { ScheduledBackups = value });
    partial void OnIntervalHoursChanged(double value) => _settings.Update(s => s with { BackupIntervalHours = (int)Math.Clamp(value, 1, 720) });
    partial void OnKeepChanged(double value) => _settings.Update(s => s with { BackupsToKeep = (int)Math.Clamp(value, 1, 100) });
    partial void OnBackupBeforeRiskyChanged(bool value) => _settings.Update(s => s with { CreateBackupBeforeRiskyChanges = value });
    partial void OnHistoryFilterChanged(string value) => LoadHistory();

    protected override Task LoadAsync() { Reload(); return Task.CompletedTask; }
    protected override Task RefreshAsync() { Reload(); return Task.CompletedTask; }

    private void Reload()
    {
        Backups.Clear();
        foreach (var b in _backups.List()) Backups.Add(new BackupItem(b));
        LoadHistory();
        OnPropertyChanged(nameof(IsEmpty));
    }

    private void LoadHistory()
    {
        History.Clear();
        var q = HistoryFilter.Trim();
        foreach (var e in _audit.Query(limit: 300).Where(e => q.Length == 0 || e.Subject.Contains(q, StringComparison.OrdinalIgnoreCase)
                                                             || e.Category.Contains(q, StringComparison.OrdinalIgnoreCase) || e.Action.Contains(q, StringComparison.OrdinalIgnoreCase)))
            History.Add(e);
    }

    [RelayCommand]
    private Task CreateAsync() => RunAsync(async () =>
    {
        var r = await _backups.CreateAsync(NewKind, NewDescription);
        Report(r.ToResult(), "Backup");
        NewDescription = "";
    });

    [RelayCommand]
    private async Task RestoreAsync(BackupItem? item)
    {
        if (item is null) return;
        if (!await _dialogs.ConfirmAsync($"Restore '{item.Description}'?",
                $"This replaces your current AETHER {(item.Info.Manifest.Kind == BackupKind.Full ? "configuration" : item.Kind.ToLowerInvariant() + " data")} with the backup from {item.Created}. " +
                "A safety backup of your current setup is created first. This is an AETHER backup, not a Windows system restore.", "Restore")) return;
        await RunAsync(async () => Report(await _backups.RestoreAsync(item.Info.Path), "Restore"));
    }

    [RelayCommand]
    private Task ValidateAsync(BackupItem? item) => item is null ? Task.CompletedTask : RunAsync(async () =>
    {
        var v = await Task.Run(() => _backups.Validate(item.Info.Path));
        Report(v.IsValid ? OperationResult.Ok($"All {v.Manifest!.Files.Count} items match their checksums.") : OperationResult.Fail("This backup is damaged.", v.Problems), "Verification");
    });

    [RelayCommand]
    private async Task ExportAsync(BackupItem? item)
    {
        if (item is null) return;
        var dest = await _dialogs.SaveFileAsync(item.Info.FileName, new FileFilter("AETHER backup", BackupService.Extension));
        if (dest is not null) Report(_backups.Export(item.Info.Path, dest));
    }

    [RelayCommand]
    private async Task ImportAsync()
    {
        var src = await _dialogs.PickFileAsync(new FileFilter("AETHER backup", BackupService.Extension));
        if (src is not null) Report(_backups.Import(src));
    }

    [RelayCommand]
    private async Task DeleteAsync(BackupItem? item)
    {
        if (item is null) return;
        if (await _dialogs.ConfirmAsync("Delete this backup?", item.Description, "Delete", destructive: true)) Report(_backups.Delete(item.Info.Path));
    }

    [RelayCommand]
    private async Task CreateRestorePointAsync()
    {
        if (!await _dialogs.ConfirmAsync("Create a Windows restore point?",
                "This asks Windows System Restore to snapshot system files and settings. It needs administrator rights and System Protection, and Windows may refuse if a point was created in the last 24 hours. AETHER reports exactly what Windows does.", "Create")) return;
        await RunAsync(async () => Report(await _restorePoints.CreateAsync("Manual restore point"), "Windows restore point"));
    }
}

public enum SettingsSection { General, Appearance, Navigation, Notifications, Startup, Background, Updates, Marketplace, Security, Privacy, Advanced, Developer, Logging, Diagnostics, ImportExport }

public sealed partial class SettingsViewModel : PageViewModel
{
    private readonly ISettingsService _settings;
    private readonly ThemeManager _themes;
    private readonly ITrustedPublisherStore _publishers;
    private readonly PluginHost _plugins;
    private readonly FileLoggerProvider _logs;
    private readonly IShellLauncher _launcher;
    private readonly IDialogService _dialogs;
    private readonly IAppPaths _paths;
    private readonly IRegistryAccess _registry;
    private readonly ISystemEnvironment _environment;
    private readonly AetherDatabase _db;
    private bool _loading;

    public SettingsViewModel(ISettingsService settings, ThemeManager themes, ITrustedPublisherStore publishers, PluginHost plugins, FileLoggerProvider logs,
        IShellLauncher launcher, IDialogService dialogs, IAppPaths paths, IRegistryAccess registry, ISystemEnvironment environment, AetherDatabase db,
        INotificationSink notifications) : base(notifications)
    {
        _settings = settings; _themes = themes; _publishers = publishers; _plugins = plugins; _logs = logs; _launcher = launcher;
        _dialogs = dialogs; _paths = paths; _registry = registry; _environment = environment; _db = db;
    }

    public IReadOnlyList<SettingsSection> Sections { get; } = Enum.GetValues<SettingsSection>();
    public IReadOnlyList<AppPalette> Palettes => BuiltinPalettes.All;
    public IReadOnlyList<LogLevelSetting> LogLevels { get; } = Enum.GetValues<LogLevelSetting>();
    public IReadOnlyList<CustomizationLevel> Levels { get; } = Enum.GetValues<CustomizationLevel>();
    public ObservableCollection<TrustedPublisher> Publishers { get; } = [];
    public ObservableCollection<PluginInfo> Plugins { get; } = [];
    public ObservableCollection<LogRecord> RecentLogs { get; } = [];
    public ObservableCollection<(string Label, string Value)> Diagnostics { get; } = [];
    public ObservableCollection<NavigationItem> HiddenCandidates { get; } = [];

    [ObservableProperty] private SettingsSection _section = SettingsSection.General;
    [ObservableProperty] private AppSettings _current = new();
    [ObservableProperty] private AppPalette? _selectedPalette;
    [ObservableProperty] private bool _launchAtSignIn;

    public string Version => typeof(SettingsViewModel).Assembly.GetName().Version?.ToString(3) ?? "0.1.0";
    public string DataFolder => _paths.Root;

    protected override Task LoadAsync()
    {
        _loading = true;
        Current = _settings.Current;
        SelectedPalette = BuiltinPalettes.All.FirstOrDefault(p => p.Id == _themes.ActiveTheme.Palette.Id);
        LaunchAtSignIn = _registry.GetValue(RegistryRoot.CurrentUser, Aether.Services.Applications.StartupManager.RunPath, "AETHER") is not null;
        foreach (var i in NavigationCatalog.All.Where(i => i.Key is not (PageKey.Overview or PageKey.Settings))) HiddenCandidates.Add(i);
        _loading = false;
        Reload();
        return Task.CompletedTask;
    }

    protected override Task RefreshAsync() { Reload(); return Task.CompletedTask; }

    private void Reload()
    {
        Publishers.Clear();
        foreach (var p in _publishers.GetAll()) Publishers.Add(p);
        Plugins.Clear();
        foreach (var p in _plugins.GetPlugins()) Plugins.Add(p);
        RecentLogs.Clear();
        foreach (var l in _logs.Recent.Reverse().Take(200)) RecentLogs.Add(l);
        Diagnostics.Clear();
        var v = _environment.Version;
        Diagnostics.Add(("AETHER", Version));
        Diagnostics.Add(("Windows", $"{v.ProductName} {v.DisplayVersion} ({v.BuildString})"));
        Diagnostics.Add((".NET", Environment.Version.ToString()));
        Diagnostics.Add(("Administrator", _environment.IsAdministrator ? "Yes" : "No"));
        Diagnostics.Add(("Data folder", _paths.Root));
        Diagnostics.Add(("Database schema", _db.SchemaVersion.ToString()));
        Diagnostics.Add(("Database size", File.Exists(_db.Path) ? $"{new FileInfo(_db.Path).Length / 1024} KB" : "—"));
        Diagnostics.Add(("Memory (working set)", $"{Environment.WorkingSet / 1024 / 1024} MB"));
    }

    /// <summary>Applies a change to the settings record. Bound controls call this through small helpers in the view.</summary>
    public void Update(Func<AppSettings, AppSettings> change)
    {
        if (_loading) return;
        _settings.Update(change);
        Current = _settings.Current;
    }

    partial void OnSelectedPaletteChanged(AppPalette? value)
    {
        if (_loading || value is null) return;
        _themes.SetActiveAppTheme(value.Id);
    }

    partial void OnLaunchAtSignInChanged(bool value)
    {
        if (_loading) return;
        try
        {
            if (value) _registry.SetValue(RegistryRoot.CurrentUser, Aether.Services.Applications.StartupManager.RunPath, "AETHER",
                RegistryData.Str($"\"{Environment.ProcessPath}\" --background"));
            else _registry.DeleteValue(RegistryRoot.CurrentUser, Aether.Services.Applications.StartupManager.RunPath, "AETHER");
            Update(s => s with { LaunchAtSignIn = value });
        }
        catch (Exception ex) { Toast("Couldn't change startup", ex.Message, NotificationSeverity.Error); }
    }

    public bool IsHidden(PageKey key) => Current.HiddenPages.Contains(key.ToString());

    public void SetHidden(PageKey key, bool hidden) =>
        Update(s => s with { HiddenPages = hidden ? [.. s.HiddenPages.Where(p => p != key.ToString()), key.ToString()] : s.HiddenPages.Where(p => p != key.ToString()).ToList() });

    [RelayCommand]
    private async Task ResetAsync()
    {
        if (!await _dialogs.ConfirmAsync("Reset AETHER settings?", "Preferences for AETHER itself go back to defaults. Mods, themes and backups are not touched.", "Reset", destructive: true)) return;
        _settings.Reset();
        Current = _settings.Current;
        Toast("Settings reset", "AETHER preferences are back to defaults.");
    }

    [RelayCommand]
    private async Task AddPublisherAsync()
    {
        var id = await _dialogs.PromptAsync("Trust a publisher", "Publisher id (from the package's signature)");
        if (string.IsNullOrWhiteSpace(id)) return;
        var name = await _dialogs.PromptAsync("Trust a publisher", "Display name", id);
        var key = await _dialogs.PromptAsync("Trust a publisher", "Public key (base64 SubjectPublicKeyInfo)");
        if (string.IsNullOrWhiteSpace(key)) return;
        try
        {
            using var ecdsa = System.Security.Cryptography.ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(key.Trim()), out _);
            _publishers.Add(new TrustedPublisher { Id = id.Trim(), Name = string.IsNullOrWhiteSpace(name) ? id : name.Trim(), PublicKey = key.Trim() });
            Reload();
            Toast("Publisher trusted", $"Packages signed by {name} will install without warnings.");
        }
        catch (Exception ex) { Toast("Invalid public key", ex.Message, NotificationSeverity.Error); }
    }

    [RelayCommand]
    private async Task RemovePublisherAsync(TrustedPublisher? p)
    {
        if (p is null) return;
        if (await _dialogs.ConfirmAsync($"Stop trusting {p.Name}?", "Their installed mods keep working but show as untrusted.", "Remove", destructive: true)) { _publishers.Remove(p.Id); Reload(); }
    }

    [RelayCommand]
    private async Task InstallPluginAsync()
    {
        var path = await _dialogs.PickFileAsync(new FileFilter("AETHER plugin", ".aetherplugin", ".zip"));
        if (path is null) return;
        Report(_plugins.Install(path), "Plugin");
        Reload();
    }

    [RelayCommand]
    private async Task TogglePluginAsync(PluginInfo? p)
    {
        if (p is null) return;
        if (p.State == PluginState.Loaded) { Report(await _plugins.DisableAsync(p.Manifest.Id)); Reload(); return; }
        if (p.MissingCapabilities.Count > 0)
        {
            var infos = p.MissingCapabilities.Select(c => new Aether.ModEngine.Manifest.PermissionInfo(c, c, Aether.SDK.PluginCapabilities.Descriptions.GetValueOrDefault(c, c), Aether.ModEngine.Manifest.RiskLevel.Medium)).ToList();
            if (!await _dialogs.RequestPermissionsAsync(p.Manifest.Name, infos, Aether.ModEngine.Manifest.RiskLevel.Medium)) return;
            _plugins.GrantCapabilities(p.Manifest.Id, p.MissingCapabilities);
        }
        Report(await _plugins.EnableAsync(p.Manifest.Id), p.Manifest.Name);
        Reload();
    }

    [RelayCommand]
    private async Task UninstallPluginAsync(PluginInfo? p)
    {
        if (p is null) return;
        if (await _dialogs.ConfirmAsync($"Remove {p.Manifest.Name}?", "The plugin is unloaded and its files deleted.", "Remove", destructive: true)) { Report(_plugins.Uninstall(p.Manifest.Id)); Reload(); }
    }

    [RelayCommand] private void OpenLogsFolder() => _launcher.OpenFolder(_paths.Logs);
    [RelayCommand] private void OpenDataFolder() => _launcher.OpenFolder(_paths.Root);
    [RelayCommand] private void RefreshLogs() => Reload();

    [RelayCommand]
    private async Task ExportSettingsAsync()
    {
        var path = await _dialogs.SaveFileAsync("aether-settings.json", new FileFilter("JSON", ".json"));
        if (path is null) return;
        await File.WriteAllTextAsync(path, AetherJson.Serialize(_settings.Current, indented: true));
        Toast("Settings exported", Path.GetFileName(path));
    }

    [RelayCommand]
    private async Task ImportSettingsAsync()
    {
        var path = await _dialogs.PickFileAsync(new FileFilter("JSON", ".json"));
        if (path is null) return;
        try
        {
            var s = AetherJson.Deserialize<AppSettings>(await File.ReadAllTextAsync(path)) ?? throw new FormatException("Empty file.");
            _settings.Update(_ => s with { OnboardingCompleted = true });
            Current = _settings.Current;
            Toast("Settings imported", "AETHER preferences updated.");
        }
        catch (Exception ex) { Toast("Not a settings file", ex.Message, NotificationSeverity.Error); }
    }

    [RelayCommand]
    private async Task RunSelfCheckAsync()
    {
        var lines = new List<string>();
        void Check(string name, Func<string> probe)
        {
            try { lines.Add($"✓ {name}: {probe()}"); }
            catch (Exception ex) { lines.Add($"✗ {name}: {ex.Message}"); }
        }
        Check("Database", () => { _db.Open().Dispose(); return "reachable"; });
        Check("Data folder", () => { var f = Path.Combine(_paths.Cache, "probe.tmp"); File.WriteAllText(f, "ok"); File.Delete(f); return "writable"; });
        Check("Registry (HKCU)", () => _registry.KeyExists(RegistryRoot.CurrentUser, "Software") ? "readable" : "unexpected state");
        Check("Windows", () => _environment.IsWindows ? _environment.Version.FriendlyName : "not Windows");
        Check("Plugins", () => $"{_plugins.GetPlugins().Count} installed");
        await _dialogs.ShowTextAsync("Self-check", string.Join("\n", lines));
    }
}

public sealed partial class OnboardingViewModel : ObservableObject
{
    private readonly ISettingsService _settings;
    private readonly ThemeManager _themes;
    private readonly BackupService _backups;
    private readonly ISystemEnvironment _environment;
    private readonly IRegistryAccess _registry;

    public OnboardingViewModel(ISettingsService settings, ThemeManager themes, BackupService backups, ISystemEnvironment environment, IRegistryAccess registry,
        IWallpaperService wallpaper, Aether.Services.Platform.ITaskbarStyler styler, IPowerShellRunner powerShell)
    {
        _settings = settings; _themes = themes; _backups = backups; _environment = environment; _registry = registry;
        var v = environment.Version;
        WindowsText = environment.IsWindows ? $"{v.ProductName} · {v.DisplayVersion} · build {v.BuildString}" : "Not running on Windows — customizations are unavailable.";
        Capabilities =
        [
            new("Registry customization", true, "Per-user settings with automatic rollback"),
            new("Per-monitor wallpapers", wallpaper.IsSupported, "IDesktopWallpaper"),
            new("Taskbar effects", styler.IsSupported, "Windows 10 1809 or newer"),
            new("PowerShell mods", powerShell.IsAvailable, "Windows PowerShell 5.1"),
            new("Windows 11 taskbar & Start settings", v.IsWindows11, v.IsWindows11 ? "Available" : "Requires Windows 11"),
            new("Administrator features", environment.IsAdministrator, environment.IsAdministrator ? "Running elevated" : "Restart as administrator when needed"),
        ];
        SelectedPalette = BuiltinPalettes.Midnight;
    }

    public static readonly string[] StepTitles = ["Welcome", "Your system", "Capabilities", "Appearance", "Customization level", "Components", "Startup", "Backup", "Ready"];
    public IReadOnlyList<CapabilityRow> Capabilities { get; }
    public IReadOnlyList<AppPalette> Palettes => BuiltinPalettes.All;
    public IReadOnlyList<CustomizationLevel> Levels { get; } = Enum.GetValues<CustomizationLevel>();
    public string WindowsText { get; }

    [ObservableProperty] private int _step;
    [ObservableProperty] private AppPalette? _selectedPalette;
    [ObservableProperty] private CustomizationLevel _level = CustomizationLevel.Balanced;
    [ObservableProperty] private bool _enableWidgets = true;
    [ObservableProperty] private bool _enableAutomation = true;
    [ObservableProperty] private bool _enableMarketplace = true;
    [ObservableProperty] private bool _launchAtSignIn;
    [ObservableProperty] private bool _startMinimized;
    [ObservableProperty] private bool _createBackup = true;
    [ObservableProperty] private string? _backupResult;
    [ObservableProperty] private bool _isWorking;

    public string StepTitle => StepTitles[Step];
    public int StepCount => StepTitles.Length;
    public bool CanGoBack => Step > 0;
    public bool IsLastStep => Step == StepTitles.Length - 1;
    public string NextLabel => IsLastStep ? "Start using AETHER" : "Continue";
    public string LevelDescription => Level switch
    {
        CustomizationLevel.Essential => "A focused set of pages for everyday personalization. Advanced pages stay hidden until you need them.",
        CustomizationLevel.Balanced => "Everything visible, with confirmations before impactful changes. Recommended.",
        CustomizationLevel.Advanced => "All pages plus advanced mod settings shown by default.",
        _ => "Everything, including developer mode for loading your own plugins. Confirmations stay on for risky changes.",
    };

    public event Action? Completed;

    partial void OnStepChanged(int value)
    {
        OnPropertyChanged(nameof(StepTitle)); OnPropertyChanged(nameof(CanGoBack)); OnPropertyChanged(nameof(IsLastStep)); OnPropertyChanged(nameof(NextLabel));
    }

    partial void OnLevelChanged(CustomizationLevel value) => OnPropertyChanged(nameof(LevelDescription));
    partial void OnSelectedPaletteChanged(AppPalette? value) { if (value is not null) _themes.SetActiveAppTheme(value.Id); }

    [RelayCommand] private void Back() { if (Step > 0) Step--; }

    [RelayCommand]
    private async Task NextAsync()
    {
        if (Step == 7 && CreateBackup && BackupResult is null)
        {
            IsWorking = true;
            var r = await _backups.CreateAsync(Aether.Services.Backup.BackupKind.Full, "Initial backup (setup)");
            BackupResult = r.Success ? "✓ Initial AETHER backup created. (This is not a Windows restore point.)" : $"Backup failed: {r.ToResult().Describe()}";
            IsWorking = false;
            return;
        }
        if (!IsLastStep) { Step++; return; }
        Finish();
    }

    private void Finish()
    {
        // Onboarding never applies Windows modifications — it only stores AETHER preferences.
        var hidden = Level == CustomizationLevel.Essential ? new List<string> { "Automation", "Marketplace", "Mods", "System" } : [];
        if (!EnableMarketplace && !hidden.Contains("Marketplace")) hidden.Add("Marketplace");
        _settings.Update(s => s with
        {
            OnboardingCompleted = true, Level = Level, WidgetsEnabled = EnableWidgets, AutomationEnabled = EnableAutomation,
            HiddenPages = hidden, StartMinimized = StartMinimized, LaunchAtSignIn = LaunchAtSignIn,
            DeveloperMode = Level == CustomizationLevel.Enthusiast || s.DeveloperMode,
        });
        if (LaunchAtSignIn)
        {
            try { _registry.SetValue(RegistryRoot.CurrentUser, Aether.Services.Applications.StartupManager.RunPath, "AETHER", RegistryData.Str($"\"{Environment.ProcessPath}\" --background")); }
            catch (Exception) { /* reported in Settings ▸ Startup */ }
        }
        Completed?.Invoke();
    }
}

public sealed record CapabilityRow(string Name, bool Available, string Detail)
{
    public string Glyph => Available ? "" : "";
}
