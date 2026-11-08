using Aether.Core.Configuration;
using Aether.Core.Diagnostics;
using Aether.Storage;
using Microsoft.Extensions.Logging;

namespace Aether.Services.Automation;

public interface IAutomationActionExecutor
{
    Task<OperationResult> ExecuteAsync(AutomationAction action, CancellationToken ct);
}

public interface IAutomationSignalSource
{
    AutomationSignals Capture(bool includeProcesses);
}

/// <summary>
/// Evaluates rules on signal changes. Polling is adaptive: nothing runs without enabled rules, and process
/// snapshots are taken only when a rule actually uses a process trigger or condition.
/// </summary>
public sealed class AutomationEngine(IDocumentStore store, IAutomationActionExecutor executor, IAutomationSignalSource signals,
    Func<(double Latitude, double Longitude)> location, ILogger<AutomationEngine> logger) : IAsyncDisposable
{
    private const string Collection = "automation-rules";
    private const string HistoryCollection = "automation-history";
    private AutomationSignals? _previous;
    private DateTimeOffset _lastTick;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public event Action<AutomationRun>? RuleRan;
    public event Action? RulesChanged;

    public TimeSpan TickInterval { get; set; } = TimeSpan.FromSeconds(15);

    public IReadOnlyList<AutomationRule> GetRules() => store.GetAll<AutomationRule>(Collection).OrderByDescending(r => r.Priority).ThenBy(r => r.Name).ToList();

    public void Save(AutomationRule rule)
    {
        if (string.IsNullOrWhiteSpace(rule.Name)) throw new ArgumentException("A rule needs a name.");
        if (rule.Triggers.Count == 0) throw new ArgumentException("A rule needs at least one trigger.");
        if (rule.Actions.Count == 0) throw new ArgumentException("A rule needs at least one action.");
        store.Put(Collection, rule.Id, rule);
        RulesChanged?.Invoke();
    }

    public void Delete(string id) { store.Delete(Collection, id); RulesChanged?.Invoke(); }

    public void SetEnabled(string id, bool enabled)
    {
        if (store.Get<AutomationRule>(Collection, id) is { } r) Save(r with { Enabled = enabled });
    }

    public IReadOnlyList<AutomationRun> GetHistory(int limit = 100) =>
        store.GetAll<AutomationRun>(HistoryCollection).OrderByDescending(r => r.At).Take(limit).ToList();

    public void ClearHistory()
    {
        foreach (var id in store.GetIds(HistoryCollection)) store.Delete(HistoryCollection, id);
        RulesChanged?.Invoke();
    }

    public void Start()
    {
        if (_cts is not null) return;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _loop = Task.Run(async () =>
        {
            await FireAsync(t => t.Kind == TriggerKind.AppStartup, "AETHER started", token).ConfigureAwait(false);
            while (!token.IsCancellationRequested)
            {
                try { await Task.Delay(TickInterval, token).ConfigureAwait(false); await TickAsync(token).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
                catch (Exception ex) { logger.LogError(ex, "Automation tick failed"); }
            }
        }, token);
    }

    public async Task StopAsync()
    {
        if (_cts is null) return;
        await _cts.CancelAsync().ConfigureAwait(false);
        try { if (_loop is not null) await _loop.ConfigureAwait(false); } catch (OperationCanceledException) { }
        _cts.Dispose();
        _cts = null;
    }

    /// <summary>Evaluates time, process and power triggers against the previous snapshot.</summary>
    public async Task TickAsync(CancellationToken ct = default)
    {
        var rules = GetRules().Where(r => r.Enabled).ToList();
        if (rules.Count == 0) { _previous = null; return; }
        var needsProcesses = rules.Any(r => r.Triggers.Any(t => t.Kind is TriggerKind.ProcessStarted or TriggerKind.ProcessStopped)
                                           || r.Conditions.Any(c => c.Kind is ConditionKind.ProcessRunning or ConditionKind.ProcessNotRunning));
        var now = signals.Capture(needsProcesses);
        var prev = _previous;
        _previous = now;
        if (prev is null) { _lastTick = now.Now; return; }
        var since = _lastTick;
        _lastTick = now.Now;

        foreach (var rule in rules)
        {
            var fired = rule.Triggers.FirstOrDefault(t => Fired(t, prev, now, since));
            if (fired is null) continue;
            if (!ConditionsHold(rule, now)) continue;
            await RunAsync(rule, Describe(fired), ct).ConfigureAwait(false);
        }
    }

    public Task NotifyDisplaysChangedAsync(CancellationToken ct = default) =>
        FireAsync(t => t.Kind == TriggerKind.DisplaysChanged, "Displays changed", ct);

    private async Task FireAsync(Func<AutomationTrigger, bool> match, string why, CancellationToken ct)
    {
        var now = signals.Capture(includeProcesses: false);
        foreach (var rule in GetRules().Where(r => r.Enabled && r.Triggers.Any(match)))
            if (ConditionsHold(rule, now)) await RunAsync(rule, why, ct).ConfigureAwait(false);
    }

    public bool Fired(AutomationTrigger t, AutomationSignals prev, AutomationSignals now, DateTimeOffset since)
    {
        bool Crossed(DateTimeOffset at) => at > since && at <= now.Now;
        switch (t.Kind)
        {
            case TriggerKind.TimeOfDay:
                if (!TimeOnly.TryParse(t.Time, out var time)) return false;
                var today = new DateTimeOffset(DateOnly.FromDateTime(now.Now.DateTime).ToDateTime(time), now.Now.Offset);
                return Crossed(today);
            case TriggerKind.Sunrise or TriggerKind.Sunset:
                var (lat, lon) = location();
                var (rise, set) = SolarCalculator.Calculate(DateOnly.FromDateTime(now.Now.DateTime), lat, lon, now.Now.Offset);
                var at = t.Kind == TriggerKind.Sunrise ? rise : set;
                return at is { } a && Crossed(a.AddMinutes(t.OffsetMinutes));
            case TriggerKind.ProcessStarted:
                return Name(t.ProcessName) is { } started && now.Processes.Contains(started) && !prev.Processes.Contains(started);
            case TriggerKind.ProcessStopped:
                return Name(t.ProcessName) is { } stopped && !now.Processes.Contains(stopped) && prev.Processes.Contains(stopped);
            case TriggerKind.BatteryBelow:
                return now.BatteryPercent is { } p && prev.BatteryPercent is { } pp && p < t.Percent && pp >= t.Percent && now.OnBattery == true;
            case TriggerKind.PowerSourceChanged:
                return now.OnBattery is { } b && prev.OnBattery is { } pb && b != pb && b == t.OnBattery;
            case TriggerKind.Interval:
                var minutes = Math.Max(1, t.IntervalMinutes);
                return Math.Floor((now.Now - DateTimeOffset.UnixEpoch).TotalMinutes / minutes) > Math.Floor((since - DateTimeOffset.UnixEpoch).TotalMinutes / minutes);
            default:
                return false;
        }
    }

    public static bool ConditionsHold(AutomationRule rule, AutomationSignals s) => rule.Conditions.All(c => c.Kind switch
    {
        ConditionKind.TimeBetween => TimeOnly.TryParse(c.From, out var f) && TimeOnly.TryParse(c.To, out var t) && TimeOnly.FromDateTime(s.Now.DateTime).IsBetween(f, t),
        ConditionKind.DaysOfWeek => c.Days.Count == 0 || c.Days.Contains(s.Now.DayOfWeek),
        ConditionKind.OnBattery => s.OnBattery == true,
        ConditionKind.OnAcPower => s.OnBattery == false,
        ConditionKind.ProcessRunning => Name(c.ProcessName) is { } n && s.Processes.Contains(n),
        ConditionKind.ProcessNotRunning => Name(c.ProcessName) is { } n2 && !s.Processes.Contains(n2),
        ConditionKind.ActiveThemeIs => s.ActiveThemeId == c.ThemeId,
        _ => true,
    });

    /// <summary>Runs a rule's actions in order (also used by "Run now" and the command palette).</summary>
    public async Task<AutomationRun> RunAsync(AutomationRule rule, string trigger, CancellationToken ct = default, bool ignoreCooldown = false)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var current = store.Get<AutomationRule>(Collection, rule.Id) ?? rule;
            if (!ignoreCooldown && current.LastRun is { } last && DateTimeOffset.Now - last < TimeSpan.FromSeconds(current.CooldownSeconds))
                return new AutomationRun(rule.Id, rule.Name, DateTimeOffset.Now, trigger, true, ["Skipped: cooldown"]);

            var log = new List<string>();
            var success = true;
            foreach (var action in current.Actions)
            {
                try
                {
                    var r = await executor.ExecuteAsync(action, ct).ConfigureAwait(false);
                    log.Add($"{action.Kind}: {r.Message}");
                    if (!r.Success) { success = false; log.AddRange(r.Errors); if (current.StopOnError) break; }
                }
                catch (Exception ex)
                {
                    success = false;
                    log.Add($"{action.Kind}: {ex.Message}");
                    logger.LogError(ex, "Automation action failed in {Rule}", rule.Name);
                    if (current.StopOnError) break;
                }
            }
            var run = new AutomationRun(rule.Id, rule.Name, DateTimeOffset.Now, trigger, success, log);
            store.Put(Collection, current.Id, current with { LastRun = run.At });
            store.Put(HistoryCollection, $"{run.At.ToUnixTimeMilliseconds():D15}-{rule.Id}", run);
            foreach (var old in store.GetIds(HistoryCollection).Order().SkipLast(200)) store.Delete(HistoryCollection, old);
            RuleRan?.Invoke(run);
            return run;
        }
        finally { _gate.Release(); }
    }

    private static string? Name(string? process)
    {
        if (string.IsNullOrWhiteSpace(process)) return null;
        var n = process.Trim().ToLowerInvariant();
        return n.EndsWith(".exe", StringComparison.Ordinal) ? n[..^4] : n;
    }

    public static string Describe(AutomationTrigger t) => t.Kind switch
    {
        TriggerKind.TimeOfDay => $"At {t.Time}",
        TriggerKind.Sunrise => t.OffsetMinutes == 0 ? "At sunrise" : $"{Math.Abs(t.OffsetMinutes)} min {(t.OffsetMinutes < 0 ? "before" : "after")} sunrise",
        TriggerKind.Sunset => t.OffsetMinutes == 0 ? "At sunset" : $"{Math.Abs(t.OffsetMinutes)} min {(t.OffsetMinutes < 0 ? "before" : "after")} sunset",
        TriggerKind.ProcessStarted => $"When {t.ProcessName} starts",
        TriggerKind.ProcessStopped => $"When {t.ProcessName} closes",
        TriggerKind.BatteryBelow => $"When battery drops below {t.Percent}%",
        TriggerKind.PowerSourceChanged => t.OnBattery ? "When unplugged" : "When plugged in",
        TriggerKind.DisplaysChanged => "When displays change",
        TriggerKind.AppStartup => "When AETHER starts",
        TriggerKind.Interval => $"Every {t.IntervalMinutes} min",
        _ => "Manually",
    };

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _gate.Dispose();
    }
}
