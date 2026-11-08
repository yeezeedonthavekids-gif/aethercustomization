using System.Text.Json.Nodes;
using Aether.Core.Abstractions;
using Aether.ModEngine.Execution;
using Aether.ModEngine.Lifecycle;
using Aether.ModEngine.Manifest;
using Aether.Services.Mods.Builtin;
using Aether.Tests.Fakes;

namespace Aether.Tests.Services;

public class BuiltinModTests
{
    private static readonly IReadOnlyList<ModManifest> All = new BuiltinModCatalog().Manifests;

    [Fact]
    public void Catalog_contains_twenty_valid_mods()
    {
        Assert.Equal(20, All.Count);
        foreach (var m in All)
            Assert.True(ManifestValidator.Validate(m).Count == 0, $"{m.Id}: {string.Join("; ", ManifestValidator.Validate(m))}");
        Assert.Equal(All.Count, All.Select(m => m.Id).Distinct().Count());
    }

    [Fact]
    public void Every_component_referenced_by_a_manifest_is_registered()
    {
        var registry = new ModComponentRegistry();
        BuiltinComponents.RegisterAll(registry);
        foreach (var m in All.Where(m => m.Implementation.Kind == ImplementationKind.Component))
            Assert.True(registry.IsRegistered(m.Implementation.Component!), m.Id);
    }

    [Fact]
    public void Clock_format_plan_respects_presets_and_custom_values()
    {
        var m = All.Single(x => x.Id == "aether.clock-format");
        var plan = PlanBuilder.Build(m, new JsonObject { ["timePreset"] = "custom", ["customTime"] = "HH.mm", ["datePreset"] = "unchanged" });
        var write = Assert.Single(plan.Steps.OfType<RegistryWriteStep>());
        Assert.Equal("sShortTime", write.Name);
        Assert.Equal("HH.mm", write.Value.Data);
        Assert.Contains("intl", plan.BroadcastAreas);
    }

    [Fact]
    public void Seconds_mod_only_restarts_explorer_when_asked()
    {
        var m = All.Single(x => x.Id == "aether.clock-seconds");
        Assert.True(PlanBuilder.Build(m, new JsonObject { ["restartExplorer"] = true }).Post.HasFlag(PostActions.ExplorerRestart));
        Assert.False(PlanBuilder.Build(m, new JsonObject { ["restartExplorer"] = false }).Post.HasFlag(PostActions.ExplorerRestart));
    }

    [Fact]
    public async Task Declarative_builtins_apply_and_fully_revert()
    {
        var declarative = All.Where(m => m.Implementation.Kind == ImplementationKind.Declarative).ToArray();
        using var h = new ModEngineHarness(declarative);
        foreach (var m in declarative)
        {
            h.Manager.GrantPermissions(m.Id, m.Permissions);
            var on = await h.Manager.EnableAsync(m.Id);
            Assert.True(on.Succeeded, $"{m.Id}: {on.Message} {string.Join(";", on.Details)}");
            var off = await h.Manager.DisableAsync(m.Id);
            Assert.True(off.Succeeded, $"{m.Id}: {off.Message}");
        }
        // Everything created by the mods was removed again.
        Assert.Empty(h.Registry.GetSubKeyNames(RegistryRoot.CurrentUser, "Software"));
        Assert.Empty(h.Registry.GetSubKeyNames(RegistryRoot.CurrentUser, "Control Panel"));
        Assert.True(h.SystemSettings.Flags[SystemFlag.ClientAreaAnimation]);
    }
}
