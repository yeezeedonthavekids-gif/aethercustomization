using System.Text.Json.Nodes;
using Aether.Core.Abstractions;
using Aether.ModEngine.Manifest;

namespace Aether.ModEngine.Execution;

public abstract record PlanStep(string Description);
public sealed record RegistryWriteStep(RegistryRoot Root, string Path, string Name, RegistryData Value, string Description) : PlanStep(Description)
{
    public string Key => $"{Root}\\{Path}\\{Name}".ToLowerInvariant();
}
public sealed record RegistryDeleteStep(RegistryRoot Root, string Path, string Name, string Description) : PlanStep(Description)
{
    public string Key => $"{Root}\\{Path}\\{Name}".ToLowerInvariant();
}
public sealed record FlagStep(SystemFlag Flag, bool Value, string Description) : PlanStep(Description);

[Flags]
public enum PostActions { None = 0, Broadcast = 1, ShellRefresh = 2, CursorReload = 4, ExplorerRestart = 8 }

public sealed record ExecutionPlan(IReadOnlyList<PlanStep> Steps, PostActions Post, IReadOnlyList<string?> BroadcastAreas)
{
    /// <summary>Human-readable preview shown before applying ("What will change").</summary>
    public IEnumerable<string> Describe() => Steps.Select(s => s.Description)
        .Concat(Post.HasFlag(PostActions.ExplorerRestart) ? ["Restart Windows Explorer"] : []);
}

public static class PlanBuilder
{
    /// <summary>Resolves a manifest's declarative operations against concrete settings.</summary>
    public static ExecutionPlan Build(ModManifest manifest, JsonObject settings)
    {
        var steps = new List<PlanStep>();
        var post = PostActions.None;
        var areas = new List<string?>();

        foreach (var op in manifest.Operations)
        {
            if (!ValueTemplate.ConditionHolds(op.When, settings)) continue;

            // Post actions also run after a revert (evaluated against the same settings).
            switch (op)
            {
                case BroadcastOperation b: post |= PostActions.Broadcast; if (!areas.Contains(b.Area)) areas.Add(b.Area); continue;
                case ShellRefreshOperation: post |= PostActions.ShellRefresh; continue;
                case CursorReloadOperation: post |= PostActions.CursorReload; continue;
                case ExplorerRestartOperation: post |= PostActions.ExplorerRestart; continue;
            }

            switch (op)
            {
                case RegistrySetOperation set:
                {
                    var resolved = ValueTemplate.Resolve(set.Value, settings);
                    resolved = ValueTemplate.ApplyMapAndTransform(resolved, set.ValueMap, set.Transform);
                    var data = ValueTemplate.ToRegistryData(resolved, set.Kind);
                    steps.Add(new RegistryWriteStep(set.Root, set.Path, set.Name, data,
                        op.Description ?? $"Set {set.Target} = {data} ({set.Kind})"));
                    break;
                }
                case RegistryDeleteOperation del:
                    steps.Add(new RegistryDeleteStep(del.Root, del.Path, del.Name, op.Description ?? $"Remove {del.Target}"));
                    break;
                case SystemFlagOperation flag:
                {
                    var resolved = ValueTemplate.ApplyMapAndTransform(ValueTemplate.Resolve(flag.Value, settings), null, flag.Transform);
                    var value = ValueTemplate.ToBool(resolved);
                    steps.Add(new FlagStep(flag.Flag, value, op.Description ?? $"{(value ? "Enable" : "Disable")} {flag.Flag}"));
                    break;
                }
            }
        }
        return new ExecutionPlan(steps, post, areas);
    }
}
