using Aether.Presentation.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Aether.App.Views;

public sealed partial class FileExplorerPage : UserControl
{
    public FileExplorerViewModel ViewModel { get; }
    public FileExplorerPage(FileExplorerViewModel vm) { ViewModel = vm; InitializeComponent(); PageFactory.Wire(Mods, vm.Related); }
    private void OnReset(object sender, RoutedEventArgs e) => ViewModel.ResetFolderCommand.Execute(((Button)sender).Tag);
}
