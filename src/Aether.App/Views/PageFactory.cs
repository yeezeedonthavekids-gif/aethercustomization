using Aether.Presentation.Infrastructure;
using Aether.Presentation.ViewModels;
using Microsoft.UI.Xaml;

namespace Aether.App.Views;

/// <summary>Pages are created lazily on first navigation and then cached by the shell.</summary>
public static class PageFactory
{
    public static FrameworkElement Create(PageKey key) => key switch
    {
        PageKey.Overview => new OverviewPage(App.Get<OverviewViewModel>()),
        PageKey.Personalization => new PersonalizationPage(App.Get<PersonalizationViewModel>()),
        PageKey.Taskbar => new TaskbarPage(App.Get<TaskbarViewModel>()),
        PageKey.StartMenu => new StartMenuPage(App.Get<StartMenuViewModel>()),
        PageKey.FileExplorer => new FileExplorerPage(App.Get<FileExplorerViewModel>()),
        PageKey.Mods => new ModsPage(App.Get<ModsViewModel>()),
        PageKey.Desktop => new DesktopPage(App.Get<DesktopViewModel>()),
        PageKey.Widgets => new WidgetsPage(App.Get<WidgetsViewModel>()),
        PageKey.Themes => new ThemesPage(App.Get<ThemesViewModel>()),
        PageKey.Marketplace => new MarketplacePage(App.Get<MarketplaceViewModel>()),
        PageKey.Applications => new ApplicationsPage(App.Get<ApplicationsViewModel>()),
        PageKey.System => new SystemPage(App.Get<SystemViewModel>()),
        PageKey.Automation => new AutomationPage(App.Get<AutomationViewModel>()),
        PageKey.Backups => new BackupsPage(App.Get<BackupsViewModel>()),
        _ => new SettingsPage(App.Get<SettingsViewModel>()),
    };

    /// <summary>Wires a ModCardsView to the shared mod interaction flow.</summary>
    public static void Wire(Controls.ModCardsView view, Aether.Presentation.Items.RelatedMods related)
    {
        view.Mods = related.Items;
        view.Toggle = related.ToggleAsync;
        view.Open = m => related.ConfigureCommand.Execute(m);
        view.RunAction = m => related.RunActionCommand.ExecuteAsync(m);
    }
}
