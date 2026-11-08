using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;
using Aether.Core.Abstractions;
using Aether.Services.Platform;
using Microsoft.Extensions.Logging;

namespace Aether.WindowsIntegration;

/// <summary>Runs processes without a shell, capturing output, with a hard timeout.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsProcessRunner : IProcessRunner
{
    public async Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var psi = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in arguments) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi) ?? throw new InvalidOperationException($"Could not start {fileName}.");
        var stdout = p.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = p.StandardError.ReadToEndAsync(cancellationToken);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);
        try { await p.WaitForExitAsync(cts.Token).ConfigureAwait(false); }
        catch (OperationCanceledException)
        {
            try { p.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            if (cancellationToken.IsCancellationRequested) throw;
            return new ProcessResult(-1, await stdout.ConfigureAwait(false), $"Timed out after {timeout.TotalSeconds:0}s.");
        }
        return new ProcessResult(p.ExitCode, await stdout.ConfigureAwait(false), await stderr.ConfigureAwait(false));
    }

    public void Launch(string target, string? arguments = null, string? workingDirectory = null, bool elevated = false)
    {
        var psi = new ProcessStartInfo(target) { UseShellExecute = true, Arguments = arguments ?? "", WorkingDirectory = workingDirectory ?? "" };
        if (elevated) psi.Verb = "runas";
        Process.Start(psi)?.Dispose();
    }
}

/// <summary>
/// Windows PowerShell 5.1 (always present). Scripts are passed with -EncodedCommand so no quoting or
/// injection issues can arise; profiles are not loaded.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsPowerShellRunner(IProcessRunner runner) : IPowerShellRunner
{
    private static readonly string Exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe");

    public bool IsAvailable => File.Exists(Exe);

    public Task<ProcessResult> RunScriptAsync(string script, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes("[Console]::OutputEncoding = [Text.Encoding]::UTF8\n" + script));
        return runner.RunAsync(Exe, ["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-EncodedCommand", encoded], timeout, cancellationToken);
    }
}

[SupportedOSPlatform("windows")]
public sealed class WindowsShellLauncher : IShellLauncher
{
    public void Open(string target, string? arguments = null, bool elevated = false, string? workingDirectory = null, ProcessWindow window = ProcessWindow.Normal)
    {
        var expanded = Environment.ExpandEnvironmentVariables(target);
        var psi = expanded.StartsWith("shell:AppsFolder", StringComparison.OrdinalIgnoreCase)
            ? new ProcessStartInfo("explorer.exe", expanded) { UseShellExecute = true }
            : new ProcessStartInfo(expanded) { UseShellExecute = true, Arguments = arguments ?? "" };
        if (!string.IsNullOrWhiteSpace(workingDirectory)) psi.WorkingDirectory = workingDirectory;
        if (elevated) psi.Verb = "runas";
        psi.WindowStyle = window switch { ProcessWindow.Maximized => ProcessWindowStyle.Maximized, ProcessWindow.Minimized => ProcessWindowStyle.Minimized, _ => ProcessWindowStyle.Normal };
        Process.Start(psi)?.Dispose();
    }

    public void OpenFolder(string path) => Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { path }, UseShellExecute = true })?.Dispose();

    public void RevealInExplorer(string path) => Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true })?.Dispose();
}

[SupportedOSPlatform("windows")]
public sealed class WindowsProcessMonitor : IProcessMonitor
{
    public IReadOnlySet<string> GetRunningProcessNames()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in Process.GetProcesses())
        {
            try { set.Add(p.ProcessName.ToLowerInvariant()); }
            catch (InvalidOperationException) { }
            finally { p.Dispose(); }
        }
        return set;
    }
}

/// <summary>Restarts explorer.exe for the current session and optionally re-opens folder windows.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsShellController(ILogger<WindowsShellController> logger) : IShellController
{
    public bool IsExplorerRunning => Process.GetProcessesByName("explorer").Any(p => { using (p) return p.SessionId == Process.GetCurrentProcess().SessionId; });

    public async Task RestartExplorerAsync(bool reopenFolderWindows = false, CancellationToken cancellationToken = default)
    {
        var folders = reopenFolderWindows ? OpenFolderPaths() : [];
        var session = Process.GetCurrentProcess().SessionId;
        foreach (var p in Process.GetProcessesByName("explorer"))
        {
            using (p)
            {
                if (p.SessionId != session) continue;
                try { p.Kill(); await p.WaitForExitAsync(cancellationToken).WaitAsync(TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false); }
                catch (Exception ex) when (ex is InvalidOperationException or TimeoutException or System.ComponentModel.Win32Exception) { logger.LogWarning(ex, "Explorer did not exit cleanly"); }
            }
        }
        // Windows usually restarts the shell automatically (AutoRestartShell); start it if it didn't.
        await Task.Delay(1500, cancellationToken).ConfigureAwait(false);
        if (!IsExplorerRunning) Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe")) { UseShellExecute = true })?.Dispose();
        if (folders.Count > 0)
        {
            await Task.Delay(2500, cancellationToken).ConfigureAwait(false);
            foreach (var f in folders.Distinct(StringComparer.OrdinalIgnoreCase).Take(12))
                Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { f }, UseShellExecute = true })?.Dispose();
        }
    }

    /// <summary>Uses the documented Shell.Application ShellWindows automation object to list open folders.</summary>
    private List<string> OpenFolderPaths()
    {
        var result = new List<string>();
        try
        {
            var type = Type.GetTypeFromProgID("Shell.Application");
            if (type is null) return result;
            dynamic shell = Activator.CreateInstance(type)!;
            foreach (dynamic window in shell.Windows())
            {
                try
                {
                    string url = window.LocationURL;
                    if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.IsFile) result.Add(uri.LocalPath);
                }
                catch (Exception) { /* not a folder window */ }
            }
        }
        catch (Exception ex) { logger.LogInformation(ex, "Could not enumerate folder windows"); }
        return result;
    }
}
