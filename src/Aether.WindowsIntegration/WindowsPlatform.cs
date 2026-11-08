using System.Runtime.Versioning;
using Aether.Core.Abstractions;
using Aether.Services.Platform;
using Aether.WidgetEngine;
using Microsoft.Extensions.DependencyInjection;

namespace Aether.WindowsIntegration;

public static class WindowsPlatform
{
    /// <summary>Registers the real Windows implementations of AETHER's platform contracts.</summary>
    [SupportedOSPlatform("windows10.0.17763.0")]
    public static IServiceCollection AddWindowsPlatform(this IServiceCollection services)
    {
        services.AddSingleton<IRegistryAccess, WindowsRegistryAccess>();
        services.AddSingleton<ISystemEnvironment, WindowsEnvironment>();
        services.AddSingleton<ISystemSettings, WindowsSystemSettings>();
        services.AddSingleton<IShellController, WindowsShellController>();
        services.AddSingleton<IProcessRunner, WindowsProcessRunner>();
        services.AddSingleton<IPowerShellRunner, WindowsPowerShellRunner>();
        services.AddSingleton<IShellLauncher, WindowsShellLauncher>();
        services.AddSingleton<IProcessMonitor, WindowsProcessMonitor>();
        services.AddSingleton<IWallpaperService, WindowsWallpaperService>();
        services.AddSingleton<ICursorService, WindowsCursorService>();
        services.AddSingleton<ITaskbarStyler, WindowsTaskbarStyler>();
        services.AddSingleton<IDesktopController, WindowsDesktopController>();
        services.AddSingleton<IShortcutService, WindowsShortcutService>();
        services.AddSingleton<WindowsWindowManager>();
        services.AddSingleton<IWindowManager>(sp => sp.GetRequiredService<WindowsWindowManager>());
        services.AddSingleton<ISystemInfoProvider, WindowsSystemInfoProvider>();
        services.AddSingleton<ISystemMetricsProvider, WindowsSystemMetricsProvider>();
        services.AddSingleton<IMediaSessionProvider, WindowsMediaSessionProvider>();
        services.AddSingleton<IRestorePointService, WindowsRestorePointService>();
        services.AddSingleton<IDisplayEvents, WindowsDisplayEvents>();
        return services;
    }
}
