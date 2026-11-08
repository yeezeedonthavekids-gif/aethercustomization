using Aether.Presentation.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Aether.App.Views;

public sealed partial class TaskbarPage : UserControl
{
    public TaskbarViewModel ViewModel { get; }
    public TaskbarPage(TaskbarViewModel vm) { ViewModel = vm; InitializeComponent(); PageFactory.Wire(Mods, vm.Related); }
    private void OnProfileClick(object sender, RoutedEventArgs e) => ViewModel.ApplyProfileCommand.Execute(((Button)sender).Tag);
}
