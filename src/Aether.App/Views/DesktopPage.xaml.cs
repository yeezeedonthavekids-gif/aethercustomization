using Aether.Presentation.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Aether.App.Views;

public sealed partial class DesktopPage : UserControl
{
    public DesktopViewModel ViewModel { get; }
    public DesktopPage(DesktopViewModel vm) { ViewModel = vm; InitializeComponent(); PageFactory.Wire(Mods, vm.Related); }
    private static object? Tag(object s) => ((FrameworkElement)s).Tag;
    private void OnUseLayout(object s, RoutedEventArgs e) => ViewModel.SetActiveLayoutCommand.Execute(Tag(s));
    private void OnArrange(object s, RoutedEventArgs e) => ViewModel.ArrangeCommand.Execute(Tag(s));
    private void OnDeleteLayout(object s, RoutedEventArgs e) => ViewModel.DeleteLayoutCommand.Execute(Tag(s));
    private void OnToggleRule(object s, RoutedEventArgs e) { if (((ToggleSwitch)s).IsOn != ((Aether.Services.Windows.WindowRule)Tag(s)!).Enabled) ViewModel.ToggleRuleCommand.Execute(Tag(s)); }
    private void OnDeleteRule(object s, RoutedEventArgs e) => ViewModel.DeleteRuleCommand.Execute(Tag(s));
    private void OnRestoreWorkspace(object s, RoutedEventArgs e) => ViewModel.RestoreWorkspaceCommand.Execute(Tag(s));
    private void OnDeleteWorkspace(object s, RoutedEventArgs e) => ViewModel.DeleteWorkspaceCommand.Execute(Tag(s));
    private void OnTopmost(object s, RoutedEventArgs e) => ViewModel.ToggleWindowTopmostCommand.Execute(Tag(s));
    private void OnOpacity(object s, RoutedEventArgs e) => ViewModel.SetWindowOpacityCommand.Execute(Tag(s));
}
