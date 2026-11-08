using System.Text.Json.Nodes;
using Aether.Core.Abstractions;
using Aether.Core.Diagnostics;
using Aether.ModEngine.Execution;
using Aether.ModEngine.Manifest;
using Aether.ModEngine.Security;
using Aether.Core.Versioning;

namespace Aether.Services.Tweaks;

/// <summary>Reads and applies <see cref="TweakDefinition"/>s through the transactional executor (baseline per tweak).</summary>
public sealed class TweakService(IRegistryAccess registry, OperationExecutor executor, BaselineStore baselines, ISystemEnvironment environment, IAuditLog audit)
{
    public event Action<string>? Changed;

    private static string Owner(string id) => "tweak." + id;

    public IReadOnlyList<TweakState> GetStates(TweakArea? area = null) =>
        TweakCatalog.All.Where(t => area is null || t.Area == area).Select(GetState).ToList();

    public TweakState GetState(TweakDefinition t)
    {
        string? note = null;
        var build = environment.Version.Build;
        var compatible = environment.IsWindows;
        if (!environment.IsWindows) note = "Windows only.";
        else if (t.MinBuild is { } min && build < min) { compatible = false; note = $"Requires Windows build {min} or newer."; }
        else if (t.MaxBuild is { } max && build > max) { compatible = false; note = $"Not used by Windows after build {max}."; }
        else if (t.RequiresAdmin && !environment.IsAdministrator) note = "Requires administrator rights.";

        string? current = null;
        try { current = ReadCurrent(t); } catch (Exception) { /* unreadable → unknown */ }
        return new TweakState(t, current, !baselines.Load(Owner(t.Id)).IsEmpty, compatible, note);
    }

    private string? ReadCurrent(TweakDefinition t)
    {
        var probe = t.Probe;
        var value = registry.GetValue(probe.Root, probe.Path, probe.Name);
        if (value is null) return t.DefaultOption;
        foreach (var option in t.Options)
        {
            var op = option.Operations.OfType<RegistrySetOperation>().FirstOrDefault(o => o.Path == probe.Path && o.Name == probe.Name);
            if (op is null) continue;
            var expected = ValueTemplate.ToRegistryData(op.Value, op.Kind);
            if (expected.ValueEquals(value)) return option.Value;
            if (expected.Kind == RegistryDataKind.String && value.Kind == RegistryDataKind.String && Equals(expected.Data, value.Data)) return option.Value;
        }
        return null; // a value Windows or another tool set that AETHER doesn't recognise
    }

    public async Task<OperationResult> SetAsync(string id, string optionValue, CancellationToken ct = default)
    {
        var t = TweakCatalog.All.FirstOrDefault(x => x.Id == id);
        if (t is null) return OperationResult.Fail($"Unknown setting '{id}'.");
        var state = GetState(t);
        if (!state.IsCompatible) return OperationResult.Fail($"'{t.Title}' is not available: {state.CompatibilityNote}");
        if (t.RequiresAdmin && !environment.IsAdministrator) return OperationResult.Fail("Restart AETHER as administrator to change this setting.");
        var option = t.Options.FirstOrDefault(o => o.Value == optionValue);
        if (option is null) return OperationResult.Fail($"'{optionValue}' is not a valid choice for '{t.Title}'.");

        var granted = new[] { ModPermissions.RegistryUser, ModPermissions.RegistryMachine, ModPermissions.StartupManage };
        foreach (var reg in option.Operations.OfType<RegistryOperation>())
        {
            var decision = RegistryPolicy.Evaluate(reg.Root, reg.Path, granted);
            if (!decision.Allowed) return OperationResult.Fail(decision.Reason);
        }

        var manifest = SyntheticManifest(t, option);
        var plan = PlanBuilder.Build(manifest, new JsonObject());
        var outcome = await executor.ApplyAsync(Owner(id), plan, ct).ConfigureAwait(false);
        audit.Record(new AuditEntry(DateTimeOffset.UtcNow, "tweaks", "set", id, outcome.Success, outcome.Success ? option.Label : outcome.Error));
        Changed?.Invoke(id);
        if (!outcome.Success) return OperationResult.Fail($"'{t.Title}' could not be changed and was rolled back.", outcome.Error ?? "");
        var restart = t.Restart switch
        {
            RestartRequirement.Explorer => " Restart Explorer to see the change everywhere.",
            RestartRequirement.SignOut => " Sign out and back in to apply it.",
            RestartRequirement.Reboot => " Restart your PC to apply it.",
            _ => "",
        };
        return OperationResult.Ok($"{t.Title}: {option.Label}.{restart}");
    }

    public async Task<OperationResult> RestoreOriginalAsync(string id, CancellationToken ct = default)
    {
        var t = TweakCatalog.All.FirstOrDefault(x => x.Id == id);
        if (t is null) return OperationResult.Fail($"Unknown setting '{id}'.");
        var post = PlanBuilder.Build(SyntheticManifest(t, t.Options[0]), new JsonObject());
        var outcome = await executor.RevertAsync(Owner(id), post with { Steps = [] }, ct).ConfigureAwait(false);
        audit.Record(new AuditEntry(DateTimeOffset.UtcNow, "tweaks", "restore", id, outcome.Success, outcome.Error));
        Changed?.Invoke(id);
        return outcome.Success ? OperationResult.Ok($"'{t.Title}' is back to its original value.") : OperationResult.Fail("Restore failed.", outcome.Error ?? "");
    }

    public async Task<OperationResult> RestoreAreaAsync(TweakArea area, CancellationToken ct = default)
    {
        var errors = new List<string>();
        foreach (var s in GetStates(area).Where(s => s.IsModified))
        {
            var r = await RestoreOriginalAsync(s.Definition.Id, ct).ConfigureAwait(false);
            if (!r.Success) errors.Add($"{s.Definition.Title}: {r.Describe()}");
        }
        return errors.Count == 0 ? OperationResult.Ok("All settings in this section are back to their original values.") : OperationResult.Fail("Some settings could not be restored.", errors);
    }

    public const string AccentOwner = "personal.accentcolor";

    public bool IsAccentCustomized => !baselines.Load(Owner(AccentOwner)).IsEmpty;

    /// <summary>
    /// Sets a custom accent color through the transactional executor (so "Restore original" brings back the
    /// Windows accent). The writes are produced by <see cref="ThemeEngine.WindowsPersonalizationApplier"/>.
    /// </summary>
    public async Task<OperationResult> ApplyAccentAsync(Aether.Core.Configuration.ColorValue color, CancellationToken ct = default)
    {
        var recorder = new RecordingRegistry(registry);
        new ThemeEngine.WindowsPersonalizationApplier(recorder, NullBroadcast.Instance).ApplyAccent(color);
        var steps = recorder.Writes.Select(w => (PlanStep)new RegistryWriteStep(w.Root, w.Path, w.Name, w.Value, $"Set {w.Name}")).ToList();
        var outcome = await executor.ApplyAsync(Owner(AccentOwner), new ExecutionPlan(steps, PostActions.Broadcast, ["ImmersiveColorSet"]), ct).ConfigureAwait(false);
        audit.Record(new AuditEntry(DateTimeOffset.UtcNow, "personalization", "accent", color.ToHex(), outcome.Success, outcome.Error));
        Changed?.Invoke(AccentOwner);
        return outcome.Success ? OperationResult.Ok($"Accent color set to {color.ToHex(false)}.") : OperationResult.Fail("The accent color could not be changed.", outcome.Error ?? "");
    }

    public async Task<OperationResult> RestoreAccentAsync(CancellationToken ct = default)
    {
        var outcome = await executor.RevertAsync(Owner(AccentOwner), new ExecutionPlan([], PostActions.Broadcast, ["ImmersiveColorSet"]), ct).ConfigureAwait(false);
        Changed?.Invoke(AccentOwner);
        return outcome.Success ? OperationResult.Ok("Your original accent color is back.") : OperationResult.Fail("Restore failed.", outcome.Error ?? "");
    }

    private sealed class RecordingRegistry(IRegistryAccess inner) : IRegistryAccess
    {
        public List<(RegistryRoot Root, string Path, string Name, RegistryData Value)> Writes { get; } = [];
        public RegistryData? GetValue(RegistryRoot root, string path, string name) => inner.GetValue(root, path, name);
        public void SetValue(RegistryRoot root, string path, string name, RegistryData value) => Writes.Add((root, path, name, value));
        public bool DeleteValue(RegistryRoot root, string path, string name) => false;
        public bool KeyExists(RegistryRoot root, string path) => inner.KeyExists(root, path);
        public void CreateKey(RegistryRoot root, string path) { }
        public bool DeleteKeyIfEmpty(RegistryRoot root, string path) => false;
        public IReadOnlyList<string> GetValueNames(RegistryRoot root, string path) => inner.GetValueNames(root, path);
        public IReadOnlyList<string> GetSubKeyNames(RegistryRoot root, string path) => inner.GetSubKeyNames(root, path);
    }

    private sealed class NullBroadcast : ISystemSettings
    {
        public static readonly NullBroadcast Instance = new();
        public bool? GetFlag(SystemFlag flag) => null;
        public void SetFlag(SystemFlag flag, bool value) { }
        public void BroadcastSettingChange(string? area) { }
        public void RefreshShell() { }
        public void ReloadCursors() { }
    }

    private static ModManifest SyntheticManifest(TweakDefinition t, TweakOption option) => new()
    {
        Id = "aether.tweak." + t.Id.Replace('.', '-'), Name = t.Title, Description = t.WhatItChanges, Author = "AETHER",
        Version = new SemVersion(1), Permissions = [ModPermissions.RegistryUser, ModPermissions.RegistryMachine], Operations = [.. option.Operations],
    };
}
