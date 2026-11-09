namespace Aether.App.Diagnostics;
public sealed partial class T7Record : Microsoft.UI.Xaml.Controls.UserControl
{
    public Aether.Presentation.ViewModels.OverviewViewModel ViewModel => App.Get<Aether.Presentation.ViewModels.OverviewViewModel>();
    public T7Record() => InitializeComponent();
}
