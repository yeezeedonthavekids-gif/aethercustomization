using System.Text.Json;
using Aether.Core.Abstractions;
using Aether.Services.Mods.Builtin.Components;
using Aether.Storage;

namespace Aether.Services.Wallpapers;

public sealed record WallpaperCollection(string Id, string Name, List<string> Files);

public sealed record OnlineWallpaper(string Id, string Title, string Copyright, Uri ImageUrl, Uri ThumbnailUrl, string Provider);

public interface IOnlineWallpaperProvider
{
    string Name { get; }
    string Description { get; }
    Task<IReadOnlyList<OnlineWallpaper>> GetLatestAsync(CancellationToken ct = default);
}

/// <summary>Bing's public image-of-the-day archive (the last 8 days). Images are for use as wallpaper only.</summary>
public sealed class BingDailyWallpaperProvider(HttpClient http) : IOnlineWallpaperProvider
{
    public string Name => "Bing image of the day";
    public string Description => "The last eight daily images from bing.com. © the respective photographers.";

    public async Task<IReadOnlyList<OnlineWallpaper>> GetLatestAsync(CancellationToken ct = default)
    {
        var json = await http.GetStringAsync("https://www.bing.com/HPImageArchive.aspx?format=js&idx=0&n=8&mkt=en-US", ct).ConfigureAwait(false);
        return Parse(json);
    }

    public static IReadOnlyList<OnlineWallpaper> Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var list = new List<OnlineWallpaper>();
        foreach (var img in doc.RootElement.GetProperty("images").EnumerateArray())
        {
            var urlBase = img.GetProperty("urlbase").GetString() ?? "";
            var id = img.TryGetProperty("hsh", out var h) ? h.GetString() ?? urlBase : urlBase;
            var title = img.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
            var copyright = img.TryGetProperty("copyright", out var c) ? c.GetString() ?? "" : "";
            list.Add(new OnlineWallpaper(id, string.IsNullOrEmpty(title) ? copyright : title, copyright,
                new Uri("https://www.bing.com" + urlBase + "_UHD.jpg"), new Uri("https://www.bing.com" + urlBase + "_480x270.jpg"), "Bing"));
        }
        return list;
    }
}

/// <summary>Local wallpaper folders, collections and downloads from online providers.</summary>
public sealed class WallpaperLibrary(IAppPaths paths, IDocumentStore store, HttpClient http, IEnumerable<IOnlineWallpaperProvider> providers)
{
    public IReadOnlyList<IOnlineWallpaperProvider> Providers { get; } = providers.ToList();
    public event Action? Changed;

    public IReadOnlyList<string> GetFolders()
    {
        var folders = store.Get<FolderList>("wallpaper", "folders")?.Folders ?? [];
        var defaults = new[] { paths.Wallpapers, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)), @"C:\Windows\Web\Wallpaper" };
        return folders.Concat(defaults).Where(f => !string.IsNullOrEmpty(f)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public void AddFolder(string folder)
    {
        var list = store.Get<FolderList>("wallpaper", "folders")?.Folders ?? [];
        if (!list.Contains(folder, StringComparer.OrdinalIgnoreCase)) list.Add(folder);
        store.Put("wallpaper", "folders", new FolderList(list));
        Changed?.Invoke();
    }

    public void RemoveFolder(string folder)
    {
        var list = (store.Get<FolderList>("wallpaper", "folders")?.Folders ?? []).Where(f => !f.Equals(folder, StringComparison.OrdinalIgnoreCase)).ToList();
        store.Put("wallpaper", "folders", new FolderList(list));
        Changed?.Invoke();
    }

    public IReadOnlyList<string> Scan(int limit = 400) =>
        GetFolders().Where(Directory.Exists).SelectMany(f =>
        {
            try { return WallpaperFiles.Enumerate(f); } catch (UnauthorizedAccessException) { return []; }
        }).Distinct(StringComparer.OrdinalIgnoreCase).Take(limit).ToList();

    public IReadOnlyList<WallpaperCollection> GetCollections() => store.GetAll<WallpaperCollection>("wallpaper-collections");

    public WallpaperCollection SaveCollection(WallpaperCollection c) { store.Put("wallpaper-collections", c.Id, c); Changed?.Invoke(); return c; }
    public void DeleteCollection(string id) { store.Delete("wallpaper-collections", id); Changed?.Invoke(); }

    public async Task<string> DownloadAsync(OnlineWallpaper wallpaper, CancellationToken ct = default)
    {
        var dir = Path.Combine(paths.Wallpapers, "online");
        Directory.CreateDirectory(dir);
        var safe = string.Concat(wallpaper.Id.Where(char.IsAsciiLetterOrDigit));
        var file = Path.Combine(dir, $"{wallpaper.Provider.ToLowerInvariant()}-{(safe.Length > 40 ? safe[..40] : safe)}.jpg");
        if (File.Exists(file)) return file;
        var bytes = await http.GetByteArrayAsync(wallpaper.ImageUrl, ct).ConfigureAwait(false);
        if (bytes.Length < 4 || bytes[0] != 0xFF || bytes[1] != 0xD8) throw new InvalidDataException("The download is not a JPEG image.");
        await File.WriteAllBytesAsync(file, bytes, ct).ConfigureAwait(false);
        Changed?.Invoke();
        return file;
    }

    private sealed record FolderList(List<string> Folders);
}
