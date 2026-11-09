namespace Aether.App.Diagnostics;
public sealed partial class T8Icon : Microsoft.UI.Xaml.Controls.UserControl
{
    public Aether.Presentation.ViewModels.OverviewViewModel ViewModel => App.Get<Aether.Presentation.ViewModels.OverviewViewModel>();
    public T8Icon() => InitializeComponent();
}
