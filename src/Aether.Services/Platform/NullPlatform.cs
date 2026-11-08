using Aether.Core.Abstractions;
using Aether.Core.Configuration;
using Aether.Core.Diagnostics;

namespace Aether.Services.Platform;

/// <summary>
/// Safe fallbacks used when AETHER runs somewhere a Windows capability is missing (tests, unsupported builds).
/// They report "unsupported" honestly instead of pretending to work.
/// </summary>
public sealed class UnsupportedPlatform : ITaskbarStyler, IWallpaperService, ICursorService, IRestorePointService, IProcessMonitor, IDisplayEvents
{
    public bool IsSupported => false;
    public void Apply(TaskbarEffect effect, ColorValue tint, bool allMonitors) => throw new PlatformNotSupportedException("Taskbar effects require Windows.");
    public void Reset() { }
    public IReadOnlyList<MonitorInfo> GetMonitors() => [];
    public string? GetWallpaper(string? monitorId) => null;
    public void SetWallpaper(string? monitorId, string imagePath) => throw new PlatformNotSupportedException("Wallpapers require Windows.");
    public WallpaperFit GetFit() => WallpaperFit.Fill;
    public void SetFit(WallpaperFit fit) { }
    public void SetBackgroundColor(ColorValue color) { }
    public IReadOnlyList<CursorScheme> GetSchemes() => [];
    public string? GetActiveSchemeName() => null;
    public void ApplyScheme(CursorScheme scheme) => throw new PlatformNotSupportedException();
    public int GetCursorSize() => 32;
    public void SetCursorSize(int size) { }
    public Task<OperationResult> CreateAsync(string description, CancellationToken ct = default) =>
        Task.FromResult(OperationResult.Fail("System Restore is only available on Windows."));
    public IReadOnlySet<string> GetRunningProcessNames() => new HashSet<string>();
#pragma warning disable CS0067
    public event Action? DisplaysChanged;
#pragma warning restore CS0067
}
