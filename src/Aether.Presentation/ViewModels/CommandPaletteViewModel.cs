using System.Collections.ObjectModel;
using Aether.Presentation.Infrastructure;
using Aether.Services.Commands;
using Aether.Services.Tweaks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Aether.Presentation.ViewModels;

public sealed record PaletteResult(CommandMatch Match)
{
    public string Title => Match.Item.Title;
    public string Subtitle => Match.Item.Subtitle;
    public string Category => Match.Item.Category;
    public string Glyph => Match.Item.Glyph;
}

/// <summary>Page navigation and settings search for the palette (settings come from the tweak catalog).</summary>
public sealed class NavigationCommandSource(INavigationService navigation) : ICommandSource
{
    public IEnumerable<CommandItem> GetCommands()
    {
        foreach (var i in NavigationCatalog.All)
            yield return new CommandItem($"nav:{i.Key}", $"Go to {i.Title}", i.Description, "Pages", i.Glyph, () => { navigation.Navigate(i.Key); return Task.CompletedTask; }) { Boost = 2 };
        foreach (var t in TweakCatalog.All)
        {
            var page = t.Area switch
            {
                TweakArea.Personalization => PageKey.Personalization,
                TweakArea.Taskbar => PageKey.Taskbar,
                TweakArea.StartMenu => PageKey.StartMenu,
                TweakArea.FileExplorer => PageKey.FileExplorer,
                _ => PageKey.System,
            };
            yield return new CommandItem($"setting:{t.Id}", t.Title, $"{NavigationCatalog.Get(page).Title} · {t.Why}", "Settings", "",
                () => { navigation.Navigate(page, t.Id); return Task.CompletedTask; }) { Keywords = t.Keywords };
        }
    }
}

public sealed partial class CommandPaletteViewModel : ObservableObject
{
    private readonly CommandPaletteService _palette;
    private CancellationTokenSource? _searchCts;

    public CommandPaletteViewModel(CommandPaletteService palette, INavigationService navigation)
    {
        _palette = palette;
        palette.AddSource(new NavigationCommandSource(navigation));
    }

    public ObservableCollection<PaletteResult> Results { get; } = [];
    [ObservableProperty] private bool _isOpen;
    [ObservableProperty] private string _query = "";
    [ObservableProperty] private int _selectedIndex;

    public bool HasNoResults => IsOpen && Results.Count == 0 && Query.Length > 0;

    partial void OnQueryChanged(string value) => _ = SearchAsync(value);

    public void Open()
    {
        Query = "";
        IsOpen = true;
        _ = SearchAsync("");
    }

    [RelayCommand] public void Close() => IsOpen = false;

    private async Task SearchAsync(string query)
    {
        _searchCts?.Cancel();
        var cts = _searchCts = new CancellationTokenSource();
        IReadOnlyList<CommandMatch> matches;
        try { matches = await Task.Run(() => _palette.Search(query, 30), cts.Token); }
        catch (OperationCanceledException) { return; }
        if (cts.IsCancellationRequested) return;
        Results.Clear();
        foreach (var m in matches) Results.Add(new PaletteResult(m));
        SelectedIndex = Results.Count > 0 ? 0 : -1;
        OnPropertyChanged(nameof(HasNoResults));
    }

    public void MoveSelection(int delta)
    {
        if (Results.Count == 0) return;
        SelectedIndex = (SelectedIndex + delta + Results.Count) % Results.Count;
    }

    [RelayCommand]
    public async Task ExecuteAsync(PaletteResult? result)
    {
        result ??= SelectedIndex >= 0 && SelectedIndex < Results.Count ? Results[SelectedIndex] : null;
        if (result is null) return;
        IsOpen = false;
        await _palette.ExecuteAsync(result.Match.Item);
    }
}
