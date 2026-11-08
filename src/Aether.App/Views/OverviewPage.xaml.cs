using Aether.Presentation.Infrastructure;
using Aether.Presentation.ViewModels;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Aether.App.Views;

public sealed partial class OverviewPage : UserControl
{
    public OverviewViewModel ViewModel { get; }

    public OverviewPage(OverviewViewModel vm)
    {
        ViewModel = vm;
        InitializeComponent();
        var interaction = App.Get<ModInteraction>();
        var nav = App.Get<INavigationService>();
        foreach (var view in new[] { ActiveCards, RecommendedCards })
        {
            view.Toggle = async (m, on) => { await interaction.SetEnabledAsync(m.Id, on); await vm.ActivateAsync(null); };
            view.Open = m => nav.Navigate(PageKey.Mods, m.Id);
            view.RunAction = m => interaction.InvokeActionAsync(m.Id, m.Mod.Manifest.Actions[0]);
        }
        ActiveCards.Mods = vm.ActiveMods;
        RecommendedCards.Mods = vm.Recommended;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(OverviewViewModel.WallpaperPath))
                WallpaperPreview.Source = vm.WallpaperPath is { } p ? new BitmapImage(new Uri(p)) { DecodePixelWidth = 500 } : null;
        };
    }
}
