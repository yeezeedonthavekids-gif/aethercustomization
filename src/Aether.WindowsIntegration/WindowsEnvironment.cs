using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using Aether.Core.Abstractions;
using Microsoft.Win32;

namespace Aether.WindowsIntegration;

[SupportedOSPlatform("windows")]
public sealed class WindowsEnvironment : ISystemEnvironment
{
    private readonly Lazy<WindowsVersionInfo> _version = new(Detect);
    private readonly Lazy<bool> _admin = new(() =>
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    });

    public WindowsVersionInfo Version => _version.Value;
    public bool IsWindows => true;
    public bool IsAdministrator => _admin.Value;
    public string MachineName => Environment.MachineName;
    public string UserName => Environment.UserName;

    private static WindowsVersionInfo Detect()
    {
        using var key = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
            .OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
        var os = Environment.OSVersion.Version;
        var build = int.TryParse(key?.GetValue("CurrentBuild") as string, out var b) ? b : os.Build;
        var ubr = key?.GetValue("UBR") is int u ? u : 0;
        var product = key?.GetValue("ProductName") as string ?? "Windows";
        // ProductName still says "Windows 10" on Windows 11; correct it from the build number.
        if (build >= 22000) product = product.Replace("Windows 10", "Windows 11");
        return new WindowsVersionInfo(product, key?.GetValue("DisplayVersion") as string ?? key?.GetValue("ReleaseId") as string ?? "",
            os.Major, os.Minor, build, ubr, key?.GetValue("EditionID") as string ?? "", RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant());
    }
}
