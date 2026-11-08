using Aether.Core.Abstractions;
using Aether.Core.Diagnostics;
using Aether.Services.Platform;
using Aether.Storage;
using Microsoft.Extensions.Logging;

namespace Aether.Services.Applications;

public enum AppSource { StartMenu, Store, Custom }

public sealed record InstalledApp(string Id, string Name, string LaunchTarget, string? Arguments, string Category, AppSource Source, string? IconPath)
{
    public string SourceLabel => Source switch { AppSource.Store => "Microsoft Store", AppSource.Custom => "Custom", _ => "Desktop app" };
}

public sealed record LaunchProfile
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Target { get; init; }
    public string? Arguments { get; init; }
    public string? WorkingDirectory { get; init; }
    public bool RunAsAdministrator { get; init; }
    public ProcessWindow WindowState { get; init; }
    public string? Hotkey { get; init; }
    public string? IconPath { get; init; }
}

public sealed record QuickLaunchGroup
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string Glyph { get; init; } = "";
    /// <summary>App ids or "profile:&lt;id&gt;" entries launched together.</summary>
    public List<string> Items { get; init; } = [];
}

public sealed record AppCustomization
{
    public string? Category { get; init; }
    public string? IconPath { get; init; }
    public string? DisplayName { get; init; }
    public bool Hidden { get; init; }
}

/// <summary>Discovers applications (Start menu shortcuts + Store apps) and manages favorites, recents, profiles and groups.</summary>
public sealed class AppCatalog(IShortcutService shortcuts, IPowerShellRunner powerShell, IShellLauncher launcher, IDocumentStore store, IAuditLog audit, ILogger<AppCatalog> logger)
{
    private IReadOnlyList<InstalledApp>? _cache;
    private readonly SemaphoreSlim _scan = new(1, 1);

    public event Action? Changed;

    public IReadOnlyList<InstalledApp> Cached => _cache ?? [];

    public async Task<IReadOnlyList<InstalledApp>> GetAppsAsync(bool refresh = false, CancellationToken ct = default)
    {
        if (_cache is not null && !refresh) return Apply(_cache);
        await _scan.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_cache is not null && !refresh) return Apply(_cache);
            var apps = new Dictionary<string, InstalledApp>(StringComparer.OrdinalIgnoreCase);
            foreach (var root in new[] { shortcuts.StartMenuProgramsFolder, shortcuts.CommonStartMenuProgramsFolder }.Where(Directory.Exists))
            {
                foreach (var file in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories))
                {
                    var ext = Path.GetExtension(file).ToLowerInvariant();
                    if (ext is not (".lnk" or ".url" or ".appref-ms")) continue;
                    var name = Path.GetFileNameWithoutExtension(file);
                    if (name.Contains("uninstall", StringComparison.OrdinalIgnoreCase) || name.Contains("readme", StringComparison.OrdinalIgnoreCase)) continue;
                    var folder = Path.GetRelativePath(root, Path.GetDirectoryName(file)!);
                    var category = folder == "." ? "General" : folder.Split(Path.DirectorySeparatorChar)[0];
                    apps.TryAdd(name, new InstalledApp("lnk:" + name.ToLowerInvariant(), name, file, null, category, AppSource.StartMenu, null));
                }
            }
            foreach (var packaged in await GetStoreAppsAsync(ct).ConfigureAwait(false))
                apps.TryAdd(packaged.Name, packaged);
            foreach (var custom in store.GetAll<InstalledApp>("custom-apps")) apps[custom.Name] = custom;
            _cache = apps.Values.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ToList();
            return Apply(_cache);
        }
        finally { _scan.Release(); }
    }

    /// <summary>Uses the documented Get-StartApps cmdlet to list packaged (Store) apps with their AppUserModelIDs.</summary>
    private async Task<IReadOnlyList<InstalledApp>> GetStoreAppsAsync(CancellationToken ct)
    {
        if (!powerShell.IsAvailable) return [];
        try
        {
            var r = await powerShell.RunScriptAsync("Get-StartApps | Where-Object { $_.AppID -like '*!*' } | ForEach-Object { \"$($_.Name)`t$($_.AppID)\" }", TimeSpan.FromSeconds(20), ct).ConfigureAwait(false);
            if (!r.Succeeded) return [];
            return r.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(l => l.Split('\t')).Where(p => p.Length == 2)
                .Select(p => new InstalledApp("aumid:" + p[1].ToLowerInvariant(), p[0], "shell:AppsFolder\\" + p[1], null, "Microsoft Store", AppSource.Store, null)).ToList();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogInformation(ex, "Store app discovery unavailable");
            return [];
        }
    }

    private IReadOnlyList<InstalledApp> Apply(IReadOnlyList<InstalledApp> apps)
    {
        var custom = store.GetIds("app-customizations").ToDictionary(id => id, id => store.Get<AppCustomization>("app-customizations", id)!);
        return apps.Where(a => !(custom.TryGetValue(a.Id, out var c) && c.Hidden))
            .Select(a => custom.TryGetValue(a.Id, out var c)
                ? a with { Category = c.Category ?? a.Category, IconPath = c.IconPath ?? a.IconPath, Name = c.DisplayName ?? a.Name }
                : a).ToList();
    }

    public AppCustomization GetCustomization(string appId) => store.Get<AppCustomization>("app-customizations", appId) ?? new AppCustomization();

    public void Customize(string appId, AppCustomization c) { store.Put("app-customizations", appId, c); Changed?.Invoke(); }

    public void AddCustomApp(string name, string target, string? arguments, string category)
    {
        var app = new InstalledApp("custom:" + Guid.NewGuid().ToString("N")[..8], name, target, arguments, category, AppSource.Custom, null);
        store.Put("custom-apps", app.Id, app);
        _cache = null;
        Changed?.Invoke();
    }

    public IReadOnlySet<string> Favorites => store.GetIds("app-favorites").ToHashSet();

    public void ToggleFavorite(string appId)
    {
        if (Favorites.Contains(appId)) store.Delete("app-favorites", appId);
        else store.Put("app-favorites", appId, new { AddedAt = DateTimeOffset.UtcNow });
        Changed?.Invoke();
    }

    public IReadOnlyList<string> Recent => store.Get<RecentList>("app-state", "recent")?.Ids ?? [];

    public OperationResult Launch(InstalledApp app, bool elevated = false)
    {
        try
        {
            launcher.Open(app.LaunchTarget, app.Arguments, elevated);
            var recent = Recent.Where(id => id != app.Id).Prepend(app.Id).Take(20).ToList();
            store.Put("app-state", "recent", new RecentList(recent));
            Changed?.Invoke();
            return OperationResult.Ok($"Launching {app.Name}…");
        }
        catch (Exception ex)
        {
            return OperationResult.Fail($"{app.Name} could not be started.", ex.Message);
        }
    }

    // ── Launch profiles ──
    public IReadOnlyList<LaunchProfile> GetProfiles() => store.GetAll<LaunchProfile>("launch-profiles");
    public void SaveProfile(LaunchProfile p) { store.Put("launch-profiles", p.Id, p); Changed?.Invoke(); }
    public void DeleteProfile(string id) { store.Delete("launch-profiles", id); Changed?.Invoke(); }

    public OperationResult Launch(LaunchProfile p)
    {
        try
        {
            launcher.Open(p.Target, p.Arguments, p.RunAsAdministrator, p.WorkingDirectory, p.WindowState);
            audit.Record(new AuditEntry(DateTimeOffset.UtcNow, "apps", "launch-profile", p.Name, true, $"{p.Target} {p.Arguments}"));
            return OperationResult.Ok($"Launching {p.Name}…");
        }
        catch (Exception ex) { return OperationResult.Fail($"{p.Name} could not be started.", ex.Message); }
    }

    // ── Quick launch groups ──
    public IReadOnlyList<QuickLaunchGroup> GetGroups() => store.GetAll<QuickLaunchGroup>("launch-groups");
    public void SaveGroup(QuickLaunchGroup g) { store.Put("launch-groups", g.Id, g); Changed?.Invoke(); }
    public void DeleteGroup(string id) { store.Delete("launch-groups", id); Changed?.Invoke(); }

    public async Task<OperationResult> LaunchGroupAsync(QuickLaunchGroup g, CancellationToken ct = default)
    {
        var apps = await GetAppsAsync(ct: ct).ConfigureAwait(false);
        var profiles = GetProfiles();
        var errors = new List<string>();
        foreach (var item in g.Items)
        {
            var r = item.StartsWith("profile:", StringComparison.Ordinal)
                ? profiles.FirstOrDefault(p => p.Id == item[8..]) is { } p ? Launch(p) : OperationResult.Fail($"Profile {item} no longer exists.")
                : apps.FirstOrDefault(a => a.Id == item) is { } a ? Launch(a) : OperationResult.Fail($"App {item} is no longer installed.");
            if (!r.Success) errors.Add(r.Describe());
            await Task.Delay(250, ct).ConfigureAwait(false);
        }
        return errors.Count == 0 ? OperationResult.Ok($"Launched '{g.Name}'.") : OperationResult.Fail($"Some items in '{g.Name}' failed to start.", errors);
    }

    private sealed record RecentList(List<string> Ids);
}
