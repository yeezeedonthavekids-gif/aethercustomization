using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Aether.App.Services;

/// <summary>Small startup window with a progress bar, shown while services, theme and pages load.</summary>
public sealed class SplashWindow : ChromelessWindow
{
    private const double W = 440, H = 250;
    private readonly ProgressBar _bar;
    private readonly TextBlock _status;

    public SplashWindow() : base(resizable: false, topmost: false)
    {
        Title = "AETHER";
        AppWindow.IsShownInSwitchers = true;
        try { AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "aether.ico")); } catch (Exception) { }

        var accent = Color.FromArgb(255, 0x7C, 0x8C, 0xFF);
        var accent2 = Color.FromArgb(255, 0xC0, 0x7C, 0xFF);

        _status = new TextBlock
        {
            Text = "Starting…",
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 0xB4, 0xBD, 0xD8)),
        };
        _bar = new ProgressBar
        {
            Minimum = 0, Maximum = 100, Value = 0, Height = 6, CornerRadius = new CornerRadius(3),
            Foreground = new LinearGradientBrush(new GradientStopCollection { new() { Color = accent, Offset = 0 }, new() { Color = accent2, Offset = 1 } }, 0),
            Background = new SolidColorBrush(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF)),
        };

        var logo = new TextBlock
        {
            Text = "AETHER",
            FontSize = 40,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            CharacterSpacing = 300,
            Foreground = new LinearGradientBrush(new GradientStopCollection { new() { Color = accent, Offset = 0 }, new() { Color = accent2, Offset = 1 } }, 0),
        };
        var tagline = new TextBlock
        {
            Text = "Windows, your way",
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 0x6E, 0x78, 0x99)),
        };

        var content = new StackPanel { Spacing = 6, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(36, 0, 36, 0) };
        content.Children.Add(logo);
        content.Children.Add(tagline);
        content.Children.Add(new Border { Height = 28 });
        content.Children.Add(_bar);
        content.Children.Add(_status);

        Content = new Grid
        {
            Background = new LinearGradientBrush(new GradientStopCollection
            {
                new() { Color = Color.FromArgb(255, 0x07, 0x0B, 0x16), Offset = 0 },
                new() { Color = Color.FromArgb(255, 0x14, 0x12, 0x30), Offset = 1 },
            }, 45),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            Children = { content },
        };

        // Center on the primary work area.
        var area = Microsoft.UI.Windowing.DisplayArea.Primary.WorkArea;
        var s = Scale;
        MoveAndResize((area.X + (area.Width - W * s) / 2) / s, (area.Y + (area.Height - H * s) / 2) / s, W, H);
    }

    /// <summary>Updates the bar and status text, then yields so the window can repaint before the next step.</summary>
    public async Task StepAsync(double percent, string status)
    {
        _bar.Value = percent;
        _status.Text = status;
        await Task.Delay(30);
    }
}
