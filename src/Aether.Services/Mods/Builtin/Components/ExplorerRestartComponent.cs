using Aether.Core.Abstractions;
using Aether.Core.Diagnostics;
using Aether.ModEngine.Execution;
using Aether.ModEngine.Manifest;

namespace Aether.Services.Mods.Builtin.Components;

/// <summary>Mod 10 — restarts explorer.exe on demand.</summary>
public sealed class ExplorerRestartComponent : ModComponentBase
{
    public override async Task<OperationResult> InvokeActionAsync(ModComponentContext context, string actionId, CancellationToken ct)
    {
        if (actionId != "restart") return OperationResult.Fail($"Unknown action '{actionId}'.");
        var shell = context.GetService<IShellController>(ModPermissions.ExplorerRestart);
        await shell.RestartExplorerAsync(ct).ConfigureAwait(false);
        return OperationResult.Ok("Windows Explorer was restarted.");
    }
}
