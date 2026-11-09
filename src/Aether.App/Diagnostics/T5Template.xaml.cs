namespace Aether.App.Diagnostics;
public sealed partial class T5Template : Microsoft.UI.Xaml.Controls.UserControl
{
    public Aether.Presentation.ViewModels.OverviewViewModel ViewModel => App.Get<Aether.Presentation.ViewModels.OverviewViewModel>();
    public T5Template() => InitializeComponent();
}
