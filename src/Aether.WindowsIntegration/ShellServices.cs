using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Runtime.Versioning;
using System.Text;
using Aether.Core.Configuration;
using Aether.Services.Platform;
using static Aether.WindowsIntegration.Native.NativeMethods;

namespace Aether.WindowsIntegration;

/// <summary>Taskbar composition effects via SetWindowCompositionAttribute from AETHER's process (no injection).</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsTaskbarStyler : ITaskbarStyler
{
    public bool IsSupported => Environment.OSVersion.Version.Build >= 17763;

    private static IEnumerable<nint> TaskbarWindows(bool all)
    {
        var main = FindWindow("Shell_TrayWnd", null);
        if (main != 0) yield return main;
        if (!all) yield break;
        nint secondary = 0;
        while ((secondary = FindWindowEx(0, secondary, "Shell_SecondaryTrayWnd", null)) != 0) yield return secondary;
    }

    public void Apply(TaskbarEffect effect, ColorValue tint, bool allMonitors)
    {
        var state = effect switch
        {
            TaskbarEffect.Clear => AccentState.TransparentGradient,
            TaskbarEffect.Blur => AccentState.BlurBehind,
            TaskbarEffect.Acrylic => AccentState.AcrylicBlurBehind,
            TaskbarEffect.Tint => AccentState.TransparentGradient,
            _ => AccentState.Disabled,
        };
        // Clear uses a fully transparent gradient; tint/acrylic use the chosen ABGR color.
        var color = effect == TaskbarEffect.Clear ? 0 : tint.ToAbgr();
        if (effect == TaskbarEffect.Acrylic && tint.A == 0) color = new ColorValue(1, tint.R, tint.G, tint.B).ToAbgr(); // acrylic needs non-zero alpha
        foreach (var hwnd in TaskbarWindows(allMonitors)) SetAccent(hwnd, state, color);
    }

    public void Reset()
    {
        foreach (var hwnd in TaskbarWindows(true)) SetAccent(hwnd, AccentState.Disabled, 0);
        // Ask Explorer to repaint the taskbar with its own style.
        SendMessageTimeout(HWND_BROADCAST, WM_SETTINGCHANGE, 0, "TraySettings", SMTO_ABORTIFHUNG, 1000, out _);
    }

    private static void SetAccent(nint hwnd, AccentState state, int color)
    {
        var policy = new AccentPolicy { AccentState = state, AccentFlags = state == AccentState.Disabled ? 0 : 2, GradientColor = color };
        var size = Marshal.SizeOf<AccentPolicy>();
        var ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(policy, ptr, false);
            var data = new WindowCompositionAttributeData { Attribute = WCA_ACCENT_POLICY, Data = ptr, SizeOfData = size };
            SetWindowCompositionAttribute(hwnd, ref data);
        }
        finally { Marshal.FreeHGlobal(ptr); }
    }
}

[SupportedOSPlatform("windows")]
public sealed class WindowsDesktopController : IDesktopController
{
    private static nint DesktopListView()
    {
        // The desktop list view lives under Progman, or under a WorkerW after the wallpaper slideshow/peek starts.
        var defView = FindWindowEx(FindWindow("Progman", null), 0, "SHELLDLL_DefView", null);
        if (defView == 0)
        {
            nint worker = 0;
            while ((worker = FindWindowEx(0, worker, "WorkerW", null)) != 0)
            {
                defView = FindWindowEx(worker, 0, "SHELLDLL_DefView", null);
                if (defView != 0) break;
            }
        }
        return defView;
    }

    public bool AreDesktopIconsVisible()
    {
        var defView = DesktopListView();
        var list = defView == 0 ? 0 : FindWindowEx(defView, 0, "SysListView32", null);
        return list != 0 && IsWindowVisible(list);
    }

    public void SetDesktopIconsVisible(bool visible)
    {
        if (AreDesktopIconsVisible() == visible) return;
        var defView = DesktopListView();
        if (defView == 0) throw new InvalidOperationException("The desktop window could not be found. Is Explorer running?");
        SendMessage(defView, WM_COMMAND, 0x7402, 0); // the shell's own "Show desktop icons" toggle command
    }

    public bool IsTaskbarAutoHide()
    {
        var data = new APPBARDATA { cbSize = Marshal.SizeOf<APPBARDATA>() };
        return ((int)SHAppBarMessage(ABM_GETSTATE, ref data) & ABS_AUTOHIDE) != 0;
    }

    public void SetTaskbarAutoHide(bool autoHide)
    {
        var data = new APPBARDATA { cbSize = Marshal.SizeOf<APPBARDATA>(), hWnd = FindWindow("Shell_TrayWnd", null), lParam = autoHide ? ABS_AUTOHIDE : ABS_ALWAYSONTOP };
        SHAppBarMessage(ABM_SETSTATE, ref data);
    }

    public IReadOnlyList<nint> MinimizeOtherWindows()
    {
        var foreground = GetForegroundWindow();
        var own = Environment.ProcessId;
        var minimized = new List<nint>();
        foreach (var w in WindowsWindowManager.EnumerateAppWindows())
        {
            if (w == foreground || IsIconic(w)) continue;
            GetWindowThreadProcessId(w, out var pid);
            if (pid == own) continue;
            ShowWindow(w, SW_SHOWMINNOACTIVE);
            minimized.Add(w);
        }
        return minimized;
    }

    public void RestoreWindows(IReadOnlyList<nint> windows)
    {
        foreach (var w in windows) if (IsIconic(w)) ShowWindow(w, SW_RESTORE);
    }

    public void OpenClipboardHistory()
    {
        INPUT Key(ushort vk, bool up) => new() { type = INPUT_KEYBOARD, u = new INPUTUNION { ki = new KEYBDINPUT { wVk = vk, dwFlags = up ? KEYEVENTF_KEYUP : 0 } } };
        INPUT[] inputs = [Key(VK_LWIN, false), Key(VK_V, false), Key(VK_V, true), Key(VK_LWIN, true)];
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }
}

// ── IShellLinkW / IPersistFile ──
[ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellLinkW
{
    void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int cch, nint findData, uint flags);
    void GetIDList(out nint ppidl);
    void SetIDList(nint pidl);
    void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int cch);
    void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
    void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder dir, int cch);
    void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir);
    void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder args, int cch);
    void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args);
    void GetHotkey(out short hotkey);
    void SetHotkey(short hotkey);
    void GetShowCmd(out int showCmd);
    void SetShowCmd(int showCmd);
    void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder iconPath, int cch, out int icon);
    void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int icon);
    void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pathRel, uint reserved);
    void Resolve(nint hwnd, uint flags);
    void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
}

[ComImport, Guid("00021401-0000-0000-C000-000000000046")]
internal class ShellLinkClass;

[SupportedOSPlatform("windows")]
public sealed class WindowsShortcutService : IShortcutService
{
    public string StartMenuProgramsFolder => Environment.GetFolderPath(Environment.SpecialFolder.Programs);
    public string CommonStartMenuProgramsFolder => Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms);
    public string DesktopFolder => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
    public string StartupFolder => Environment.GetFolderPath(Environment.SpecialFolder.Startup);
    public string CommonStartupFolder => Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup);

    public void CreateShortcut(string path, string target, string? arguments = null, string? description = null, string? iconLocation = null, string? workingDirectory = null)
    {
        var link = (IShellLinkW)new ShellLinkClass();
        try
        {
            link.SetPath(target);
            if (arguments is not null) link.SetArguments(arguments);
            if (description is not null) link.SetDescription(description);
            if (workingDirectory is not null) link.SetWorkingDirectory(workingDirectory);
            if (iconLocation is not null)
            {
                var comma = iconLocation.LastIndexOf(',');
                if (comma > 0 && int.TryParse(iconLocation[(comma + 1)..], out var index)) link.SetIconLocation(iconLocation[..comma], index);
                else link.SetIconLocation(iconLocation, 0);
            }
            ((IPersistFile)link).Save(path, true);
        }
        finally { Marshal.ReleaseComObject(link); }
    }

    public ShortcutInfo? ReadShortcut(string path)
    {
        if (!File.Exists(path)) return null;
        var link = (IShellLinkW)new ShellLinkClass();
        try
        {
            ((IPersistFile)link).Load(path, 0);
            var target = new StringBuilder(1024);
            link.GetPath(target, target.Capacity, 0, 0x4 /* SLGP_RAWPATH */);
            var args = new StringBuilder(2048);
            link.GetArguments(args, args.Capacity);
            var dir = new StringBuilder(1024);
            link.GetWorkingDirectory(dir, dir.Capacity);
            var icon = new StringBuilder(1024);
            link.GetIconLocation(icon, icon.Capacity, out var iconIndex);
            var desc = new StringBuilder(1024);
            try { link.GetDescription(desc, desc.Capacity); } catch (COMException) { }
            return new ShortcutInfo(path, target.ToString(), args.ToString(), dir.ToString(), icon.Length > 0 ? $"{icon},{iconIndex}" : null, desc.ToString());
        }
        catch (COMException) { return null; }
        finally { Marshal.ReleaseComObject(link); }
    }
}
