using Aether.Presentation.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Aether.App.Views;

public sealed partial class ApplicationsPage : UserControl
{
    public ApplicationsViewModel ViewModel { get; }
    public ApplicationsPage(ApplicationsViewModel vm) { ViewModel = vm; InitializeComponent(); }
    private static object? T(object s) => ((FrameworkElement)s).Tag;
    private void OnAppClick(object s, ItemClickEventArgs e) => ViewModel.LaunchCommand.Execute(e.ClickedItem);
    private void OnLaunch(object s, RoutedEventArgs e) => ViewModel.LaunchCommand.Execute(T(s));
    private void OnLaunchAdmin(object s, RoutedEventArgs e) => ViewModel.LaunchElevatedCommand.Execute(T(s));
    private void OnFavorite(object s, RoutedEventArgs e) => ViewModel.ToggleFavoriteCommand.Execute(T(s));
    private void OnCategory(object s, RoutedEventArgs e) => ViewModel.SetCategoryCommand.Execute(T(s));
    private void OnIcon(object s, RoutedEventArgs e) => ViewModel.SetIconCommand.Execute(T(s));
    private void OnProfile(object s, RoutedEventArgs e) => ViewModel.NewProfileCommand.Execute(T(s));
    private void OnHide(object s, RoutedEventArgs e) => ViewModel.HideCommand.Execute(T(s));
    private void OnRemoveStartup(object s, RoutedEventArgs e) => ViewModel.RemoveStartupCommand.Execute(T(s));
    private void OnLaunchProfile(object s, RoutedEventArgs e) => ViewModel.LaunchProfileCommand.Execute(T(s));
    private void OnDeleteProfile(object s, RoutedEventArgs e) => ViewModel.DeleteProfileCommand.Execute(T(s));
    private void OnLaunchGroup(object s, RoutedEventArgs e) => ViewModel.LaunchGroupCommand.Execute(T(s));
    private void OnDeleteGroup(object s, RoutedEventArgs e) => ViewModel.DeleteGroupCommand.Execute(T(s));
}
