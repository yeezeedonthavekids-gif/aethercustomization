namespace Aether.ModEngine.Manifest;

public sealed record PermissionInfo(string Id, string Title, string Description, RiskLevel Risk, bool RequiresAdmin = false);

/// <summary>Capabilities a mod or plugin must declare and the user must grant before it runs.</summary>
public static class ModPermissions
{
    public const string RegistryUser = "registry.user";
    public const string RegistryMachine = "registry.machine";
    public const string ShellClasses = "registry.shellClasses";
    public const string StartupManage = "startup.manage";
    public const string SystemParameters = "system.parameters";
    public const string ExplorerRestart = "explorer.restart";
    public const string PowerShell = "powershell.execute";
    public const string ProcessLaunch = "process.launch";
    public const string FileSystemUser = "filesystem.user";
    public const string Wallpaper = "wallpaper.change";
    public const string WindowManage = "window.manage";
    public const string Network = "network.access";
    public const string Power = "power.manage";
    public const string ShellAppearance = "shell.appearance";
    public const string Notifications = "notifications.show";

    public static IReadOnlyDictionary<string, PermissionInfo> All { get; } = new PermissionInfo[]
    {
        new(RegistryUser, "Change your user settings", "Writes values under HKEY_CURRENT_USER. Original values are snapshotted and restored when the mod is disabled.", RiskLevel.Low),
        new(RegistryMachine, "Change machine-wide settings", "Writes values under HKEY_LOCAL_MACHINE, affecting every user. Requires administrator rights.", RiskLevel.High, RequiresAdmin: true),
        new(ShellClasses, "Override shell class registrations", "Writes under HKCU\\Software\\Classes, which can change how Explorer loads components (e.g. the classic context menu).", RiskLevel.High),
        new(StartupManage, "Manage startup applications", "Reads and changes the programs that start when you sign in.", RiskLevel.Medium),
        new(SystemParameters, "Change visual system parameters", "Toggles documented SystemParametersInfo settings such as animations and shadows.", RiskLevel.Low),
        new(ExplorerRestart, "Restart Windows Explorer", "Restarts explorer.exe so shell changes take effect. Open File Explorer windows will close.", RiskLevel.Medium),
        new(PowerShell, "Run PowerShell scripts", "Executes the mod's bundled PowerShell scripts. You will be shown the script and its SHA-256 hash.", RiskLevel.High),
        new(ProcessLaunch, "Launch applications", "Starts programs on your behalf.", RiskLevel.Medium),
        new(FileSystemUser, "Write files in your profile", "Creates or removes files such as shortcuts inside your user profile.", RiskLevel.Medium),
        new(Wallpaper, "Change the desktop wallpaper", "Sets wallpapers through the documented IDesktopWallpaper interface.", RiskLevel.Low),
        new(WindowManage, "Move and resize windows", "Repositions, resizes, minimizes or changes the transparency of other applications' windows.", RiskLevel.Medium),
        new(Network, "Access the internet", "Downloads data from the configured online providers.", RiskLevel.Medium),
        new(Power, "Change power settings", "Switches the active power plan using powercfg.", RiskLevel.Medium),
        new(ShellAppearance, "Restyle shell windows", "Applies composition effects to the taskbar using an undocumented but non-invasive window attribute. No code is injected into Explorer.", RiskLevel.Medium),
        new(Notifications, "Show notifications", "Shows AETHER notifications.", RiskLevel.Low),
    }.ToDictionary(p => p.Id, StringComparer.OrdinalIgnoreCase);

    public static PermissionInfo Describe(string permission)
    {
        var baseId = permission.Split(':')[0];
        return All.TryGetValue(baseId, out var info)
            ? info
            : new PermissionInfo(permission, permission, "Unknown permission. Packages declaring unknown permissions are rejected.", RiskLevel.High);
    }

    public static bool IsKnown(string permission) => All.ContainsKey(permission.Split(':')[0]);
}
