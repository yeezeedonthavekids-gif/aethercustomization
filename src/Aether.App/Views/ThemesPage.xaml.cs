using Aether.Presentation.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace Aether.App.Views;

public sealed partial class ThemesPage : UserControl
{
    public ThemesViewModel ViewModel { get; }
    public ThemesPage(ThemesViewModel vm) { ViewModel = vm; InitializeComponent(); }
}
