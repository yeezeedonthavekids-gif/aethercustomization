using Aether.Core.Configuration;
using Aether.ThemeEngine;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Aether.App.Services;

/// <summary>Pushes the active AETHER palette into the shared brushes so the whole UI restyles live.</summary>
public static class ThemeResources
{
    public static AppPalette Current { get; private set; } = BuiltinPalettes.Midnight;
    public static event Action? Changed;

    public static Color ToColor(string hex) => ColorValue.TryParse(hex, out var c) ? Color.FromArgb(c.A, c.R, c.G, c.B) : Color.FromArgb(255, 255, 0, 255);

    public static Color WithAlpha(Color c, double alpha) => Color.FromArgb((byte)Math.Clamp(alpha * 255, 0, 255), c.R, c.G, c.B);

    public static void Apply(AppPalette p, string? accentOverride = null)
    {
        if (accentOverride is { Length: > 0 } && ColorValue.TryParse(accentOverride, out _)) p = p.WithAccent(accentOverride);
        Current = p;
        var r = Application.Current.Resources;
        void Set(string key, Color color) { if (r.TryGetValue(key, out var o) && o is SolidColorBrush b) b.Color = color; }

        var accent = ToColor(p.Accent);
        Set("AeBackground", ToColor(p.Background));
        Set("AeBackground2", ToColor(p.BackgroundGradient));
        Set("AeSurface", WithAlpha(ToColor(p.Surface), p.GlassOpacity));
        Set("AeSurfaceSolid", ToColor(p.Surface));
        Set("AeSurfaceElevated", WithAlpha(ToColor(p.SurfaceElevated), Math.Min(1, p.GlassOpacity + 0.18)));
        Set("AeSurfaceHover", p.IsDark ? Color.FromArgb(0x1A, 255, 255, 255) : Color.FromArgb(0x10, 0, 0, 0));
        Set("AeBorder", ToColor(p.Border));
        Set("AeText", ToColor(p.TextPrimary));
        Set("AeTextSecondary", ToColor(p.TextSecondary));
        Set("AeTextMuted", ToColor(p.TextMuted));
        Set("AeAccent", accent);
        Set("AeAccent2", ToColor(p.AccentSecondary));
        Set("AeAccentSoft", WithAlpha(accent, 0.2));
        Set("AeOnAccent", ColorValue.Parse(p.Accent).Luminance > 0.45 ? Color.FromArgb(255, 10, 12, 20) : Color.FromArgb(255, 255, 255, 255));
        Set("AeSuccess", ToColor(p.Success));
        Set("AeWarning", ToColor(p.Warning));
        Set("AeDanger", ToColor(p.Danger));
        if (r.TryGetValue("AeAccentGradient", out var g) && g is LinearGradientBrush ag && ag.GradientStops.Count == 2)
        {
            ag.GradientStops[0].Color = accent;
            ag.GradientStops[1].Color = ToColor(p.AccentSecondary);
        }
        if (r.TryGetValue("AeBackdropGradient", out var bg) && bg is LinearGradientBrush bd && bd.GradientStops.Count == 2)
        {
            bd.GradientStops[0].Color = ToColor(p.Background);
            bd.GradientStops[1].Color = ToColor(p.BackgroundGradient);
        }
        Changed?.Invoke();
    }

    public static Brush Brush(string key) => (Brush)Application.Current.Resources[key];
}
