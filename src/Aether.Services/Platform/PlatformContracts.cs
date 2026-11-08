using Aether.Core.Configuration;

namespace Aether.Services.Platform;

public enum TaskbarEffect { Default, Clear, Blur, Acrylic, Tint }

/// <summary>Applies composition effects to the taskbar windows (SetWindowCompositionAttribute, no injection).</summary>
public interface ITaskbarStyler
{
    bool IsSupported { get; }
    void Apply(TaskbarEffect effect, ColorValue tint, bool allMonitors);
    void Reset();
}

/// <summary>Desktop-level shell operations.</summary>
public interface IDesktopController
{
    bool AreDesktopIconsVisible();
    void SetDesktopIconsVisible(bool visible);
    bool IsTaskbarAutoHide();
    void SetTaskbarAutoHide(bool autoHide);
    /// <summary>Minimizes visible top-level windows except AETHER's and the foreground window; returns their handles.</summary>
    IReadOnlyList<nint> MinimizeOtherWindows();
    void RestoreWindows(IReadOnlyList<nint> windows);
    /// <summary>Opens the Windows clipboard history (sends Win+V).</summary>
    void OpenClipboardHistory();
}

public sealed record ShortcutInfo(string Path, string Target, string? Arguments, string? WorkingDirectory, string? IconLocation, string? Description);

public interface IShortcutService
{
    string StartMenuProgramsFolder { get; }
    string CommonStartMenuProgramsFolder { get; }
    string DesktopFolder { get; }
    string StartupFolder { get; }
    string CommonStartupFolder { get; }
    void CreateShortcut(string path, string target, string? arguments = null, string? description = null, string? iconLocation = null, string? workingDirectory = null);
    ShortcutInfo? ReadShortcut(string path);
}

public sealed record WindowInfo(nint Handle, string Title, string ProcessName, int ProcessId, int Left, int Top, int Width, int Height, bool IsTopmost, bool IsMinimized, byte Opacity);

public sealed record WorkArea(string MonitorId, string Name, int Left, int Top, int Width, int Height, bool IsPrimary);

/// <summary>Window inspection and placement (EnumWindows, SetWindowPos, layered window attributes).</summary>
public interface IWindowManager
{
    IReadOnlyList<WindowInfo> GetWindows();
    IReadOnlyList<WorkArea> GetWorkAreas();
    nint GetForegroundWindow();
    void MoveWindow(nint handle, int left, int top, int width, int height);
    void SetTopmost(nint handle, bool topmost);
    void SetOpacity(nint handle, byte opacity);
    void Minimize(nint handle);
    void Restore(nint handle);
    /// <summary>Raised (on a background thread) when a new top-level window appears.</summary>
    event Action<WindowInfo>? WindowShown;
}

/// <summary>Global hotkeys owned by the UI shell (RegisterHotKey on its message window).</summary>
public interface IHotkeyService
{
    /// <returns>False when the shortcut is taken by another application.</returns>
    bool Register(string id, Hotkey hotkey, Action callback);
    void Unregister(string id);
}

/// <summary>Implemented by the UI shell, which owns widget windows.</summary>
public interface IWidgetHost
{
    bool IsRunning { get; }
    Task StartAsync();
    Task StopAsync();
    void OpenEditor();
}

public sealed record SystemSummary(string OsName, string OsVersion, string Build, string Edition, string Cpu, int LogicalCores, ulong TotalMemoryBytes,
    IReadOnlyList<string> Gpus, TimeSpan Uptime, string MachineName, string UserName, bool IsAdministrator, string Architecture);

public interface ISystemInfoProvider
{
    SystemSummary GetSummary();
}

/// <summary>Creates real Windows System Restore points (requires administrator rights and System Protection).</summary>
public interface IRestorePointService
{
    bool IsSupported { get; }
    Task<Core.Diagnostics.OperationResult> CreateAsync(string description, CancellationToken ct = default);
}

/// <summary>Display topology changes (monitors connected/disconnected).</summary>
public interface IDisplayEvents
{
    event Action? DisplaysChanged;
}

/// <summary>Running-process signals used by automation (low-frequency snapshot polling).</summary>
public interface IProcessMonitor
{
    IReadOnlySet<string> GetRunningProcessNames();
}

public enum ProcessWindow { Normal, Maximized, Minimized }

/// <summary>Opens files, folders and URLs with the shell.</summary>
public interface IShellLauncher
{
    void Open(string target, string? arguments = null, bool elevated = false, string? workingDirectory = null, ProcessWindow window = ProcessWindow.Normal);
    void OpenFolder(string path);
    void RevealInExplorer(string path);
}
