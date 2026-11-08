using Aether.Presentation.Infrastructure;
using Aether.Presentation.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Aether.Presentation;

public static class PresentationServices
{
    public static IServiceCollection AddAetherPresentation(this IServiceCollection services)
    {
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<ModInteraction>();
        services.AddSingleton<CommandPaletteViewModel>();
        services.AddSingleton<ShellViewModel>();
        services.AddSingleton<OnboardingViewModel>();
        services.AddSingleton<DockViewModel>();
        services.AddSingleton<LauncherViewModel>();
        // Page view models are singletons so state survives navigation; each loads lazily on first visit.
        services.AddSingleton<OverviewViewModel>();
        services.AddSingleton<PersonalizationViewModel>();
        services.AddSingleton<TaskbarViewModel>();
        services.AddSingleton<StartMenuViewModel>();
        services.AddSingleton<FileExplorerViewModel>();
        services.AddSingleton<ModsViewModel>();
        services.AddSingleton<DesktopViewModel>();
        services.AddSingleton<WidgetsViewModel>();
        services.AddSingleton<ThemesViewModel>();
        services.AddSingleton<MarketplaceViewModel>();
        services.AddSingleton<ApplicationsViewModel>();
        services.AddSingleton<SystemViewModel>();
        services.AddSingleton<AutomationViewModel>();
        services.AddSingleton<BackupsViewModel>();
        services.AddSingleton<SettingsViewModel>();
        return services;
    }

    public static Type ViewModelFor(PageKey key) => key switch
    {
        PageKey.Overview => typeof(OverviewViewModel),
        PageKey.Personalization => typeof(PersonalizationViewModel),
        PageKey.Taskbar => typeof(TaskbarViewModel),
        PageKey.StartMenu => typeof(StartMenuViewModel),
        PageKey.FileExplorer => typeof(FileExplorerViewModel),
        PageKey.Mods => typeof(ModsViewModel),
        PageKey.Desktop => typeof(DesktopViewModel),
        PageKey.Widgets => typeof(WidgetsViewModel),
        PageKey.Themes => typeof(ThemesViewModel),
        PageKey.Marketplace => typeof(MarketplaceViewModel),
        PageKey.Applications => typeof(ApplicationsViewModel),
        PageKey.System => typeof(SystemViewModel),
        PageKey.Automation => typeof(AutomationViewModel),
        PageKey.Backups => typeof(BackupsViewModel),
        _ => typeof(SettingsViewModel),
    };
}
