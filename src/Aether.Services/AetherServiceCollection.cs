using Aether.Core.Abstractions;
using Aether.Core.Diagnostics;
using Aether.Core.Events;
using Aether.ModEngine.Execution;
using Aether.ModEngine.Lifecycle;
using Aether.ModEngine.Security;
using Aether.PluginSystem;
using Aether.Services.Applications;
using Aether.Services.Automation;
using Aether.Services.Backup;
using Aether.Services.Commands;
using Aether.Services.Logging;
using Aether.Services.Marketplace;
using Aether.Services.Mods.Builtin;
using Aether.Services.Platform;
using Aether.Services.Profiles;
using Aether.Services.Settings;
using Aether.Services.Tweaks;
using Aether.Services.Wallpapers;
using Aether.Services.Windows;
using Aether.Storage;
using Aether.ThemeEngine;
using Aether.WidgetEngine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Aether.Services;

public sealed class AetherHostOptions
{
    public required IAppPaths Paths { get; init; }
    public string Version { get; init; } = "0.1.0";
    /// <summary>Folder that ships with the app containing the bundled marketplace catalog.</summary>
    public string BundledMarketplaceFolder { get; init; } = Path.Combine(AppContext.BaseDirectory, "Marketplace");
}

public static class AetherServiceCollection
{
    /// <summary>
    /// Registers every AETHER service. Platform services (registry, wallpaper, windows…) must be registered by the
    /// host first; anything missing falls back to honest "unsupported" implementations.
    /// </summary>
    public static IServiceCollection AddAether(this IServiceCollection services, AetherHostOptions options)
    {
        services.AddSingleton(options);
        services.AddSingleton(options.Paths);
        services.TryAddSingleton(TimeProvider.System);

        // Storage
        services.AddSingleton(_ => new AetherDatabase(options.Paths.DatabaseFile));
        services.AddSingleton<IDocumentStore, SqliteDocumentStore>();
        services.AddSingleton<IModStateRepository, ModStateRepository>();
        services.AddSingleton<ISnapshotRepository, SnapshotRepository>();
        services.AddSingleton<IPermissionGrantRepository, PermissionGrantRepository>();
        services.AddSingleton<SqliteAuditLog>();
        services.AddSingleton<IAuditLog>(sp => sp.GetRequiredService<SqliteAuditLog>());

        // Infrastructure
        services.AddSingleton<IEventBus, EventBus>();
        services.AddSingleton<NotificationHub>();
        services.AddSingleton<INotificationSink>(sp => sp.GetRequiredService<NotificationHub>());
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton(sp =>
        {
            var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd($"AETHER/{options.Version} (+https://github.com/aether-customization)");
            return http;
        });
        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<ISettingsService>();
            return new FileLoggerProvider(options.Paths.Logs, () => settings.Current.LogLevel switch
            {
                LogLevelSetting.Error => LogLevel.Error,
                LogLevelSetting.Warning => LogLevel.Warning,
                LogLevelSetting.Debug => LogLevel.Debug,
                _ => LogLevel.Information,
            }, settings.Current.LogRetentionDays);
        });
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Debug));
        services.AddSingleton<ILoggerProvider>(sp => sp.GetRequiredService<FileLoggerProvider>());

        // Platform fallbacks
        var unsupported = new UnsupportedPlatform();
        services.TryAddSingleton<ITaskbarStyler>(unsupported);
        services.TryAddSingleton<IWallpaperService>(unsupported);
        services.TryAddSingleton<ICursorService>(unsupported);
        services.TryAddSingleton<IRestorePointService>(unsupported);
        services.TryAddSingleton<IProcessMonitor>(unsupported);
        services.TryAddSingleton<IDisplayEvents>(unsupported);

        // Mod engine
        services.AddSingleton<BaselineStore>();
        services.AddSingleton<OperationExecutor>();
        services.AddSingleton<PowerShellExecutor>();
        services.AddSingleton<IModComponentRegistry>(_ =>
        {
            var registry = new ModComponentRegistry();
            BuiltinComponents.RegisterAll(registry);
            return registry;
        });
        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<ISettingsService>();
            return new ModEngineOptions
            {
                HostVersion = options.Version,
                AllowUnsignedPackages = () => settings.Current.AllowUnsignedPackages,
                ReapplyOnStartup = () => settings.Current.ReapplyModsOnStartup,
            };
        });
        services.AddSingleton<CompatibilityChecker>();
        services.AddSingleton<IBuiltinModCatalog, BuiltinModCatalog>();
        services.AddSingleton<ITrustedPublisherStore>(sp =>
        {
            var store = new TrustedPublisherStore(sp.GetRequiredService<IDocumentStore>());
            foreach (var p in TrustedPublishers.BuiltIn) if (store.GetAll().All(x => x.Id != p.Id)) store.Add(p);
            return store;
        });
        services.AddSingleton<ModManager>();

        // Themes, widgets, plugins
        services.AddSingleton<WindowsPersonalizationApplier>();
        services.AddSingleton<IThemeApplyStep, WidgetLayoutThemeStep>();
        services.AddSingleton<IThemeApplyStep, ShellProfileThemeStep>();
        services.AddSingleton<IThemeApplyStep, ModConfigurationThemeStep>();
        services.AddSingleton<ThemeManager>();
        services.AddSingleton<WidgetManager>();
        services.AddSingleton<IWeatherProvider, OpenMeteoWeatherProvider>();
        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<ISettingsService>();
            return new PluginHost(options.Paths, sp.GetRequiredService<IDocumentStore>(), sp.GetRequiredService<IPermissionGrantRepository>(),
                sp.GetRequiredService<ITrustedPublisherStore>(), sp.GetRequiredService<INotificationSink>(), sp.GetRequiredService<IAuditLog>(),
                sp.GetRequiredService<ILoggerFactory>(), options.Version, () => settings.Current.DeveloperMode);
        });

        // Feature services
        services.AddSingleton<StartupManager>();
        services.AddSingleton<PowerPlanService>();
        services.AddSingleton<WallpaperAssignments>();
        services.AddSingleton<IOnlineWallpaperProvider, BingDailyWallpaperProvider>();
        services.AddSingleton<WallpaperLibrary>();
        services.AddSingleton<TweakService>();
        services.AddSingleton(sp => new BackupService(options.Paths, sp.GetRequiredService<IDocumentStore>(), sp.GetRequiredService<ModManager>(),
            sp.GetRequiredService<IAuditLog>(), sp.GetRequiredService<ILogger<BackupService>>(), options.Version));
        services.AddSingleton<AppCatalog>();
        services.AddSingleton<Explorer.FolderCustomizer>();
        services.AddSingleton<WindowLayoutService>();
        services.AddSingleton(sp => new ShellProfileService(sp.GetRequiredService<TweakService>(), sp.GetRequiredService<IDesktopController>(),
            sp.GetRequiredService<IDocumentStore>(), sp.GetService<IDockHost>(), sp.GetService<ILauncherHost>()));
        services.AddSingleton<IAutomationActionExecutor, AutomationActionExecutor>();
        services.AddSingleton<IAutomationSignalSource>(sp => new AutomationSignalSource(sp.GetRequiredService<IProcessMonitor>(),
            sp.GetService<ISystemMetricsProvider>(), sp.GetRequiredService<ThemeManager>(), sp.GetRequiredService<TimeProvider>()));
        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<ISettingsService>();
            return new AutomationEngine(sp.GetRequiredService<IDocumentStore>(), sp.GetRequiredService<IAutomationActionExecutor>(),
                sp.GetRequiredService<IAutomationSignalSource>(), () => (settings.Current.Latitude, settings.Current.Longitude),
                sp.GetRequiredService<ILogger<AutomationEngine>>());
        });
        services.AddSingleton(sp => new MarketplaceService(sp.GetRequiredService<ISettingsService>(), options.Paths, sp.GetRequiredService<IDocumentStore>(),
            sp.GetRequiredService<ModManager>(), sp.GetRequiredService<ThemeManager>(), sp.GetRequiredService<WidgetManager>(), sp.GetRequiredService<PluginHost>(),
            sp.GetRequiredService<ITrustedPublisherStore>(), sp.GetRequiredService<HttpClient>(), sp.GetRequiredService<ILogger<MarketplaceService>>(),
            options.BundledMarketplaceFolder));

        // Command palette
        services.AddSingleton<ICommandSource, ModCommandSource>();
        services.AddSingleton<ICommandSource, ThemeCommandSource>();
        services.AddSingleton<ICommandSource, AppCommandSource>();
        services.AddSingleton<ICommandSource, WidgetCommandSource>();
        services.AddSingleton<ICommandSource, AutomationCommandSource>();
        services.AddSingleton<ICommandSource, PluginCommandSource>();
        services.AddSingleton<CommandPaletteService>();

        services.AddSingleton<AetherRuntime>();
        return services;
    }
}

/// <summary>Publishers trusted out of the box. Their packages install without the "unverified" warning.</summary>
public static class TrustedPublishers
{
    public static IReadOnlyList<TrustedPublisher> BuiltIn { get; } = [];
}
