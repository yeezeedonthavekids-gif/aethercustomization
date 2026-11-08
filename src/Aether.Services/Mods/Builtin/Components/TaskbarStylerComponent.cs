using Aether.Core.Configuration;
using Aether.ModEngine.Execution;
using Aether.ModEngine.Manifest;
using Aether.Services.Platform;

namespace Aether.Services.Mods.Builtin.Components;

/// <summary>Mod 1 — keeps the configured composition effect on the taskbar(s).</summary>
public sealed class TaskbarStylerComponent : LoopingComponent
{
    private ITaskbarStyler? _styler;

    public override string? Status => _styler is null ? null : "Effect active";

    protected override TimeSpan Interval(ModComponentContext context) => TimeSpan.FromSeconds(1);

    public override Task StartAsync(ModComponentContext context, CancellationToken ct)
    {
        _styler = context.GetService<ITaskbarStyler>(ModPermissions.ShellAppearance);
        if (!_styler.IsSupported) throw new PlatformNotSupportedException("Taskbar effects are not supported on this system.");
        Apply(context);
        StartLoop(context);
        return Task.CompletedTask;
    }

    public override Task OnSettingsChangedAsync(ModComponentContext context, CancellationToken ct)
    {
        Apply(context);
        return Task.CompletedTask;
    }

    protected override Task TickAsync(ModComponentContext context, CancellationToken ct)
    {
        Apply(context);
        return Task.CompletedTask;
    }

    private void Apply(ModComponentContext context)
    {
        var effect = context.Settings.GetString("effect") switch
        {
            "clear" => TaskbarEffect.Clear,
            "acrylic" => TaskbarEffect.Acrylic,
            "tint" => TaskbarEffect.Tint,
            _ => TaskbarEffect.Blur,
        };
        var tint = ColorValue.TryParse(context.Settings.GetString("tint"), out var c) ? c : new ColorValue(0x33, 0, 0, 0);
        _styler!.Apply(effect, tint, context.Settings.GetBool("allMonitors", true));
    }

    public override async Task StopAsync(ModComponentContext context, CancellationToken ct)
    {
        await StopLoopAsync().ConfigureAwait(false);
        _styler?.Reset();
        _styler = null;
    }
}
