using System.Text.RegularExpressions;
using Aether.Core.Abstractions;

namespace Aether.Services.Applications;

public sealed record PowerPlan(Guid Id, string Name, bool IsActive);

/// <summary>Lists and switches power plans with powercfg.exe (documented, ships with Windows).</summary>
public sealed partial class PowerPlanService(IProcessRunner runner)
{
    public static readonly Guid Balanced = Guid.Parse("381b4222-f694-41f0-9685-ff5bb260df2e");
    public static readonly Guid HighPerformance = Guid.Parse("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
    public static readonly Guid Ultimate = Guid.Parse("e9a42b02-d5df-448d-aa00-03f14749eb61");
    public static readonly Guid PowerSaver = Guid.Parse("a1841308-3541-4fab-bc81-f71556f20b4a");

    [GeneratedRegex(@"([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})\s+\((.+?)\)\s*(\*)?")]
    private static partial Regex PlanLine();

    public static IReadOnlyList<PowerPlan> Parse(string output) =>
        output.Split('\n').Select(l => PlanLine().Match(l)).Where(m => m.Success)
            .Select(m => new PowerPlan(Guid.Parse(m.Groups[1].Value), m.Groups[2].Value.Trim(), m.Groups[3].Success)).ToList();

    public async Task<IReadOnlyList<PowerPlan>> GetPlansAsync(CancellationToken ct = default)
    {
        var r = await runner.RunAsync("powercfg.exe", ["/list"], TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
        if (!r.Succeeded) throw new InvalidOperationException($"powercfg failed: {r.StandardError.Trim()}");
        return Parse(r.StandardOutput);
    }

    public async Task<PowerPlan?> GetActiveAsync(CancellationToken ct = default) =>
        (await GetPlansAsync(ct).ConfigureAwait(false)).FirstOrDefault(p => p.IsActive);

    public async Task SetActiveAsync(Guid id, CancellationToken ct = default)
    {
        var r = await runner.RunAsync("powercfg.exe", ["/setactive", id.ToString()], TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
        if (!r.Succeeded) throw new InvalidOperationException($"Could not activate the power plan: {r.StandardError.Trim()} {r.StandardOutput.Trim()}");
    }
}
