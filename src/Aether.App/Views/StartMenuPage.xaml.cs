using Aether.Presentation.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Aether.App.Views;

public sealed partial class StartMenuPage : UserControl
{
    public StartMenuViewModel ViewModel { get; }
    public StartMenuPage(StartMenuViewModel vm) { ViewModel = vm; InitializeComponent(); PageFactory.Wire(Mods, vm.Related); }
    private void OnProfileClick(object sender, RoutedEventArgs e) => ViewModel.ActivateProfileCommand.Execute(((Button)sender).Tag);
    private void OnUnpin(object sender, RoutedEventArgs e) => ViewModel.UnpinCommand.Execute(((Button)sender).Tag);
}
