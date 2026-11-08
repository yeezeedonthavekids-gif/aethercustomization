using System.Runtime.InteropServices;
using Aether.Core.Abstractions;
using Aether.Core.Versioning;
using Aether.ModEngine.Manifest;

namespace Aether.ModEngine.Lifecycle;

public sealed class CompatibilityChecker(ISystemEnvironment environment, ModEngineOptions options)
{
    public CompatibilityReport Check(ModManifest m)
    {
        var problems = new List<string>();
        var warnings = new List<string>();
        var v = environment.Version;
        var c = m.Compatibility;

        if (!environment.IsWindows)
            problems.Add("This mod changes Windows settings and can only run on Windows.");
        else
        {
            if (c.MinBuild is { } min && !v.IsAtLeast(min, c.MinRevision ?? 0))
                problems.Add($"Requires Windows build {min}{(c.MinRevision is { } r ? "." + r : "")} or newer (you have {v.BuildString}).");
            if (c.MaxBuild is { } max && v.Build > max)
                problems.Add($"Only supported up to Windows build {max} (you have {v.Build}).");
        }

        if (c.Architectures.Count > 0)
        {
            var arch = RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant();
            if (!c.Architectures.Any(a => a.Equals(arch, StringComparison.OrdinalIgnoreCase)))
                problems.Add($"Supports {string.Join(", ", c.Architectures)} only (this PC is {arch}).");
        }

        if (c.MinHostVersion is { } hv && SemVersion.TryParse(hv, out var need) && SemVersion.TryParse(options.HostVersion, out var have) && have < need)
            problems.Add($"Requires AETHER {need} or newer.");

        var needsAdmin = c.RequiresAdmin || m.Permissions.Contains(ModPermissions.RegistryMachine);
        if (needsAdmin && !environment.IsAdministrator)
            warnings.Add("Requires administrator rights. Restart AETHER as administrator to enable it.");

        if (m.Restart != RestartRequirement.None)
            warnings.Add(m.Restart switch
            {
                RestartRequirement.Explorer => "Windows Explorer restarts when this mod is applied or removed.",
                RestartRequirement.SignOut => "Some changes take effect after you sign out.",
                _ => "Some changes take effect after a restart.",
            });

        if (c.Notes is { Length: > 0 } notes) warnings.Add(notes);
        return new CompatibilityReport(problems.Count == 0, problems, warnings);
    }
}
