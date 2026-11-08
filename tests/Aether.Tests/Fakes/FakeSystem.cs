using Aether.Core.Abstractions;

namespace Aether.Tests.Fakes;

public sealed class FakeEnvironment : ISystemEnvironment
{
    public WindowsVersionInfo Version { get; set; } = new("Windows 11 Pro", "24H2", 10, 0, 26100, 2314, "Professional", "x64");
    public bool IsWindows { get; set; } = true;
    public bool IsAdministrator { get; set; }
    public string MachineName => "TEST-PC";
    public string UserName => "tester";
}

public sealed class FakeSystemSettings : ISystemSettings
{
    public Dictionary<SystemFlag, bool> Flags { get; } = new() { [SystemFlag.ClientAreaAnimation] = true, [SystemFlag.MenuAnimation] = true };
    public List<string?> Broadcasts { get; } = [];
    public int ShellRefreshes { get; private set; }
    public int CursorReloads { get; private set; }

    public bool? GetFlag(SystemFlag flag) => Flags.TryGetValue(flag, out var v) ? v : null;
    public void SetFlag(SystemFlag flag, bool value) => Flags[flag] = value;
    public void BroadcastSettingChange(string? area) => Broadcasts.Add(area);
    public void RefreshShell() => ShellRefreshes++;
    public void ReloadCursors() => CursorReloads++;
}

public sealed class FakeShell : IShellController
{
    public int Restarts { get; private set; }
    public bool IsExplorerRunning => true;
    public Task RestartExplorerAsync(CancellationToken cancellationToken = default) { Restarts++; return Task.CompletedTask; }
}

public sealed class FakePowerShell : IPowerShellRunner
{
    public List<string> Scripts { get; } = [];
    public int ExitCode { get; set; }
    public bool IsAvailable => true;
    public Task<ProcessResult> RunScriptAsync(string script, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        Scripts.Add(script);
        return Task.FromResult(new ProcessResult(ExitCode, "", ExitCode == 0 ? "" : "boom"));
    }
}

public sealed class TempDirectory : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "aether-tests", Guid.NewGuid().ToString("N"));
    public TempDirectory() => Directory.CreateDirectory(Path);
    public void Dispose() { try { Directory.Delete(Path, true); } catch { } }
}
