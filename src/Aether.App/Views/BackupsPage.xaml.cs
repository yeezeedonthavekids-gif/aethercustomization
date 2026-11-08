using Aether.Presentation.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Aether.App.Views;

public sealed partial class BackupsPage : UserControl
{
    public BackupsViewModel ViewModel { get; }
    public BackupsPage(BackupsViewModel vm) { ViewModel = vm; InitializeComponent(); }
    private static object? T(object s) => ((FrameworkElement)s).Tag;
    private void OnRestore(object s, RoutedEventArgs e) => ViewModel.RestoreCommand.Execute(T(s));
    private void OnVerify(object s, RoutedEventArgs e) => ViewModel.ValidateCommand.Execute(T(s));
    private void OnExport(object s, RoutedEventArgs e) => ViewModel.ExportCommand.Execute(T(s));
    private void OnDelete(object s, RoutedEventArgs e) => ViewModel.DeleteCommand.Execute(T(s));
}
