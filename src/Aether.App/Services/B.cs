using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Aether.App;

/// <summary>Static helpers for x:Bind function bindings.</summary>
public static class B
{
    public static Visibility Show(bool value) => value ? Visibility.Visible : Visibility.Collapsed;
    public static Visibility Hide(bool value) => value ? Visibility.Collapsed : Visibility.Visible;
    public static Visibility ShowIf(object? value) => value is null || value is string { Length: 0 } ? Visibility.Collapsed : Visibility.Visible;
    public static Visibility HideIf(object? value) => value is null || value is string { Length: 0 } ? Visibility.Visible : Visibility.Collapsed;
    public static bool ShowCountBool(int count) => count > 0;
    public static Visibility ShowCount(int count) => count > 0 ? Visibility.Visible : Visibility.Collapsed;
    public static Visibility HideCount(int count) => count > 0 ? Visibility.Collapsed : Visibility.Visible;
    public static bool Not(bool value) => !value;
    public static SolidColorBrush Brush(string? hex) => new(Services.ThemeResources.ToColor(hex ?? "#00000000"));
    public static double Opacity(bool active) => active ? 1 : 0.55;
    public static string Format(double value, string format) => value.ToString(format);
    public static string Count(int value, string singular, string plural) => $"{value} {(value == 1 ? singular : plural)}";
    public static string Time(DateTimeOffset value) => value == default ? "—" : value.ToLocalTime().ToString("g");
    public static string Str(object? value) => value?.ToString() ?? "";
    public static ImageSource? Image(string? path, int decodeWidth)
    {
        if (string.IsNullOrEmpty(path)) return null;
        try { return new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(path)) { DecodePixelWidth = decodeWidth }; }
        catch (UriFormatException) { return null; }
    }
    public static ImageSource? ImageUri(Uri? uri) => uri is null ? null : new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(uri);
    public static Brush StatusBrush(bool good) => Services.ThemeResources.Brush(good ? "AeSuccess" : "AeWarning");
}
