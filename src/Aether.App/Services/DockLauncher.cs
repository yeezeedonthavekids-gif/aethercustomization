using Aether.Presentation.Infrastructure;
using Aether.Presentation.ViewModels;
using Aether.Services.Applications;
using Aether.Services.Profiles;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using static Aether.App.Controls.Ui;

namespace Aether.App.Services;

/// <summary>Shows/hides the AETHER dock window — an independent window, never a modified Windows taskbar.</summary>
public sealed class DockHost : IDockHost
{
    private DockWindow? _window;
    public bool IsRunning => _window is not null;

    public Task ShowAsync(DockSettings settings)
    {
        var tcs = new TaskCompletionSource();
        App.Get<IUiDispatcher>().Post(() =>
        {
            var vm = App.Get<DockViewModel>();
            vm.Settings = settings;
            if (_window is null) { _window = new DockWindow(vm); _window.Activate(); }
            _window.ApplySettings();
            tcs.TrySetResult();
        });
        return tcs.Task;
    }

    public Task HideAsync()
    {
        var tcs = new TaskCompletionSource();
        App.Get<IUiDispatcher>().Post(() => { _window?.Shutdown(); _window = null; tcs.TrySetResult(); });
        return tcs.Task;
    }
}

public sealed class DockWindow : ChromelessWindow
{
    private readonly DockViewModel _vm;
    private readonly StackPanel _items = new() { Spacing = 8, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly Border _root = new();
    private readonly DispatcherQueueTimer _timer;

    public DockWindow(DockViewModel vm) : base(resizable: false, topmost: true)
    {
        _vm = vm;
        Title = "AETHER dock";
        _root.Child = _items;
        Content = _root;
        SystemBackdrop = new DesktopAcrylicBackdrop();
        _timer = DispatcherQueue.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(3);
        _timer.Tick += async (_, _) => { await _vm.RefreshAsync(); Render(); };
        _timer.Start();
        vm.Items.CollectionChanged += (_, _) => Render();
        _ = RefreshAsync();
    }

    private async Task RefreshAsync() { await _vm.RefreshAsync(); Render(); }

    public void ApplySettings()
    {
        var s = _vm.Settings;
        var vertical = s.Edge is DockEdge.Left or DockEdge.Right;
        _items.Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal;
        _root.Background = new SolidColorBrush(ThemeResources.WithAlpha(ThemeResources.ToColor(ThemeResources.Current.SurfaceElevated), s.Opacity));
        _root.CornerRadius = new CornerRadius(s.CornerRadius);
        _root.BorderBrush = Brush("AeBorder");
        _root.BorderThickness = new Thickness(1);
        _root.Padding = new Thickness(10);
        Position();
        Render();
    }

    private void Position()
    {
        var s = _vm.Settings;
        var area = Microsoft.UI.Windowing.DisplayArea.Primary.WorkArea;
        var count = Math.Max(4, _vm.Items.Count + (s.ShowStartButton ? 1 : 0) + (s.ShowClock ? 2 : 0));
        var thickness = s.IconSize + 24 + (s.Magnify ? 12 : 0);
        var length = Math.Min(count * (s.IconSize + 8) + 40, (s.Edge is DockEdge.Left or DockEdge.Right ? area.Height : area.Width) / Scale - 40);
        if (s.Style == DockStyle.Floating) length = Math.Min(area.Width / Scale - 2 * s.Margin - 200, Math.Max(length, 700));
        double w, h, x, y;
        var aw = area.Width / Scale; var ah = area.Height / Scale; var ax = area.X / Scale; var ay = area.Y / Scale;
        switch (s.Edge)
        {
            case DockEdge.Left: w = thickness; h = length; x = ax + s.Margin; y = ay + (ah - h) / 2; break;
            case DockEdge.Right: w = thickness; h = length; x = ax + aw - thickness - s.Margin; y = ay + (ah - h) / 2; break;
            case DockEdge.Top: w = length; h = thickness; x = ax + (aw - w) / 2; y = ay + s.Margin; break;
            default: w = length; h = thickness; x = ax + (aw - w) / 2; y = ay + ah - thickness - s.Margin; break;
        }
        MoveAndResize(x, y, w, h);
    }

    private void Render()
    {
        var s = _vm.Settings;
        _items.Children.Clear();
        if (s.ShowStartButton)
        {
            var start = Tile("Æ", "Open AETHER launcher", s.IconSize, true);
            start.Click += (_, _) => _vm.OpenLauncherCommand.Execute(null);
            _items.Children.Add(start);
        }
        foreach (var item in _vm.Items)
        {
            var tile = Tile(item.Initial, item.Name, s.IconSize, false);
            var captured = item;
            tile.Click += (_, _) => _vm.ActivateCommand.Execute(captured);
            var stack = new StackPanel { Spacing = 3 };
            stack.Children.Add(tile);
            stack.Children.Add(new Border { Width = 5, Height = 5, CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Center,
                Background = item.IsRunning ? Brush("AeAccent") : new SolidColorBrush(Microsoft.UI.Colors.Transparent) });
            _items.Children.Add(stack);
        }
        if (s.ShowClock) _items.Children.Add(new TextBlock { Text = _vm.Clock, VerticalAlignment = VerticalAlignment.Center, FontSize = 14, Foreground = Brush("AeText"), Margin = new Thickness(8, 0, 4, 0) });
        Position();
    }

    private Button Tile(string text, string tooltip, double size, bool accent)
    {
        var b = new Button
        {
            Width = size, Height = size, Padding = new Thickness(0), CornerRadius = new CornerRadius(size * 0.28),
            Background = accent ? Brush("AeAccentGradient") : Brush("AeSurfaceHover"), BorderThickness = new Thickness(0),
            Content = new TextBlock { Text = text, FontSize = size * 0.4, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = accent ? Brush("AeOnAccent") : Brush("AeText") },
        };
        ToolTipService.SetToolTip(b, tooltip);
        if (_vm.Settings.Magnify)
        {
            b.PointerEntered += (_, _) => { b.Width = size * 1.25; b.Height = size * 1.25; };
            b.PointerExited += (_, _) => { b.Width = size; b.Height = size; };
        }
        return b;
    }

    public void Shutdown() { _timer.Stop(); Close(); }
}

/// <summary>Opens the AETHER launcher (an independent Start-menu alternative) with the active layout.</summary>
public sealed class LauncherHost : ILauncherHost
{
    private LauncherWindow? _window;

    public void Toggle() => App.Get<IUiDispatcher>().Post(() =>
    {
        if (_window is { IsOpen: true }) { _window.HideLauncher(); return; }
        _window ??= new LauncherWindow(App.Get<LauncherViewModel>());
        _ = _window.ShowLauncherAsync();
    });

    public void Hide() => App.Get<IUiDispatcher>().Post(() => _window?.HideLauncher());

    public void Apply(StartProfile profile) { /* the launcher reads the active profile each time it opens */ }
}

public sealed class LauncherWindow : ChromelessWindow
{
    private readonly LauncherViewModel _vm;
    private readonly TextBox _search = new() { PlaceholderText = "Type to search apps", FontSize = 17, CornerRadius = new CornerRadius(12), Padding = new Thickness(14, 10, 14, 10) };
    private readonly GridView _grid = new() { SelectionMode = ListViewSelectionMode.Single, IsItemClickEnabled = true };
    private readonly StackPanel _side = new() { Spacing = 6 };
    private readonly Border _root = new();
    public bool IsOpen { get; private set; }

    public LauncherWindow(LauncherViewModel vm) : base(resizable: false, topmost: true)
    {
        _vm = vm;
        Title = "AETHER launcher";
        SystemBackdrop = new DesktopAcrylicBackdrop();
        _grid.ItemsSource = vm.Results;
        _grid.ItemClick += (_, e) => { _vm.LaunchCommand.Execute(e.ClickedItem as InstalledApp); HideLauncher(); };
        _grid.ContainerContentChanging += (_, e) =>
        {
            if (e.Item is not InstalledApp a || e.ItemContainer is not GridViewItem c) return;
            var size = _vm.Profile.IconSize;
            c.Content = new StackPanel
            {
                Width = Math.Max(90, size * 2), Spacing = 6, Padding = new Thickness(6),
                Children =
                {
                    new Border { Width = size, Height = size, CornerRadius = new CornerRadius(size * 0.28), Background = Brush("AeAccentGradient"),
                        Child = new TextBlock { Text = a.Name[..1].ToUpperInvariant(), FontSize = size * 0.42, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = Brush("AeOnAccent"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } },
                    new TextBlock { Text = a.Name, FontSize = 12, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 2, TextWrapping = TextWrapping.Wrap, Foreground = Brush("AeText") },
                },
            };
        };
        _search.TextChanged += (_, _) => _vm.Query = _search.Text;
        _search.KeyDown += (_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Enter) { _vm.LaunchCommand.Execute(null); HideLauncher(); }
            else if (e.Key == Windows.System.VirtualKey.Escape) HideLauncher();
        };
        var layout = new Grid { ColumnSpacing = 16, RowSpacing = 12 };
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition());
        layout.ColumnDefinitions.Add(new ColumnDefinition());
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
        Grid.SetColumnSpan(_search, 2);
        Grid.SetRow(_grid, 1);
        var sideScroll = new ScrollViewer { Content = _side };
        Grid.SetRow(sideScroll, 1); Grid.SetColumn(sideScroll, 1);
        layout.Children.Add(_search); layout.Children.Add(_grid); layout.Children.Add(sideScroll);
        _root.Child = layout;
        _root.Padding = new Thickness(18);
        _root.BorderBrush = Brush("AeBorder");
        _root.BorderThickness = new Thickness(1);
        Content = _root;
        Activated += (_, e) => { if (e.WindowActivationState == WindowActivationState.Deactivated && IsOpen) HideLauncher(); };
    }

    public async Task ShowLauncherAsync()
    {
        await _vm.ReloadAsync();
        var p = _vm.Profile;
        _root.CornerRadius = new CornerRadius(p.CornerRadius);
        var surface = p.Background == "accent" ? ThemeResources.ToColor(ThemeResources.Current.Accent) : ThemeResources.ToColor(ThemeResources.Current.Surface);
        _root.Background = new SolidColorBrush(ThemeResources.WithAlpha(surface, p.Background == "accent" ? 0.35 : p.Opacity));
        SystemBackdrop = p.Blur ? new DesktopAcrylicBackdrop() : null;
        _search.Visibility = p.ShowSearch ? Visibility.Visible : Visibility.Collapsed;
        _side.Children.Clear();
        if (p.ShowRecent && _vm.Recent.Count > 0)
        {
            _side.Children.Add(Text("Recent", "AeMuted"));
            foreach (var r in _vm.Recent) { var app = r; _side.Children.Add(Button(r.Name, () => { _vm.LaunchCommand.Execute(app); HideLauncher(); })); }
        }
        _side.Children.Add(Text("Shortcuts", "AeMuted"));
        foreach (var (key, label, glyph) in _vm.Shortcuts) { var k = key; _side.Children.Add(Button(label, () => { _vm.OpenShortcutCommand.Execute(k); HideLauncher(); }, glyph: glyph)); }
        if (p.ShowCategories)
        {
            _side.Children.Add(Text("Categories", "AeMuted"));
            _side.Children.Add(Button("All", () => _vm.Category = null));
            foreach (var c in _vm.Categories.Take(12)) { var cat = c; _side.Children.Add(Button(c, () => _vm.Category = cat)); }
        }
        if (p.ShowPowerButtons)
            _side.Children.Add(HStack(6, Button("Lock", () => _vm.PowerCommand.Execute("lock")), Button("Sign out", () => _vm.PowerCommand.Execute("signout"))));
        var area = Microsoft.UI.Windowing.DisplayArea.Primary.WorkArea;
        var w = Math.Min(p.Width, area.Width / Scale - 40);
        var h = Math.Min(p.Height, area.Height / Scale - 40);
        MoveAndResize(area.X / Scale + (area.Width / Scale - w) / 2, area.Y / Scale + (area.Height / Scale - h) / 2 - (p.Style == LauncherStyle.Spotlight ? 0 : 20), w, h);
        _search.Text = "";
        IsOpen = true;
        AppWindow.Show();
        Activate();
        _search.Focus(FocusState.Programmatic);
    }

    public void HideLauncher()
    {
        IsOpen = false;
        AppWindow.Hide();
    }
}
