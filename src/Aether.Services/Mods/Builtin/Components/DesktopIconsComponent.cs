using Aether.Core.Abstractions;
using Aether.Core.Configuration;
using Aether.ModEngine.Execution;
using Aether.ModEngine.Manifest;
using Aether.Services.Platform;

namespace Aether.Services.Mods.Builtin.Components;

/// <summary>Mod 8 — per-icon visibility (registry, baselined) and the global "show desktop icons" view state.</summary>
public sealed class DesktopIconsComponent : ModComponentBase
{
    private const string Panel = @"Software\Microsoft\Windows\CurrentVersion\Explorer\HideDesktopIcons\NewStartPanel";
    private static readonly (string Key, string Clsid)[] Icons =
    [
        ("thisPc", "{20D04FE0-3AEA-1069-A2D8-08002B30309D}"),
        ("recycleBin", "{645FF040-5081-101B-9F08-00AA002F954E}"),
        ("userFolder", "{59031a47-3f72-44a7-89c5-5595fe6b30ee}"),
        ("network", "{F02C1A0D-BE21-4350-88B0-7367FC96EF3C}"),
        ("controlPanel", "{5399E694-6CE5-4D6C-8FCE-1D8870FDCBA0}"),
    ];

    private bool? _iconsVisibleBefore;
    private IDesktopController? _desktop;

    public override Task StartAsync(ModComponentContext context, CancellationToken ct) { Apply(context); return Task.CompletedTask; }
    public override Task OnSettingsChangedAsync(ModComponentContext context, CancellationToken ct) { Apply(context); return Task.CompletedTask; }

    private void Apply(ModComponentContext context)
    {
        foreach (var (key, clsid) in Icons)
            context.Registry.SetValue(RegistryRoot.CurrentUser, Panel, clsid, RegistryData.Dword(context.Settings.GetBool(key) ? 0 : 1));

        _desktop ??= TryGetDesktop(context);
        if (_desktop is null) return;
        var hideAll = context.Settings.GetBool("hideAll");
        if (hideAll)
        {
            _iconsVisibleBefore ??= _desktop.AreDesktopIconsVisible();
            if (_desktop.AreDesktopIconsVisible()) _desktop.SetDesktopIconsVisible(false);
        }
        else if (_iconsVisibleBefore is { } before)
        {
            _desktop.SetDesktopIconsVisible(before);
            _iconsVisibleBefore = null;
        }
    }

    private static IDesktopController? TryGetDesktop(ModComponentContext context)
    {
        try { return context.GetService<IDesktopController>(ModPermissions.RegistryUser); }
        catch (InvalidOperationException) { return null; }
    }

    public override Task StopAsync(ModComponentContext context, CancellationToken ct)
    {
        if (_iconsVisibleBefore is { } before && _desktop is not null) _desktop.SetDesktopIconsVisible(before);
        _iconsVisibleBefore = null;
        return Task.CompletedTask;
    }
}
