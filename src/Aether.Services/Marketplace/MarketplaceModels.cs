namespace Aether.Services.Marketplace;

public enum ListingKind { Mod, Theme, WidgetLayout, Plugin }

/// <summary>
/// A marketplace entry. Ratings, rating counts and install counts are nullable on purpose: AETHER never invents
/// them. Local sources don't have such data and the UI says so.
/// </summary>
public sealed record MarketplaceListing
{
    public required string Id { get; init; }
    public required ListingKind Kind { get; init; }
    public required string Name { get; init; }
    public required string Author { get; init; }
    public string? PublisherId { get; init; }
    public required string Description { get; init; }
    public required string Version { get; init; }
    public List<string> Categories { get; init; } = [];
    public List<string> Tags { get; init; } = [];
    public string Compatibility { get; init; } = "Windows 10 and 11";
    public List<string> Permissions { get; init; } = [];
    public List<string> Dependencies { get; init; } = [];
    public List<string> Changelog { get; init; } = [];
    public List<string> Previews { get; init; } = [];
    public string? Glyph { get; init; }
    public string? AccentColor { get; init; }
    public double? Rating { get; init; }
    public int? RatingCount { get; init; }
    public long? InstallCount { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public DateTimeOffset ReleasedAt { get; init; }
    public bool Featured { get; init; }
    /// <summary>Package location relative to the source (file, folder or URL).</summary>
    public required string Package { get; init; }
    public string SourceId { get; init; } = "";
    public string SourceName { get; init; } = "";
}

public sealed class MarketplaceCatalog
{
    public int Format { get; init; } = 1;
    public string Name { get; init; } = "Local";
    public List<MarketplaceListing> Listings { get; init; } = [];
}

public interface IMarketplaceProvider
{
    string Id { get; }
    string Name { get; }
    bool IsOnline { get; }
    Task<IReadOnlyList<MarketplaceListing>> GetListingsAsync(CancellationToken ct = default);
    /// <summary>Returns a local file or folder path containing the package (downloading it first if needed).</summary>
    Task<string> FetchPackageAsync(MarketplaceListing listing, CancellationToken ct = default);
}

public enum InstallState { NotInstalled, Installed, UpdateAvailable }

public sealed record MarketplaceEntry(MarketplaceListing Listing, InstallState State, string? InstalledVersion, bool IsFavorite);

public sealed record PublisherProfile(string Name, string? PublisherId, int ListingCount, IReadOnlyList<ListingKind> Kinds, bool IsTrusted);
