using Aether.Core.Abstractions;
using Aether.ModEngine.Lifecycle;
using Aether.PluginSystem;
using Aether.Services.Automation;
using Aether.Services.Backup;
using Aether.Services.Platform;
using Aether.Services.Settings;
using Aether.Services.Windows;
using Aether.ThemeEngine;
using Microsoft.Extensions.Logging;

namespace Aether.Services;

/// <summary>
/// Starts and stops AETHER's background components according to Settings ▸ Background services. Each component
/// is isolated: one failing never prevents the others from starting, and every failure is surfaced as a notification.
/// </summary>
public sealed class AetherRuntime(
    ModManager mods, PluginHost plugins, AutomationEngine automation, ThemeManager themes, BackupService backups,
    WindowLayoutService windows, ISettingsService settings, INotificationSink notifications, IDisplayEvents displays,
    TimeProvider time, Aether.Core.Events.IEventBus events, ILogger<AetherRuntime> logger) : IAsyncDisposable
{
    private readonly List<IDisposable> _subscriptions = [];
    private CancellationTokenSource? _cts;
    private readonly List<Task> _loops = [];
    private bool _started;

    public bool IsStarted => _started;
    public event Action<string>? StatusChanged;

    public async Task StartAsync()
    {
        if (_started) return;
        _started = true;
        _cts = new CancellationTokenSource();
        var s = settings.Current;

        await Safely("Mods", async () =>
        {
            var results = await mods.StartAsync(_cts.Token).ConfigureAwait(false);
            var failed = results.Where(r => !r.Result.Succeeded).ToList();
            if (failed.Count > 0)
                notifications.Notify(new AppNotification("Some mods need attention",
                    string.Join("\n", failed.Select(f => $"{mods.GetMod(f.Id)?.Manifest.Name ?? f.Id}: {f.Result.Message}")), NotificationSeverity.Warning));
        }).ConfigureAwait(false);

        await Safely("Plugins", () => plugins.StartAsync(_cts.Token)).ConfigureAwait(false);
        // Forward host events to plugins that subscribed to them.
        _subscriptions.Add(events.Subscribe<ThemeAppliedEvent>(e => plugins.Publish(Aether.SDK.PluginEvents.ThemeApplied, new Dictionary<string, string> { ["id"] = e.Theme.Id, ["name"] = e.Theme.Name })));
        _subscriptions.Add(events.Subscribe<Aether.ModEngine.Lifecycle.ModChangedEvent>(e => plugins.Publish(Aether.SDK.PluginEvents.ModChanged, new Dictionary<string, string> { ["id"] = e.ModId, ["change"] = e.Change })));
        automation.RuleRan += r => plugins.Publish(Aether.SDK.PluginEvents.AutomationRuleRan, new Dictionary<string, string> { ["id"] = r.RuleId, ["name"] = r.RuleName, ["success"] = r.Success.ToString() });

        if (s.AutomationEnabled) await Safely("Automation", () => { automation.Start(); return Task.CompletedTask; }).ConfigureAwait(false);
        automation.RuleRan += OnRuleRan;
        displays.DisplaysChanged += OnDisplaysChanged;

        await Safely("Window rules", () => { windows.StartRules(); return Task.CompletedTask; }).ConfigureAwait(false);

        _loops.Add(Loop("Theme schedule", TimeSpan.FromMinutes(1), ThemeScheduleTickAsync));
        _loops.Add(Loop("Scheduled backups", TimeSpan.FromMinutes(30), BackupTickAsync));
        settings.Changed += OnSettingsChanged;
        StatusChanged?.Invoke("started");
    }

    private void OnSettingsChanged(AppSettings before, AppSettings after)
    {
        if (before.AutomationEnabled == after.AutomationEnabled) return;
        if (after.AutomationEnabled) automation.Start();
        else _ = automation.StopAsync();
    }

    private void OnRuleRan(AutomationRun run)
    {
        if (!settings.Current.NotifyAutomationRuns || run.Log is ["Skipped: cooldown"]) return;
        notifications.Notify(new AppNotification($"Automation: {run.RuleName}", run.Success ? run.Trigger : string.Join("\n", run.Log),
            run.Success ? NotificationSeverity.Info : NotificationSeverity.Error));
    }

    private void OnDisplaysChanged() => _ = Safely("Display automation", () => automation.NotifyDisplaysChangedAsync());

    private async Task ThemeScheduleTickAsync(CancellationToken ct)
    {
        var schedule = themes.Schedule;
        if (!schedule.Enabled || !settings.Current.ThemeSchedulerEnabled) return;
        var (wanted, _) = ThemeScheduleCalculator.Evaluate(schedule, time.GetLocalNow());
        if (themes.ActiveThemeId == wanted || themes.GetTheme(wanted) is null) return;
        var r = await themes.ApplyAsync(wanted, ct: ct).ConfigureAwait(false);
        notifications.Notify(new AppNotification("Theme schedule", r.Describe(), r.Success ? NotificationSeverity.Info : NotificationSeverity.Error));
    }

    private async Task BackupTickAsync(CancellationToken ct)
    {
        var s = settings.Current;
        if (!s.ScheduledBackups) return;
        var last = backups.List().FirstOrDefault(b => b.Manifest.Automatic)?.Manifest.CreatedAt;
        if (last is { } l && time.GetUtcNow() - l < TimeSpan.FromHours(Math.Max(1, s.BackupIntervalHours))) return;
        var r = await backups.CreateAsync(BackupKind.Full, "Scheduled backup", automatic: true, ct).ConfigureAwait(false);
        if (!r.Success) notifications.Notify(new AppNotification("Scheduled backup failed", r.ToResult().Describe(), NotificationSeverity.Error));
        backups.Prune(s.BackupsToKeep);
    }

    private Task Loop(string name, TimeSpan interval, Func<CancellationToken, Task> body)
    {
        var token = _cts!.Token;
        return Task.Run(async () =>
        {
            try { await Task.Delay(TimeSpan.FromSeconds(5), time, token).ConfigureAwait(false); } catch (OperationCanceledException) { return; }
            while (!token.IsCancellationRequested)
            {
                try { await body(token).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
                catch (Exception ex) { logger.LogError(ex, "{Loop} failed", name); }
                try { await Task.Delay(interval, time, token).ConfigureAwait(false); } catch (OperationCanceledException) { break; }
            }
        }, token);
    }

    private async Task Safely(string component, Func<Task> action)
    {
        try { await action().ConfigureAwait(false); }
        catch (Exception ex)
        {
            logger.LogError(ex, "{Component} failed to start", component);
            notifications.Notify(new AppNotification($"{component} could not start", ex.Message, NotificationSeverity.Error));
        }
    }

    public async Task StopAsync()
    {
        if (!_started) return;
        _started = false;
        settings.Changed -= OnSettingsChanged;
        foreach (var sub in _subscriptions) sub.Dispose();
        _subscriptions.Clear();
        automation.RuleRan -= OnRuleRan;
        displays.DisplaysChanged -= OnDisplaysChanged;
        if (_cts is not null) await _cts.CancelAsync().ConfigureAwait(false);
        try { await Task.WhenAll(_loops).ConfigureAwait(false); } catch (OperationCanceledException) { }
        _loops.Clear();
        windows.StopRules();
        await automation.StopAsync().ConfigureAwait(false);
        await mods.StopAsync().ConfigureAwait(false);
        await plugins.DisposeAsync().ConfigureAwait(false);
        _cts?.Dispose();
        _cts = null;
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);
}
