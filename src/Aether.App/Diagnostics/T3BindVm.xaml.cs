namespace Aether.App.Diagnostics;
public sealed partial class T3BindVm : Microsoft.UI.Xaml.Controls.UserControl
{
    public Aether.Presentation.ViewModels.OverviewViewModel ViewModel => App.Get<Aether.Presentation.ViewModels.OverviewViewModel>();
    public T3BindVm() => InitializeComponent();
}
