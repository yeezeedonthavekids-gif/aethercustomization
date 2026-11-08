namespace Aether.Services.Settings;

public enum SidebarMode { Expanded, Compact }
public enum StartupPage { Overview, LastVisited }
public enum LogLevelSetting { Error, Warning, Information, Debug }
public enum CustomizationLevel { Essential, Balanced, Advanced, Enthusiast }

/// <summary>All user preferences for the AETHER application itself.</summary>
public sealed record AppSettings
{
    // General
    public bool OnboardingCompleted { get; init; }
    public CustomizationLevel Level { get; init; } = CustomizationLevel.Balanced;
    public string Language { get; init; } = "en-US";
    public bool ConfirmRiskyChanges { get; init; } = true;

    // Appearance
    public string? AccentOverride { get; init; }
    public bool ReduceMotion { get; init; }
    public bool UseMicaBackdrop { get; init; } = true;
    public double UiScale { get; init; } = 1.0;

    // Navigation
    public SidebarMode Sidebar { get; init; } = SidebarMode.Expanded;
    public List<string> SidebarOrder { get; init; } = [];
    public List<string> HiddenPages { get; init; } = [];
    public StartupPage StartupPage { get; init; } = StartupPage.Overview;
    public string? LastPage { get; init; }
    public string CommandPaletteHotkey { get; init; } = "Ctrl+Space";
    public string LauncherHotkey { get; init; } = "Ctrl+Alt+Space";

    // Notifications
    public bool ShowToasts { get; init; } = true;
    public bool NotifyAutomationRuns { get; init; } = true;
    public bool NotifyUpdates { get; init; } = true;

    // Startup & background
    public bool LaunchAtSignIn { get; init; }
    public bool StartMinimized { get; init; }
    public bool MinimizeToTray { get; init; } = true;
    public bool ReapplyModsOnStartup { get; init; } = true;
    public bool AutomationEnabled { get; init; } = true;
    public bool WidgetsEnabled { get; init; } = true;
    public bool ThemeSchedulerEnabled { get; init; } = true;

    // Updates & marketplace
    public bool CheckForModUpdates { get; init; } = true;
    public List<MarketplaceSource> MarketplaceSources { get; init; } = [];

    // Security
    public bool AllowUnsignedPackages { get; init; }
    public bool CreateBackupBeforeRiskyChanges { get; init; } = true;

    // Privacy
    public bool AllowNetworkWidgets { get; init; } = true;
    public bool AllowOnlineWallpapers { get; init; } = true;
    public bool ShareDiagnostics { get; init; } // no telemetry is ever sent; reserved and shown as off

    // Backups
    public bool ScheduledBackups { get; init; } = true;
    public int BackupIntervalHours { get; init; } = 24;
    public int BackupsToKeep { get; init; } = 10;

    // Advanced
    public bool DeveloperMode { get; init; }
    public LogLevelSetting LogLevel { get; init; } = LogLevelSetting.Information;
    public int LogRetentionDays { get; init; } = 14;

    // Location (sunrise/sunset & weather)
    public double Latitude { get; init; } = 51.5072;
    public double Longitude { get; init; } = -0.1276;
    public string LocationName { get; init; } = "London";
}

public sealed record MarketplaceSource(string Id, string Name, string Location, bool Enabled, bool IsBuiltIn);
