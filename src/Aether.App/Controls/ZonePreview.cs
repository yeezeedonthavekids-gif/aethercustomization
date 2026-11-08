using Aether.Services.Windows;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Aether.App.Controls;

/// <summary>Visual preview of a window layout's zones.</summary>
public sealed class ZonePreview : Canvas
{
    public static readonly DependencyProperty ZonesProperty =
        DependencyProperty.Register(nameof(Zones), typeof(IReadOnlyList<Zone>), typeof(ZonePreview), new PropertyMetadata(null, (d, _) => ((ZonePreview)d).Draw()));

    public IReadOnlyList<Zone>? Zones { get => (IReadOnlyList<Zone>?)GetValue(ZonesProperty); set => SetValue(ZonesProperty, value); }

    public ZonePreview() { SizeChanged += (_, _) => Draw(); Background = Ui.Brush("AeSurfaceSolid"); }

    private void Draw()
    {
        Children.Clear();
        if (Zones is null || ActualWidth <= 0) return;
        var i = 1;
        foreach (var z in Zones)
        {
            var b = new Border
            {
                Width = Math.Max(2, z.Width * ActualWidth - 4), Height = Math.Max(2, z.Height * ActualHeight - 4), CornerRadius = new CornerRadius(5),
                Background = Ui.Brush("AeAccentSoft"), BorderBrush = Ui.Brush("AeAccent"), BorderThickness = new Thickness(1),
                Child = new TextBlock { Text = (i++).ToString(), FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Foreground = Ui.Brush("AeText") },
            };
            SetLeft(b, z.X * ActualWidth + 2);
            SetTop(b, z.Y * ActualHeight + 2);
            Children.Add(b);
        }
    }
}
