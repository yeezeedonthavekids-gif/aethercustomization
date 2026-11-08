using Aether.Core.Abstractions;
using Aether.Core.Configuration;
using Aether.Core.Diagnostics;
using Aether.ModEngine.Execution;
using Aether.ModEngine.Manifest;
using Aether.Services.Platform;
using Aether.Storage;

namespace Aether.Services.Mods.Builtin.Components;

/// <summary>Mod 16 — Start menu / desktop power shortcuts; deletes exactly the files it created.</summary>
public sealed class PowerShortcutsComponent(IDocumentStore store) : ModComponentBase
{
    private sealed record Created(List<string> Files);

    private static readonly (string Key, string Name, string Target, string Args, int Icon)[] Shortcuts =
    [
        ("shutdown", "Shut down", "shutdown.exe", "/s /t 0", 27),
        ("restart", "Restart", "shutdown.exe", "/r /t 0", 238),
        ("sleep", "Sleep", "rundll32.exe", "powrprof.dll,SetSuspendState 0,1,0", 25),
        ("lock", "Lock", "rundll32.exe", "user32.dll,LockWorkStation", 47),
        ("signout", "Sign out", "shutdown.exe", "/l", 44),
    ];

    public override Task StartAsync(ModComponentContext context, CancellationToken ct) { Sync(context); return Task.CompletedTask; }
    public override Task OnSettingsChangedAsync(ModComponentContext context, CancellationToken ct) { Sync(context); return Task.CompletedTask; }

    private void Sync(ModComponentContext context)
    {
        var shell = context.GetService<IShortcutService>(ModPermissions.FileSystemUser);
        var system32 = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var folder = Path.Combine(shell.StartMenuProgramsFolder, "AETHER Power");
        var wanted = new List<(string Path, string Target, string Args, int Icon, string Name)>();
        foreach (var (key, name, target, args, icon) in Shortcuts.Where(s => context.Settings.GetBool(s.Key, true)))
        {
            wanted.Add((Path.Combine(folder, name + ".lnk"), target, args, icon, name));
            if (context.Settings.GetBool("desktop")) wanted.Add((Path.Combine(shell.DesktopFolder, name + ".lnk"), target, args, icon, name));
        }

        var created = store.Get<Created>("power-shortcuts", "created")?.Files ?? [];
        foreach (var stale in created.Except(wanted.Select(w => w.Path), StringComparer.OrdinalIgnoreCase).ToList())
        {
            if (File.Exists(stale)) File.Delete(stale);
            created.Remove(stale);
        }
        foreach (var w in wanted)
        {
            if (File.Exists(w.Path) && created.Contains(w.Path, StringComparer.OrdinalIgnoreCase)) continue;
            if (File.Exists(w.Path)) continue; // never overwrite a file AETHER didn't create
            Directory.CreateDirectory(Path.GetDirectoryName(w.Path)!);
            shell.CreateShortcut(w.Path, Path.Combine(system32, w.Target), w.Args, $"{w.Name} (AETHER)", Path.Combine(system32, "shell32.dll") + "," + w.Icon);
            created.Add(w.Path);
        }
        store.Put("power-shortcuts", "created", new Created(created));
    }

    public override Task StopAsync(ModComponentContext context, CancellationToken ct)
    {
        var created = store.Get<Created>("power-shortcuts", "created")?.Files ?? [];
        foreach (var file in created.Where(File.Exists)) File.Delete(file);
        var shell = context.GetService<IShortcutService>(ModPermissions.FileSystemUser);
        var folder = Path.Combine(shell.StartMenuProgramsFolder, "AETHER Power");
        if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any()) Directory.Delete(folder);
        store.Delete("power-shortcuts", "created");
        return Task.CompletedTask;
    }
}

/// <summary>Mod 19 — copies an installed cursor scheme into HKCU\Control Panel\Cursors (baselined) and reloads cursors.</summary>
public sealed class CursorProfileComponent(ISystemSettings system) : ModComponentBase
{
    public const string SchemesPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Control Panel\Cursors\Schemes";
    public const string UserCursors = @"Control Panel\Cursors";
    public static readonly string[] CursorNames =
        ["Arrow", "Help", "AppStarting", "Wait", "Crosshair", "IBeam", "NWPen", "No", "SizeNS", "SizeWE", "SizeNWSE", "SizeNESW", "SizeAll", "UpArrow", "Hand", "Pin", "Person"];

    public override Task StartAsync(ModComponentContext context, CancellationToken ct) { Apply(context); return Task.CompletedTask; }
    public override Task OnSettingsChangedAsync(ModComponentContext context, CancellationToken ct) { Apply(context); return Task.CompletedTask; }

    private void Apply(ModComponentContext context)
    {
        var name = context.Settings.GetString("scheme", "Windows Default");
        var raw = context.Registry.GetValue(RegistryRoot.LocalMachine, SchemesPath, name)?.Data as string
                  ?? throw new InvalidOperationException($"The cursor scheme '{name}' is not installed on this PC.");
        var files = raw.Split(',');
        for (var i = 0; i < CursorNames.Length; i++)
            context.Registry.SetValue(RegistryRoot.CurrentUser, UserCursors, CursorNames[i],
                new RegistryData(RegistryDataKind.ExpandString, i < files.Length ? files[i] : ""));
        context.Registry.SetValue(RegistryRoot.CurrentUser, UserCursors, "", RegistryData.Str(name));
        context.Registry.SetValue(RegistryRoot.CurrentUser, UserCursors, "Scheme Source", RegistryData.Dword(2));
        if (context.Has(ModPermissions.SystemParameters)) context.SetFlag(SystemFlag.CursorShadow, context.Settings.GetBool("shadow", true));
        system.ReloadCursors();
    }
}

/// <summary>Mod 20 — runs the widget host owned by the UI shell.</summary>
public sealed class WidgetHostComponent : ModComponentBase
{
    private IWidgetHost? _host;
    public override string? Status => _host?.IsRunning == true ? "Widgets visible" : null;

    public override async Task StartAsync(ModComponentContext context, CancellationToken ct)
    {
        _host = context.GetService<IWidgetHost>(ModPermissions.Network);
        if (context.Settings.GetBool("startWithAether", true)) await _host.StartAsync().ConfigureAwait(false);
    }

    public override async Task OnSettingsChangedAsync(ModComponentContext context, CancellationToken ct)
    {
        if (_host is null) return;
        if (context.Settings.GetBool("startWithAether", true) && !_host.IsRunning) await _host.StartAsync().ConfigureAwait(false);
        else if (!context.Settings.GetBool("startWithAether", true) && _host.IsRunning) await _host.StopAsync().ConfigureAwait(false);
    }

    public override async Task StopAsync(ModComponentContext context, CancellationToken ct)
    {
        if (_host is not null) await _host.StopAsync().ConfigureAwait(false);
    }

    public override Task<OperationResult> InvokeActionAsync(ModComponentContext context, string actionId, CancellationToken ct)
    {
        (_host ?? context.GetService<IWidgetHost>(ModPermissions.Network)).OpenEditor();
        return Task.FromResult(OperationResult.Ok("Widget editor opened."));
    }
}
