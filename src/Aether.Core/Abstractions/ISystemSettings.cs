namespace Aether.Core.Abstractions;

/// <summary>Named, documented SystemParametersInfo flags that AETHER can toggle.</summary>
public enum SystemFlag
{
    /// <summary>SPI_SETCLIENTAREAANIMATION — animations inside windows and controls.</summary>
    ClientAreaAnimation,
    /// <summary>SPI_SETANIMATION — minimize/maximize window animation.</summary>
    MinimizeMaximizeAnimation,
    /// <summary>SPI_SETMENUANIMATION — menu fade/slide.</summary>
    MenuAnimation,
    /// <summary>SPI_SETCOMBOBOXANIMATION — combo box slide-open.</summary>
    ComboBoxAnimation,
    /// <summary>SPI_SETTOOLTIPANIMATION.</summary>
    TooltipAnimation,
    /// <summary>SPI_SETDRAGFULLWINDOWS — show window contents while dragging.</summary>
    DragFullWindows,
    /// <summary>SPI_SETFONTSMOOTHING.</summary>
    FontSmoothing,
    /// <summary>SPI_SETDROPSHADOW — shadows under windows.</summary>
    WindowShadows,
    /// <summary>SPI_SETCURSORSHADOW.</summary>
    CursorShadow,
    /// <summary>SPI_SETSNAPTODEFBUTTON — move pointer to default dialog button.</summary>
    SnapToDefaultButton,
    /// <summary>SPI_SETMOUSE acceleration — "Enhance pointer precision".</summary>
    EnhancePointerPrecision,
    /// <summary>SPI_SETWINARRANGING — snap windows by dragging them to screen edges.</summary>
    SnapWindows,
}

/// <summary>Documented Windows setting refresh and broadcast hooks.</summary>
public interface ISystemSettings
{
    bool? GetFlag(SystemFlag flag);
    void SetFlag(SystemFlag flag, bool value);

    /// <summary>Broadcasts WM_SETTINGCHANGE with the given area (e.g. "ImmersiveColorSet", "intl", "TraySettings").</summary>
    void BroadcastSettingChange(string? area);

    /// <summary>Asks the shell to refresh icons/associations (SHChangeNotify SHCNE_ASSOCCHANGED).</summary>
    void RefreshShell();

    /// <summary>Re-reads the cursor scheme from HKCU\Control Panel\Cursors (SPI_SETCURSORS).</summary>
    void ReloadCursors();
}

public interface IShellController
{
    bool IsExplorerRunning { get; }
    /// <param name="reopenFolderWindows">Re-open the File Explorer folder windows that were open before the restart.</param>
    Task RestartExplorerAsync(bool reopenFolderWindows = false, CancellationToken cancellationToken = default);
}

public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError)
{
    public bool Succeeded => ExitCode == 0;
}

public interface IProcessRunner
{
    /// <summary>Runs a process to completion, capturing output. Never uses the shell.</summary>
    Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken = default);

    /// <summary>Starts an application without waiting (used by launchers).</summary>
    void Launch(string target, string? arguments = null, string? workingDirectory = null, bool elevated = false);
}

public interface IPowerShellRunner
{
    bool IsAvailable { get; }
    Task<ProcessResult> RunScriptAsync(string script, TimeSpan timeout, CancellationToken cancellationToken = default);
}
