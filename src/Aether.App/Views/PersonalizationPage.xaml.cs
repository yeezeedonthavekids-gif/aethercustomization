using Aether.Presentation.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Aether.App.Views;

public sealed partial class PersonalizationPage : UserControl
{
    public PersonalizationViewModel ViewModel { get; }
    public PersonalizationPage(PersonalizationViewModel vm)
    {
        ViewModel = vm;
        InitializeComponent();
        PageFactory.Wire(Mods, vm.Related);
    }

    private void OnSwatchClick(object sender, RoutedEventArgs e) => ViewModel.ApplyAccentCommand.Execute((string)((Button)sender).Tag);
    private void OnWallpaperClick(object sender, ItemClickEventArgs e) => ViewModel.SetWallpaperCommand.Execute(e.ClickedItem);
    private void OnAllMonitorsClick(object sender, RoutedEventArgs e) => ViewModel.SetWallpaperAllCommand.Execute(((Button)sender).Tag);
    private void OnOnlineClick(object sender, ItemClickEventArgs e) => ViewModel.UseOnlineCommand.Execute(e.ClickedItem);
}
