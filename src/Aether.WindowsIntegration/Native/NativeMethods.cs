using System.Runtime.InteropServices;
using System.Text;

namespace Aether.WindowsIntegration.Native;

/// <summary>
/// Win32 declarations used by AETHER. Every API here is documented by Microsoft except
/// SetWindowCompositionAttribute (used only by the Taskbar Transparency mod, clearly labelled in its manifest).
/// </summary>
internal static class NativeMethods
{
    public const int SPI_GETANIMATION = 0x0048, SPI_SETANIMATION = 0x0049;
    public const int SPI_GETCLIENTAREAANIMATION = 0x1042, SPI_SETCLIENTAREAANIMATION = 0x1043;
    public const int SPI_GETMENUANIMATION = 0x1002, SPI_SETMENUANIMATION = 0x1003;
    public const int SPI_GETCOMBOBOXANIMATION = 0x1004, SPI_SETCOMBOBOXANIMATION = 0x1005;
    public const int SPI_GETTOOLTIPANIMATION = 0x1016, SPI_SETTOOLTIPANIMATION = 0x1017;
    public const int SPI_GETDRAGFULLWINDOWS = 0x0026, SPI_SETDRAGFULLWINDOWS = 0x0025;
    public const int SPI_GETFONTSMOOTHING = 0x004A, SPI_SETFONTSMOOTHING = 0x004B;
    public const int SPI_GETDROPSHADOW = 0x1024, SPI_SETDROPSHADOW = 0x1025;
    public const int SPI_GETCURSORSHADOW = 0x101A, SPI_SETCURSORSHADOW = 0x101B;
    public const int SPI_GETSNAPTODEFBUTTON = 0x005F, SPI_SETSNAPTODEFBUTTON = 0x0060;
    public const int SPI_GETMOUSE = 0x0003, SPI_SETMOUSE = 0x0004;
    public const int SPI_GETWINARRANGING = 0x0082, SPI_SETWINARRANGING = 0x0083;
    public const int SPI_SETCURSORS = 0x0057;
    public const int SPI_SETDESKWALLPAPER = 0x0014;
    public const int SPIF_UPDATEINIFILE = 0x01, SPIF_SENDCHANGE = 0x02;

    public const int HWND_BROADCAST = 0xFFFF;
    public const int WM_SETTINGCHANGE = 0x001A;
    public const int WM_COMMAND = 0x0111;
    public const uint SMTO_ABORTIFHUNG = 0x0002;

    public const int SHCNE_ASSOCCHANGED = 0x08000000;
    public const int SHCNF_IDLIST = 0x0000;

    public const int GWL_STYLE = -16, GWL_EXSTYLE = -20;
    public const long WS_VISIBLE = 0x10000000, WS_MINIMIZE = 0x20000000;
    public const long WS_EX_TOOLWINDOW = 0x00000080, WS_EX_TOPMOST = 0x00000008, WS_EX_LAYERED = 0x00080000, WS_EX_NOACTIVATE = 0x08000000, WS_EX_APPWINDOW = 0x00040000;
    public const int LWA_ALPHA = 0x2;
    public const int SW_MINIMIZE = 6, SW_RESTORE = 9, SW_SHOWMINNOACTIVE = 7;
    public const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010, SWP_FRAMECHANGED = 0x0020;
    public static readonly nint HWND_TOPMOST = -1, HWND_NOTOPMOST = -2;
    public const uint GW_OWNER = 4;
    public const int DWMWA_CLOAKED = 14;

    public const uint ABM_GETSTATE = 0x4, ABM_SETSTATE = 0xA;
    public const int ABS_AUTOHIDE = 0x1, ABS_ALWAYSONTOP = 0x2;

    public const uint EVENT_OBJECT_SHOW = 0x8002;
    public const int OBJID_WINDOW = 0;
    public const uint WINEVENT_OUTOFCONTEXT = 0x0000, WINEVENT_SKIPOWNPROCESS = 0x0002;

    public const uint INPUT_KEYBOARD = 1, KEYEVENTF_KEYUP = 0x0002;
    public const ushort VK_LWIN = 0x5B, VK_V = 0x56;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SystemParametersInfo(int uiAction, int uiParam, ref int pvParam, int fWinIni);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SystemParametersInfo(int uiAction, int uiParam, nint pvParam, int fWinIni);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SystemParametersInfo(int uiAction, int uiParam, int[] pvParam, int fWinIni);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SystemParametersInfo(int uiAction, int uiParam, ref ANIMATIONINFO pvParam, int fWinIni);

    [StructLayout(LayoutKind.Sequential)]
    public struct ANIMATIONINFO { public int cbSize; public int iMinAnimate; }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern nint SendMessageTimeout(nint hWnd, int msg, nint wParam, string? lParam, uint flags, uint timeout, out nint result);

    [DllImport("user32.dll")]
    public static extern nint SendMessage(nint hWnd, int msg, nint wParam, nint lParam);

    [DllImport("shell32.dll")]
    public static extern void SHChangeNotify(int wEventId, int uFlags, nint dwItem1, nint dwItem2);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern nint FindWindow(string? className, string? windowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern nint FindWindowEx(nint parent, nint childAfter, string? className, string? windowName);

    public delegate bool EnumWindowsProc(nint hWnd, nint lParam);

    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc callback, nint lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowText(nint hWnd, StringBuilder text, int max);

    [DllImport("user32.dll")]
    public static extern int GetWindowTextLength(nint hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetClassName(nint hWnd, StringBuilder name, int max);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(nint hWnd);

    [DllImport("user32.dll")]
    public static extern bool IsIconic(nint hWnd);

    [DllImport("user32.dll")]
    public static extern nint GetWindow(nint hWnd, uint cmd);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    public static extern nint GetWindowLongPtr(nint hWnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    public static extern nint SetWindowLongPtr(nint hWnd, int index, nint value);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(nint hWnd, out int processId);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(nint hWnd, out RECT rect);

    [DllImport("user32.dll")]
    public static extern bool SetWindowPos(nint hWnd, nint after, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(nint hWnd, int cmd);

    [DllImport("user32.dll")]
    public static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern bool SetLayeredWindowAttributes(nint hWnd, uint key, byte alpha, uint flags);

    [DllImport("user32.dll")]
    public static extern bool GetLayeredWindowAttributes(nint hWnd, out uint key, out byte alpha, out uint flags);

    [DllImport("dwmapi.dll")]
    public static extern int DwmGetWindowAttribute(nint hWnd, int attr, out int value, int size);

    public delegate bool MonitorEnumProc(nint hMonitor, nint hdc, ref RECT rect, nint data);

    [DllImport("user32.dll")]
    public static extern bool EnumDisplayMonitors(nint hdc, nint clip, MonitorEnumProc callback, nint data);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool GetMonitorInfo(nint hMonitor, ref MONITORINFOEX info);

    [StructLayout(LayoutKind.Sequential)]
    public struct APPBARDATA
    {
        public int cbSize;
        public nint hWnd;
        public uint uCallbackMessage;
        public uint uEdge;
        public RECT rc;
        public nint lParam;
    }

    [DllImport("shell32.dll")]
    public static extern nint SHAppBarMessage(uint msg, ref APPBARDATA data);

    public delegate void WinEventDelegate(nint hook, uint eventType, nint hwnd, int idObject, int idChild, uint thread, uint time);

    [DllImport("user32.dll")]
    public static extern nint SetWinEventHook(uint min, uint max, nint module, WinEventDelegate callback, uint process, uint thread, uint flags);

    [DllImport("user32.dll")]
    public static extern bool UnhookWinEvent(nint hook);

    [StructLayout(LayoutKind.Sequential)]
    public struct MSG { public nint hwnd; public uint message; public nint wParam; public nint lParam; public uint time; public int x, y; }

    [DllImport("user32.dll")]
    public static extern int GetMessage(out MSG msg, nint hWnd, uint min, uint max);

    [DllImport("user32.dll")]
    public static extern bool TranslateMessage(ref MSG msg);

    [DllImport("user32.dll")]
    public static extern nint DispatchMessage(ref MSG msg);

    [DllImport("user32.dll")]
    public static extern bool PostThreadMessage(uint thread, uint msg, nint wParam, nint lParam);

    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();

    [StructLayout(LayoutKind.Sequential)]
    public struct KEYBDINPUT { public ushort wVk; public ushort wScan; public uint dwFlags; public uint time; public nint dwExtraInfo; }

    [StructLayout(LayoutKind.Explicit)]
    public struct INPUTUNION { [FieldOffset(0)] public KEYBDINPUT ki; [FieldOffset(0)] public MOUSEINPUTPAD pad; }

    [StructLayout(LayoutKind.Sequential)]
    public struct MOUSEINPUTPAD { public int dx, dy; public uint mouseData, dwFlags, time; public nint dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    public struct INPUT { public uint type; public INPUTUNION u; }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint SendInput(uint count, INPUT[] inputs, int size);

    // ── Composition (undocumented; Taskbar Transparency mod only) ──
    public enum AccentState { Disabled = 0, Gradient = 1, TransparentGradient = 2, BlurBehind = 3, AcrylicBlurBehind = 4, InvalidState = 5 }

    [StructLayout(LayoutKind.Sequential)]
    public struct AccentPolicy { public AccentState AccentState; public int AccentFlags; public int GradientColor; public int AnimationId; }

    [StructLayout(LayoutKind.Sequential)]
    public struct WindowCompositionAttributeData { public int Attribute; public nint Data; public int SizeOfData; }

    public const int WCA_ACCENT_POLICY = 19;

    [DllImport("user32.dll")]
    public static extern int SetWindowCompositionAttribute(nint hwnd, ref WindowCompositionAttributeData data);

    // ── System information ──
    [StructLayout(LayoutKind.Sequential)]
    public struct MEMORYSTATUSEX
    {
        public uint dwLength, dwMemoryLoad;
        public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX status);

    [StructLayout(LayoutKind.Sequential)]
    public struct FILETIME { public uint Low, High; public ulong Value => (ulong)High << 32 | Low; }

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool GetSystemTimes(out FILETIME idle, out FILETIME kernel, out FILETIME user);

    [StructLayout(LayoutKind.Sequential)]
    public struct SYSTEM_POWER_STATUS { public byte ACLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag; public int BatteryLifeTime, BatteryFullLifeTime; }

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);

    // ── PDH (GPU counters) ──
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    public static extern int PdhOpenQuery(string? dataSource, nint userData, out nint query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    public static extern int PdhAddEnglishCounter(nint query, string path, nint userData, out nint counter);

    [DllImport("pdh.dll")]
    public static extern int PdhCollectQueryData(nint query);

    [DllImport("pdh.dll")]
    public static extern int PdhCloseQuery(nint query);

    [StructLayout(LayoutKind.Sequential)]
    public struct PDH_FMT_COUNTERVALUE { public uint CStatus; public double doubleValue; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct PDH_FMT_COUNTERVALUE_ITEM { public nint szName; public PDH_FMT_COUNTERVALUE FmtValue; }

    public const uint PDH_FMT_DOUBLE = 0x00000200;
    public const int PDH_MORE_DATA = unchecked((int)0x800007D2);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    public static extern int PdhGetFormattedCounterArray(nint counter, uint format, ref int bufferSize, out int itemCount, nint buffer);
}
