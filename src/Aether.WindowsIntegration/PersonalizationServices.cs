using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Aether.Core.Abstractions;
using Aether.Core.Configuration;
using Microsoft.Win32;
using static Aether.WindowsIntegration.Native.NativeMethods;

namespace Aether.WindowsIntegration;

// ── IDesktopWallpaper (shobjidl_core.h), documented since Windows 8 ──
[ComImport, Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDesktopWallpaper
{
    void SetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string? monitorID, [MarshalAs(UnmanagedType.LPWStr)] string wallpaper);
    [return: MarshalAs(UnmanagedType.LPWStr)] string GetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string? monitorID);
    [return: MarshalAs(UnmanagedType.LPWStr)] string GetMonitorDevicePathAt(uint monitorIndex);
    uint GetMonitorDevicePathCount();
    RECT GetMonitorRECT([MarshalAs(UnmanagedType.LPWStr)] string monitorID);
    void SetBackgroundColor(uint color);
    uint GetBackgroundColor();
    void SetPosition(int position);
    int GetPosition();
    void SetSlideshow(nint items);
    nint GetSlideshow();
    void SetSlideshowOptions(int options, uint slideshowTick);
    void GetSlideshowOptions(out int options, out uint slideshowTick);
    void AdvanceSlideshow([MarshalAs(UnmanagedType.LPWStr)] string? monitorID, int direction);
    int GetStatus();
    void Enable([MarshalAs(UnmanagedType.Bool)] bool enable);
}

[ComImport, Guid("C2CF3110-460E-4fc1-B9D0-8A1C0C9CC4BD")]
internal class DesktopWallpaperClass;

/// <summary>Per-monitor wallpapers through the documented IDesktopWallpaper COM interface.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsWallpaperService : IWallpaperService
{
    private static IDesktopWallpaper Create() => (IDesktopWallpaper)new DesktopWallpaperClass();

    public bool IsSupported => true;

    public IReadOnlyList<MonitorInfo> GetMonitors()
    {
        var wp = Create();
        try
        {
            var list = new List<MonitorInfo>();
            var count = wp.GetMonitorDevicePathCount();
            var primary = Screen();
            for (uint i = 0; i < count; i++)
            {
                var id = wp.GetMonitorDevicePathAt(i);
                RECT r;
                try { r = wp.GetMonitorRECT(id); } catch (COMException) { continue; } // detached monitor
                var isPrimary = r.Left == primary.Left && r.Top == primary.Top;
                list.Add(new MonitorInfo(id, $"Display {list.Count + 1}{(isPrimary ? " (main)" : "")}", r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top, isPrimary, 1.0));
            }
            return list;
        }
        finally { Marshal.ReleaseComObject(wp); }
    }

    private static RECT Screen()
    {
        RECT primary = default;
        EnumDisplayMonitors(0, 0, (nint h, nint _, ref RECT rect, nint _) =>
        {
            var info = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
            if (GetMonitorInfo(h, ref info) && (info.dwFlags & 1) != 0) primary = info.rcMonitor;
            return true;
        }, 0);
        return primary;
    }

    public string? GetWallpaper(string? monitorId)
    {
        var wp = Create();
        try { var path = wp.GetWallpaper(monitorId); return string.IsNullOrEmpty(path) ? null : path; }
        catch (COMException) { return null; } // "different wallpapers per monitor" when monitorId is null
        finally { Marshal.ReleaseComObject(wp); }
    }

    public void SetWallpaper(string? monitorId, string imagePath)
    {
        if (!File.Exists(imagePath)) throw new FileNotFoundException("Wallpaper not found.", imagePath);
        var wp = Create();
        try { wp.SetWallpaper(monitorId, Path.GetFullPath(imagePath)); }
        finally { Marshal.ReleaseComObject(wp); }
    }

    public WallpaperFit GetFit()
    {
        var wp = Create();
        try
        {
            return wp.GetPosition() switch { 0 => WallpaperFit.Center, 1 => WallpaperFit.Tile, 2 => WallpaperFit.Stretch, 3 => WallpaperFit.Fit, 5 => WallpaperFit.Span, _ => WallpaperFit.Fill };
        }
        finally { Marshal.ReleaseComObject(wp); }
    }

    public void SetFit(WallpaperFit fit)
    {
        var wp = Create();
        try
        {
            wp.SetPosition(fit switch { WallpaperFit.Center => 0, WallpaperFit.Tile => 1, WallpaperFit.Stretch => 2, WallpaperFit.Fit => 3, WallpaperFit.Span => 5, _ => 4 });
        }
        finally { Marshal.ReleaseComObject(wp); }
    }

    public void SetBackgroundColor(ColorValue color)
    {
        var wp = Create();
        try { wp.SetBackgroundColor((uint)(color.B << 16 | color.G << 8 | color.R)); }
        finally { Marshal.ReleaseComObject(wp); }
    }
}

/// <summary>Cursor schemes registered under HKLM/HKCU ...\Control Panel\Cursors\Schemes.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsCursorService(IRegistryAccess registry, ISystemSettings system) : ICursorService
{
    private const string SystemSchemes = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Control Panel\Cursors\Schemes";
    private const string UserSchemes = @"Control Panel\Cursors\Schemes";
    private static readonly string[] Names = ["Arrow", "Help", "AppStarting", "Wait", "Crosshair", "IBeam", "NWPen", "No", "SizeNS", "SizeWE", "SizeNWSE", "SizeNESW", "SizeAll", "UpArrow", "Hand", "Pin", "Person"];

    public IReadOnlyList<CursorScheme> GetSchemes()
    {
        var list = new List<CursorScheme>();
        void Read(RegistryRoot root, string path, bool isSystem)
        {
            foreach (var name in registry.GetValueNames(root, path))
            {
                if (registry.GetValue(root, path, name)?.Data is not string raw) continue;
                var files = raw.Split(',');
                list.Add(new CursorScheme(name, isSystem, Names.Select((n, i) => (n, i < files.Length ? files[i] : "")).ToDictionary(x => x.n, x => x.Item2)));
            }
        }
        Read(RegistryRoot.LocalMachine, SystemSchemes, true);
        Read(RegistryRoot.CurrentUser, UserSchemes, false);
        return list.OrderBy(s => s.Name).ToList();
    }

    public string? GetActiveSchemeName() => registry.GetValue(RegistryRoot.CurrentUser, @"Control Panel\Cursors", "")?.Data as string;

    public void ApplyScheme(CursorScheme scheme)
    {
        foreach (var (name, file) in scheme.Cursors)
            registry.SetValue(RegistryRoot.CurrentUser, @"Control Panel\Cursors", name, new RegistryData(RegistryDataKind.ExpandString, file));
        registry.SetValue(RegistryRoot.CurrentUser, @"Control Panel\Cursors", "", RegistryData.Str(scheme.Name));
        registry.SetValue(RegistryRoot.CurrentUser, @"Control Panel\Cursors", "Scheme Source", RegistryData.Dword(scheme.IsSystem ? 2 : 1));
        system.ReloadCursors();
    }

    public int GetCursorSize() => registry.GetValue(RegistryRoot.CurrentUser, @"Control Panel\Cursors", "CursorBaseSize")?.Data is int s ? s : 32;

    public void SetCursorSize(int size)
    {
        registry.SetValue(RegistryRoot.CurrentUser, @"Control Panel\Cursors", "CursorBaseSize", RegistryData.Dword(Math.Clamp(size, 32, 256)));
        system.ReloadCursors();
    }
}
