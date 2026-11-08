using Aether.Presentation.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Aether.App.Views;

public sealed partial class WidgetsPage : UserControl
{
    public WidgetsViewModel ViewModel { get; }
    public WidgetsPage(WidgetsViewModel vm) { ViewModel = vm; InitializeComponent(); }
    private void OnAdd(object sender, RoutedEventArgs e) => ViewModel.AddCommand.Execute(((Button)sender).Tag);
}
