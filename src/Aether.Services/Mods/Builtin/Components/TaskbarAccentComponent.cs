using Aether.Core.Abstractions;
using Aether.Core.Configuration;
using Aether.ModEngine.Execution;
using Aether.ThemeEngine;

namespace Aether.Services.Mods.Builtin.Components;

/// <summary>Mod 2 — accent color prevalence and custom accent, written through the guarded (baselined) registry.</summary>
public sealed class TaskbarAccentComponent : ModComponentBase
{
    public override Task StartAsync(ModComponentContext context, CancellationToken ct) { Apply(context); return Task.CompletedTask; }
    public override Task OnSettingsChangedAsync(ModComponentContext context, CancellationToken ct) { Apply(context); return Task.CompletedTask; }

    private static void Apply(ModComponentContext context)
    {
        var r = context.Registry;
        var s = context.Settings;
        r.SetValue(RegistryRoot.CurrentUser, WindowsPersonalizationApplier.PersonalizePath, "ColorPrevalence", RegistryData.Dword(s.GetBool("showOnTaskbar") ? 1 : 0));
        r.SetValue(RegistryRoot.CurrentUser, WindowsPersonalizationApplier.DwmPath, "ColorPrevalence", RegistryData.Dword(s.GetBool("showOnTitleBars") ? 1 : 0));
        if (s.GetBool("useCustomColor") && ColorValue.TryParse(s.GetString("color"), out var accent))
        {
            var applier = new WindowsPersonalizationApplier(r, NoBroadcast.Instance);
            applier.ApplyAccent(accent);
        }
    }

    /// <summary>The manifest's broadcast operation handles notification; avoid double broadcasting here.</summary>
    private sealed class NoBroadcast : ISystemSettings
    {
        public static readonly NoBroadcast Instance = new();
        public bool? GetFlag(SystemFlag flag) => null;
        public void SetFlag(SystemFlag flag, bool value) { }
        public void BroadcastSettingChange(string? area) { }
        public void RefreshShell() { }
        public void ReloadCursors() { }
    }
}
