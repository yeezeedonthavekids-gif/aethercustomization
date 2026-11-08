using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;

namespace Aether.App.Services;

/// <summary>Borderless, taskbar-less window used for widgets, the dock and the launcher.</summary>
public class ChromelessWindow : Window
{
    private const int WM_NCLBUTTONDOWN = 0xA1, HTCAPTION = 2;

    public ChromelessWindow(bool resizable, bool topmost)
    {
        var presenter = OverlappedPresenter.Create();
        presenter.SetBorderAndTitleBar(false, false);
        presenter.IsResizable = resizable;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.IsAlwaysOnTop = topmost;
        AppWindow.SetPresenter(presenter);
        AppWindow.IsShownInSwitchers = false;
        Hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var round = 2; // DWMWCP_ROUND (Windows 11; ignored on Windows 10)
        DwmSetWindowAttribute(Hwnd, 33, ref round, sizeof(int));
    }

    public nint Hwnd { get; }
    public double Scale => GetDpiForWindow(Hwnd) / 96.0;

    public void SetTopmost(bool topmost) { if (AppWindow.Presenter is OverlappedPresenter p) p.IsAlwaysOnTop = topmost; }

    public void MoveAndResize(double x, double y, double width, double height) =>
        AppWindow.MoveAndResize(new Windows.Graphics.RectInt32((int)(x * Scale), (int)(y * Scale), (int)(width * Scale), (int)(height * Scale)));

    /// <summary>Starts a native window drag from a pointer press.</summary>
    protected void BeginDrag(object sender, PointerRoutedEventArgs e)
    {
        if (!e.GetCurrentPoint(null).Properties.IsLeftButtonPressed) return;
        ReleaseCapture();
        SendMessage(Hwnd, WM_NCLBUTTONDOWN, HTCAPTION, 0);
    }

    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern nint SendMessage(nint hwnd, int msg, nint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint hwnd);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(nint hwnd, int attr, ref int value, int size);
}
