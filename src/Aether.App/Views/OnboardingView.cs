using Aether.Presentation.ViewModels;
using Aether.Services.Settings;
using Aether.ThemeEngine;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using static Aether.App.Controls.Ui;

namespace Aether.App.Views;

/// <summary>First-launch setup. Nothing in Windows is modified here; only AETHER preferences are stored.</summary>
public sealed class OnboardingView : UserControl
{
    private readonly OnboardingViewModel _vm;
    private readonly ContentControl _body = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private readonly TextBlock _stepLabel = Text("", "AeMuted");
    private readonly ProgressBar _progress = new() { Maximum = 1, Height = 4, CornerRadius = new CornerRadius(2) };
    private readonly Button _next;
    private readonly Button _back;

    public OnboardingView(OnboardingViewModel vm)
    {
        _vm = vm;
        _next = Button("Continue", () => _ = vm.NextCommand.ExecuteAsync(null), accent: true);
        _back = Button("Back", () => vm.BackCommand.Execute(null));
        _body.ContentTransitions = [new ContentThemeTransition { HorizontalOffset = 40 }];

        var card = new Border
        {
            Width = 760, MinHeight = 520, CornerRadius = new CornerRadius(24), Padding = new Thickness(40), Background = Brush("AeSurfaceElevated"),
            BorderBrush = Brush("AeBorder"), BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        var layout = new Grid { RowSpacing = 18 };
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition());
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var top = Stack(8, _stepLabel, _progress);
        Grid.SetRow(_body, 1);
        var buttons = HStack(10, _back, _next);
        buttons.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetRow(buttons, 2);
        layout.Children.Add(top); layout.Children.Add(_body); layout.Children.Add(buttons);
        card.Child = layout;
        Content = new Grid { Background = Brush("AeBackdropGradient"), Children = { card } };
        vm.PropertyChanged += (_, e) => { if (e.PropertyName is nameof(OnboardingViewModel.Step) or nameof(OnboardingViewModel.BackupResult) or nameof(OnboardingViewModel.IsWorking)) Render(); };
        Render();
    }

    private static TextBlock Big(string s) => new() { Text = s, FontSize = 30, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = Brush("AeText"), TextWrapping = TextWrapping.Wrap };

    private void Render()
    {
        _stepLabel.Text = $"Step {_vm.Step + 1} of {_vm.StepCount} · {_vm.StepTitle}";
        _progress.Value = (_vm.Step + 1.0) / _vm.StepCount;
        _back.Visibility = _vm.CanGoBack ? Visibility.Visible : Visibility.Collapsed;
        _next.Content = _vm.Step == 7 && _vm.CreateBackup && _vm.BackupResult is null ? "Create backup" : _vm.NextLabel;
        _next.IsEnabled = !_vm.IsWorking;
        _body.Content = _vm.Step switch
        {
            0 => Stack(14, IconTile("", 64), Big("Welcome to AETHER"), Text("Your Windows. Your Rules.", "AeSectionTitle"),
                Text("AETHER brings mods, themes, widgets, a launcher, automation and backups together. Every change is explained before it happens and can be undone.", "AeCaption")),
            1 => Stack(14, Big("Your system"), Text(_vm.WindowsText), Text("AETHER adapts its options to your Windows build and hides settings your system doesn't support.", "AeCaption")),
            2 => Capabilities(),
            3 => Appearance(),
            4 => Levels(),
            5 => Stack(12, Big("Optional components"),
                    Switch("Desktop widgets", "Clocks, monitors, media and more.", _vm.EnableWidgets, v => _vm.EnableWidgets = v),
                    Switch("Automation", "Rules that react to time, apps, power and displays.", _vm.EnableAutomation, v => _vm.EnableAutomation = v),
                    Switch("Marketplace", "Browse and install mods, themes and plugins.", _vm.EnableMarketplace, v => _vm.EnableMarketplace = v)),
            6 => Stack(12, Big("Startup"),
                    Switch("Start AETHER when I sign in", "Needed for live effects like taskbar transparency and widgets.", _vm.LaunchAtSignIn, v => _vm.LaunchAtSignIn = v),
                    Switch("Start minimized", "Open quietly in the notification area.", _vm.StartMinimized, v => _vm.StartMinimized = v)),
            7 => Stack(12, Big("Initial backup"),
                    Text("Create an AETHER configuration backup now, so you always have a clean starting point. This is not a Windows restore point — you can create one of those later from Backups.", "AeCaption"),
                    Switch("Create an initial backup", "Recommended.", _vm.CreateBackup, v => _vm.CreateBackup = v),
                    _vm.IsWorking ? new ProgressRing { IsActive = true, HorizontalAlignment = HorizontalAlignment.Left } : Text(_vm.BackupResult ?? "", "AeCaption")),
            _ => Stack(14, IconTile("", 64), Big("You're all set"),
                    Text("No changes have been made to Windows. Explore Windows Mods, pick a theme, or press Ctrl+Space anywhere to search everything.", "AeCaption")),
        };
    }

    private UIElement Capabilities()
    {
        var s = Stack(10, Big("What AETHER can do here"));
        foreach (var c in _vm.Capabilities)
            s.Children.Add(Card(Row(Stack(2, Title(c.Name, 14), Text(c.Detail, "AeCaption")), Icon(c.Glyph, 18, c.Available ? "AeSuccess" : "AeTextMuted"))));
        return s;
    }

    private UIElement Appearance()
    {
        var grid = new GridView { ItemsSource = _vm.Palettes, SelectedItem = _vm.SelectedPalette, SelectionMode = ListViewSelectionMode.Single };
        grid.ItemTemplate = null;
        grid.ContainerContentChanging += (_, e) =>
        {
            if (e.Item is AppPalette p && e.ItemContainer is GridViewItem item)
                item.Content = new Border
                {
                    Width = 150, Height = 96, CornerRadius = new CornerRadius(14), Padding = new Thickness(12),
                    Background = B.Brush(p.Background), BorderBrush = Brush("AeBorder"), BorderThickness = new Thickness(1),
                    Child = Stack(8, new TextBlock { Text = p.Name, Foreground = B.Brush(p.TextPrimary), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold },
                        HStack(6, new Border { Width = 26, Height = 26, CornerRadius = new CornerRadius(8), Background = B.Brush(p.Accent) },
                                  new Border { Width = 26, Height = 26, CornerRadius = new CornerRadius(8), Background = B.Brush(p.AccentSecondary) },
                                  new Border { Width = 26, Height = 26, CornerRadius = new CornerRadius(8), Background = B.Brush(p.Surface) })),
                };
        };
        grid.SelectionChanged += (_, _) => _vm.SelectedPalette = grid.SelectedItem as AppPalette;
        return Stack(12, Big("Choose a look"), Text("This styles AETHER itself. Windows isn't changed.", "AeCaption"), grid);
    }

    private UIElement Levels()
    {
        var s = Stack(10, Big("How hands-on are you?"));
        var desc = Text(_vm.LevelDescription, "AeCaption");
        var radios = new RadioButtons { ItemsSource = _vm.Levels, SelectedItem = _vm.Level };
        radios.SelectionChanged += (_, _) => { if (radios.SelectedItem is CustomizationLevel l) { _vm.Level = l; desc.Text = _vm.LevelDescription; } };
        s.Children.Add(radios);
        s.Children.Add(desc);
        return s;
    }

    private static UIElement Switch(string title, string description, bool value, Action<bool> set)
    {
        var t = new ToggleSwitch { IsOn = value, OnContent = "", OffContent = "", MinWidth = 0 };
        t.Toggled += (_, _) => set(t.IsOn);
        return Card(Row(Stack(2, Title(title, 14), Text(description, "AeCaption")), t));
    }
}
