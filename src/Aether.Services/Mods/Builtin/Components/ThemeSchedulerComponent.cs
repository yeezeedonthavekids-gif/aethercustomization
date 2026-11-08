using Aether.Core.Abstractions;
using Aether.Core.Configuration;
using Aether.ModEngine.Execution;
using Aether.ThemeEngine;

namespace Aether.Services.Mods.Builtin.Components;

/// <summary>Mod 7 — flips AppsUseLightTheme/SystemUsesLightTheme on a time or solar schedule.</summary>
public sealed class ThemeSchedulerComponent(ISystemSettings system, TimeProvider time) : LoopingComponent
{
    private string? _status;
    public override string? Status => _status;

    protected override TimeSpan Interval(ModComponentContext context) => TimeSpan.FromSeconds(30);

    public override Task StartAsync(ModComponentContext context, CancellationToken ct)
    {
        Evaluate(context);
        StartLoop(context);
        return Task.CompletedTask;
    }

    public override Task OnSettingsChangedAsync(ModComponentContext context, CancellationToken ct) { Evaluate(context); return Task.CompletedTask; }

    protected override Task TickAsync(ModComponentContext context, CancellationToken ct) { Evaluate(context); return Task.CompletedTask; }

    private void Evaluate(ModComponentContext context)
    {
        var s = context.Settings;
        var schedule = new ThemeSchedule
        {
            Enabled = true,
            Mode = s.GetString("mode") == "sun" ? ScheduleMode.SunriseSunset : ScheduleMode.FixedTimes,
            LightThemeId = "light", DarkThemeId = "dark",
            LightAt = s.GetString("lightAt", "07:00"), DarkAt = s.GetString("darkAt", "19:00"),
            Latitude = s.GetDouble("latitude"), Longitude = s.GetDouble("longitude"),
        };
        var now = time.GetLocalNow();
        var (wanted, next) = ThemeScheduleCalculator.Evaluate(schedule, now);
        var light = wanted == "light" ? 1 : 0;
        var changed = false;
        if (s.GetBool("apps", true)) changed |= SetIfDifferent(context, "AppsUseLightTheme", light);
        if (s.GetBool("system", true)) changed |= SetIfDifferent(context, "SystemUsesLightTheme", light);
        if (changed) system.BroadcastSettingChange("ImmersiveColorSet");
        _status = $"{(light == 1 ? "Light" : "Dark")} now · switches to {(light == 1 ? "dark" : "light")} at {next:HH:mm}";
    }

    private static bool SetIfDifferent(ModComponentContext context, string name, int value)
    {
        var current = context.Registry.GetValue(RegistryRoot.CurrentUser, WindowsPersonalizationApplier.PersonalizePath, name);
        if (current is { Data: int v } && v == value) return false;
        context.Registry.SetValue(RegistryRoot.CurrentUser, WindowsPersonalizationApplier.PersonalizePath, name, RegistryData.Dword(value));
        return true;
    }
}
