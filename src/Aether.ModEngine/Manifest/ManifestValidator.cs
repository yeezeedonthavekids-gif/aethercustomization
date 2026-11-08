using System.Text.RegularExpressions;
using Aether.Core.Abstractions;
using Aether.Core.Configuration;
using Aether.Core.Versioning;
using Aether.ModEngine.Execution;
using Aether.ModEngine.Security;

namespace Aether.ModEngine.Manifest;

public static partial class ManifestValidator
{
    [GeneratedRegex(@"^[a-z0-9]+(?:[.\-][a-z0-9]+)+$")]
    private static partial Regex IdPattern();

    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9_]*$")]
    private static partial Regex KeyPattern();

    /// <summary>Returns every problem found. An empty list means the manifest is valid.</summary>
    public static IReadOnlyList<string> Validate(ModManifest m)
    {
        var errors = new List<string>();
        if (m.SchemaVersion != ModManifest.CurrentSchemaVersion)
            errors.Add($"Unsupported manifest schemaVersion {m.SchemaVersion}; expected {ModManifest.CurrentSchemaVersion}.");
        if (string.IsNullOrWhiteSpace(m.Id) || !IdPattern().IsMatch(m.Id) || m.Id.Length > 100)
            errors.Add("Id must be lowercase dotted (e.g. 'publisher.mod-name') and at most 100 characters.");
        if (string.IsNullOrWhiteSpace(m.Name) || m.Name.Length > 80) errors.Add("Name is required and must be at most 80 characters.");
        if (string.IsNullOrWhiteSpace(m.Description)) errors.Add("Description is required.");
        if (string.IsNullOrWhiteSpace(m.Author)) errors.Add("Author is required.");

        foreach (var p in m.Permissions)
            if (!ModPermissions.IsKnown(p)) errors.Add($"Unknown permission '{p}'.");

        if (m.Compatibility.MinBuild is { } min && m.Compatibility.MaxBuild is { } max && min > max)
            errors.Add("compatibility.minBuild is greater than maxBuild.");
        if (m.Compatibility.MinHostVersion is { } hv && !SemVersion.TryParse(hv, out _))
            errors.Add($"compatibility.minHostVersion '{hv}' is not a valid version.");

        foreach (var d in m.Dependencies)
        {
            if (d.Id == m.Id) errors.Add("A mod cannot depend on itself.");
            if (!VersionRange.TryParse(d.VersionRange, out _)) errors.Add($"Dependency '{d.Id}' has an invalid version range '{d.VersionRange}'.");
        }

        var keys = new HashSet<string>();
        foreach (var s in m.Settings)
        {
            if (!KeyPattern().IsMatch(s.Key)) errors.Add($"Setting key '{s.Key}' must be alphanumeric.");
            if (!keys.Add(s.Key)) errors.Add($"Duplicate setting key '{s.Key}'.");
            if (s.Type == SettingKind.Choice && (s.Options is null || s.Options.Count == 0))
                errors.Add($"Choice setting '{s.Key}' needs options.");
            if (s.Default is not null && SettingValues.Validate(s, s.Default) is { } err)
                errors.Add($"Default for '{s.Key}' is invalid: {err}");
            if (s.VisibleWhen is { } vw && m.Settings.All(o => o.Key != vw))
                errors.Add($"Setting '{s.Key}' references unknown visibleWhen '{vw}'.");
        }

        switch (m.Implementation.Kind)
        {
            case ImplementationKind.Component when string.IsNullOrWhiteSpace(m.Implementation.Component):
                errors.Add("Component mods must name implementation.component.");
                break;
            case ImplementationKind.PowerShell:
                if (string.IsNullOrWhiteSpace(m.Implementation.ApplyScript) || string.IsNullOrWhiteSpace(m.Implementation.RevertScript))
                    errors.Add("PowerShell mods must provide both applyScript and revertScript so they stay reversible.");
                if (!m.Permissions.Contains(ModPermissions.PowerShell))
                    errors.Add($"PowerShell mods must declare the '{ModPermissions.PowerShell}' permission.");
                break;
            case ImplementationKind.Declarative when m.Operations.Count == 0:
                errors.Add("Declarative mods must define at least one operation.");
                break;
        }

        var actionIds = new HashSet<string>();
        foreach (var a in m.Actions)
            if (!actionIds.Add(a.Id)) errors.Add($"Duplicate action id '{a.Id}'.");

        for (var i = 0; i < m.Operations.Count; i++)
            ValidateOperation(m, m.Operations[i], $"operations[{i}]", keys, errors);

        return errors;
    }

    private static void ValidateOperation(ModManifest m, ModOperation op, string where, HashSet<string> keys, List<string> errors)
    {
        if (op.When is { } w && !keys.Contains(w.Setting))
            errors.Add($"{where}: condition references unknown setting '{w.Setting}'.");

        switch (op)
        {
            case RegistryOperation reg:
                RegistryRoot root;
                try { root = reg.Root; }
                catch (InvalidOperationException ex) { errors.Add($"{where}: {ex.Message}"); return; }
                if (string.IsNullOrWhiteSpace(reg.Path) || reg.Path.Contains("..") || reg.Path.StartsWith('\\'))
                    errors.Add($"{where}: invalid registry path '{reg.Path}'.");
                var decision = RegistryPolicy.Evaluate(root, reg.Path, m.Permissions);
                if (!decision.Allowed) errors.Add($"{where}: {decision.Reason}");
                if (reg is RegistrySetOperation set)
                {
                    foreach (var key in ValueTemplate.ReferencedKeys(set.Value))
                        if (!keys.Contains(key)) errors.Add($"{where}: value references unknown setting '{key}'.");
                }
                break;
            case SystemFlagOperation flag:
                if (!m.Permissions.Contains(ModPermissions.SystemParameters))
                    errors.Add($"{where}: system.flag requires the '{ModPermissions.SystemParameters}' permission.");
                foreach (var key in ValueTemplate.ReferencedKeys(flag.Value))
                    if (!keys.Contains(key)) errors.Add($"{where}: value references unknown setting '{key}'.");
                break;
            case ExplorerRestartOperation:
                if (!m.Permissions.Contains(ModPermissions.ExplorerRestart))
                    errors.Add($"{where}: explorer.restart requires the '{ModPermissions.ExplorerRestart}' permission.");
                break;
        }
    }
}
