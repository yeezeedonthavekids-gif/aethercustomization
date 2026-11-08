using Aether.Core.Abstractions;
using Aether.Services.Platform;

namespace Aether.Tests.Fakes;

public sealed class FakeProcessRunner : IProcessRunner
{
    public List<string> Commands { get; } = [];
    public Func<string, IReadOnlyList<string>, ProcessResult> Handler { get; set; } = (_, _) => new ProcessResult(0, "", "");
    public Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        Commands.Add($"{fileName} {string.Join(' ', arguments)}");
        return Task.FromResult(Handler(fileName, arguments));
    }
    public void Launch(string target, string? arguments = null, string? workingDirectory = null, bool elevated = false) => Commands.Add($"launch {target}");
}

public sealed class FakeDesktop : IDesktopController
{
    public bool Icons { get; set; } = true;
    public bool AutoHide { get; set; }
    public bool AreDesktopIconsVisible() => Icons;
    public void SetDesktopIconsVisible(bool visible) => Icons = visible;
    public bool IsTaskbarAutoHide() => AutoHide;
    public void SetTaskbarAutoHide(bool autoHide) => AutoHide = autoHide;
    public IReadOnlyList<nint> MinimizeOtherWindows() => [1, 2];
    public void RestoreWindows(IReadOnlyList<nint> windows) { }
    public void OpenClipboardHistory() { }
}

public sealed class FakeShortcuts(string root) : IShortcutService
{
    public string StartMenuProgramsFolder => Path.Combine(root, "start");
    public string CommonStartMenuProgramsFolder => Path.Combine(root, "common-start");
    public string DesktopFolder => Path.Combine(root, "desktop");
    public string StartupFolder => Path.Combine(root, "startup");
    public string CommonStartupFolder => Path.Combine(root, "common-startup");
    public void CreateShortcut(string path, string target, string? arguments = null, string? description = null, string? iconLocation = null, string? workingDirectory = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, $"{target} {arguments}");
    }
    public ShortcutInfo? ReadShortcut(string path) => File.Exists(path) ? new ShortcutInfo(path, File.ReadAllText(path).Split(' ')[0], null, null, null, null) : null;
}

public sealed class FakeWindowManager : IWindowManager
{
    public List<WindowInfo> Windows { get; } = [];
    public List<(nint, int, int, int, int)> Moves { get; } = [];
    public IReadOnlyList<WindowInfo> GetWindows() => Windows;
    public IReadOnlyList<WorkArea> GetWorkAreas() => [new("m1", "Display 1", 0, 0, 1920, 1040, true)];
    public nint GetForegroundWindow() => Windows.FirstOrDefault()?.Handle ?? 0;
    public void MoveWindow(nint handle, int left, int top, int width, int height) => Moves.Add((handle, left, top, width, height));
    public void SetTopmost(nint handle, bool topmost) { }
    public void SetOpacity(nint handle, byte opacity) { }
    public void Minimize(nint handle) { }
    public void Restore(nint handle) { }
    public event Action<WindowInfo>? WindowShown;
    public void RaiseShown(WindowInfo w) => WindowShown?.Invoke(w);
}

public sealed class FakeLauncher : IShellLauncher
{
    public List<string> Opened { get; } = [];
    public void Open(string target, string? arguments = null, bool elevated = false, string? workingDirectory = null, ProcessWindow window = ProcessWindow.Normal) => Opened.Add(target);
    public void OpenFolder(string path) => Opened.Add(path);
    public void RevealInExplorer(string path) => Opened.Add(path);
}
