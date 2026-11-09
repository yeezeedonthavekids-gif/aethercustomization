namespace Aether.App.Diagnostics;
public sealed partial class T4BindFunc : Microsoft.UI.Xaml.Controls.UserControl
{
    public Aether.Presentation.ViewModels.OverviewViewModel ViewModel => App.Get<Aether.Presentation.ViewModels.OverviewViewModel>();
    public T4BindFunc() => InitializeComponent();
}
