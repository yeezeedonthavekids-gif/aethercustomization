using Aether.Core.Configuration;

namespace Aether.ThemeEngine;

/// <summary>Pure logic deciding which theme a schedule wants at a given instant and when it next changes.</summary>
public static class ThemeScheduleCalculator
{
    public static (string ThemeId, DateTimeOffset NextChange) Evaluate(ThemeSchedule schedule, DateTimeOffset now)
    {
        var (lightAt, darkAt) = Boundaries(schedule, DateOnly.FromDateTime(now.DateTime), now.Offset);
        if (now < lightAt) return (schedule.DarkThemeId, lightAt);
        if (now < darkAt) return (schedule.LightThemeId, darkAt);
        var (nextLight, _) = Boundaries(schedule, DateOnly.FromDateTime(now.DateTime).AddDays(1), now.Offset);
        return (schedule.DarkThemeId, nextLight);
    }

    private static (DateTimeOffset Light, DateTimeOffset Dark) Boundaries(ThemeSchedule s, DateOnly date, TimeSpan offset)
    {
        if (s.Mode == ScheduleMode.SunriseSunset)
        {
            var (rise, set) = SolarCalculator.Calculate(date, s.Latitude, s.Longitude, offset);
            if (rise is { } r && set is { } st) return (r, st);
        }
        var light = TimeOnly.TryParse(s.LightAt, out var l) ? l : new TimeOnly(7, 0);
        var dark = TimeOnly.TryParse(s.DarkAt, out var d) ? d : new TimeOnly(19, 0);
        return (new DateTimeOffset(date.ToDateTime(light), offset), new DateTimeOffset(date.ToDateTime(dark), offset));
    }
}
