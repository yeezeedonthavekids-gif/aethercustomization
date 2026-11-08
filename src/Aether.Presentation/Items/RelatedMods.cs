using System.Collections.ObjectModel;
using Aether.ModEngine.Lifecycle;
using Aether.Presentation.Infrastructure;
using CommunityToolkit.Mvvm.Input;

namespace Aether.Presentation.Items;

/// <summary>A set of mod cards shown on a feature page (e.g. Taskbar shows the taskbar mods).</summary>
public sealed partial class RelatedMods
{
    private readonly ModManager _mods;
    private readonly ModInteraction _interaction;
    private readonly INavigationService _navigation;

    public RelatedMods(ModManager mods, ModInteraction interaction, INavigationService navigation, IUiDispatcher ui, params string[] ids)
    {
        _mods = mods; _interaction = interaction; _navigation = navigation;
        foreach (var id in ids) if (mods.GetMod(id) is { } m) Items.Add(new ModItemViewModel(m));
        mods.Changed += e => ui.Post(() => { if (Items.FirstOrDefault(i => i.Id == e.ModId) is { } item && mods.GetMod(e.ModId) is { } m) item.Update(m); });
    }

    public ObservableCollection<ModItemViewModel> Items { get; } = [];

    public async Task ToggleAsync(ModItemViewModel item, bool enable)
    {
        if (item.IsWorking || item.Mod.IsEnabled == enable) return;
        item.IsWorking = true;
        try { await _interaction.SetEnabledAsync(item.Id, enable); }
        finally { item.IsWorking = false; if (_mods.GetMod(item.Id) is { } m) item.Update(m); }
    }

    [RelayCommand]
    private void Configure(ModItemViewModel? item) { if (item is not null) _navigation.Navigate(PageKey.Mods, item.Id); }

    [RelayCommand]
    private Task RunActionAsync(ModItemViewModel? item) =>
        item is { HasActions: true } ? _interaction.InvokeActionAsync(item.Id, item.Mod.Manifest.Actions[0]) : Task.CompletedTask;
}
