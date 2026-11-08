using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json.Nodes;
using Aether.Core.Configuration;
using Aether.Services.Platform;
using Aether.Storage;
using Aether.WidgetEngine;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Aether.Presentation.ViewModels;

public sealed record WidgetRow(string Label, string Value, double Percent);

public sealed partial class TodoItem : ObservableObject
{
    [ObservableProperty] private string _text = "";
    [ObservableProperty] private bool _done;
}

/// <summary>
/// Live state of one desktop widget. <see cref="TickAsync"/> is called by the widget window on its refresh
/// interval; each tick samples only what that widget type needs.
/// </summary>
public sealed partial class WidgetRuntimeViewModel : ObservableObject
{
    private readonly WidgetManager _manager;
    private readonly ISystemMetricsProvider? _metrics;
    private readonly IMediaSessionProvider? _media;
    private readonly IWeatherProvider _weather;
    private readonly IShellLauncher _launcher;
    private readonly IDesktopController _desktop;
    private readonly IDocumentStore _store;
    private GeoLocation? _location;
    private DateTimeOffset _lastWeather;
    private DateTimeOffset? _focusEnds;
    private TimeSpan _focusRemaining;
    private bool _focusBreak;

    public WidgetRuntimeViewModel(WidgetInstance instance, WidgetManager manager, IWeatherProvider weather, IShellLauncher launcher, IDesktopController desktop,
        IDocumentStore store, ISystemMetricsProvider? metrics, IMediaSessionProvider? media)
    {
        Instance = instance; _manager = manager; _weather = weather; _launcher = launcher; _desktop = desktop; _store = store; _metrics = metrics; _media = media;
        Definition = WidgetCatalog.Find(instance.TypeId)!;
        _notes = instance.Settings.GetString("text");
        foreach (var t in store.Get<TodoList>("widget-todo", instance.Id)?.Items ?? []) Todos.Add(new TodoItem { Text = t.Text, Done = t.Done });
        _focusRemaining = TimeSpan.FromMinutes(instance.Settings.GetDouble("work", 25));
    }

    public WidgetInstance Instance { get; private set; }
    public WidgetDefinition Definition { get; }
    public string TypeId => Instance.TypeId;
    public ObservableCollection<double> Sparkline { get; } = [];
    public ObservableCollection<WidgetRow> Rows { get; } = [];
    public ObservableCollection<TodoItem> Todos { get; } = [];
    public ObservableCollection<string> LauncherApps { get; } = [];
    public ObservableCollection<CalendarDay> CalendarDays { get; } = [];

    [ObservableProperty] private string _primary = "";
    [ObservableProperty] private string _secondary = "";
    [ObservableProperty] private string _tertiary = "";
    [ObservableProperty] private double _percent;
    [ObservableProperty] private string _glyph = "";
    [ObservableProperty] private bool _isPlaying;
    [ObservableProperty] private byte[]? _artwork;
    [ObservableProperty] private string? _error;
    [ObservableProperty] private string _notes;
    [ObservableProperty] private string _newTodo = "";
    [ObservableProperty] private bool _focusRunning;

    public void UpdateInstance(WidgetInstance instance) { Instance = instance; OnPropertyChanged(nameof(Instance)); }

    partial void OnNotesChanged(string value)
    {
        var settings = (JsonObject)Instance.Settings.DeepClone();
        settings["text"] = value;
        Instance = Instance with { Settings = settings };
        _manager.UpdateWidgetSettings(Instance.Id, settings);
    }

    public async Task TickAsync()
    {
        try
        {
            Error = null;
            var s = Instance.Settings;
            switch (TypeId)
            {
                case "clock":
                {
                    var now = DateTime.Now;
                    var fmt = (s.GetBool("hour24", true) ? "HH:mm" : "h:mm") + (s.GetBool("seconds") ? ":ss" : "");
                    Primary = now.ToString(fmt, CultureInfo.CurrentCulture);
                    Secondary = s.GetBool("showDate", true) ? now.ToString("dddd, d MMMM", CultureInfo.CurrentCulture) : "";
                    Tertiary = s.GetBool("hour24", true) ? "" : now.ToString("tt", CultureInfo.CurrentCulture);
                    break;
                }
                case "calendar":
                    BuildCalendar(s.GetString("firstDay", "monday") == "sunday" ? DayOfWeek.Sunday : DayOfWeek.Monday);
                    break;
                case "worldclocks":
                {
                    Rows.Clear();
                    foreach (var id in s.GetString("zones").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Take(8))
                    {
                        try
                        {
                            var tz = TimeZoneInfo.FindSystemTimeZoneById(id);
                            var t = TimeZoneInfo.ConvertTime(DateTimeOffset.Now, tz);
                            var diff = (t.Offset - DateTimeOffset.Now.Offset).TotalHours;
                            Rows.Add(new WidgetRow(id.Contains('/') ? id[(id.LastIndexOf('/') + 1)..].Replace('_', ' ') : id, t.ToString("HH:mm"), diff));
                        }
                        catch (TimeZoneNotFoundException) { Rows.Add(new WidgetRow(id, "unknown zone", 0)); }
                    }
                    break;
                }
                case "countdown":
                {
                    Primary = s.GetString("title");
                    if (DateTime.TryParseExact(s.GetString("target"), "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var target))
                    {
                        var left = target - DateTime.Now;
                        Secondary = left <= TimeSpan.Zero ? "It's here!" : $"{(int)left.TotalDays}d {left.Hours}h {left.Minutes}m {left.Seconds}s";
                        Tertiary = target.ToString("f", CultureInfo.CurrentCulture);
                    }
                    else { Secondary = "Set a date as yyyy-MM-dd HH:mm"; Tertiary = ""; }
                    break;
                }
                case "focus":
                    TickFocus(s);
                    break;
                case "weather":
                    if (DateTimeOffset.Now - _lastWeather < TimeSpan.FromSeconds(Math.Max(300, Instance.RefreshSeconds)) && Primary.Length > 0) break;
                    _lastWeather = DateTimeOffset.Now;
                    var city = s.GetString("location", "London");
                    if (_location is null || !_location.Name.Equals(city, StringComparison.OrdinalIgnoreCase))
                        _location = await _weather.FindLocationAsync(city) ?? throw new InvalidOperationException($"'{city}' wasn't found.");
                    var f = s.GetString("units") == "fahrenheit";
                    var w = await _weather.GetCurrentAsync(_location, f);
                    var unit = f ? "°F" : "°C";
                    Primary = $"{w.Temperature:0}{unit}";
                    Secondary = $"{w.Condition} · {_location.Name}";
                    Tertiary = $"H {w.High:0}° L {w.Low:0}° · Wind {w.WindSpeed:0} {(f ? "mph" : "km/h")} · {w.Humidity}%";
                    Glyph = w.Glyph;
                    break;
                case "cpu":
                    Push(_metrics?.GetCpuUsage() ?? 0, "CPU");
                    break;
                case "ram":
                {
                    var m = _metrics?.GetMemory();
                    if (m is null) break;
                    Push(m.Percent, "Memory");
                    Secondary = $"{m.UsedBytes / 1073741824d:0.0} of {m.TotalBytes / 1073741824d:0.0} GB";
                    break;
                }
                case "gpu":
                {
                    var g = _metrics?.GetGpuUsage();
                    if (g is null) { Primary = "—"; Secondary = "GPU counters aren't available on this PC"; break; }
                    Push(g.Value, "GPU 3D");
                    break;
                }
                case "network":
                {
                    var n = _metrics?.GetNetwork();
                    if (n is null) break;
                    Primary = $"↓ {Rate(n.BytesReceivedPerSecond)}";
                    Secondary = $"↑ {Rate(n.BytesSentPerSecond)}";
                    AddSample(n.BytesReceivedPerSecond / 1024);
                    break;
                }
                case "storage":
                    Rows.Clear();
                    foreach (var d in _metrics?.GetDrives() ?? [])
                        Rows.Add(new WidgetRow($"{d.Name} {d.Label}".Trim(), $"{d.FreeBytes / 1073741824d:0} GB free of {d.TotalBytes / 1073741824d:0} GB", d.PercentUsed));
                    break;
                case "battery":
                {
                    var b = _metrics?.GetBattery();
                    if (b is null || !b.Present) { Primary = "—"; Secondary = "No battery (desktop PC)"; Percent = 0; break; }
                    Percent = b.Percent;
                    Primary = $"{b.Percent}%";
                    Secondary = b.Charging ? "Charging" : b.OnAcPower ? "Plugged in" : b.Remaining is { } r ? $"{(int)r.TotalHours}h {r.Minutes}m left" : "On battery";
                    Glyph = b.Charging ? "" : "";
                    break;
                }
                case "system":
                {
                    Rows.Clear();
                    if (_metrics is null) break;
                    var cpu = _metrics.GetCpuUsage();
                    var mem = _metrics.GetMemory();
                    var gpu = _metrics.GetGpuUsage();
                    var net = _metrics.GetNetwork();
                    Rows.Add(new WidgetRow("CPU", $"{cpu:0}%", cpu));
                    Rows.Add(new WidgetRow("Memory", $"{mem.Percent:0}%", mem.Percent));
                    if (gpu is { } gv) Rows.Add(new WidgetRow("GPU", $"{gv:0}%", gv));
                    Rows.Add(new WidgetRow("Network", $"↓ {Rate(net.BytesReceivedPerSecond)}  ↑ {Rate(net.BytesSentPerSecond)}", 0));
                    AddSample(cpu);
                    break;
                }
                case "media" or "nowplaying":
                {
                    if (_media is null || !_media.IsSupported) { Primary = "Media sessions unavailable"; break; }
                    var info = await _media.GetCurrentAsync();
                    if (info is null) { Primary = "Nothing playing"; Secondary = ""; Tertiary = ""; IsPlaying = false; Artwork = null; break; }
                    Primary = info.Title.Length > 0 ? info.Title : "Unknown title";
                    Secondary = info.Artist;
                    Tertiary = info.Album;
                    IsPlaying = info.State == MediaPlaybackState.Playing;
                    Artwork = info.Thumbnail;
                    break;
                }
                case "launcher":
                {
                    var apps = s.GetString("apps").Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                    if (!apps.SequenceEqual(LauncherApps)) { LauncherApps.Clear(); foreach (var a in apps) LauncherApps.Add(a); }
                    break;
                }
                case "clipboard":
                    Primary = "Clipboard history";
                    Secondary = "Opens Win+V";
                    break;
            }
        }
        catch (Exception ex) { Error = ex.Message; }
    }

    private void Push(double value, string label)
    {
        Percent = value;
        Primary = $"{value:0}%";
        Secondary = label;
        AddSample(value);
    }

    private void AddSample(double v)
    {
        Sparkline.Add(v);
        while (Sparkline.Count > 40) Sparkline.RemoveAt(0);
    }

    private static string Rate(double bytesPerSecond) => bytesPerSecond switch
    {
        >= 1048576 => $"{bytesPerSecond / 1048576:0.0} MB/s",
        >= 1024 => $"{bytesPerSecond / 1024:0} KB/s",
        _ => $"{bytesPerSecond:0} B/s",
    };

    private void BuildCalendar(DayOfWeek first)
    {
        var today = DateTime.Today;
        Primary = today.ToString("MMMM yyyy", CultureInfo.CurrentCulture);
        if (CalendarDays.Count > 0 && CalendarDays.Any(d => d.IsToday && d.Date == today)) return;
        CalendarDays.Clear();
        var start = new DateTime(today.Year, today.Month, 1);
        var offset = ((int)start.DayOfWeek - (int)first + 7) % 7;
        start = start.AddDays(-offset);
        for (var i = 0; i < 42; i++)
        {
            var d = start.AddDays(i);
            CalendarDays.Add(new CalendarDay(d, d.Month == today.Month, d == today));
        }
    }

    private void TickFocus(JsonObject s)
    {
        if (FocusRunning && _focusEnds is { } end)
        {
            _focusRemaining = end - DateTimeOffset.Now;
            if (_focusRemaining <= TimeSpan.Zero)
            {
                _focusBreak = !_focusBreak;
                _focusRemaining = TimeSpan.FromMinutes(s.GetDouble(_focusBreak ? "break" : "work", _focusBreak ? 5 : 25));
                _focusEnds = DateTimeOffset.Now + _focusRemaining;
            }
        }
        var total = TimeSpan.FromMinutes(s.GetDouble(_focusBreak ? "break" : "work", _focusBreak ? 5 : 25));
        Primary = $"{(int)_focusRemaining.TotalMinutes:00}:{_focusRemaining.Seconds:00}";
        Secondary = _focusBreak ? "Break" : "Focus";
        Percent = total.TotalSeconds <= 0 ? 0 : 100 * (1 - _focusRemaining.TotalSeconds / total.TotalSeconds);
    }

    [RelayCommand]
    private void ToggleFocus()
    {
        FocusRunning = !FocusRunning;
        _focusEnds = FocusRunning ? DateTimeOffset.Now + _focusRemaining : null;
    }

    [RelayCommand]
    private void ResetFocus()
    {
        FocusRunning = false; _focusBreak = false; _focusEnds = null;
        _focusRemaining = TimeSpan.FromMinutes(Instance.Settings.GetDouble("work", 25));
        TickFocus(Instance.Settings);
    }

    [RelayCommand] private async Task PlayPauseAsync() { if (_media is not null) { await _media.PlayPauseAsync(); await TickAsync(); } }
    [RelayCommand] private async Task NextAsync() { if (_media is not null) { await _media.NextAsync(); await TickAsync(); } }
    [RelayCommand] private async Task PreviousAsync() { if (_media is not null) { await _media.PreviousAsync(); await TickAsync(); } }

    [RelayCommand]
    private void LaunchApp(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        try { _launcher.Open(path); } catch (Exception ex) { Error = ex.Message; }
    }

    [RelayCommand] private void OpenClipboard() => _desktop.OpenClipboardHistory();

    [RelayCommand]
    private void AddTodo()
    {
        if (string.IsNullOrWhiteSpace(NewTodo)) return;
        var item = new TodoItem { Text = NewTodo.Trim() };
        item.PropertyChanged += (_, _) => SaveTodos();
        Todos.Add(item);
        NewTodo = "";
        SaveTodos();
    }

    [RelayCommand] private void RemoveTodo(TodoItem? t) { if (t is not null) { Todos.Remove(t); SaveTodos(); } }
    [RelayCommand] private void ClearDone() { foreach (var t in Todos.Where(t => t.Done).ToList()) Todos.Remove(t); SaveTodos(); }

    public void SaveTodos() => _store.Put("widget-todo", Instance.Id, new TodoList(Todos.Select(t => new TodoEntry(t.Text, t.Done)).ToList()));

    public void SaveBounds(double x, double y, double w, double h)
    {
        Instance = Instance with { X = x, Y = y, Width = w, Height = h };
        _manager.UpdateBounds(Instance.Id, x, y, w, h);
    }

    private sealed record TodoList(List<TodoEntry> Items);
    private sealed record TodoEntry(string Text, bool Done);
}

public sealed record CalendarDay(DateTime Date, bool InMonth, bool IsToday)
{
    public string Day => Date.Day.ToString(CultureInfo.CurrentCulture);
}
