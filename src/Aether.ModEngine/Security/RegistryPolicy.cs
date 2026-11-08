using Aether.Core.Abstractions;
using Aether.ModEngine.Manifest;

namespace Aether.ModEngine.Security;

public sealed record PolicyDecision(bool Allowed, string Reason);

/// <summary>
/// Central allow/deny rules for registry writes. Security-critical areas (Defender, UAC, Windows Update,
/// services, LSA, Winlogon, debugger hijacks, environment) are never writable by mods, regardless of permissions.
/// </summary>
public static class RegistryPolicy
{
    private static readonly string[] Forbidden =
    [
        @"SOFTWARE\Microsoft\Windows Defender",
        @"SOFTWARE\Policies\Microsoft\Windows Defender",
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System",
        @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate",
        @"SOFTWARE\Microsoft\WindowsUpdate",
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate",
        @"SYSTEM\CurrentControlSet\Services",
        @"SYSTEM\CurrentControlSet\Control\Lsa",
        @"SYSTEM\CurrentControlSet\Control\SecurityProviders",
        @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon",
        @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options",
        @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Windows",          // AppInit_DLLs
        @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\SilentProcessExit",
        @"SOFTWARE\Policies\Microsoft\Windows\System",
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce",
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\ShellExecuteHooks",
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Browser Helper Objects",
        @"Environment",
        @"SOFTWARE\Microsoft\Command Processor",
    ];

    private static readonly (string Prefix, string Permission)[] Gated =
    [
        (@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", ModPermissions.StartupManage),
        (@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved", ModPermissions.StartupManage),
        (@"SOFTWARE\Classes", ModPermissions.ShellClasses),
    ];

    public static PolicyDecision Evaluate(RegistryRoot root, string path, IEnumerable<string> grantedPermissions)
    {
        var granted = new HashSet<string>(grantedPermissions, StringComparer.OrdinalIgnoreCase);
        var normalized = Normalize(path);

        foreach (var f in Forbidden)
            if (IsUnder(normalized, f))
                return new PolicyDecision(false, $@"'{path}' is a protected security area and can never be modified by mods.");

        var hivePermission = root == RegistryRoot.CurrentUser ? ModPermissions.RegistryUser : ModPermissions.RegistryMachine;
        if (!granted.Contains(hivePermission))
            return new PolicyDecision(false, $"Writing to {(root == RegistryRoot.CurrentUser ? "HKCU" : "HKLM")} requires the '{hivePermission}' permission.");

        foreach (var (prefix, permission) in Gated)
            if (IsUnder(normalized, prefix) && !granted.Contains(permission))
                return new PolicyDecision(false, $"'{path}' additionally requires the '{permission}' permission.");

        return new PolicyDecision(true, "Allowed.");
    }

    private static string Normalize(string path) => path.Replace('/', '\\').Trim('\\');

    private static bool IsUnder(string path, string prefix) =>
        path.Equals(prefix, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(prefix + "\\", StringComparison.OrdinalIgnoreCase);
}
