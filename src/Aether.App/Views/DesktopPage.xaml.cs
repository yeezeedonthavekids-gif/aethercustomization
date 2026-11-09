using Aether.Presentation.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Aether.App.Views;

public sealed partial class DesktopPage : UserControl
{
    public DesktopViewModel ViewModel { get; }
    public DesktopPage(DesktopViewModel vm) { ViewModel = vm; InitializeComponent(); PageFactory.Wire(Mods, vm.Related); }
    private static object? TagOf(object s) => ((FrameworkElement)s).Tag;
    private void OnUseLayout(object s, RoutedEventArgs e) => ViewModel.SetActiveLayoutCommand.Execute(TagOf(s));
    private void OnArrange(object s, RoutedEventArgs e) => ViewModel.ArrangeCommand.Execute(TagOf(s));
    private void OnDeleteLayout(object s, RoutedEventArgs e) => ViewModel.DeleteLayoutCommand.Execute(TagOf(s));
    private void OnToggleRule(object s, RoutedEventArgs e) { if (((ToggleSwitch)s).IsOn != ((Aether.Services.Windows.WindowRule)TagOf(s)!).Enabled) ViewModel.ToggleRuleCommand.Execute(TagOf(s)); }
    private void OnDeleteRule(object s, RoutedEventArgs e) => ViewModel.DeleteRuleCommand.Execute(TagOf(s));
    private void OnRestoreWorkspace(object s, RoutedEventArgs e) => ViewModel.RestoreWorkspaceCommand.Execute(TagOf(s));
    private void OnDeleteWorkspace(object s, RoutedEventArgs e) => ViewModel.DeleteWorkspaceCommand.Execute(TagOf(s));
    private void OnTopmost(object s, RoutedEventArgs e) => ViewModel.ToggleWindowTopmostCommand.Execute(TagOf(s));
    private void OnOpacity(object s, RoutedEventArgs e) => ViewModel.SetWindowOpacityCommand.Execute(TagOf(s));
}
