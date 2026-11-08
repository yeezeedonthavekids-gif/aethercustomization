using Aether.Presentation.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Aether.App.Controls.Ui;

namespace Aether.App.Views;

public sealed partial class SystemPage : UserControl
{
    public SystemViewModel ViewModel { get; }

    public SystemPage(SystemViewModel vm)
    {
        ViewModel = vm;
        InitializeComponent();
        vm.InfoRows.CollectionChanged += (_, _) => RenderInfo();
        RenderInfo();
    }

    // Tuples can't be x:Bind targets, so the info grid is built in code.
    private void RenderInfo()
    {
        var panel = new StackPanel { Spacing = 6 };
        foreach (var (label, value) in ViewModel.InfoRows)
        {
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            var l = Text(label, "AeCaption");
            var v = Text(value);
            Grid.SetColumn(v, 1);
            row.Children.Add(l); row.Children.Add(v);
            panel.Children.Add(row);
        }
        InfoList.Items.Clear();
        InfoList.Items.Add(panel);
    }
}
