namespace Aether.Services.Automation;

public enum TriggerKind { TimeOfDay, Sunrise, Sunset, ProcessStarted, ProcessStopped, BatteryBelow, PowerSourceChanged, DisplaysChanged, AppStartup, Interval, Manual }
public enum ConditionKind { TimeBetween, DaysOfWeek, OnBattery, OnAcPower, ProcessRunning, ProcessNotRunning, ActiveThemeIs }
public enum ActionKind { ApplyTheme, ApplyAppThemeOnly, SetWindowsMode, EnableMod, DisableMod, SetWallpaper, ActivateWidgetLayout, ApplyWindowLayout, ApplyTaskbarProfile, LaunchApp, Notify, Wait }

/// <summary>Flat, editor-friendly trigger description. Only the fields relevant to <see cref="Kind"/> are used.</summary>
public sealed record AutomationTrigger
{
    public TriggerKind Kind { get; init; }
    public string? Time { get; init; }            // TimeOfDay "HH:mm"
    public int OffsetMinutes { get; init; }        // Sunrise/Sunset
    public string? ProcessName { get; init; }      // Process*
    public int Percent { get; init; } = 20;        // BatteryBelow
    public bool OnBattery { get; init; } = true;   // PowerSourceChanged
    public int IntervalMinutes { get; init; } = 60;
}

public sealed record AutomationCondition
{
    public ConditionKind Kind { get; init; }
    public string? From { get; init; }             // TimeBetween
    public string? To { get; init; }
    public List<DayOfWeek> Days { get; init; } = [];
    public string? ProcessName { get; init; }
    public string? ThemeId { get; init; }
}

public sealed record AutomationAction
{
    public ActionKind Kind { get; init; }
    /// <summary>Theme id, mod id, layout id, profile id, file path or app path depending on the kind.</summary>
    public string? Target { get; init; }
    public string? Argument { get; init; }
    public string? Text { get; init; }
    public int Seconds { get; init; }
}

public sealed record AutomationRule
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string Description { get; init; } = "";
    public bool Enabled { get; init; } = true;
    /// <summary>Higher runs first when several rules fire together.</summary>
    public int Priority { get; init; }
    public List<AutomationTrigger> Triggers { get; init; } = [];
    public List<AutomationCondition> Conditions { get; init; } = [];
    public List<AutomationAction> Actions { get; init; } = [];
    public bool StopOnError { get; init; } = true;
    public int CooldownSeconds { get; init; } = 60;
    public DateTimeOffset? LastRun { get; init; }
}

public sealed record AutomationRun(string RuleId, string RuleName, DateTimeOffset At, string Trigger, bool Success, IReadOnlyList<string> Log);

/// <summary>A point-in-time view of the signals rules react to.</summary>
public sealed record AutomationSignals(DateTimeOffset Now, IReadOnlySet<string> Processes, bool? OnBattery, int? BatteryPercent, string ActiveThemeId);
