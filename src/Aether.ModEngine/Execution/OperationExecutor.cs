using Aether.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace Aether.ModEngine.Execution;

public sealed record ExecutionOutcome(bool Success, IReadOnlyList<string> Applied, string? Error, bool RolledBack);

/// <summary>
/// Applies and reverts execution plans transactionally. Before each change the original value is persisted
/// to the mod's baseline; if any step fails, every step already applied in this transaction is undone.
/// </summary>
public sealed class OperationExecutor(
    IRegistryAccess registry,
    ISystemSettings systemSettings,
    IShellController shell,
    BaselineStore baselines,
    ILogger<OperationExecutor> logger)
{
    public async Task<ExecutionOutcome> ApplyAsync(string modId, ExecutionPlan plan, CancellationToken ct = default)
    {
        var undo = new Stack<Action>();
        var applied = new List<string>();
        var baseline = baselines.Load(modId);

        // Targets this mod changed previously but which are no longer part of the plan (e.g. a setting
        // switched a conditional operation off) go back to their original values.
        var planned = plan.Steps.Select(KeyOf).Where(k => k is not null).ToHashSet();
        foreach (var stale in baseline.Registry.Where(r => !planned.Contains(r.Key)).ToList())
        {
            try
            {
                stale.Restore(registry);
                baseline.Registry.Remove(stale);
                applied.Add($"Restored {stale.Root}\\{stale.Path}\\{stale.Name}");
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to restore stale target {Key} for {Mod}", stale.Key, modId);
            }
        }
        foreach (var stale in baseline.Flags.Where(f => !plan.Steps.OfType<FlagStep>().Any(s => s.Flag == f.Flag)).ToList())
        {
            if (stale.Value is { } v) systemSettings.SetFlag(stale.Flag, v);
            baseline.Flags.Remove(stale);
        }
        baselines.Save(baseline);

        try
        {
            foreach (var step in plan.Steps)
            {
                ct.ThrowIfCancellationRequested();
                switch (step)
                {
                    case RegistryWriteStep w:
                    {
                        baselines.EnsureCaptured(modId, registry, w.Root, w.Path, w.Name);
                        var before = RegistryOriginal.Capture(registry, w.Root, w.Path, w.Name);
                        if (before.ToData() is { } current && current.ValueEquals(w.Value)) { applied.Add(w.Description + " (already set)"); break; }
                        registry.SetValue(w.Root, w.Path, w.Name, w.Value);
                        undo.Push(() => before.Restore(registry));
                        applied.Add(w.Description);
                        break;
                    }
                    case RegistryDeleteStep d:
                    {
                        baselines.EnsureCaptured(modId, registry, d.Root, d.Path, d.Name);
                        var before = RegistryOriginal.Capture(registry, d.Root, d.Path, d.Name);
                        if (!before.Existed) { applied.Add(d.Description + " (not present)"); break; }
                        registry.DeleteValue(d.Root, d.Path, d.Name);
                        undo.Push(() => before.Restore(registry));
                        applied.Add(d.Description);
                        break;
                    }
                    case FlagStep f:
                    {
                        baselines.EnsureCaptured(modId, systemSettings, f.Flag);
                        var before = systemSettings.GetFlag(f.Flag);
                        if (before == f.Value) { applied.Add(f.Description + " (already set)"); break; }
                        systemSettings.SetFlag(f.Flag, f.Value);
                        undo.Push(() => { if (before is { } b) systemSettings.SetFlag(f.Flag, b); });
                        applied.Add(f.Description);
                        break;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Applying {Mod} failed; rolling back {Count} step(s)", modId, undo.Count);
            var rollbackErrors = Rollback(undo);
            await RunPostActionsAsync(plan, ct: CancellationToken.None).ConfigureAwait(false);
            var message = ex is OperationCanceledException ? "The operation was cancelled." : ex.Message;
            if (rollbackErrors.Count > 0) message += " Rollback was incomplete: " + string.Join("; ", rollbackErrors);
            return new ExecutionOutcome(false, applied, message, rollbackErrors.Count == 0);
        }

        await RunPostActionsAsync(plan, ct).ConfigureAwait(false);
        return new ExecutionOutcome(true, applied, null, false);
    }

    /// <summary>Restores everything in the mod's baseline. Entries that fail stay in the baseline for retry.</summary>
    public async Task<ExecutionOutcome> RevertAsync(string modId, ExecutionPlan? postActionsFrom, CancellationToken ct = default)
    {
        var baseline = baselines.Load(modId);
        var restored = new List<string>();
        var errors = new List<string>();

        foreach (var original in Enumerable.Reverse(baseline.Registry).ToList())
        {
            try
            {
                original.Restore(registry);
                baseline.Registry.Remove(original);
                restored.Add($"Restored {original.Root}\\{original.Path}\\{original.Name}");
            }
            catch (Exception ex)
            {
                errors.Add($"{original.Path}\\{original.Name}: {ex.Message}");
            }
        }
        foreach (var flag in baseline.Flags.ToList())
        {
            try
            {
                if (flag.Value is { } v) systemSettings.SetFlag(flag.Flag, v);
                baseline.Flags.Remove(flag);
                restored.Add($"Restored {flag.Flag}");
            }
            catch (Exception ex) { errors.Add($"{flag.Flag}: {ex.Message}"); }
        }

        baselines.Save(baseline);
        if (postActionsFrom is not null) await RunPostActionsAsync(postActionsFrom, ct).ConfigureAwait(false);
        return errors.Count == 0
            ? new ExecutionOutcome(true, restored, null, false)
            : new ExecutionOutcome(false, restored, string.Join("; ", errors), false);
    }

    private List<string> Rollback(Stack<Action> undo)
    {
        var errors = new List<string>();
        while (undo.TryPop(out var action))
        {
            try { action(); }
            catch (Exception ex) { errors.Add(ex.Message); logger.LogError(ex, "Rollback step failed"); }
        }
        return errors;
    }

    private async Task RunPostActionsAsync(ExecutionPlan plan, CancellationToken ct)
    {
        try
        {
            if (plan.Post.HasFlag(PostActions.Broadcast))
                foreach (var area in plan.BroadcastAreas) systemSettings.BroadcastSettingChange(area);
            if (plan.Post.HasFlag(PostActions.CursorReload)) systemSettings.ReloadCursors();
            if (plan.Post.HasFlag(PostActions.ShellRefresh)) systemSettings.RefreshShell();
            if (plan.Post.HasFlag(PostActions.ExplorerRestart)) await shell.RestartExplorerAsync(false, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Post actions only refresh state; the change itself is already persisted.
            logger.LogWarning(ex, "Post action failed");
        }
    }

    private static string? KeyOf(PlanStep s) => s switch
    {
        RegistryWriteStep w => w.Key,
        RegistryDeleteStep d => d.Key,
        _ => null,
    };
}
