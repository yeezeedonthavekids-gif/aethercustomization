using System.Runtime.Versioning;
using Aether.Core.Abstractions;
using static Aether.WindowsIntegration.Native.NativeMethods;

namespace Aether.WindowsIntegration;

/// <summary>Documented SystemParametersInfo flags and setting-change broadcasts.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsSystemSettings : ISystemSettings
{
    private static (int Get, int Set) Codes(SystemFlag flag) => flag switch
    {
        SystemFlag.ClientAreaAnimation => (SPI_GETCLIENTAREAANIMATION, SPI_SETCLIENTAREAANIMATION),
        SystemFlag.MenuAnimation => (SPI_GETMENUANIMATION, SPI_SETMENUANIMATION),
        SystemFlag.ComboBoxAnimation => (SPI_GETCOMBOBOXANIMATION, SPI_SETCOMBOBOXANIMATION),
        SystemFlag.TooltipAnimation => (SPI_GETTOOLTIPANIMATION, SPI_SETTOOLTIPANIMATION),
        SystemFlag.DragFullWindows => (SPI_GETDRAGFULLWINDOWS, SPI_SETDRAGFULLWINDOWS),
        SystemFlag.FontSmoothing => (SPI_GETFONTSMOOTHING, SPI_SETFONTSMOOTHING),
        SystemFlag.WindowShadows => (SPI_GETDROPSHADOW, SPI_SETDROPSHADOW),
        SystemFlag.CursorShadow => (SPI_GETCURSORSHADOW, SPI_SETCURSORSHADOW),
        SystemFlag.SnapToDefaultButton => (SPI_GETSNAPTODEFBUTTON, SPI_SETSNAPTODEFBUTTON),
        SystemFlag.SnapWindows => (SPI_GETWINARRANGING, SPI_SETWINARRANGING),
        _ => (0, 0),
    };

    public bool? GetFlag(SystemFlag flag)
    {
        switch (flag)
        {
            case SystemFlag.MinimizeMaximizeAnimation:
            {
                var info = new ANIMATIONINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<ANIMATIONINFO>() };
                return SystemParametersInfo(SPI_GETANIMATION, info.cbSize, ref info, 0) ? info.iMinAnimate != 0 : null;
            }
            case SystemFlag.EnhancePointerPrecision:
            {
                var mouse = new int[3];
                return SystemParametersInfo(SPI_GETMOUSE, 0, mouse, 0) ? mouse[2] != 0 : null;
            }
            default:
            {
                var (get, _) = Codes(flag);
                var value = 0;
                return get != 0 && SystemParametersInfo(get, 0, ref value, 0) ? value != 0 : null;
            }
        }
    }

    public void SetFlag(SystemFlag flag, bool value)
    {
        const int persist = SPIF_UPDATEINIFILE | SPIF_SENDCHANGE;
        bool ok;
        switch (flag)
        {
            case SystemFlag.MinimizeMaximizeAnimation:
            {
                var info = new ANIMATIONINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<ANIMATIONINFO>(), iMinAnimate = value ? 1 : 0 };
                ok = SystemParametersInfo(SPI_SETANIMATION, info.cbSize, ref info, persist);
                break;
            }
            case SystemFlag.EnhancePointerPrecision:
            {
                int[] mouse = value ? [6, 10, 1] : [0, 0, 0];
                ok = SystemParametersInfo(SPI_SETMOUSE, 0, mouse, persist);
                break;
            }
            case SystemFlag.DragFullWindows or SystemFlag.FontSmoothing or SystemFlag.SnapToDefaultButton:
                // These take the value in uiParam.
                ok = SystemParametersInfo(Codes(flag).Set, value ? 1 : 0, 0, persist);
                break;
            default:
                // The remaining flags take the value as pvParam (a BOOL cast to a pointer).
                ok = SystemParametersInfo(Codes(flag).Set, 0, value ? 1 : 0, persist);
                break;
        }
        if (!ok) throw new System.ComponentModel.Win32Exception();
    }

    public void BroadcastSettingChange(string? area) =>
        SendMessageTimeout(HWND_BROADCAST, WM_SETTINGCHANGE, 0, area, SMTO_ABORTIFHUNG, 3000, out _);

    public void RefreshShell() => SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, 0, 0);

    public void ReloadCursors() => SystemParametersInfo(SPI_SETCURSORS, 0, 0, SPIF_SENDCHANGE);
}
