using System.Runtime.InteropServices;
using Aether.Core.Configuration;
using Aether.Services.Platform;

namespace Aether.App.Services;

/// <summary>
/// A hidden message-only window on the UI thread that owns AETHER's global hotkeys (RegisterHotKey) and
/// the notification-area icon (Shell_NotifyIcon).
/// </summary>
public sealed class MessageWindow : IHotkeyService, IDisposable
{
    private const int WM_HOTKEY = 0x0312, WM_APP_TRAY = 0x8000 + 1, WM_LBUTTONUP = 0x0202, WM_RBUTTONUP = 0x0205, WM_COMMAND = 0x0111;
    private const uint NIM_ADD = 0, NIM_DELETE = 2, NIF_MESSAGE = 1, NIF_ICON = 2, NIF_TIP = 4;
    private const uint MOD_NOREPEAT = 0x4000, TPM_RIGHTBUTTON = 0x2, TPM_RETURNCMD = 0x100, MF_STRING = 0, MF_SEPARATOR = 0x800;

    private readonly WndProc _proc;
    private readonly Dictionary<int, (string Id, Action Callback)> _hotkeys = new();
    private readonly nint _hwnd;
    private int _nextId = 1;
    private bool _trayVisible;

    public MessageWindow()
    {
        _proc = Handle;
        var cls = new WNDCLASSEX { cbSize = Marshal.SizeOf<WNDCLASSEX>(), lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_proc), hInstance = GetModuleHandle(null), lpszClassName = "AetherMessageWindow" };
        RegisterClassEx(ref cls);
        _hwnd = CreateWindowEx(0, "AetherMessageWindow", "AETHER", 0, 0, 0, 0, 0, new nint(-3) /* HWND_MESSAGE */, 0, cls.hInstance, 0);
    }

    public event Action? TrayOpenRequested;
    public event Action? TrayExitRequested;
    public event Action? TrayPaletteRequested;

    public bool Register(string id, Hotkey hotkey, Action callback)
    {
        Unregister(id);
        var mods = MOD_NOREPEAT;
        if (hotkey.Modifiers.HasFlag(HotkeyModifiers.Alt)) mods |= 1;
        if (hotkey.Modifiers.HasFlag(HotkeyModifiers.Control)) mods |= 2;
        if (hotkey.Modifiers.HasFlag(HotkeyModifiers.Shift)) mods |= 4;
        if (hotkey.Modifiers.HasFlag(HotkeyModifiers.Win)) mods |= 8;
        var n = _nextId++;
        if (!RegisterHotKey(_hwnd, n, mods, (uint)hotkey.VirtualKey)) return false;
        _hotkeys[n] = (id, callback);
        return true;
    }

    public void Unregister(string id)
    {
        foreach (var (n, entry) in _hotkeys.Where(h => h.Value.Id == id).ToList())
        {
            UnregisterHotKey(_hwnd, n);
            _hotkeys.Remove(n);
        }
    }

    public void ShowTrayIcon(string iconPath)
    {
        if (_trayVisible) return;
        var data = TrayData();
        data.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP;
        data.hIcon = File.Exists(iconPath) ? LoadImage(0, iconPath, 1, 0, 0, 0x10) : LoadIcon(0, new nint(32512));
        data.szTip = "AETHER";
        _trayVisible = Shell_NotifyIcon(NIM_ADD, ref data);
    }

    public void HideTrayIcon()
    {
        if (!_trayVisible) return;
        var data = TrayData();
        Shell_NotifyIcon(NIM_DELETE, ref data);
        _trayVisible = false;
    }

    private NOTIFYICONDATA TrayData() => new() { cbSize = Marshal.SizeOf<NOTIFYICONDATA>(), hWnd = _hwnd, uID = 1, uCallbackMessage = WM_APP_TRAY, szTip = "", szInfo = "", szInfoTitle = "" };

    private nint Handle(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        if (msg == WM_HOTKEY && _hotkeys.TryGetValue((int)wParam, out var hk))
        {
            try { hk.Callback(); } catch (Exception) { /* callbacks report their own errors */ }
            return 0;
        }
        if (msg == WM_APP_TRAY)
        {
            var evt = (int)(lParam & 0xFFFF);
            if (evt == WM_LBUTTONUP) TrayOpenRequested?.Invoke();
            else if (evt == WM_RBUTTONUP) ShowTrayMenu();
            return 0;
        }
        return DefWindowProc(hwnd, msg, wParam, lParam);
    }

    private void ShowTrayMenu()
    {
        var menu = CreatePopupMenu();
        AppendMenu(menu, MF_STRING, 1, "Open AETHER");
        AppendMenu(menu, MF_STRING, 2, "Command palette");
        AppendMenu(menu, MF_SEPARATOR, 0, null);
        AppendMenu(menu, MF_STRING, 3, "Exit");
        GetCursorPos(out var pt);
        SetForegroundWindow(_hwnd);
        var cmd = TrackPopupMenu(menu, TPM_RIGHTBUTTON | TPM_RETURNCMD, pt.X, pt.Y, 0, _hwnd, 0);
        DestroyMenu(menu);
        switch (cmd)
        {
            case 1: TrayOpenRequested?.Invoke(); break;
            case 2: TrayPaletteRequested?.Invoke(); break;
            case 3: TrayExitRequested?.Invoke(); break;
        }
    }

    public void Dispose()
    {
        foreach (var n in _hotkeys.Keys) UnregisterHotKey(_hwnd, n);
        _hotkeys.Clear();
        HideTrayIcon();
        DestroyWindow(_hwnd);
    }

    private delegate nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEX
    {
        public int cbSize; public uint style; public nint lpfnWndProc; public int cbClsExtra, cbWndExtra;
        public nint hInstance, hIcon, hCursor, hbrBackground; public string? lpszMenuName; public string lpszClassName; public nint hIconSm;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public int cbSize; public nint hWnd; public uint uID, uFlags, uCallbackMessage; public nint hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState, dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags; public Guid guidItem; public nint hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern ushort RegisterClassEx(ref WNDCLASSEX cls);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint CreateWindowEx(int ex, string cls, string name, int style, int x, int y, int w, int h, nint parent, nint menu, nint inst, nint param);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern nint DefWindowProc(nint hwnd, uint msg, nint wParam, nint lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandle(string? name);
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(nint hwnd, int id, uint mods, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(nint hwnd, int id);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern bool Shell_NotifyIcon(uint msg, ref NOTIFYICONDATA data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint LoadImage(nint inst, string name, uint type, int cx, int cy, uint load);
    [DllImport("user32.dll")] private static extern nint LoadIcon(nint inst, nint name);
    [DllImport("user32.dll")] private static extern nint CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool AppendMenu(nint menu, uint flags, nint id, string? text);
    [DllImport("user32.dll")] private static extern int TrackPopupMenu(nint menu, uint flags, int x, int y, int reserved, nint hwnd, nint rect);
    [DllImport("user32.dll")] private static extern bool DestroyMenu(nint menu);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT pt);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint hwnd);
}
