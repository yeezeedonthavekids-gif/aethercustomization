using System.Security.Cryptography;
using System.Text;
using Aether.Core.Abstractions;
using Aether.ModEngine.Manifest;

namespace Aether.ModEngine.Execution;

/// <summary>
/// Runs PowerShell-based mods. Consent is bound to the SHA-256 of the scripts: the grant
/// "powershell.execute:&lt;hash&gt;" must exist, so a changed script always needs fresh consent.
/// </summary>
public sealed class PowerShellExecutor(IPowerShellRunner runner)
{
    public static readonly TimeSpan Timeout = TimeSpan.FromMinutes(2);

    public static string ScriptHash(string applyScript, string revertScript) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(applyScript + "\n--revert--\n" + revertScript)));

    public static string ConsentPermission(string hash) => $"{ModPermissions.PowerShell}:{hash}";

    public bool IsAvailable => runner.IsAvailable;

    public async Task<ProcessResult> RunAsync(string script, System.Text.Json.Nodes.JsonObject settings, CancellationToken ct)
    {
        // Settings are passed as a JSON literal assigned to $AetherSettings; values are never spliced into code.
        var json = settings.ToJsonString().Replace("'", "''");
        var preamble = $"$ErrorActionPreference = 'Stop'\n$AetherSettings = ConvertFrom-Json -InputObject '{json}'\n";
        return await runner.RunScriptAsync(preamble + script, Timeout, ct).ConfigureAwait(false);
    }
}
