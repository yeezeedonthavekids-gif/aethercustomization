using Aether.Core.Abstractions;
using Aether.Core.Configuration;
using Aether.Presentation.Infrastructure;
using Aether.Presentation.ViewModels;
using Aether.Services.Platform;
using Aether.Storage;
using Aether.WidgetEngine;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using static Aether.App.Controls.Ui;

namespace Aether.App.Services;

/// <summary>Owns one window per widget in the active layout and keeps them in sync with layout changes.</summary>
public sealed class WidgetHost : IWidgetHost
{
    private readonly Dictionary<string, WidgetWindow> _windows = new();
    private readonly WidgetManager _manager;

    public WidgetHost(WidgetManager manager)
    {
        _manager = manager;
        manager.LayoutChanged += () => { if (IsRunning) RunOnUi(Sync); };
    }

    public bool IsRunning { get; private set; }
    public int WindowCount => _windows.Count;

    public Task StartAsync()
    {
        var tcs = new TaskCompletionSource();
        RunOnUi(() => { IsRunning = true; _manager.HostEnabled = true; Sync(); tcs.TrySetResult(); });
        return tcs.Task;
    }

    public Task StopAsync()
    {
        var tcs = new TaskCompletionSource();
        RunOnUi(() =>
        {
            IsRunning = false;
            foreach (var w in _windows.Values) w.CloseWidget();
            _windows.Clear();
            tcs.TrySetResult();
        });
        return tcs.Task;
    }

    public void OpenEditor() => RunOnUi(() => App.Get<INavigationService>().Navigate(PageKey.Widgets));

    private void RunOnUi(Action a) => App.Get<IUiDispatcher>().Post(a);

    private void Sync()
    {
        if (!IsRunning) return;
        var layout = _manager.ActiveLayout;
        foreach (var id in _windows.Keys.Except(layout.Widgets.Where(w => w.Visible).Select(w => w.Id)).ToList())
        {
            _windows[id].CloseWidget();
            _windows.Remove(id);
        }
        foreach (var w in layout.Widgets.Where(w => w.Visible))
        {
            if (_windows.TryGetValue(w.Id, out var existing)) existing.Apply(w);
            else
            {
                var vm = new WidgetRuntimeViewModel(w, _manager, App.Get<IWeatherProvider>(), App.Get<IShellLauncher>(), App.Get<IDesktopController>(),
                    App.Get<IDocumentStore>(), App.Services.GetService(typeof(ISystemMetricsProvider)) as ISystemMetricsProvider,
                    App.Services.GetService(typeof(IMediaSessionProvider)) as IMediaSessionProvider);
                var window = new WidgetWindow(vm);
                _windows[w.Id] = window;
                window.Activate();
            }
        }
    }
}

/// <summary>A single desktop widget: borderless glass window rendering live data.</summary>
public sealed class WidgetWindow : ChromelessWindow
{
    private readonly WidgetRuntimeViewModel _vm;
    private readonly DispatcherQueueTimer _timer;
    private readonly Border _root = new();
    private bool _closing;

    public WidgetWindow(WidgetRuntimeViewModel vm) : base(resizable: true, topmost: vm.Instance.AlwaysOnTop)
    {
        _vm = vm;
        Title = vm.Definition.Name;
        Content = _root;
        _root.PointerPressed += (s, e) => { if (!_vm.Instance.Locked && e.OriginalSource is Border or Grid or TextBlock) BeginDrag(s, e); };
        Apply(vm.Instance);
        _timer = DispatcherQueue.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(Math.Max(1, vm.Instance.RefreshSeconds));
        _timer.Tick += async (_, _) => { await _vm.TickAsync(); Render(); };
        _timer.Start();
        _ = FirstTickAsync();
        AppWindow.Changed += (_, e) =>
        {
            if (_closing || (!e.DidPositionChange && !e.DidSizeChange)) return;
            var p = AppWindow.Position; var s = AppWindow.Size;
            _vm.SaveBounds(p.X / Scale, p.Y / Scale, s.Width / Scale, s.Height / Scale);
        };
    }

    private async Task FirstTickAsync() { await _vm.TickAsync(); Render(); }

    public void Apply(WidgetInstance w)
    {
        _vm.UpdateInstance(w);
        var a = w.Appearance;
        var bg = ThemeResources.ToColor(a.Background);
        _root.Background = new SolidColorBrush(ThemeResources.WithAlpha(bg, a.Opacity));
        _root.CornerRadius = new CornerRadius(a.CornerRadius);
        _root.BorderBrush = a.ShowBorder ? new SolidColorBrush(ThemeResources.WithAlpha(ThemeResources.ToColor(a.TextColor), 0.12)) : null;
        _root.BorderThickness = new Thickness(a.ShowBorder ? 1 : 0);
        _root.Padding = new Thickness(16);
        SystemBackdrop = a.Blur ? new DesktopAcrylicBackdrop() : null;
        SetTopmost(w.AlwaysOnTop);
        MoveAndResize(w.X, w.Y, w.Width, w.Height);
        if (_timer is not null) _timer.Interval = TimeSpan.FromSeconds(Math.Max(1, w.RefreshSeconds));
        Render();
    }

    public void CloseWidget()
    {
        _closing = true;
        _timer.Stop();
        _vm.SaveTodos();
        Close();
    }

    private TextBlock T(string text, double size, bool bold = false, double opacity = 1) => new()
    {
        Text = text, FontSize = size, Opacity = opacity, TextWrapping = TextWrapping.Wrap,
        FontWeight = bold ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
        FontFamily = new FontFamily(_vm.Instance.Appearance.FontFamily), Foreground = new SolidColorBrush(ThemeResources.ToColor(_vm.Instance.Appearance.TextColor)),
    };

    private Brush AccentBrush => new SolidColorBrush(ThemeResources.ToColor(_vm.Instance.Appearance.AccentColor));

    private UIElement Bar(double percent) => new ProgressBar { Value = percent, Maximum = 100, Height = 6, CornerRadius = new CornerRadius(3), Foreground = AccentBrush };

    private UIElement Spark()
    {
        var line = new Microsoft.UI.Xaml.Shapes.Polyline { Stroke = AccentBrush, StrokeThickness = 2 };
        var data = _vm.Sparkline.ToList();
        var max = Math.Max(1, data.DefaultIfEmpty(0).Max());
        for (var i = 0; i < data.Count; i++) line.Points.Add(new Windows.Foundation.Point(i * 6, 40 - data[i] / max * 38));
        return new Viewbox { Stretch = Stretch.Fill, Height = 40, Child = new Canvas { Width = Math.Max(6, (data.Count - 1) * 6), Height = 40, Children = { line } } };
    }

    private void Render()
    {
        var header = HStack(8, new FontIcon { Glyph = _vm.Definition.Glyph, FontSize = 13, FontFamily = (FontFamily)Application.Current.Resources["AeIcons"], Foreground = AccentBrush },
            T(_vm.Definition.Name.ToUpperInvariant(), 10.5, opacity: 0.7));
        var body = new StackPanel { Spacing = 6 };
        body.Children.Add(header);
        if (_vm.Error is { } err) { body.Children.Add(T(err, 12, opacity: 0.8)); _root.Child = body; return; }

        switch (_vm.TypeId)
        {
            case "clock":
                body.Children.Add(HStack(6, T(_vm.Primary, 46, true), T(_vm.Tertiary, 16, opacity: 0.7)));
                body.Children.Add(T(_vm.Secondary, 14, opacity: 0.75));
                break;
            case "calendar":
            {
                body.Children.Add(T(_vm.Primary, 16, true));
                var grid = new Grid();
                for (var c = 0; c < 7; c++) grid.ColumnDefinitions.Add(new ColumnDefinition());
                for (var r = 0; r < 6; r++) grid.RowDefinitions.Add(new RowDefinition());
                for (var i = 0; i < _vm.CalendarDays.Count; i++)
                {
                    var d = _vm.CalendarDays[i];
                    var cell = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(0, 3, 0, 3), Background = d.IsToday ? AccentBrush : null,
                        Child = T(d.Day, 12, d.IsToday, d.InMonth ? 1 : 0.35) };
                    ((TextBlock)cell.Child).HorizontalAlignment = HorizontalAlignment.Center;
                    Grid.SetColumn(cell, i % 7); Grid.SetRow(cell, i / 7);
                    grid.Children.Add(cell);
                }
                body.Children.Add(grid);
                break;
            }
            case "worldclocks" or "storage" or "system":
                foreach (var row in _vm.Rows)
                {
                    body.Children.Add(Row(T(row.Label, 13, opacity: 0.8), T(row.Value, 13, true)));
                    if (_vm.TypeId != "worldclocks" && row.Percent > 0) body.Children.Add(Bar(row.Percent));
                }
                if (_vm.TypeId == "system") body.Children.Add(Spark());
                break;
            case "cpu" or "ram" or "gpu":
                body.Children.Add(T(_vm.Primary, 34, true));
                body.Children.Add(T(_vm.Secondary, 12, opacity: 0.7));
                body.Children.Add(Spark());
                break;
            case "network":
                body.Children.Add(T(_vm.Primary, 20, true));
                body.Children.Add(T(_vm.Secondary, 16, opacity: 0.8));
                body.Children.Add(Spark());
                break;
            case "battery" or "focus":
                body.Children.Add(T(_vm.Primary, 34, true));
                body.Children.Add(T(_vm.Secondary, 12, opacity: 0.7));
                body.Children.Add(Bar(_vm.Percent));
                if (_vm.TypeId == "focus")
                    body.Children.Add(HStack(8, Button(_vm.FocusRunning ? "Pause" : "Start", () => { _vm.ToggleFocusCommand.Execute(null); Render(); }, accent: true),
                        Button("Reset", () => { _vm.ResetFocusCommand.Execute(null); Render(); })));
                break;
            case "weather":
                body.Children.Add(HStack(10, new FontIcon { Glyph = _vm.Glyph.Length > 0 ? _vm.Glyph : "", FontSize = 30, FontFamily = (FontFamily)Application.Current.Resources["AeIcons"], Foreground = AccentBrush }, T(_vm.Primary, 34, true)));
                body.Children.Add(T(_vm.Secondary, 13));
                body.Children.Add(T(_vm.Tertiary, 11.5, opacity: 0.7));
                body.Children.Add(T("Weather data by Open-Meteo.com", 9.5, opacity: 0.5));
                break;
            case "countdown":
                body.Children.Add(T(_vm.Primary, 14, opacity: 0.8));
                body.Children.Add(T(_vm.Secondary, 26, true));
                body.Children.Add(T(_vm.Tertiary, 11.5, opacity: 0.6));
                break;
            case "media" or "nowplaying":
                body.Children.Add(T(_vm.Primary, 16, true));
                body.Children.Add(T(_vm.Secondary, 13, opacity: 0.8));
                if (_vm.TypeId == "media")
                    body.Children.Add(HStack(8, Button("⏮", () => _vm.PreviousCommand.Execute(null)), Button(_vm.IsPlaying ? "⏸" : "▶", () => _vm.PlayPauseCommand.Execute(null), accent: true), Button("⏭", () => _vm.NextCommand.Execute(null))));
                else body.Children.Add(T(_vm.Tertiary, 12, opacity: 0.6));
                break;
            case "launcher":
            {
                var wrap = new VariableSizedWrapGrid { Orientation = Orientation.Horizontal, ItemWidth = 84, ItemHeight = 36 };
                foreach (var app in _vm.LauncherApps)
                {
                    var path = app;
                    var b = Button(Path.GetFileNameWithoutExtension(app), () => _vm.LaunchAppCommand.Execute(path));
                    b.Margin = new Thickness(0, 0, 6, 6);
                    wrap.Children.Add(b);
                }
                body.Children.Add(wrap);
                break;
            }
            case "notes":
            {
                var box = new TextBox { Text = _vm.Notes, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Background = null, BorderThickness = new Thickness(0), MinHeight = 120 };
                box.TextChanged += (_, _) => _vm.Notes = box.Text;
                body.Children.Add(box);
                break;
            }
            case "todo":
            {
                foreach (var item in _vm.Todos)
                {
                    var todo = item;
                    var cb = new CheckBox { Content = item.Text, IsChecked = item.Done };
                    cb.Checked += (_, _) => { todo.Done = true; _vm.SaveTodos(); };
                    cb.Unchecked += (_, _) => { todo.Done = false; _vm.SaveTodos(); };
                    body.Children.Add(cb);
                }
                var add = new TextBox { PlaceholderText = "Add a task and press Enter" };
                add.KeyDown += (_, e) => { if (e.Key == Windows.System.VirtualKey.Enter) { _vm.NewTodo = add.Text; _vm.AddTodoCommand.Execute(null); Render(); } };
                body.Children.Add(add);
                body.Children.Add(Button("Clear completed", () => { _vm.ClearDoneCommand.Execute(null); Render(); }));
                break;
            }
            case "clipboard":
                body.Children.Add(Button("Open clipboard history (Win+V)", () => _vm.OpenClipboardCommand.Execute(null), accent: true));
                body.Children.Add(T("Turn it on in Windows Settings ▸ System ▸ Clipboard if nothing appears.", 11, opacity: 0.6));
                break;
        }
        _root.Child = body;
    }
}
