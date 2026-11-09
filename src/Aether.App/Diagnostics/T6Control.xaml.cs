namespace Aether.App.Diagnostics;
public sealed partial class T6Control : Microsoft.UI.Xaml.Controls.UserControl
{
    public Aether.Presentation.ViewModels.OverviewViewModel ViewModel => App.Get<Aether.Presentation.ViewModels.OverviewViewModel>();
    public T6Control() => InitializeComponent();
}
