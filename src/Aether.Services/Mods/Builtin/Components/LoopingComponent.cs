using Aether.ModEngine.Execution;
using Microsoft.Extensions.Logging;

namespace Aether.Services.Mods.Builtin.Components;

/// <summary>Base for components that do periodic work on a background loop that ends when the mod stops.</summary>
public abstract class LoopingComponent : ModComponentBase
{
    private CancellationTokenSource? _cts;
    private Task? _loop;

    protected abstract TimeSpan Interval(ModComponentContext context);
    protected abstract Task TickAsync(ModComponentContext context, CancellationToken ct);

    protected void StartLoop(ModComponentContext context)
    {
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _loop = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try { await Task.Delay(Interval(context), token).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
                try { await TickAsync(context, token).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
                catch (Exception ex) { context.Logger.LogWarning(ex, "{Mod} periodic task failed", context.ModId); }
            }
        }, token);
    }

    protected async Task StopLoopAsync()
    {
        if (_cts is null) return;
        await _cts.CancelAsync().ConfigureAwait(false);
        try { if (_loop is not null) await _loop.ConfigureAwait(false); } catch (OperationCanceledException) { }
        _cts.Dispose();
        _cts = null;
    }

    public override async Task StopAsync(ModComponentContext context, CancellationToken ct) => await StopLoopAsync().ConfigureAwait(false);
    public override async ValueTask DisposeAsync() => await StopLoopAsync().ConfigureAwait(false);
}
