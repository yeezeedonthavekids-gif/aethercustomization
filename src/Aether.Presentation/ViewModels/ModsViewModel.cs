using System.Collections.ObjectModel;
using Aether.Core.Abstractions;
using Aether.Core.Versioning;
using Aether.ModEngine.Lifecycle;
using Aether.ModEngine.Manifest;
using Aether.ModEngine.Packaging;
using Aether.Presentation.Infrastructure;
using Aether.Presentation.Items;
using Aether.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Aether.Presentation.ViewModels;

public enum ModFilter { All, Active, Off, Attention, BuiltIn, Installed }

public sealed partial class ModsViewModel : PageViewModel
{
    private readonly ModManager _mods;
    private readonly ModInteraction _interaction;
    private readonly IDialogService _dialogs;
    private readonly IUiDispatcher _ui;
    private List<ModItemViewModel> _all = [];

    public ModsViewModel(ModManager mods, ModInteraction interaction, IDialogService dialogs, IUiDispatcher ui, INotificationSink notifications) : base(notifications)
    {
        _mods = mods; _interaction = interaction; _dialogs = dialogs; _ui = ui;
        _mods.Changed += e => _ui.Post(() => OnModChanged(e.ModId));
    }

    public ObservableCollection<ModItemViewModel> Mods { get; } = [];
    public IReadOnlyList<string> Categories { get; } = ["All categories", .. Enum.GetValues<ModCategory>().Select(ModItemViewModel.CategoryName)];
    public IReadOnlyList<ModFilter> Filters { get; } = Enum.GetValues<ModFilter>();

    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private string _selectedCategory = "All categories";
    [ObservableProperty] private ModFilter _selectedFilter = ModFilter.All;
    [ObservableProperty] private ModItemViewModel? _selected;
    [ObservableProperty] private SettingsFormViewModel? _form;
    [ObservableProperty] private int _activeCount;

    public ObservableCollection<string> PreviewLines { get; } = [];
    public ObservableCollection<ModHistoryRecord> History { get; } = [];
    public ObservableCollection<string> StoredVersions { get; } = [];
    public ObservableCollection<PermissionInfo> SelectedPermissions { get; } = [];

    public bool HasSelection => Selected is not null;
    public bool CanUninstall => Selected is { IsBuiltIn: false };
    public bool SelectedIncompatible => Selected is { IsCompatible: false };
    public bool IsEmpty => Mods.Count == 0;

    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnSelectedCategoryChanged(string value) => ApplyFilter();
    partial void OnSelectedFilterChanged(ModFilter value) => ApplyFilter();

    partial void OnSelectedChanged(ModItemViewModel? value)
    {
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(CanUninstall));
        OnPropertyChanged(nameof(SelectedIncompatible));
        LoadDetails();
    }

    protected override Task LoadAsync()
    {
        _all = _mods.GetMods().Select(m => new ModItemViewModel(m)).ToList();
        ApplyFilter();
        return Task.CompletedTask;
    }

    protected override Task RefreshAsync()
    {
        foreach (var m in _mods.GetMods())
        {
            var item = _all.FirstOrDefault(x => x.Id == m.Id);
            if (item is null) _all.Add(new ModItemViewModel(m)); else item.Update(m);
        }
        _all.RemoveAll(x => _mods.GetMod(x.Id) is null);
        ApplyFilter();
        return Task.CompletedTask;
    }

    protected override void OnActivated(object? parameter)
    {
        if (parameter is string id && _all.FirstOrDefault(m => m.Id == id) is { } item) Selected = item;
    }

    private void OnModChanged(string id)
    {
        var mod = _mods.GetMod(id);
        var item = _all.FirstOrDefault(x => x.Id == id);
        if (mod is null) { if (item is not null) { _all.Remove(item); ApplyFilter(); } return; }
        if (item is null) { _all.Add(new ModItemViewModel(mod)); ApplyFilter(); }
        else item.Update(mod);
        ActiveCount = _all.Count(m => m.IsEnabled);
        if (Selected?.Id == id) LoadDetails(keepForm: true);
    }

    private void ApplyFilter()
    {
        var q = SearchText.Trim();
        var list = _all.Where(m =>
            (q.Length == 0 || m.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || m.Description.Contains(q, StringComparison.OrdinalIgnoreCase)
                || m.Mod.Manifest.Tags.Any(t => t.Contains(q, StringComparison.OrdinalIgnoreCase))) &&
            (SelectedCategory == "All categories" || m.CategoryLabel == SelectedCategory) &&
            SelectedFilter switch
            {
                ModFilter.Active => m.IsEnabled,
                ModFilter.Off => !m.IsEnabled,
                ModFilter.Attention => m.HasError || !m.IsCompatible,
                ModFilter.BuiltIn => m.IsBuiltIn,
                ModFilter.Installed => !m.IsBuiltIn,
                _ => true,
            }).OrderByDescending(m => m.IsEnabled).ThenBy(m => m.Name).ToList();
        Mods.Clear();
        foreach (var m in list) Mods.Add(m);
        ActiveCount = _all.Count(m => m.IsEnabled);
        OnPropertyChanged(nameof(IsEmpty));
    }

    private void LoadDetails(bool keepForm = false)
    {
        PreviewLines.Clear(); History.Clear(); StoredVersions.Clear(); SelectedPermissions.Clear();
        if (Selected is null) { Form = null; return; }
        var mod = _mods.GetMod(Selected.Id);
        if (mod is null) return;
        if (!keepForm || Form is null || !Form.IsDirty)
        {
            Form = new SettingsFormViewModel(mod.Manifest.Settings, mod.Settings);
            Form.Changed += UpdatePreview;
        }
        UpdatePreview();
        foreach (var h in _mods.GetHistory(mod.Id, 30)) History.Add(h);
        foreach (var v in _mods.GetStoredVersions(mod.Id).Where(v => !v.Equals(mod.Manifest.Version))) StoredVersions.Add(v.ToString());
        foreach (var p in mod.Manifest.Permissions.Select(ModPermissions.Describe)) SelectedPermissions.Add(p);
    }

    private void UpdatePreview()
    {
        PreviewLines.Clear();
        if (Selected is null || Form is null) return;
        foreach (var l in _mods.PreviewChanges(Selected.Id, Form.ToJson())) PreviewLines.Add(l);
    }

    /// <summary>Called by the toggle on a mod card.</summary>
    public async Task ToggleAsync(ModItemViewModel item, bool enable)
    {
        if (item.IsWorking || item.Mod.IsEnabled == enable) return;
        item.IsWorking = true;
        try { await _interaction.SetEnabledAsync(item.Id, enable); }
        finally
        {
            item.IsWorking = false;
            if (_mods.GetMod(item.Id) is { } m) item.Update(m);
        }
    }

    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        if (Selected is null || Form is null) return;
        if (Form.HasErrors) { Toast("Check the highlighted settings", "Some values are invalid.", NotificationSeverity.Warning); return; }
        await RunAsync(async () =>
        {
            var r = await _mods.UpdateSettingsAsync(Selected.Id, Form.ToJson());
            Toast(Selected.Name, r.Message + (r.Details.Count > 0 && !r.Succeeded ? "\n• " + string.Join("\n• ", r.Details) : ""),
                r.Succeeded ? NotificationSeverity.Success : NotificationSeverity.Error);
            if (r.Succeeded) Form.MarkClean();
        });
    }

    [RelayCommand]
    private void ResetSettings() => Form?.ResetAll();

    [RelayCommand]
    private Task InvokeActionAsync(ModAction? action) =>
        Selected is null || action is null ? Task.CompletedTask : RunAsync(() => _interaction.InvokeActionAsync(Selected.Id, action));

    [RelayCommand]
    private async Task InstallFromFileAsync()
    {
        var path = await _dialogs.PickFileAsync(new FileFilter("AETHER mod package", ModPackage.Extension, ".zip"));
        if (path is null) return;
        await RunAsync(async () =>
        {
            ModPackage package;
            try { package = ModPackage.ReadZip(path); }
            catch (Exception ex) { Toast("Not a valid mod package", ex.Message, NotificationSeverity.Error); return; }
            var inspection = _mods.Inspect(package);
            var m = inspection.Manifest;
            var text = $"{m.Name} {m.Version} by {m.Author}\n{m.Description}\n\nVerification: {inspection.Verification.Message}\nDigest: {inspection.Verification.Digest}\n" +
                       $"Permissions: {(m.Permissions.Count == 0 ? "none" : string.Join(", ", m.Permissions.Select(p => ModPermissions.Describe(p).Title)))}\n" +
                       (inspection.Compatibility.Problems.Count > 0 ? "\nCompatibility: " + string.Join(" ", inspection.Compatibility.Problems) : "") +
                       (inspection.ValidationErrors.Count > 0 ? "\nProblems:\n• " + string.Join("\n• ", inspection.ValidationErrors) : "") +
                       $"\n\n{inspection.Summary}";
            if (!inspection.CanInstall) { await _dialogs.ShowTextAsync("This package can't be installed", text); return; }
            if (!await _dialogs.ConfirmAsync(inspection.InstalledVersion is null ? "Install mod?" : "Update mod?", text, inspection.InstalledVersion is null ? "Install" : "Update")) return;
            var r = await _mods.InstallAsync(package);
            Toast(m.Name, r.Message, r.Succeeded ? NotificationSeverity.Success : NotificationSeverity.Error);
            await RefreshAsync();
            Selected = _all.FirstOrDefault(x => x.Id == m.Id);
        });
    }

    [RelayCommand]
    private async Task UninstallAsync()
    {
        if (Selected is null || Selected.IsBuiltIn) return;
        if (!await _dialogs.ConfirmAsync($"Uninstall {Selected.Name}?", "Its changes are reverted first, then its files are removed.", "Uninstall", destructive: true)) return;
        await RunAsync(async () =>
        {
            var r = await _mods.UninstallAsync(Selected.Id);
            Toast(Selected.Name, r.Message, r.Succeeded ? NotificationSeverity.Success : NotificationSeverity.Error);
            if (r.Succeeded) { Selected = null; await RefreshAsync(); }
        });
    }

    [RelayCommand]
    private async Task RestoreVersionAsync(string? version)
    {
        if (Selected is null || version is null || !SemVersion.TryParse(version, out var v)) return;
        if (!await _dialogs.ConfirmAsync($"Restore version {version}?", "The current version is replaced by the stored copy. Settings are kept where still valid.", "Restore")) return;
        await RunAsync(async () =>
        {
            var r = await _mods.RestoreVersionAsync(Selected.Id, v);
            Toast(Selected.Name, r.Message, r.Succeeded ? NotificationSeverity.Success : NotificationSeverity.Error);
        });
    }

    [RelayCommand]
    private async Task ExportConfigurationAsync()
    {
        var path = await _dialogs.SaveFileAsync("aether-mods.json", new FileFilter("JSON", ".json"));
        if (path is null) return;
        await RunAsync(async () =>
        {
            await File.WriteAllTextAsync(path, _mods.ExportConfiguration());
            Toast("Exported", $"Mod configuration saved to {Path.GetFileName(path)}.");
        });
    }

    [RelayCommand]
    private async Task ImportConfigurationAsync()
    {
        var path = await _dialogs.PickFileAsync(new FileFilter("JSON", ".json"));
        if (path is null) return;
        await RunAsync(async () =>
        {
            var results = await _mods.ImportConfigurationAsync(await File.ReadAllTextAsync(path));
            var failed = results.Where(r => !r.Result.Succeeded).ToList();
            Toast("Import finished", failed.Count == 0 ? $"{results.Count} mod configurations applied."
                : $"{results.Count - failed.Count} applied, {failed.Count} need attention:\n• " + string.Join("\n• ", failed.Select(f => $"{f.Id}: {f.Result.Message}")),
                failed.Count == 0 ? NotificationSeverity.Success : NotificationSeverity.Warning);
        });
    }

    [RelayCommand]
    private async Task ShowLogAsync()
    {
        var log = _mods.GetHistory(Selected?.Id, 200);
        var text = log.Count == 0 ? "No entries yet." : string.Join("\n", log.Select(h => $"{h.Timestamp.ToLocalTime():g}  {(h.Success ? "✓" : "✗")} {h.ModId} {h.Version} {h.Action}{(h.Details is null ? "" : " — " + h.Details)}"));
        await _dialogs.ShowTextAsync(Selected is null ? "Installation log" : $"{Selected.Name} log", text);
    }
}
