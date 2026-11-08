using Aether.App.Services;
using Aether.Presentation;
using Aether.Presentation.Infrastructure;
using Aether.Presentation.ViewModels;
using Aether.Services.Settings;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace Aether.App.Views;

public sealed partial class MainWindow : Window
{
    private readonly Dictionary<PageKey, FrameworkElement> _pages = new();
    private readonly INavigationService _navigation;
    private readonly ISettingsService _settings;
    private PageViewModel? _currentVm;

    public ShellViewModel Shell { get; }

    public MainWindow()
    {
        Shell = App.Get<ShellViewModel>();
        _navigation = App.Get<INavigationService>();
        _settings = App.Get<ISettingsService>();
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBar);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        AppWindow.TitleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
        AppWindow.TitleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "aether.ico"));
        if (_settings.Current.UseMicaBackdrop && Microsoft.UI.Composition.SystemBackdrops.MicaController.IsSupported()) SystemBackdrop = new MicaBackdrop();
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1440, 920));
        PaletteHint.Text = _settings.Current.CommandPaletteHotkey;

        ApplySidebar();
        Shell.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(ShellViewModel.IsCompact)) ApplySidebar(); };
        Shell.Palette.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(CommandPaletteViewModel.IsOpen) && Shell.Palette.IsOpen) PaletteBox.Focus(FocusState.Programmatic);
        };
        ThemeResources.Changed += () => Root.RequestedTheme = ThemeResources.Current.IsDark ? ElementTheme.Dark : ElementTheme.Light;
        Root.RequestedTheme = ThemeResources.Current.IsDark ? ElementTheme.Dark : ElementTheme.Light;
        Root.KeyDown += OnRootKeyDown;

        _navigation.Navigated += (key, parameter) => _ = ShowPageAsync(key, parameter);
        AppWindow.Closing += OnClosing;

        var start = _settings.Current.StartupPage == StartupPage.LastVisited && Enum.TryParse<PageKey>(_settings.Current.LastPage, out var last) ? last : PageKey.Overview;
        _ = ShowPageAsync(start, null);
        if (start != PageKey.Overview) _navigation.Navigate(start);

        if (!_settings.Current.OnboardingCompleted) ShowOnboarding();
    }

    private void ApplySidebar() => SidebarColumn.Width = new GridLength(Shell.IsCompact ? 72 : 264);

    private async Task ShowPageAsync(PageKey key, object? parameter)
    {
        if (!_pages.TryGetValue(key, out var page))
        {
            page = PageFactory.Create(key);
            _pages[key] = page;
        }
        PageHost.Content = page;
        NavList.SelectedItem = Shell.NavItems.FirstOrDefault(n => n.Key == key);
        _currentVm = (PageViewModel)App.Services.GetService(PresentationServices.ViewModelFor(key))!;
        await _currentVm.ActivateAsync(parameter);
    }

    public string? CurrentPageError() => _currentVm?.ErrorMessage;

    private void OnNavClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is NavItemViewModel item) _navigation.Navigate(item.Key);
    }

    private void OnNotificationsOpened(object sender, object e) => Shell.MarkNotificationsReadCommand.Execute(null);

    private void OnRootKeyDown(object sender, KeyRoutedEventArgs e)
    {
        var ctrl = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        if (ctrl && (e.Key == Windows.System.VirtualKey.K || e.Key == Windows.System.VirtualKey.Space)) { Shell.Palette.Open(); e.Handled = true; }
        else if (e.Key == Windows.System.VirtualKey.Escape && Shell.Palette.IsOpen) { Shell.Palette.Close(); e.Handled = true; }
        else if (ctrl && e.Key >= Windows.System.VirtualKey.Number1 && e.Key <= Windows.System.VirtualKey.Number9)
        {
            var idx = e.Key - Windows.System.VirtualKey.Number1;
            if (idx < Shell.NavItems.Count) { _navigation.Navigate(Shell.NavItems[idx].Key); e.Handled = true; }
        }
    }

    private void OnPaletteKeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case Windows.System.VirtualKey.Down: Shell.Palette.MoveSelection(1); PaletteList.ScrollIntoView(PaletteList.SelectedItem); e.Handled = true; break;
            case Windows.System.VirtualKey.Up: Shell.Palette.MoveSelection(-1); PaletteList.ScrollIntoView(PaletteList.SelectedItem); e.Handled = true; break;
            case Windows.System.VirtualKey.Enter: _ = Shell.Palette.ExecuteAsync(null); e.Handled = true; break;
            case Windows.System.VirtualKey.Escape: Shell.Palette.Close(); e.Handled = true; break;
        }
    }

    private void OnPaletteItemClick(object sender, ItemClickEventArgs e) => _ = Shell.Palette.ExecuteAsync(e.ClickedItem as PaletteResult);
    private void OnPaletteBackdropTapped(object sender, TappedRoutedEventArgs e) => Shell.Palette.Close();
    private void OnPaletteTapped(object sender, TappedRoutedEventArgs e) => e.Handled = true;

    private void ShowOnboarding()
    {
        var vm = App.Get<OnboardingViewModel>();
        var view = new OnboardingView(vm);
        OnboardingHost.Content = view;
        OnboardingHost.Visibility = Visibility.Visible;
        vm.Completed += () =>
        {
            OnboardingHost.Visibility = Visibility.Collapsed;
            OnboardingHost.Content = null;
            _ = ShowPageAsync(PageKey.Overview, null);
        };
    }

    public void ShowAndFocus()
    {
        AppWindow.Show();
        if (AppWindow.Presenter is OverlappedPresenter p && p.State == OverlappedPresenterState.Minimized) p.Restore();
        Activate();
    }

    private void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (App.IsExiting || App.SmokeReport is not null) return;
        if (_settings.Current.MinimizeToTray)
        {
            args.Cancel = true;
            sender.Hide();
            return;
        }
        args.Cancel = true;
        _ = App.ExitAppAsync();
    }
}
