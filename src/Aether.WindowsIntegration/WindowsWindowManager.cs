using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Aether.Services.Platform;
using static Aether.WindowsIntegration.Native.NativeMethods;

namespace Aether.WindowsIntegration;

/// <summary>
/// Top-level window enumeration and placement. New-window notifications come from an out-of-context
/// SetWinEventHook on a dedicated message-loop thread, created only while someone subscribes.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsWindowManager : IWindowManager, IDisposable
{
    private readonly object _gate = new();
    private Action<WindowInfo>? _shown;
    private Thread? _hookThread;
    private uint _hookThreadId;
    private WinEventDelegate? _callback; // kept alive while hooked

    internal static List<nint> EnumerateAppWindows()
    {
        var list = new List<nint>();
        EnumWindows((h, _) =>
        {
            if (IsAppWindow(h)) list.Add(h);
            return true;
        }, 0);
        return list;
    }

    private static bool IsAppWindow(nint h)
    {
        if (!IsWindowVisible(h) || GetWindowTextLength(h) == 0) return false;
        var ex = (long)GetWindowLongPtr(h, GWL_EXSTYLE);
        if ((ex & WS_EX_TOOLWINDOW) != 0 && (ex & WS_EX_APPWINDOW) == 0) return false;
        if ((ex & WS_EX_NOACTIVATE) != 0) return false;
        if (GetWindow(h, GW_OWNER) != 0 && (ex & WS_EX_APPWINDOW) == 0) return false;
        if (DwmGetWindowAttribute(h, DWMWA_CLOAKED, out var cloaked, sizeof(int)) == 0 && cloaked != 0) return false; // hidden UWP / other desktops
        var cls = new StringBuilder(64);
        GetClassName(h, cls, cls.Capacity);
        return cls.ToString() is not ("Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd");
    }

    private static WindowInfo? Describe(nint h)
    {
        if (!GetWindowRect(h, out var r)) return null;
        var title = new StringBuilder(Math.Max(1, GetWindowTextLength(h) + 1));
        GetWindowText(h, title, title.Capacity);
        GetWindowThreadProcessId(h, out var pid);
        string process;
        try { using var p = Process.GetProcessById(pid); process = p.ProcessName; }
        catch (ArgumentException) { process = "?"; }
        var ex = (long)GetWindowLongPtr(h, GWL_EXSTYLE);
        byte alpha = 255;
        if ((ex & WS_EX_LAYERED) != 0 && GetLayeredWindowAttributes(h, out _, out var a, out var flags) && (flags & LWA_ALPHA) != 0) alpha = a;
        return new WindowInfo(h, title.ToString(), process, pid, r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top, (ex & WS_EX_TOPMOST) != 0, IsIconic(h), alpha);
    }

    public IReadOnlyList<WindowInfo> GetWindows() => EnumerateAppWindows().Select(Describe).OfType<WindowInfo>().ToList();

    public IReadOnlyList<WorkArea> GetWorkAreas()
    {
        var list = new List<WorkArea>();
        EnumDisplayMonitors(0, 0, (nint h, nint _, ref RECT _, nint _) =>
        {
            var info = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
            if (GetMonitorInfo(h, ref info))
                list.Add(new WorkArea(info.szDevice, $"Display {list.Count + 1}", info.rcWork.Left, info.rcWork.Top,
                    info.rcWork.Right - info.rcWork.Left, info.rcWork.Bottom - info.rcWork.Top, (info.dwFlags & 1) != 0));
            return true;
        }, 0);
        return list;
    }

    public nint GetForegroundWindow() => Native.NativeMethods.GetForegroundWindow();

    public void MoveWindow(nint handle, int left, int top, int width, int height) =>
        SetWindowPos(handle, 0, left, top, width, height, SWP_NOZORDER | SWP_NOACTIVATE);

    public void SetTopmost(nint handle, bool topmost) =>
        SetWindowPos(handle, topmost ? HWND_TOPMOST : HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);

    public void SetOpacity(nint handle, byte opacity)
    {
        var ex = (long)GetWindowLongPtr(handle, GWL_EXSTYLE);
        if (opacity == 255 && (ex & WS_EX_LAYERED) != 0)
        {
            SetLayeredWindowAttributes(handle, 0, 255, LWA_ALPHA);
            return;
        }
        if ((ex & WS_EX_LAYERED) == 0) SetWindowLongPtr(handle, GWL_EXSTYLE, (nint)(ex | WS_EX_LAYERED));
        SetLayeredWindowAttributes(handle, 0, opacity, LWA_ALPHA);
    }

    public void Minimize(nint handle) => ShowWindow(handle, SW_MINIMIZE);
    public void Restore(nint handle) { if (IsIconic(handle)) ShowWindow(handle, SW_RESTORE); }

    public event Action<WindowInfo>? WindowShown
    {
        add
        {
            lock (_gate)
            {
                _shown += value;
                if (_hookThread is null) StartHook();
            }
        }
        remove
        {
            lock (_gate)
            {
                _shown -= value;
                if (_shown is null) StopHook();
            }
        }
    }

    private void StartHook()
    {
        var ready = new ManualResetEventSlim();
        _hookThread = new Thread(() =>
        {
            _hookThreadId = GetCurrentThreadId();
            _callback = (_, _, hwnd, idObject, _, _, _) =>
            {
                if (idObject != OBJID_WINDOW || !IsAppWindow(hwnd)) return;
                var info = Describe(hwnd);
                if (info is not null) ThreadPool.QueueUserWorkItem(_ => _shown?.Invoke(info));
            };
            var hook = SetWinEventHook(EVENT_OBJECT_SHOW, EVENT_OBJECT_SHOW, 0, _callback, 0, 0, WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
            ready.Set();
            while (GetMessage(out var msg, 0, 0, 0) > 0) { TranslateMessage(ref msg); DispatchMessage(ref msg); }
            UnhookWinEvent(hook);
        }) { IsBackground = true, Name = "AETHER window events" };
        _hookThread.Start();
        ready.Wait(TimeSpan.FromSeconds(2));
    }

    private void StopHook()
    {
        if (_hookThread is null) return;
        PostThreadMessage(_hookThreadId, 0x0012 /* WM_QUIT */, 0, 0);
        _hookThread.Join(TimeSpan.FromSeconds(2));
        _hookThread = null;
    }

    public void Dispose()
    {
        lock (_gate) { _shown = null; StopHook(); }
    }
}
