using Aether.Core.Abstractions;
using Aether.Core.Diagnostics;
using Aether.Services.Platform;

namespace Aether.Services.Applications;

public enum StartupSource { UserRun, MachineRun, MachineRun32, UserFolder, CommonFolder }

public sealed record StartupEntry(string Name, string Command, StartupSource Source, bool Enabled, bool RequiresAdmin)
{
    public string Id => $"{Source}:{Name}";
    public string SourceLabel => Source switch
    {
        StartupSource.UserRun => "Registry (you)",
        StartupSource.MachineRun => "Registry (all users)",
        StartupSource.MachineRun32 => "Registry (all users, 32-bit)",
        StartupSource.UserFolder => "Startup folder (you)",
        _ => "Startup folder (all users)",
    };
}

/// <summary>
/// Reads and toggles sign-in programs using the same mechanism as Task Manager: the entries stay in place and
/// the StartupApproved value marks them enabled (0x02) or disabled (0x03 + timestamp).
/// </summary>
public sealed class StartupManager(IRegistryAccess registry, IShortcutService shortcuts, ISystemEnvironment environment, IAuditLog audit)
{
    public const string RunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string Run32Path = @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run";
    public const string ApprovedRun = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    public const string ApprovedRun32 = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run32";
    public const string ApprovedFolder = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder";

    public IReadOnlyList<StartupEntry> GetEntries()
    {
        var list = new List<StartupEntry>();
        void FromRegistry(RegistryRoot root, string path, StartupSource source, string approvedPath)
        {
            if (!registry.KeyExists(root, path)) return;
            foreach (var name in registry.GetValueNames(root, path).Where(n => n.Length > 0))
            {
                var cmd = registry.GetValue(root, path, name)?.Data as string ?? "";
                list.Add(new StartupEntry(name, cmd, source, IsApproved(root, approvedPath, name), root == RegistryRoot.LocalMachine));
            }
        }
        FromRegistry(RegistryRoot.CurrentUser, RunPath, StartupSource.UserRun, ApprovedRun);
        FromRegistry(RegistryRoot.LocalMachine, RunPath, StartupSource.MachineRun, ApprovedRun);
        FromRegistry(RegistryRoot.LocalMachine, Run32Path, StartupSource.MachineRun32, ApprovedRun32);

        void FromFolder(string folder, StartupSource source, RegistryRoot root)
        {
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return;
            foreach (var file in Directory.GetFiles(folder).Where(f => !Path.GetFileName(f).Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)))
            {
                var name = Path.GetFileName(file);
                var target = Path.GetExtension(file).Equals(".lnk", StringComparison.OrdinalIgnoreCase) ? shortcuts.ReadShortcut(file)?.Target ?? file : file;
                list.Add(new StartupEntry(name, target, source, IsApproved(root, ApprovedFolder, name), root == RegistryRoot.LocalMachine));
            }
        }
        FromFolder(shortcuts.StartupFolder, StartupSource.UserFolder, RegistryRoot.CurrentUser);
        FromFolder(shortcuts.CommonStartupFolder, StartupSource.CommonFolder, RegistryRoot.LocalMachine);
        return list.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private bool IsApproved(RegistryRoot root, string approvedPath, string name) =>
        registry.GetValue(root, approvedPath, name)?.Data is not byte[] { Length: > 0 } b || (b[0] & 1) == 0;

    public OperationResult SetEnabled(StartupEntry entry, bool enabled, IRegistryAccess? writer = null)
    {
        if (entry.RequiresAdmin && !environment.IsAdministrator)
            return OperationResult.Fail("Changing programs that start for all users requires administrator rights.");
        var (root, path) = ApprovedLocation(entry.Source);
        var data = new byte[12];
        data[0] = enabled ? (byte)0x02 : (byte)0x03;
        if (!enabled) BitConverter.GetBytes(DateTime.UtcNow.ToFileTimeUtc()).CopyTo(data, 4);
        try
        {
            (writer ?? registry).SetValue(root, path, entry.Name, RegistryData.Bin(data));
            audit.Record(new AuditEntry(DateTimeOffset.UtcNow, "startup", enabled ? "enable" : "disable", entry.Name, true, entry.SourceLabel));
            return OperationResult.Ok($"{entry.Name} will {(enabled ? "" : "no longer ")}start when you sign in.");
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            audit.Record(new AuditEntry(DateTimeOffset.UtcNow, "startup", enabled ? "enable" : "disable", entry.Name, false, ex.Message));
            return OperationResult.Fail($"Could not change {entry.Name}.", ex.Message);
        }
    }

    public OperationResult AddUserEntry(string name, string command)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(command)) return OperationResult.Fail("A name and a command are required.");
        if (registry.GetValue(RegistryRoot.CurrentUser, RunPath, name) is not null) return OperationResult.Fail($"A startup entry named '{name}' already exists.");
        registry.SetValue(RegistryRoot.CurrentUser, RunPath, name, RegistryData.Str(command));
        audit.Record(new AuditEntry(DateTimeOffset.UtcNow, "startup", "add", name, true, command));
        return OperationResult.Ok($"{name} will start when you sign in.");
    }

    public OperationResult RemoveUserEntry(StartupEntry entry)
    {
        if (entry.Source != StartupSource.UserRun) return OperationResult.Fail("Only your own registry entries can be removed here. Disable the others instead.");
        registry.DeleteValue(RegistryRoot.CurrentUser, RunPath, entry.Name);
        registry.DeleteValue(RegistryRoot.CurrentUser, ApprovedRun, entry.Name);
        audit.Record(new AuditEntry(DateTimeOffset.UtcNow, "startup", "remove", entry.Name, true, entry.Command));
        return OperationResult.Ok($"{entry.Name} was removed from startup.");
    }

    private static (RegistryRoot, string) ApprovedLocation(StartupSource source) => source switch
    {
        StartupSource.UserRun => (RegistryRoot.CurrentUser, ApprovedRun),
        StartupSource.MachineRun => (RegistryRoot.LocalMachine, ApprovedRun),
        StartupSource.MachineRun32 => (RegistryRoot.LocalMachine, ApprovedRun32),
        StartupSource.UserFolder => (RegistryRoot.CurrentUser, ApprovedFolder),
        _ => (RegistryRoot.LocalMachine, ApprovedFolder),
    };
}
