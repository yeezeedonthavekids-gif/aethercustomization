using System.Collections.ObjectModel;
using Aether.Core.Abstractions;
using Aether.ModEngine.Lifecycle;
using Aether.Presentation.Infrastructure;
using Aether.Presentation.Items;
using Aether.Services.Applications;
using Aether.Services.Automation;
using Aether.Services.Platform;
using Aether.Services.Tweaks;
using Aether.ThemeEngine;
using Aether.WidgetEngine;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Aether.Presentation.ViewModels;

public sealed record SystemCategory(TweakArea Area, string Title, string Glyph, string Description, TweakGroupViewModel Group);

public sealed partial class SystemViewModel : PageViewModel
{
    private readonly TweakService _tweaks;
    private readonly ISystemInfoProvider _info;
    private readonly PowerPlanService _power;
    private readonly ISystemMetricsProvider? _metrics;

    public SystemViewModel(TweakService tweaks, ISystemInfoProvider info, PowerPlanService power, INotificationSink notifications, ISystemMetricsProvider? metrics = null) : base(notifications)
    {
        _tweaks = tweaks; _info = info; _power = power; _metrics = metrics;
        (TweakArea, string, string, string)[] defs =
        [
            (TweakArea.Performance, "Performance", "", "Responsiveness settings. None of these promise higher frame rates."),
            (TweakArea.Privacy, "Privacy", "", "Advertising, suggestions and data used for personalization."),
            (TweakArea.Notifications, "Notifications", "", "Banners, sounds and Windows tips."),
            (TweakArea.Accessibility, "Accessibility", "", "Text size and keyboard helpers."),
            (TweakArea.WindowsBehavior, "Windows behavior", "", "Snapping, shaking and keyboard state."),
            (TweakArea.Gaming, "Gaming", "", "Game Mode, background recording and Game Bar."),
            (TweakArea.Storage, "Storage", "", "Automatic cleanup."),
        ];
        Categories = defs.Select(d => new SystemCategory(d.Item1, d.Item2, d.Item3, d.Item4, new TweakGroupViewModel(tweaks, d.Item1, r => Report(r)))).ToList();
        SelectedCategory = Categories[0];
    }

    public IReadOnlyList<SystemCategory> Categories { get; }
    public ObservableCollection<PowerPlan> PowerPlans { get; } = [];
    public ObservableCollection<(string Label, string Value)> InfoRows { get; } = [];

    [ObservableProperty] private SystemCategory? _selectedCategory;
    [ObservableProperty] private PowerPlan? _activePlan;
    [ObservableProperty] private SystemSummary? _summary;
    [ObservableProperty] private string? _powerError;
    private bool _suppressPlan;

    protected override async Task LoadAsync()
    {
        Summary = await Task.Run(() => { try { return _info.GetSummary(); } catch (Exception) { return null; } });
        InfoRows.Clear();
        if (Summary is { } s)
        {
            InfoRows.Add(("Windows", $"{s.OsName} {s.OsVersion}"));
            InfoRows.Add(("Build", s.Build));
            InfoRows.Add(("Edition", s.Edition));
            InfoRows.Add(("Processor", $"{s.Cpu} ({s.LogicalCores} threads)"));
            InfoRows.Add(("Memory", $"{s.TotalMemoryBytes / 1024d / 1024 / 1024:0.#} GB"));
            InfoRows.Add(("Graphics", s.Gpus.Count == 0 ? "Unknown" : string.Join(", ", s.Gpus)));
            InfoRows.Add(("Architecture", s.Architecture));
            InfoRows.Add(("Device", s.MachineName));
            InfoRows.Add(("Signed in as", $"{s.UserName}{(s.IsAdministrator ? " (administrator)" : "")}"));
            InfoRows.Add(("Uptime", $"{(int)s.Uptime.TotalDays}d {s.Uptime.Hours}h {s.Uptime.Minutes}m"));
        }
        if (_metrics?.GetBattery() is { Present: true } b) InfoRows.Add(("Battery", $"{b.Percent}%{(b.Charging ? " (charging)" : b.OnAcPower ? " (plugged in)" : "")}"));
        await LoadPowerAsync();
    }

    protected override Task RefreshAsync()
    {
        foreach (var c in Categories) c.Group.Refresh();
        return Task.CompletedTask;
    }

    protected override void OnActivated(object? parameter)
    {
        if (parameter is string id && TweakCatalog.All.FirstOrDefault(t => t.Id == id) is { } t)
            SelectedCategory = Categories.FirstOrDefault(c => c.Area == t.Area) ?? SelectedCategory;
    }

    private async Task LoadPowerAsync()
    {
        try
        {
            var plans = await _power.GetPlansAsync();
            _suppressPlan = true;
            PowerPlans.Clear();
            foreach (var p in plans) PowerPlans.Add(p);
            ActivePlan = PowerPlans.FirstOrDefault(p => p.IsActive);
            PowerError = null;
        }
        catch (Exception ex) { PowerError = $"Power plans are unavailable: {ex.Message}"; }
        finally { _suppressPlan = false; }
    }

    partial void OnActivePlanChanged(PowerPlan? value)
    {
        if (_suppressPlan || value is null || value.IsActive) return;
        _ = RunAsync(async () =>
        {
            await _power.SetActiveAsync(value.Id);
            Toast("Power plan", $"'{value.Name}' is now active.");
            await LoadPowerAsync();
        }, "Couldn't change the power plan");
    }

    [RelayCommand]
    private Task RestoreCategoryAsync() => SelectedCategory is null ? Task.CompletedTask : RunAsync(SelectedCategory.Group.RestoreAllAsync);
}

public sealed record RuleItem(AutomationRule Rule)
{
    public string Name => Rule.Name;
    public bool Enabled => Rule.Enabled;
    public string TriggerText => string.Join(" or ", Rule.Triggers.Select(AutomationEngine.Describe));
    public string ActionText => string.Join(", then ", Rule.Actions.Select(a => a.Kind.ToString()));
    public string LastRunText => Rule.LastRun is { } r ? $"Last ran {r.ToLocalTime():g}" : "Never ran";
    public int Priority => Rule.Priority;
}

public sealed partial class EditableTrigger : ObservableObject
{
    [ObservableProperty] private TriggerKind _kind = TriggerKind.TimeOfDay;
    [ObservableProperty] private string _time = "19:00";
    [ObservableProperty] private double _offset;
    [ObservableProperty] private string _process = "";
    [ObservableProperty] private double _percent = 20;
    [ObservableProperty] private bool _onBattery = true;
    [ObservableProperty] private double _interval = 60;
    public AutomationTrigger ToModel() => new() { Kind = Kind, Time = Time, OffsetMinutes = (int)Offset, ProcessName = Process, Percent = (int)Percent, OnBattery = OnBattery, IntervalMinutes = (int)Interval };
    public static EditableTrigger From(AutomationTrigger t) => new() { Kind = t.Kind, Time = t.Time ?? "19:00", Offset = t.OffsetMinutes, Process = t.ProcessName ?? "", Percent = t.Percent, OnBattery = t.OnBattery, Interval = t.IntervalMinutes };
    public string Summary => AutomationEngine.Describe(ToModel());
    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e) { base.OnPropertyChanged(e); if (e.PropertyName != nameof(Summary)) base.OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(Summary))); }
}

public sealed partial class EditableCondition : ObservableObject
{
    [ObservableProperty] private ConditionKind _kind = ConditionKind.TimeBetween;
    [ObservableProperty] private string _fromTime = "09:00";
    [ObservableProperty] private string _toTime = "17:00";
    [ObservableProperty] private string _days = "Mon,Tue,Wed,Thu,Fri";
    [ObservableProperty] private string _process = "";
    [ObservableProperty] private string _themeId = "";
    public AutomationCondition ToModel() => new()
    {
        Kind = Kind, From = FromTime, To = ToTime, ProcessName = Process, ThemeId = ThemeId,
        Days = Days.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(d => Enum.GetValues<DayOfWeek>().FirstOrDefault(x => x.ToString().StartsWith(d, StringComparison.OrdinalIgnoreCase), (DayOfWeek)(-1)))
            .Where(d => (int)d >= 0).ToList(),
    };
    public static EditableCondition From(AutomationCondition c) => new()
    { Kind = c.Kind, FromTime = c.From ?? "09:00", ToTime = c.To ?? "17:00", Days = string.Join(",", c.Days.Select(d => d.ToString()[..3])), Process = c.ProcessName ?? "", ThemeId = c.ThemeId ?? "" };
}

public sealed partial class EditableAction : ObservableObject
{
    [ObservableProperty] private ActionKind _kind = ActionKind.ApplyTheme;
    [ObservableProperty] private string _target = "";
    [ObservableProperty] private string _argument = "";
    [ObservableProperty] private string _text = "";
    [ObservableProperty] private double _seconds = 5;
    public AutomationAction ToModel() => new() { Kind = Kind, Target = Target, Argument = string.IsNullOrWhiteSpace(Argument) ? null : Argument, Text = Text, Seconds = (int)Seconds };
    public static EditableAction From(AutomationAction a) => new() { Kind = a.Kind, Target = a.Target ?? "", Argument = a.Argument ?? "", Text = a.Text ?? "", Seconds = a.Seconds };
}

public sealed partial class AutomationViewModel : PageViewModel
{
    private readonly AutomationEngine _engine;
    private readonly IDialogService _dialogs;
    private readonly ThemeManager _themes;
    private readonly ModManager _mods;
    private readonly WidgetManager _widgets;

    public AutomationViewModel(AutomationEngine engine, IDialogService dialogs, ThemeManager themes, ModManager mods, WidgetManager widgets, IUiDispatcher ui, INotificationSink notifications) : base(notifications)
    {
        _engine = engine; _dialogs = dialogs; _themes = themes; _mods = mods; _widgets = widgets;
        engine.RuleRan += _ => ui.Post(Reload);
        engine.RulesChanged += () => ui.Post(Reload);
    }

    public ObservableCollection<RuleItem> Rules { get; } = [];
    public ObservableCollection<AutomationRun> History { get; } = [];
    public ObservableCollection<EditableTrigger> Triggers { get; } = [];
    public ObservableCollection<EditableCondition> Conditions { get; } = [];
    public ObservableCollection<EditableAction> Actions { get; } = [];
    public IReadOnlyList<TriggerKind> TriggerKinds { get; } = Enum.GetValues<TriggerKind>().Where(k => k != TriggerKind.Manual).ToList();
    public IReadOnlyList<ConditionKind> ConditionKinds { get; } = Enum.GetValues<ConditionKind>();
    public IReadOnlyList<ActionKind> ActionKinds { get; } = Enum.GetValues<ActionKind>();
    /// <summary>Hints for action targets (theme ids, mod ids, layout ids).</summary>
    public ObservableCollection<string> TargetSuggestions { get; } = [];

    [ObservableProperty] private RuleItem? _selected;
    [ObservableProperty] private bool _isEditing;
    [ObservableProperty] private string _editName = "";
    [ObservableProperty] private string _editDescription = "";
    [ObservableProperty] private double _editPriority;
    [ObservableProperty] private double _editCooldown = 60;
    [ObservableProperty] private bool _editStopOnError = true;
    private string? _editingId;

    public bool IsEmpty => Rules.Count == 0;

    protected override Task LoadAsync()
    {
        Reload();
        foreach (var t in _themes.GetThemes()) TargetSuggestions.Add(t.Id);
        foreach (var m in _mods.GetMods()) TargetSuggestions.Add(m.Id);
        foreach (var l in _widgets.GetLayouts()) TargetSuggestions.Add(l.Id);
        foreach (var w in Aether.Services.Windows.BuiltinLayouts.All) TargetSuggestions.Add(w.Id);
        return Task.CompletedTask;
    }

    private void Reload()
    {
        Rules.Clear();
        foreach (var r in _engine.GetRules()) Rules.Add(new RuleItem(r));
        History.Clear();
        foreach (var h in _engine.GetHistory(50)) History.Add(h);
        OnPropertyChanged(nameof(IsEmpty));
    }

    [RelayCommand]
    private void NewRule(string? template)
    {
        _editingId = null;
        EditName = "New rule"; EditDescription = ""; EditPriority = 0; EditCooldown = 60; EditStopOnError = true;
        Triggers.Clear(); Conditions.Clear(); Actions.Clear();
        switch (template)
        {
            case "gaming":
                EditName = "Gaming mode";
                Triggers.Add(new EditableTrigger { Kind = TriggerKind.ProcessStarted, Process = "game.exe" });
                Actions.Add(new EditableAction { Kind = ActionKind.EnableMod, Target = "aether.gaming-profile" });
                break;
            case "work":
                EditName = "Work hours";
                Triggers.Add(new EditableTrigger { Kind = TriggerKind.TimeOfDay, Time = "09:00" });
                Conditions.Add(new EditableCondition { Kind = ConditionKind.DaysOfWeek });
                Actions.Add(new EditableAction { Kind = ActionKind.ApplyTheme, Target = "nord" });
                break;
            case "sunset":
                EditName = "Evening look";
                Triggers.Add(new EditableTrigger { Kind = TriggerKind.Sunset });
                Actions.Add(new EditableAction { Kind = ActionKind.SetWindowsMode, Target = "dark" });
                break;
            case "battery":
                EditName = "Battery saver look";
                Triggers.Add(new EditableTrigger { Kind = TriggerKind.BatteryBelow, Percent = 20 });
                Actions.Add(new EditableAction { Kind = ActionKind.DisableMod, Target = "aether.taskbar-transparency" });
                Actions.Add(new EditableAction { Kind = ActionKind.Notify, Target = "Battery low", Text = "Visual effects reduced to save power." });
                break;
            default:
                Triggers.Add(new EditableTrigger());
                Actions.Add(new EditableAction());
                break;
        }
        IsEditing = true;
    }

    [RelayCommand]
    private void Edit(RuleItem? item)
    {
        if ((item ?? Selected) is not { } r) return;
        _editingId = r.Rule.Id;
        EditName = r.Rule.Name; EditDescription = r.Rule.Description; EditPriority = r.Rule.Priority; EditCooldown = r.Rule.CooldownSeconds; EditStopOnError = r.Rule.StopOnError;
        Triggers.Clear(); foreach (var t in r.Rule.Triggers) Triggers.Add(EditableTrigger.From(t));
        Conditions.Clear(); foreach (var c in r.Rule.Conditions) Conditions.Add(EditableCondition.From(c));
        Actions.Clear(); foreach (var a in r.Rule.Actions) Actions.Add(EditableAction.From(a));
        IsEditing = true;
    }

    [RelayCommand] private void AddTrigger() => Triggers.Add(new EditableTrigger());
    [RelayCommand] private void RemoveTrigger(EditableTrigger? t) { if (t is not null) Triggers.Remove(t); }
    [RelayCommand] private void AddCondition() => Conditions.Add(new EditableCondition());
    [RelayCommand] private void RemoveCondition(EditableCondition? c) { if (c is not null) Conditions.Remove(c); }
    [RelayCommand] private void AddAction() => Actions.Add(new EditableAction());
    [RelayCommand] private void RemoveAction(EditableAction? a) { if (a is not null) Actions.Remove(a); }
    [RelayCommand] private void MoveActionUp(EditableAction? a) { if (a is not null && Actions.IndexOf(a) is > 0 and var i) Actions.Move(i, i - 1); }
    [RelayCommand] private void CancelEdit() => IsEditing = false;

    [RelayCommand]
    private void Save()
    {
        try
        {
            var existing = _editingId is null ? null : _engine.GetRules().FirstOrDefault(r => r.Id == _editingId);
            _engine.Save(new AutomationRule
            {
                Id = _editingId ?? "rule-" + Guid.NewGuid().ToString("N")[..8], Name = EditName.Trim(), Description = EditDescription,
                Priority = (int)EditPriority, CooldownSeconds = (int)EditCooldown, StopOnError = EditStopOnError, Enabled = existing?.Enabled ?? true,
                LastRun = existing?.LastRun,
                Triggers = Triggers.Select(t => t.ToModel()).ToList(), Conditions = Conditions.Select(c => c.ToModel()).ToList(), Actions = Actions.Select(a => a.ToModel()).ToList(),
            });
            IsEditing = false;
            Toast("Rule saved", $"'{EditName}' is ready.");
        }
        catch (ArgumentException ex) { Toast("Can't save rule", ex.Message, NotificationSeverity.Warning); }
    }

    [RelayCommand]
    private void Toggle(RuleItem? item) { if (item is not null) _engine.SetEnabled(item.Rule.Id, !item.Enabled); }

    [RelayCommand]
    private Task RunNowAsync(RuleItem? item) => (item ?? Selected) is not { } r ? Task.CompletedTask : RunAsync(async () =>
    {
        var run = await _engine.RunAsync(r.Rule, "Run now", ignoreCooldown: true);
        Toast(r.Name, string.Join("\n", run.Log), run.Success ? NotificationSeverity.Success : NotificationSeverity.Error);
    });

    [RelayCommand]
    private async Task DeleteAsync(RuleItem? item)
    {
        if ((item ?? Selected) is not { } r) return;
        if (await _dialogs.ConfirmAsync($"Delete '{r.Name}'?", "The rule stops running.", "Delete", destructive: true)) _engine.Delete(r.Rule.Id);
    }

    [RelayCommand] private void ClearHistory() => _engine.ClearHistory();
}
