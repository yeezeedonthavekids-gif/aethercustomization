using Aether.Core.Abstractions;
using Aether.Core.Diagnostics;
using Aether.ModEngine.Lifecycle;
using Aether.Services.Platform;
using Aether.Services.Profiles;
using Aether.Services.Windows;
using Aether.ThemeEngine;
using Aether.WidgetEngine;

namespace Aether.Services.Automation;

/// <summary>Executes automation actions against the real AETHER services.</summary>
public sealed class AutomationActionExecutor(
    ThemeManager themes, ModManager mods, IWallpaperService wallpaper, WidgetManager widgets, WindowLayoutService windows,
    ShellProfileService shell, IShellLauncher launcher, INotificationSink notifications, WindowsPersonalizationApplier personalization) : IAutomationActionExecutor
{
    public async Task<OperationResult> ExecuteAsync(AutomationAction a, CancellationToken ct)
    {
        switch (a.Kind)
        {
            case ActionKind.ApplyTheme:
                return await themes.ApplyAsync(a.Target ?? "", ct: ct).ConfigureAwait(false);
            case ActionKind.ApplyAppThemeOnly:
                themes.SetActiveAppTheme(a.Target ?? "");
                return OperationResult.Ok("AETHER appearance changed.");
            case ActionKind.SetWindowsMode:
                var mode = a.Target == "light" ? ThemeMode.Light : ThemeMode.Dark;
                personalization.Apply(new WindowsPersonalization { AppsMode = mode, SystemMode = mode });
                return OperationResult.Ok($"Windows switched to {mode.ToString().ToLowerInvariant()} mode.");
            case ActionKind.EnableMod:
            {
                var r = await mods.EnableAsync(a.Target ?? "", ct).ConfigureAwait(false);
                return r.Status == ModActionStatus.NeedsConsent
                    ? OperationResult.Fail("The mod needs permissions you haven't granted yet. Enable it once from Windows Mods.")
                    : new OperationResult(r.Succeeded, r.Message, r.Details);
            }
            case ActionKind.DisableMod:
            {
                var r = await mods.DisableAsync(a.Target ?? "", ct).ConfigureAwait(false);
                return new OperationResult(r.Succeeded, r.Message, r.Details);
            }
            case ActionKind.SetWallpaper:
                if (!wallpaper.IsSupported) return OperationResult.Fail("Wallpaper control is not available.");
                if (a.Target is null || !File.Exists(a.Target)) return OperationResult.Fail($"Wallpaper not found: {a.Target}");
                wallpaper.SetWallpaper(null, a.Target);
                return OperationResult.Ok("Wallpaper changed.");
            case ActionKind.ActivateWidgetLayout:
                widgets.ActivateLayout(a.Target ?? "");
                return OperationResult.Ok("Widget layout activated.");
            case ActionKind.ApplyWindowLayout:
                return windows.Arrange(a.Target ?? "halves");
            case ActionKind.ApplyTaskbarProfile:
                return await shell.ApplyTaskbarProfileAsync(a.Target ?? "", ct).ConfigureAwait(false);
            case ActionKind.LaunchApp:
                if (string.IsNullOrWhiteSpace(a.Target)) return OperationResult.Fail("No application specified.");
                launcher.Open(a.Target, a.Argument);
                return OperationResult.Ok($"Launched {Path.GetFileName(a.Target)}.");
            case ActionKind.Notify:
                notifications.Notify(new AppNotification(a.Target ?? "AETHER automation", a.Text ?? ""));
                return OperationResult.Ok("Notification shown.");
            case ActionKind.Wait:
                await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(a.Seconds, 0, 600)), ct).ConfigureAwait(false);
                return OperationResult.Ok($"Waited {a.Seconds}s.");
            default:
                return OperationResult.Fail($"Unsupported action {a.Kind}.");
        }
    }
}

public sealed class AutomationSignalSource(IProcessMonitor processes, ISystemMetricsProvider? metrics, ThemeManager themes, TimeProvider time) : IAutomationSignalSource
{
    public AutomationSignals Capture(bool includeProcesses)
    {
        bool? onBattery = null;
        int? percent = null;
        try
        {
            if (metrics?.GetBattery() is { Present: true } b) { onBattery = !b.OnAcPower; percent = b.Percent; }
            else if (metrics is not null) onBattery = false;
        }
        catch (Exception) { /* power status unavailable */ }
        return new AutomationSignals(time.GetLocalNow(), includeProcesses ? processes.GetRunningProcessNames() : new HashSet<string>(), onBattery, percent, themes.ActiveThemeId);
    }
}
