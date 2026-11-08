using Aether.Core.Abstractions;
using Aether.Core.Configuration;

namespace Aether.ThemeEngine;

/// <summary>Captured values of the Windows personalization registry keys, used for theme rollback.</summary>
public sealed record PersonalizationState(int? AppsUseLightTheme, int? SystemUsesLightTheme, int? EnableTransparency, int? ColorPrevalence,
    int? DwmColorPrevalence, int? AccentColor, int? AccentColorMenu, int? ColorizationColor, string? AccentPalette);

/// <summary>
/// Applies <see cref="WindowsPersonalization"/> through the same per-user registry values the Windows Settings app
/// writes (HKCU\...\Themes\Personalize, HKCU\Software\Microsoft\Windows\DWM, ...\Explorer\Accent) and broadcasts
/// ImmersiveColorSet so open apps and the shell pick the change up.
/// </summary>
public sealed class WindowsPersonalizationApplier(IRegistryAccess registry, ISystemSettings system)
{
    public const string PersonalizePath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    public const string DwmPath = @"Software\Microsoft\Windows\DWM";
    public const string AccentPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent";

    public PersonalizationState Capture()
    {
        int? D(string path, string name) => registry.GetValue(RegistryRoot.CurrentUser, path, name) is { Kind: RegistryDataKind.DWord } v ? (int)v.Data : null;
        var palette = registry.GetValue(RegistryRoot.CurrentUser, AccentPath, "AccentPalette");
        return new PersonalizationState(
            D(PersonalizePath, "AppsUseLightTheme"), D(PersonalizePath, "SystemUsesLightTheme"), D(PersonalizePath, "EnableTransparency"),
            D(PersonalizePath, "ColorPrevalence"), D(DwmPath, "ColorPrevalence"), D(DwmPath, "AccentColor"), D(AccentPath, "AccentColorMenu"),
            D(DwmPath, "ColorizationColor"), palette?.Data is byte[] b ? Convert.ToBase64String(b) : null);
    }

    public void Apply(WindowsPersonalization p)
    {
        void Set(string path, string name, int value) => registry.SetValue(RegistryRoot.CurrentUser, path, name, RegistryData.Dword(value));

        if (p.AppsMode != ThemeMode.Unchanged) Set(PersonalizePath, "AppsUseLightTheme", p.AppsMode == ThemeMode.Light ? 1 : 0);
        if (p.SystemMode != ThemeMode.Unchanged) Set(PersonalizePath, "SystemUsesLightTheme", p.SystemMode == ThemeMode.Light ? 1 : 0);
        if (p.Transparency is { } t) Set(PersonalizePath, "EnableTransparency", t ? 1 : 0);
        if (p.AccentOnTaskbarAndStart is { } a) Set(PersonalizePath, "ColorPrevalence", a ? 1 : 0);
        if (p.AccentOnTitleBars is { } tb) Set(DwmPath, "ColorPrevalence", tb ? 1 : 0);
        if (p.AccentColor is { } hex && ColorValue.TryParse(hex, out var accent)) ApplyAccent(accent);
        system.BroadcastSettingChange("ImmersiveColorSet");
    }

    public void ApplyAccent(ColorValue accent)
    {
        var opaque = accent.WithAlpha(0xFF);
        registry.SetValue(RegistryRoot.CurrentUser, DwmPath, "AccentColor", RegistryData.Dword(opaque.ToAbgr()));
        registry.SetValue(RegistryRoot.CurrentUser, DwmPath, "ColorizationColor",
            RegistryData.Dword(unchecked((int)(0xC4000000u | (uint)opaque.R << 16 | (uint)opaque.G << 8 | opaque.B))));
        registry.SetValue(RegistryRoot.CurrentUser, AccentPath, "AccentColorMenu", RegistryData.Dword(opaque.ToAbgr()));
        registry.SetValue(RegistryRoot.CurrentUser, AccentPath, "StartColorMenu", RegistryData.Dword(opaque.Lerp(new ColorValue(255, 0, 0, 0), 0.25).ToAbgr()));
        registry.SetValue(RegistryRoot.CurrentUser, AccentPath, "AccentPalette", RegistryData.Bin(BuildAccentPalette(opaque)));
    }

    /// <summary>Eight RGBA swatches: three lighter, the accent, three darker and a neutral, as Windows expects.</summary>
    public static byte[] BuildAccentPalette(ColorValue accent)
    {
        var white = new ColorValue(255, 255, 255, 255);
        var black = new ColorValue(255, 0, 0, 0);
        ColorValue[] swatches =
        [
            accent.Lerp(white, 0.6), accent.Lerp(white, 0.4), accent.Lerp(white, 0.2), accent,
            accent.Lerp(black, 0.2), accent.Lerp(black, 0.4), accent.Lerp(black, 0.6), new ColorValue(255, 0x88, 0x88, 0x88),
        ];
        var bytes = new byte[32];
        for (var i = 0; i < swatches.Length; i++)
        {
            bytes[i * 4] = swatches[i].R;
            bytes[i * 4 + 1] = swatches[i].G;
            bytes[i * 4 + 2] = swatches[i].B;
            bytes[i * 4 + 3] = 0;
        }
        return bytes;
    }

    public void Restore(PersonalizationState s)
    {
        void Put(string path, string name, int? value)
        {
            if (value is { } v) registry.SetValue(RegistryRoot.CurrentUser, path, name, RegistryData.Dword(v));
            else registry.DeleteValue(RegistryRoot.CurrentUser, path, name);
        }
        Put(PersonalizePath, "AppsUseLightTheme", s.AppsUseLightTheme);
        Put(PersonalizePath, "SystemUsesLightTheme", s.SystemUsesLightTheme);
        Put(PersonalizePath, "EnableTransparency", s.EnableTransparency);
        Put(PersonalizePath, "ColorPrevalence", s.ColorPrevalence);
        Put(DwmPath, "ColorPrevalence", s.DwmColorPrevalence);
        Put(DwmPath, "AccentColor", s.AccentColor);
        Put(DwmPath, "ColorizationColor", s.ColorizationColor);
        Put(AccentPath, "AccentColorMenu", s.AccentColorMenu);
        if (s.AccentPalette is { } p) registry.SetValue(RegistryRoot.CurrentUser, AccentPath, "AccentPalette", RegistryData.Bin(Convert.FromBase64String(p)));
        system.BroadcastSettingChange("ImmersiveColorSet");
    }
}
