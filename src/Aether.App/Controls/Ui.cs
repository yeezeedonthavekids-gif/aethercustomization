using Aether.App.Services;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Aether.App.Controls;

/// <summary>Small factory helpers for code-built UI, all styled from the AETHER design system.</summary>
public static class Ui
{
    public static Style Style(string key) => (Style)Application.Current.Resources[key];
    public static Brush Brush(string key) => ThemeResources.Brush(key);

    public static TextBlock Text(string text, string style = "AeBody") => new() { Text = text, Style = Style(style) };
    public static TextBlock Title(string text, double size = 15) => new() { Text = text, FontSize = size, FontWeight = FontWeights.SemiBold, Foreground = Brush("AeText"), TextWrapping = TextWrapping.Wrap };

    public static FontIcon Icon(string glyph, double size = 18, string brush = "AeText") =>
        new() { Glyph = glyph, FontSize = size, FontFamily = (FontFamily)Application.Current.Resources["AeIcons"], Foreground = Brush(brush) };

    public static Border Card(UIElement child, bool elevated = false) => new() { Style = Style(elevated ? "AeCardElevated" : "AeCard"), Child = child };

    public static Border Chip(string text, string brush = "AeAccentSoft", string foreground = "AeText") => new()
    {
        Background = Brush(brush), CornerRadius = new CornerRadius(8), Padding = new Thickness(8, 2, 8, 3),
        Child = new TextBlock { Text = text, FontSize = 11.5, Foreground = Brush(foreground) },
    };

    public static Border IconTile(string glyph, double size = 40) => new()
    {
        Width = size, Height = size, CornerRadius = new CornerRadius(size * 0.3), Background = Brush("AeAccentSoft"),
        Child = Icon(glyph, size * 0.45, "AeAccent"),
    };

    public static Button Button(string text, Action onClick, bool accent = false, string? glyph = null)
    {
        object content = text;
        if (glyph is not null)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            sp.Children.Add(Icon(glyph, 14, accent ? "AeOnAccent" : "AeText"));
            sp.Children.Add(new TextBlock { Text = text });
            content = sp;
        }
        var b = new Button { Content = content, Style = Style(accent ? "AeAccentButton" : "AeButton") };
        b.Click += (_, _) => onClick();
        return b;
    }

    public static Grid Row(UIElement left, UIElement right, double spacing = 16)
    {
        var g = new Grid { ColumnSpacing = spacing };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn((FrameworkElement)right, 1);
        ((FrameworkElement)right).VerticalAlignment = VerticalAlignment.Center;
        g.Children.Add(left);
        g.Children.Add(right);
        return g;
    }

    public static StackPanel Stack(double spacing = 8, params UIElement[] children)
    {
        var s = new StackPanel { Spacing = spacing };
        foreach (var c in children) s.Children.Add(c);
        return s;
    }

    public static StackPanel HStack(double spacing = 8, params UIElement[] children)
    {
        var s = new StackPanel { Orientation = Orientation.Horizontal, Spacing = spacing };
        foreach (var c in children) s.Children.Add(c);
        return s;
    }
}
