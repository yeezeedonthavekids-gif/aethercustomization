using Aether.Presentation.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Aether.App.Views;

public sealed partial class AutomationPage : UserControl
{
    public AutomationViewModel ViewModel { get; }
    public AutomationPage(AutomationViewModel vm) { ViewModel = vm; InitializeComponent(); }
    private static object? T(object s) => ((FrameworkElement)s).Tag;
    private void OnToggle(object s, RoutedEventArgs e) { if (T(s) is RuleItem r && ((ToggleSwitch)s).IsOn != r.Enabled) ViewModel.ToggleCommand.Execute(r); }
    private void OnRun(object s, RoutedEventArgs e) => ViewModel.RunNowCommand.Execute(T(s));
    private void OnEdit(object s, RoutedEventArgs e) => ViewModel.EditCommand.Execute(T(s));
    private void OnDelete(object s, RoutedEventArgs e) => ViewModel.DeleteCommand.Execute(T(s));
}
