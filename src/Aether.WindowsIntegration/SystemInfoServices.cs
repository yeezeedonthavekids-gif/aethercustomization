using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Aether.Core.Abstractions;
using Aether.Core.Diagnostics;
using Aether.Services.Platform;
using Aether.WidgetEngine;
using Microsoft.Win32;
using static Aether.WindowsIntegration.Native.NativeMethods;

namespace Aether.WindowsIntegration;

[SupportedOSPlatform("windows")]
public sealed class WindowsSystemInfoProvider(ISystemEnvironment environment) : ISystemInfoProvider
{
    public SystemSummary GetSummary()
    {
        var v = environment.Version;
        using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        var cpu = (hklm.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0")?.GetValue("ProcessorNameString") as string)?.Trim() ?? "Unknown processor";
        var gpus = new List<string>();
        using (var display = hklm.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}"))
            foreach (var sub in display?.GetSubKeyNames().Where(n => n.All(char.IsDigit)) ?? [])
                if (display!.OpenSubKey(sub)?.GetValue("DriverDesc") is string name && !gpus.Contains(name) && !name.Contains("Basic Display", StringComparison.OrdinalIgnoreCase))
                    gpus.Add(name);
        var mem = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        GlobalMemoryStatusEx(ref mem);
        return new SystemSummary(v.ProductName, v.DisplayVersion, v.BuildString, v.Edition, cpu, Environment.ProcessorCount, mem.ullTotalPhys, gpus,
            TimeSpan.FromMilliseconds(Environment.TickCount64), environment.MachineName, environment.UserName, environment.IsAdministrator, v.Architecture);
    }
}

/// <summary>On-demand metrics: each call samples the system; nothing polls in the background.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsSystemMetricsProvider : ISystemMetricsProvider, IDisposable
{
    private readonly object _gate = new();
    private ulong _lastIdle, _lastTotal;
    private long _lastSent, _lastReceived;
    private DateTime _lastNetSample;
    private nint _gpuQuery, _gpuCounter;
    private bool _gpuUnavailable;

    public double GetCpuUsage()
    {
        lock (_gate)
        {
            if (!GetSystemTimes(out var idle, out var kernel, out var user)) return 0;
            var total = kernel.Value + user.Value;
            var dIdle = idle.Value - _lastIdle;
            var dTotal = total - _lastTotal;
            var first = _lastTotal == 0;
            _lastIdle = idle.Value; _lastTotal = total;
            return first || dTotal == 0 ? 0 : Math.Clamp(100.0 * (dTotal - dIdle) / dTotal, 0, 100);
        }
    }

    public MemoryInfo GetMemory()
    {
        var m = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        return GlobalMemoryStatusEx(ref m) ? new MemoryInfo(m.ullTotalPhys - m.ullAvailPhys, m.ullTotalPhys) : new MemoryInfo(0, 0);
    }

    public double? GetGpuUsage()
    {
        lock (_gate)
        {
            if (_gpuUnavailable) return null;
            if (_gpuQuery == 0)
            {
                if (PdhOpenQuery(null, 0, out _gpuQuery) != 0 || PdhAddEnglishCounter(_gpuQuery, @"\GPU Engine(*engtype_3D)\Utilization Percentage", 0, out _gpuCounter) != 0)
                {
                    _gpuUnavailable = true;
                    return null;
                }
                PdhCollectQueryData(_gpuQuery);
                return 0;
            }
            if (PdhCollectQueryData(_gpuQuery) != 0) return null;
            var size = 0;
            var status = PdhGetFormattedCounterArray(_gpuCounter, PDH_FMT_DOUBLE, ref size, out var count, 0);
            if (status != PDH_MORE_DATA || size == 0) return 0;
            var buffer = Marshal.AllocHGlobal(size);
            try
            {
                if (PdhGetFormattedCounterArray(_gpuCounter, PDH_FMT_DOUBLE, ref size, out count, buffer) != 0) return null;
                var itemSize = Marshal.SizeOf<PDH_FMT_COUNTERVALUE_ITEM>();
                double sum = 0;
                for (var i = 0; i < count; i++) sum += Marshal.PtrToStructure<PDH_FMT_COUNTERVALUE_ITEM>(buffer + i * itemSize).FmtValue.doubleValue;
                return Math.Clamp(sum, 0, 100);
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
    }

    public NetworkThroughput GetNetwork()
    {
        long sent = 0, received = 0;
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
            var s = nic.GetIPStatistics();
            sent += s.BytesSent; received += s.BytesReceived;
        }
        lock (_gate)
        {
            var now = DateTime.UtcNow;
            var seconds = (now - _lastNetSample).TotalSeconds;
            var result = _lastNetSample == default || seconds <= 0
                ? new NetworkThroughput(0, 0)
                : new NetworkThroughput(Math.Max(0, (sent - _lastSent) / seconds), Math.Max(0, (received - _lastReceived) / seconds));
            _lastSent = sent; _lastReceived = received; _lastNetSample = now;
            return result;
        }
    }

    public IReadOnlyList<DriveUsage> GetDrives() => DriveInfo.GetDrives()
        .Where(d => d.IsReady && d.DriveType is DriveType.Fixed or DriveType.Removable)
        .Select(d => new DriveUsage(d.Name.TrimEnd('\\'), d.VolumeLabel, (ulong)d.TotalSize, (ulong)d.AvailableFreeSpace)).ToList();

    public BatteryInfo GetBattery()
    {
        if (!GetSystemPowerStatus(out var s)) return new BatteryInfo(false, 0, false, true, null);
        var present = (s.BatteryFlag & 128) == 0 && s.BatteryFlag != 255;
        return new BatteryInfo(present, s.BatteryLifePercent == 255 ? 0 : s.BatteryLifePercent, (s.BatteryFlag & 8) != 0, s.ACLineStatus == 1,
            s.BatteryLifeTime > 0 ? TimeSpan.FromSeconds(s.BatteryLifeTime) : null);
    }

    public void Dispose()
    {
        lock (_gate) if (_gpuQuery != 0) { PdhCloseQuery(_gpuQuery); _gpuQuery = 0; }
    }
}

/// <summary>Windows media sessions (GlobalSystemMediaTransportControlsSessionManager, Windows 10 1809+).</summary>
[SupportedOSPlatform("windows10.0.17763.0")]
public sealed class WindowsMediaSessionProvider : IMediaSessionProvider
{
    private Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager? _manager;

    public bool IsSupported => true;

    private async Task<Windows.Media.Control.GlobalSystemMediaTransportControlsSession?> SessionAsync()
    {
        _manager ??= await Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        return _manager.GetCurrentSession();
    }

    public async Task<MediaInfo?> GetCurrentAsync(CancellationToken ct = default)
    {
        var session = await SessionAsync().ConfigureAwait(false);
        if (session is null) return null;
        var props = await session.TryGetMediaPropertiesAsync();
        var playback = session.GetPlaybackInfo();
        var state = playback.PlaybackStatus switch
        {
            Windows.Media.Control.GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing => MediaPlaybackState.Playing,
            Windows.Media.Control.GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused => MediaPlaybackState.Paused,
            Windows.Media.Control.GlobalSystemMediaTransportControlsSessionPlaybackStatus.Stopped => MediaPlaybackState.Stopped,
            _ => MediaPlaybackState.None,
        };
        byte[]? thumb = null;
        if (props.Thumbnail is not null)
        {
            try
            {
                using var stream = await props.Thumbnail.OpenReadAsync();
                using var net = stream.AsStreamForRead();
                using var ms = new MemoryStream();
                await net.CopyToAsync(ms, ct).ConfigureAwait(false);
                thumb = ms.ToArray();
            }
            catch (Exception) { /* thumbnails are optional */ }
        }
        return new MediaInfo(props.Title ?? "", props.Artist ?? "", props.AlbumTitle ?? "", session.SourceAppUserModelId ?? "", state, thumb);
    }

    public async Task<bool> PlayPauseAsync(CancellationToken ct = default) => await SessionAsync().ConfigureAwait(false) is { } s && await s.TryTogglePlayPauseAsync();
    public async Task<bool> NextAsync(CancellationToken ct = default) => await SessionAsync().ConfigureAwait(false) is { } s && await s.TrySkipNextAsync();
    public async Task<bool> PreviousAsync(CancellationToken ct = default) => await SessionAsync().ConfigureAwait(false) is { } s && await s.TrySkipPreviousAsync();
}

/// <summary>
/// Creates System Restore points with Checkpoint-Computer. Reports failure honestly — including the case where
/// Windows skips creation because another point was made in the last 24 hours.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsRestorePointService(IPowerShellRunner powerShell, ISystemEnvironment environment) : IRestorePointService
{
    public bool IsSupported => powerShell.IsAvailable;

    public async Task<OperationResult> CreateAsync(string description, CancellationToken ct = default)
    {
        if (!environment.IsAdministrator) return OperationResult.Fail("Creating a Windows restore point requires administrator rights. Restart AETHER as administrator.");
        var safe = new string(description.Where(c => char.IsLetterOrDigit(c) || c is ' ' or '-' or '_').ToArray());
        if (safe.Length == 0) safe = "AETHER";
        var script = $$"""
            $before = (Get-ComputerRestorePoint | Measure-Object).Count
            Checkpoint-Computer -Description 'AETHER: {{safe}}' -RestorePointType 'MODIFY_SETTINGS' -WarningVariable w -WarningAction SilentlyContinue
            $after = (Get-ComputerRestorePoint | Measure-Object).Count
            if ($w) { Write-Output "WARNING: $w" }
            Write-Output "COUNT:$before:$after"
            """;
        var r = await powerShell.RunScriptAsync(script, TimeSpan.FromMinutes(5), ct).ConfigureAwait(false);
        if (!r.Succeeded) return OperationResult.Fail("Windows did not create a restore point.", r.StandardError.Trim().Length > 0 ? r.StandardError.Trim() : "Is System Protection turned on for the system drive?");
        var countLine = r.StandardOutput.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith("COUNT:", StringComparison.Ordinal));
        var parts = countLine?.Split(':');
        if (parts is { Length: 3 } && int.TryParse(parts[1], out var before) && int.TryParse(parts[2], out var after) && after > before)
            return OperationResult.Ok("A Windows restore point was created.");
        var warning = r.StandardOutput.Split('\n').FirstOrDefault(l => l.StartsWith("WARNING:", StringComparison.Ordinal))?.Trim();
        return OperationResult.Fail("Windows did not create a new restore point.", warning ?? "Windows limits restore points to one every 24 hours by default, or System Protection may be off.");
    }
}

/// <summary>Detects monitor topology changes by comparing work areas (only while subscribed, every 5 s).</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsDisplayEvents(IWindowManager windows) : IDisplayEvents, IDisposable
{
    private Timer? _timer;
    private string _last = "";
    private Action? _changed;

    public event Action? DisplaysChanged
    {
        add
        {
            _changed += value;
            if (_timer is null)
            {
                _last = Topology();
                _timer = new Timer(_ =>
                {
                    var now = Topology();
                    if (now == _last) return;
                    _last = now;
                    _changed?.Invoke();
                }, null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
            }
        }
        remove
        {
            _changed -= value;
            if (_changed is null) { _timer?.Dispose(); _timer = null; }
        }
    }

    private string Topology() => string.Join('|', windows.GetWorkAreas().Select(a => $"{a.MonitorId}:{a.Width}x{a.Height}"));

    public void Dispose() { _timer?.Dispose(); _timer = null; }
}
