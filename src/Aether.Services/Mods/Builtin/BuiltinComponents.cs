using Aether.Core.Abstractions;
using Aether.ModEngine.Execution;
using Aether.Services.Applications;
using Aether.Services.Mods.Builtin.Components;
using Aether.Services.Wallpapers;
using Aether.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Aether.Services.Mods.Builtin;

public static class BuiltinComponents
{
    /// <summary>Registers the component implementations referenced by the built-in manifests.</summary>
    public static void RegisterAll(IModComponentRegistry registry)
    {
        registry.Register("taskbar-styler", _ => new TaskbarStylerComponent());
        registry.Register("taskbar-accent", _ => new TaskbarAccentComponent());
        registry.Register("theme-scheduler", sp => new ThemeSchedulerComponent(sp.GetRequiredService<ISystemSettings>(), sp.GetRequiredService<TimeProvider>()));
        registry.Register("desktop-icons", _ => new DesktopIconsComponent());
        registry.Register("explorer-restart", _ => new ExplorerRestartComponent());
        registry.Register("startup-guard", sp => new StartupGuardComponent(sp.GetRequiredService<StartupManager>()));
        registry.Register("wallpaper-scheduler", sp => new WallpaperSchedulerComponent(sp.GetRequiredService<IDocumentStore>()));
        registry.Register("focus-mode", sp => new FocusModeComponent(sp.GetRequiredService<TimeProvider>()));
        registry.Register("gaming-profile", sp => new GamingProfileComponent(sp.GetRequiredService<PowerPlanService>(), sp.GetRequiredService<IDocumentStore>()));
        registry.Register("power-shortcuts", sp => new PowerShortcutsComponent(sp.GetRequiredService<IDocumentStore>()));
        registry.Register("multimonitor-wallpaper", sp => new MultiMonitorWallpaperComponent(sp.GetRequiredService<IDocumentStore>(), sp.GetRequiredService<WallpaperAssignments>()));
        registry.Register("cursor-profile", sp => new CursorProfileComponent(sp.GetRequiredService<ISystemSettings>()));
        registry.Register("widget-host", _ => new WidgetHostComponent());
    }
}
