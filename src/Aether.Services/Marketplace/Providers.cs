using System.Security.Cryptography;
using Aether.Storage;

namespace Aether.Services.Marketplace;

/// <summary>Reads catalog.json from a folder; package paths are relative to that folder.</summary>
public sealed class LocalMarketplaceProvider(string id, string name, string folder) : IMarketplaceProvider
{
    public string Id => id;
    public string Name => name;
    public bool IsOnline => false;
    public string Folder => folder;

    public async Task<IReadOnlyList<MarketplaceListing>> GetListingsAsync(CancellationToken ct = default)
    {
        var catalogPath = Path.Combine(folder, "catalog.json");
        if (!File.Exists(catalogPath)) return [];
        var catalog = AetherJson.Deserialize<MarketplaceCatalog>(await File.ReadAllTextAsync(catalogPath, ct).ConfigureAwait(false)) ?? new MarketplaceCatalog();
        return catalog.Listings
            .Where(l => File.Exists(Resolve(l.Package)) || Directory.Exists(Resolve(l.Package)))
            .Select(l => l with
            {
                SourceId = id, SourceName = name,
                Previews = l.Previews.Select(p => Uri.IsWellFormedUriString(p, UriKind.Absolute) ? p : Resolve(p)).Where(p => p.StartsWith("http") || File.Exists(p)).ToList(),
                // Local catalogs cannot know ratings or install counts.
                Rating = null, RatingCount = null, InstallCount = null,
            }).ToList();
    }

    public Task<string> FetchPackageAsync(MarketplaceListing listing, CancellationToken ct = default) => Task.FromResult(Resolve(listing.Package));

    private string Resolve(string relative)
    {
        var full = Path.GetFullPath(Path.Combine(folder, relative));
        var root = Path.GetFullPath(folder);
        return full.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? full : throw new InvalidDataException($"Package path '{relative}' escapes the catalog folder.");
    }
}

/// <summary>
/// An online repository: a static HTTPS index (index.json in <see cref="MarketplaceCatalog"/> format) with absolute
/// or relative package URLs. Packages are downloaded to the cache and verified by the normal installers.
/// </summary>
public sealed class HttpMarketplaceProvider(string id, string name, Uri indexUrl, HttpClient http, string cacheFolder) : IMarketplaceProvider
{
    public string Id => id;
    public string Name => name;
    public bool IsOnline => true;

    public async Task<IReadOnlyList<MarketplaceListing>> GetListingsAsync(CancellationToken ct = default)
    {
        if (indexUrl.Scheme != Uri.UriSchemeHttps) throw new InvalidOperationException("Online sources must use HTTPS.");
        var json = await http.GetStringAsync(indexUrl, ct).ConfigureAwait(false);
        var catalog = AetherJson.Deserialize<MarketplaceCatalog>(json) ?? new MarketplaceCatalog();
        return catalog.Listings.Select(l => l with { SourceId = id, SourceName = name, Package = new Uri(indexUrl, l.Package).ToString() }).ToList();
    }

    public async Task<string> FetchPackageAsync(MarketplaceListing listing, CancellationToken ct = default)
    {
        var uri = new Uri(listing.Package);
        if (uri.Scheme != Uri.UriSchemeHttps) throw new InvalidOperationException("Packages must be downloaded over HTTPS.");
        Directory.CreateDirectory(cacheFolder);
        var name = Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(listing.Package)))[..16] + Path.GetExtension(uri.AbsolutePath);
        var target = Path.Combine(cacheFolder, name);
        using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > 200L * 1024 * 1024) throw new InvalidDataException("The package is too large.");
        await using (var fs = File.Create(target))
            await response.Content.CopyToAsync(fs, ct).ConfigureAwait(false);
        return target;
    }
}
