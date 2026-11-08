using Aether.ModEngine.Manifest;
using Aether.Services.Tweaks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Aether.Presentation.Items;

/// <summary>A native Windows setting row with full disclosure: what, why, side effects, admin/restart, revert.</summary>
public sealed partial class TweakItemViewModel : ObservableObject
{
    private readonly Func<TweakItemViewModel, string, Task> _apply;
    private readonly Func<TweakItemViewModel, Task> _restore;
    private bool _suppress;

    public TweakItemViewModel(TweakState state, Func<TweakItemViewModel, string, Task> apply, Func<TweakItemViewModel, Task> restore)
    {
        _apply = apply;
        _restore = restore;
        Options = state.Definition.Options.Select(o => new OptionItem(o.Value, o.Label)).ToList();
        Update(state);
    }

    public TweakDefinition Definition { get; private set; } = null!;
    public IReadOnlyList<OptionItem> Options { get; }
    public string Id => Definition.Id;
    public string Title => Definition.Title;
    public string Why => Definition.Why;
    public string WhatItChanges => Definition.WhatItChanges;
    public string SideEffects => Definition.SideEffects;
    public string Revert => Definition.Revert;
    public string? Group => Definition.Group;
    public bool IsToggle => Definition.IsToggle;
    public bool IsChoice => !Definition.IsToggle;
    public bool RequiresAdmin => Definition.RequiresAdmin;
    public string RestartText => Definition.Restart switch
    {
        RestartRequirement.Explorer => "Explorer restart",
        RestartRequirement.SignOut => "Sign out to apply",
        RestartRequirement.Reboot => "Restart to apply",
        _ => "Applies immediately",
    };
    public bool NeedsRestart => Definition.Restart != RestartRequirement.None;
    public string AdminText => Definition.RequiresAdmin ? "Administrator required" : "No admin needed";
    public string Details => $"What it changes: {WhatItChanges}\nWhy: {Why}\nSide effects: {SideEffects}\n{AdminText} · {RestartText}\nHow to revert: {Revert}";

    [ObservableProperty] private bool _isModified;
    [ObservableProperty] private bool _isCompatible;
    [ObservableProperty] private string? _note;
    [ObservableProperty] private bool _isWorking;
    [ObservableProperty] private string? _currentOption;

    public bool HasNote => !string.IsNullOrEmpty(Note);
    public bool IsUnknownValue => CurrentOption is null;

    public bool IsOn
    {
        get => CurrentOption == "on";
        set { if (!_suppress && value != IsOn) _ = _apply(this, value ? "on" : "off"); }
    }

    public OptionItem? SelectedOption
    {
        get => Options.FirstOrDefault(o => o.Value == CurrentOption);
        set { if (!_suppress && value is not null && value.Value != CurrentOption) _ = _apply(this, value.Value); }
    }

    [RelayCommand]
    private Task RestoreAsync() => _restore(this);

    public void Update(TweakState state)
    {
        _suppress = true;
        Definition = state.Definition;
        CurrentOption = state.CurrentOption;
        IsModified = state.IsModified;
        IsCompatible = state.IsCompatible;
        Note = state.CompatibilityNote;
        OnPropertyChanged(nameof(IsOn));
        OnPropertyChanged(nameof(SelectedOption));
        OnPropertyChanged(nameof(HasNote));
        OnPropertyChanged(nameof(IsUnknownValue));
        _suppress = false;
    }
}

/// <summary>Builds tweak rows for an area and keeps them in sync with the tweak service.</summary>
public sealed class TweakGroupViewModel
{
    private readonly TweakService _tweaks;
    private readonly Action<Aether.Core.Diagnostics.OperationResult> _report;

    public TweakGroupViewModel(TweakService tweaks, TweakArea area, Action<Aether.Core.Diagnostics.OperationResult> report)
    {
        _tweaks = tweaks;
        _report = report;
        Area = area;
        Items = tweaks.GetStates(area).Select(s => new TweakItemViewModel(s, ApplyAsync, RestoreAsync)).ToList();
    }

    public TweakArea Area { get; }
    public IReadOnlyList<TweakItemViewModel> Items { get; }
    public bool HasModified => Items.Any(i => i.IsModified);

    private async Task ApplyAsync(TweakItemViewModel item, string option)
    {
        item.IsWorking = true;
        try { _report(await _tweaks.SetAsync(item.Id, option)); }
        finally { Refresh(item); item.IsWorking = false; }
    }

    private async Task RestoreAsync(TweakItemViewModel item)
    {
        item.IsWorking = true;
        try { _report(await _tweaks.RestoreOriginalAsync(item.Id)); }
        finally { Refresh(item); item.IsWorking = false; }
    }

    public async Task RestoreAllAsync()
    {
        _report(await _tweaks.RestoreAreaAsync(Area));
        foreach (var i in Items) Refresh(i);
    }

    public void Refresh(TweakItemViewModel? only = null)
    {
        foreach (var i in Items.Where(i => only is null || i == only)) i.Update(_tweaks.GetState(i.Definition));
    }
}
