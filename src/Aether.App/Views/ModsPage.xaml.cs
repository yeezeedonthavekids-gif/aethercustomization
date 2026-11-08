using Aether.ModEngine.Manifest;
using Aether.Presentation.Infrastructure;
using Aether.Presentation.Items;
using Aether.Presentation.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Aether.App.Views;

public sealed partial class ModsPage : UserControl
{
    public ModsViewModel ViewModel { get; }

    public ModsPage(ModsViewModel vm)
    {
        ViewModel = vm;
        InitializeComponent();
        var dialogs = App.Get<IDialogService>();
        Form.PickPath = folder => folder ? dialogs.PickFolderAsync() : dialogs.PickFileAsync();
    }

    private async void OnToggle(object sender, RoutedEventArgs e)
    {
        var t = (ToggleSwitch)sender;
        if (t.Tag is ModItemViewModel m && t.IsOn != m.IsEnabled)
        {
            await ViewModel.ToggleAsync(m, t.IsOn);
            t.IsOn = m.IsEnabled;
        }
    }

    private void OnAction(object sender, RoutedEventArgs e) => ViewModel.InvokeActionCommand.Execute(((Button)sender).Tag as ModAction);
    private void OnRestoreVersion(object sender, RoutedEventArgs e) => ViewModel.RestoreVersionCommand.Execute(((Button)sender).Tag as string);
}
