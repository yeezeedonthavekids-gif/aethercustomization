using Aether.Presentation.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Aether.App.Views;

public sealed partial class MarketplacePage : UserControl
{
    public MarketplaceViewModel ViewModel { get; }
    public MarketplacePage(MarketplaceViewModel vm) { ViewModel = vm; InitializeComponent(); }
    private void OnPublisher(object s, RoutedEventArgs e) => ViewModel.ShowPublisherCommand.Execute(((FrameworkElement)s).Tag);
    private void OnSourceToggled(object s, RoutedEventArgs e)
    {
        var t = (ToggleSwitch)s;
        if (t.Tag is MarketplaceSourceView src && t.IsOn != src.Enabled) ViewModel.ToggleSourceCommand.Execute(src);
    }
    private void OnRemoveSource(object s, RoutedEventArgs e) => ViewModel.RemoveSourceCommand.Execute(((FrameworkElement)s).Tag);
}
