using Aether.Core.Abstractions;
using Aether.Core.Diagnostics;
using Aether.Core.Versioning;
using Aether.ModEngine.Lifecycle;
using Aether.ModEngine.Packaging;
using Aether.ModEngine.Security;
using Aether.PluginSystem;
using Aether.Services.Settings;
using Aether.Storage;
using Aether.ThemeEngine;
using Aether.WidgetEngine;
using Microsoft.Extensions.Logging;

namespace Aether.Services.Marketplace;

/// <summary>Aggregates marketplace sources, tracks install/update state and favorites, and installs listings.</summary>
public sealed class MarketplaceService(
    ISettingsService settings, IAppPaths paths, IDocumentStore store, ModManager mods, ThemeManager themes, WidgetManager widgets,
    PluginHost plugins, ITrustedPublisherStore publishers, HttpClient http, ILogger<MarketplaceService> logger, string bundledFolder)
{
    public const string BundledSourceId = "bundled";
    public const string UserSourceId = "user-folder";
    private IReadOnlyList<MarketplaceListing> _cache = [];
    private readonly List<string> _errors = [];

    public event Action? Changed;

    public IReadOnlyList<string> LastErrors { get { lock (_errors) return [.. _errors]; } }

    public IReadOnlyList<MarketplaceSource> GetSources()
    {
        var builtIn = new List<MarketplaceSource>
        {
            new(BundledSourceId, "AETHER Collection", bundledFolder, true, true),
            new(UserSourceId, "My packages folder", paths.Marketplace, true, true),
        };
        var configured = settings.Current.MarketplaceSources;
        return builtIn.Select(b => configured.FirstOrDefault(c => c.Id == b.Id) is { } c ? b with { Enabled = c.Enabled } : b)
            .Concat(configured.Where(c => !c.IsBuiltIn)).ToList();
    }

    public OperationResult AddSource(string name, string location)
    {
        var isUrl = Uri.TryCreate(location, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http";
        if (isUrl && uri!.Scheme != "https") return OperationResult.Fail("Online sources must use HTTPS.");
        if (!isUrl && !Directory.Exists(location)) return OperationResult.Fail("The folder does not exist.");
        var source = new MarketplaceSource("src-" + Guid.NewGuid().ToString("N")[..8], string.IsNullOrWhiteSpace(name) ? location : name, location, true, false);
        settings.Update(s => s with { MarketplaceSources = [.. s.MarketplaceSources, source] });
        Changed?.Invoke();
        return OperationResult.Ok($"Added source '{source.Name}'. Packages from it are still verified before installation.");
    }

    public void SetSourceEnabled(string id, bool enabled)
    {
        settings.Update(s =>
        {
            var list = s.MarketplaceSources.Where(x => x.Id != id).ToList();
            var existing = GetSources().First(x => x.Id == id);
            list.Add(existing with { Enabled = enabled });
            return s with { MarketplaceSources = list };
        });
        Changed?.Invoke();
    }

    public void RemoveSource(string id)
    {
        settings.Update(s => s with { MarketplaceSources = s.MarketplaceSources.Where(x => x.Id != id || x.IsBuiltIn).ToList() });
        Changed?.Invoke();
    }

    private IEnumerable<IMarketplaceProvider> Providers()
    {
        foreach (var s in GetSources().Where(s => s.Enabled))
        {
            if (Uri.TryCreate(s.Location, UriKind.Absolute, out var uri) && uri.Scheme == "https")
                yield return new HttpMarketplaceProvider(s.Id, s.Name, uri, http, Path.Combine(paths.Cache, "marketplace"));
            else
                yield return new LocalMarketplaceProvider(s.Id, s.Name, s.Location);
        }
    }

    public async Task<IReadOnlyList<MarketplaceEntry>> RefreshAsync(CancellationToken ct = default)
    {
        var all = new List<MarketplaceListing>();
        lock (_errors) _errors.Clear();
        foreach (var p in Providers())
        {
            try { all.AddRange(await p.GetListingsAsync(ct).ConfigureAwait(false)); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Marketplace source {Source} failed", p.Name);
                lock (_errors) _errors.Add($"{p.Name}: {ex.Message}");
            }
        }
        _cache = all.GroupBy(l => (l.Kind, l.Id)).Select(g => g.OrderByDescending(l => SemVersion.TryParse(l.Version, out var v) ? v : new SemVersion(0)).First()).ToList();
        return GetEntries();
    }

    public IReadOnlyList<MarketplaceEntry> GetEntries()
    {
        var favorites = Favorites();
        return _cache.Select(l =>
        {
            var installed = InstalledVersion(l);
            var state = installed is null ? InstallState.NotInstalled
                : SemVersion.TryParse(installed, out var iv) && SemVersion.TryParse(l.Version, out var lv) && lv > iv ? InstallState.UpdateAvailable
                : InstallState.Installed;
            return new MarketplaceEntry(l, state, installed, favorites.Contains(Key(l)));
        }).ToList();
    }

    public int UpdatesAvailable => GetEntries().Count(e => e.State == InstallState.UpdateAvailable);

    public IReadOnlyList<PublisherProfile> GetPublishers()
    {
        var trusted = publishers.GetAll().Select(p => p.Id).ToHashSet();
        return _cache.GroupBy(l => l.Author).Select(g => new PublisherProfile(g.Key, g.First().PublisherId, g.Count(), g.Select(l => l.Kind).Distinct().ToList(),
            g.First().PublisherId is { } pid && trusted.Contains(pid))).OrderByDescending(p => p.ListingCount).ToList();
    }

    private string? InstalledVersion(MarketplaceListing l) => l.Kind switch
    {
        ListingKind.Mod => mods.GetMod(l.Id)?.Manifest.Version.ToString(),
        ListingKind.Theme => themes.GetThemes().FirstOrDefault(t => t.Id == l.Id || t.Name == l.Name)?.Version,
        ListingKind.WidgetLayout => widgets.GetLayouts().Any(w => w.Name == l.Name) ? l.Version : null,
        ListingKind.Plugin => plugins.GetPlugins().FirstOrDefault(p => p.Manifest.Id == l.Id)?.Manifest.Version,
        _ => null,
    };

    /// <summary>Downloads (if online) and inspects a mod package so the user can review it before installing.</summary>
    public async Task<OperationResult<PackageInspection>> InspectModAsync(MarketplaceListing listing, CancellationToken ct = default)
    {
        try
        {
            var path = await ProviderFor(listing).FetchPackageAsync(listing, ct).ConfigureAwait(false);
            var package = Directory.Exists(path) ? ModPackage.ReadDirectory(path) : ModPackage.ReadZip(path);
            return OperationResult<PackageInspection>.Ok(mods.Inspect(package));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return OperationResult<PackageInspection>.Fail("The package could not be read.", ex.Message);
        }
    }

    public async Task<OperationResult> InstallAsync(MarketplaceListing listing, CancellationToken ct = default)
    {
        try
        {
            var path = await ProviderFor(listing).FetchPackageAsync(listing, ct).ConfigureAwait(false);
            OperationResult result;
            switch (listing.Kind)
            {
                case ListingKind.Mod:
                    var r = await mods.InstallFromFileAsync(path, ct).ConfigureAwait(false);
                    result = new OperationResult(r.Succeeded, r.Message, r.Details);
                    break;
                case ListingKind.Theme:
                    await using (var fs = File.OpenRead(path))
                        result = themes.Import(fs, paths.Themes).ToResult();
                    break;
                case ListingKind.WidgetLayout:
                    var layout = widgets.ImportLayout(await File.ReadAllTextAsync(path, ct).ConfigureAwait(false));
                    result = OperationResult.Ok($"Widget layout '{layout.Name}' added. Activate it in Widgets.");
                    break;
                case ListingKind.Plugin:
                    result = plugins.Install(path);
                    break;
                default:
                    result = OperationResult.Fail("Unsupported listing type.");
                    break;
            }
            Changed?.Invoke();
            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Install of {Listing} failed", listing.Id);
            return OperationResult.Fail($"{listing.Name} could not be installed.", ex.Message);
        }
    }

    private IMarketplaceProvider ProviderFor(MarketplaceListing l) =>
        Providers().FirstOrDefault(p => p.Id == l.SourceId) ?? throw new InvalidOperationException("The source of this listing is no longer enabled.");

    public IReadOnlySet<string> Favorites() => store.GetIds("marketplace-favorites").ToHashSet();

    public void ToggleFavorite(MarketplaceListing l)
    {
        var key = Key(l);
        if (Favorites().Contains(key)) store.Delete("marketplace-favorites", key);
        else store.Put("marketplace-favorites", key, new { l.Name, AddedAt = DateTimeOffset.UtcNow });
        Changed?.Invoke();
    }

    private static string Key(MarketplaceListing l) => $"{l.Kind}:{l.Id}".ToLowerInvariant();
}
