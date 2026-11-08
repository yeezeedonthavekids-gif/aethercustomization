using Aether.Core.Diagnostics;
using Aether.ModEngine.Lifecycle;
using Aether.Services.Profiles;
using Aether.ThemeEngine;
using Aether.WidgetEngine;

namespace Aether.Services;

/// <summary>Theme extension steps: widget layout, taskbar/launcher profiles and bundled mod configurations.</summary>
public sealed class WidgetLayoutThemeStep(WidgetManager widgets) : IThemeApplyStep
{
    public string Name => "Widget layout";
    public Task<OperationResult> ApplyAsync(ThemePack theme, CancellationToken ct)
    {
        if (theme.WidgetLayoutId is not { } id) return Task.FromResult(OperationResult.Ok("No layout."));
        if (widgets.GetLayouts().All(l => l.Id != id)) return Task.FromResult(OperationResult.Fail($"Widget layout '{id}' doesn't exist."));
        widgets.ActivateLayout(id);
        return Task.FromResult(OperationResult.Ok("Widget layout activated."));
    }
}

public sealed class ShellProfileThemeStep(ShellProfileService shell) : IThemeApplyStep
{
    public string Name => "Taskbar and launcher profiles";
    public async Task<OperationResult> ApplyAsync(ThemePack theme, CancellationToken ct)
    {
        if (theme.StartProfileId is { } sid && shell.GetStartProfiles().Any(p => p.Id == sid)) shell.ActivateStartProfile(sid);
        return theme.TaskbarProfileId is { } tid ? await shell.ApplyTaskbarProfileAsync(tid, ct).ConfigureAwait(false) : OperationResult.Ok("No taskbar profile.");
    }
}

public sealed class ModConfigurationThemeStep(ModManager mods) : IThemeApplyStep
{
    public string Name => "Mod configurations";
    public async Task<OperationResult> ApplyAsync(ThemePack theme, CancellationToken ct)
    {
        var problems = new List<string>();
        foreach (var cfg in theme.ModConfigurations)
        {
            if (mods.GetMod(cfg.ModId) is not { } mod) { problems.Add($"{cfg.ModId} is not installed."); continue; }
            var r = await mods.UpdateSettingsAsync(cfg.ModId, cfg.Settings, ct).ConfigureAwait(false);
            if (r.Succeeded && cfg.Enabled != mod.IsEnabled)
                r = cfg.Enabled ? await mods.EnableAsync(cfg.ModId, ct).ConfigureAwait(false) : await mods.DisableAsync(cfg.ModId, ct).ConfigureAwait(false);
            if (!r.Succeeded) problems.Add(r.Status == ModActionStatus.NeedsConsent ? $"{mod.Manifest.Name} needs your permission first." : $"{mod.Manifest.Name}: {r.Message}");
        }
        return problems.Count == 0 ? OperationResult.Ok("Mods configured.") : OperationResult.Fail("Some mods were not configured.", problems);
    }
}
