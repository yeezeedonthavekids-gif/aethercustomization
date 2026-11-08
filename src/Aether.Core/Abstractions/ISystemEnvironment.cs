namespace Aether.Core.Abstractions;

public sealed record WindowsVersionInfo(
    string ProductName,
    string DisplayVersion,
    int Major,
    int Minor,
    int Build,
    int Revision,
    string Edition,
    string Architecture)
{
    public bool IsWindows11 => Build >= 22000;
    public string FriendlyName => IsWindows11 ? "Windows 11" : "Windows 10";
    public string BuildString => Revision > 0 ? $"{Build}.{Revision}" : Build.ToString();

    /// <summary>Compares the full build number (build + update build revision).</summary>
    public bool IsAtLeast(int build, int revision = 0) => Build > build || (Build == build && Revision >= revision);

    public static WindowsVersionInfo Unknown { get; } = new("Unknown OS", "", 0, 0, 0, 0, "", "unknown");
}

public interface ISystemEnvironment
{
    WindowsVersionInfo Version { get; }
    bool IsWindows { get; }
    bool IsAdministrator { get; }
    string MachineName { get; }
    string UserName { get; }
}
