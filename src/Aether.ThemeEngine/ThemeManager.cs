using System.IO.Compression;
using System.Text;
using Aether.Core.Abstractions;
using Aether.Core.Diagnostics;
using Aether.Core.Events;
using Aether.Storage;
using Microsoft.Extensions.Logging;

namespace Aether.ThemeEngine;

/// <summary>Optional extension steps run when a theme is applied (widgets, mods, taskbar profiles...).</summary>
public interface IThemeApplyStep
{
    string Name { get; }
    Task<OperationResult> ApplyAsync(ThemePack theme, CancellationToken ct);
}

public sealed record ThemeRollback(string? PreviousThemeId, PersonalizationState? Personalization, Dictionary<string, string>? Wallpapers, DateTimeOffset CreatedAt);

public sealed class ThemeApplyOptions
{
    public bool ApplyWindowsSettings { get; init; } = true;
    public bool ApplyWallpaper { get; init; } = true;
    public bool ApplyCursor { get; init; } = true;
    public bool ApplyExtensions { get; init; } = true;
}

/// <summary>Creates, stores, imports/exports and applies themes, with automatic rollback snapshots.</summary>
public sealed class ThemeManager(
    IDocumentStore store,
    ISnapshotRepository snapshots,
    WindowsPersonalizationApplier personalization,
    IWallpaperService wallpaper,
    ICursorService cursors,
    IEnumerable<IThemeApplyStep> steps,
    ISystemEnvironment environment,
    IAuditLog audit,
    IEventBus events,
    ILogger<ThemeManager> logger)
{
    private const string Collection = "themes";
    private const string StateCollection = "theme-state";

    public event Action? ThemesChanged;
    public event Action<ThemePack>? ThemeApplied;

    public IReadOnlyList<ThemePack> GetThemes() =>
        BuiltinThemes.All.Concat(store.GetAll<ThemePack>(Collection).OrderBy(t => t.Name)).ToList();

    public ThemePack? GetTheme(string id) =>
        BuiltinThemes.All.FirstOrDefault(t => t.Id == id) ?? store.Get<ThemePack>(Collection, id);

    public string ActiveThemeId => store.Get<ActiveThemeRecord>(StateCollection, "active")?.Id ?? BuiltinThemes.All[0].Id;

    public ThemePack ActiveTheme => GetTheme(ActiveThemeId) ?? BuiltinThemes.All[0];

    public ThemeSchedule Schedule
    {
        get => store.Get<ThemeSchedule>(StateCollection, "schedule") ?? new ThemeSchedule();
        set { store.Put(StateCollection, "schedule", value); ThemesChanged?.Invoke(); }
    }

    public ThemePack Save(ThemePack theme)
    {
        if (theme.BuiltIn) throw new InvalidOperationException("Built-in themes are read-only. Duplicate the theme to customize it.");
        if (string.IsNullOrWhiteSpace(theme.Name)) throw new ArgumentException("A theme needs a name.");
        var saved = theme with { UpdatedAt = DateTimeOffset.UtcNow };
        store.Put(Collection, saved.Id, saved);
        ThemesChanged?.Invoke();
        if (saved.Id == ActiveThemeId) ThemeApplied?.Invoke(saved);
        return saved;
    }

    public ThemePack Duplicate(string id, string? newName = null)
    {
        var source = GetTheme(id) ?? throw new KeyNotFoundException($"Theme '{id}' not found.");
        var copy = source with
        {
            Id = NewId(newName ?? source.Name + " copy"), Name = newName ?? source.Name + " (copy)", BuiltIn = false, Author = "You",
            Palette = source.Palette with { Id = "custom" }, CreatedAt = DateTimeOffset.UtcNow,
        };
        return Save(copy);
    }

    public bool Delete(string id)
    {
        if (GetTheme(id) is { BuiltIn: true }) return false;
        if (id == ActiveThemeId) store.Put(StateCollection, "active", new ActiveThemeRecord(BuiltinThemes.All[0].Id));
        var ok = store.Delete(Collection, id);
        if (ok) ThemesChanged?.Invoke();
        return ok;
    }

    /// <summary>Applies the interface palette only (no Windows changes). Used for previews and app-only themes.</summary>
    public void SetActiveAppTheme(string id)
    {
        var theme = GetTheme(id) ?? throw new KeyNotFoundException($"Theme '{id}' not found.");
        store.Put(StateCollection, "active", new ActiveThemeRecord(id));
        ThemeApplied?.Invoke(theme);
        events.Publish(new ThemeAppliedEvent(theme));
    }

    public async Task<OperationResult> ApplyAsync(string id, ThemeApplyOptions? options = null, CancellationToken ct = default)
    {
        options ??= new ThemeApplyOptions();
        var theme = GetTheme(id);
        if (theme is null) return OperationResult.Fail($"Theme '{id}' was not found.");
        var warnings = new List<string>();

        CreateRollback();
        try
        {
            if (options.ApplyWindowsSettings && environment.IsWindows) personalization.Apply(theme.Windows);

            if (options.ApplyWallpaper && theme.Wallpaper is { } wp && wallpaper.IsSupported)
            {
                if (wp.Path is { } path)
                {
                    if (File.Exists(path)) { wallpaper.SetWallpaper(null, path); wallpaper.SetFit(wp.Fit); }
                    else warnings.Add($"Wallpaper file not found: {path}");
                }
                foreach (var (monitor, file) in wp.PerMonitor ?? [])
                    if (File.Exists(file)) wallpaper.SetWallpaper(monitor, file); else warnings.Add($"Wallpaper file not found: {file}");
            }

            if (options.ApplyCursor && theme.CursorScheme is { } schemeName)
            {
                var scheme = cursors.GetSchemes().FirstOrDefault(s => s.Name.Equals(schemeName, StringComparison.OrdinalIgnoreCase));
                if (scheme is not null) cursors.ApplyScheme(scheme); else warnings.Add($"Cursor scheme '{schemeName}' is not installed.");
            }

            if (options.ApplyExtensions)
                foreach (var step in steps)
                {
                    var r = await step.ApplyAsync(theme, ct).ConfigureAwait(false);
                    if (!r.Success) warnings.Add($"{step.Name}: {r.Message}");
                }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Applying theme {Theme} failed", id);
            var rollback = Rollback();
            audit.Record(new AuditEntry(DateTimeOffset.UtcNow, "themes", "apply", id, false, ex.Message));
            return OperationResult.Fail($"Applying '{theme.Name}' failed. {(rollback.Success ? "Your previous look was restored." : rollback.Message)}", ex.Message);
        }

        store.Put(StateCollection, "active", new ActiveThemeRecord(id));
        audit.Record(new AuditEntry(DateTimeOffset.UtcNow, "themes", "apply", id, true, warnings.Count > 0 ? string.Join("; ", warnings) : null));
        ThemeApplied?.Invoke(theme);
        events.Publish(new ThemeAppliedEvent(theme));
        return warnings.Count == 0
            ? OperationResult.Ok($"'{theme.Name}' applied.")
            : new OperationResult(true, $"'{theme.Name}' applied with warnings.", warnings);
    }

    public bool CanRollback => snapshots.GetLatest("themes", "rollback") is not null;

    /// <summary>Restores the look captured before the last theme was applied.</summary>
    public OperationResult Rollback()
    {
        var rec = snapshots.GetLatest("themes", "rollback");
        if (rec is null) return OperationResult.Fail("There is nothing to roll back.");
        var data = AetherJson.Deserialize<ThemeRollback>(rec.PayloadJson);
        if (data is null) return OperationResult.Fail("The rollback snapshot is unreadable.");
        var errors = new List<string>();
        try { if (data.Personalization is { } p && environment.IsWindows) personalization.Restore(p); }
        catch (Exception ex) { errors.Add(ex.Message); }
        if (wallpaper.IsSupported)
            foreach (var (monitor, file) in data.Wallpapers ?? [])
            {
                try { if (File.Exists(file)) wallpaper.SetWallpaper(monitor.Length == 0 ? null : monitor, file); }
                catch (Exception ex) { errors.Add(ex.Message); }
            }
        if (data.PreviousThemeId is { } prev && GetTheme(prev) is { } prevTheme)
        {
            store.Put(StateCollection, "active", new ActiveThemeRecord(prev));
            ThemeApplied?.Invoke(prevTheme);
        }
        snapshots.Delete(rec.Id);
        audit.Record(new AuditEntry(DateTimeOffset.UtcNow, "themes", "rollback", data.PreviousThemeId ?? "-", errors.Count == 0, string.Join("; ", errors)));
        return errors.Count == 0 ? OperationResult.Ok("Your previous look was restored.") : OperationResult.Fail("Rollback was incomplete.", errors);
    }

    private void CreateRollback()
    {
        Dictionary<string, string>? walls = null;
        try
        {
            if (wallpaper.IsSupported)
            {
                walls = [];
                foreach (var m in wallpaper.GetMonitors())
                    if (wallpaper.GetWallpaper(m.Id) is { } w) walls[m.Id] = w;
            }
        }
        catch (Exception ex) { logger.LogWarning(ex, "Could not capture wallpapers for rollback"); }

        var data = new ThemeRollback(ActiveThemeId, environment.IsWindows ? personalization.Capture() : null, walls, DateTimeOffset.UtcNow);
        snapshots.Save(new SnapshotRecord($"themes:rollback:{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}", "themes", "rollback", AetherJson.Serialize(data), DateTimeOffset.UtcNow));
        foreach (var old in snapshots.List("themes").Where(s => s.Kind == "rollback").Skip(10)) snapshots.Delete(old.Id);
    }

    // ───────────── Import / export ─────────────

    /// <summary>Exports a theme as a zip containing theme.json and (optionally) its wallpaper.</summary>
    public void Export(string id, Stream destination, bool includeWallpaper = true)
    {
        var theme = GetTheme(id) ?? throw new KeyNotFoundException($"Theme '{id}' not found.");
        using var zip = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: true);
        var exported = theme with { BuiltIn = false };
        if (includeWallpaper && theme.Wallpaper?.Path is { } path && File.Exists(path))
        {
            var name = "wallpaper" + Path.GetExtension(path);
            zip.CreateEntryFromFile(path, name);
            exported = exported with { Wallpaper = theme.Wallpaper with { Path = name, PerMonitor = null } };
        }
        var entry = zip.CreateEntry("theme.json");
        using var s = entry.Open();
        s.Write(Encoding.UTF8.GetBytes(AetherJson.Serialize(exported, indented: true)));
    }

    public OperationResult<ThemePack> Import(Stream source, string assetDirectory)
    {
        try
        {
            using var zip = new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: true);
            var themeEntry = zip.GetEntry("theme.json") ?? throw new InvalidDataException("theme.json is missing.");
            if (themeEntry.Length > 1024 * 1024) throw new InvalidDataException("theme.json is too large.");
            using var reader = new StreamReader(themeEntry.Open());
            var theme = AetherJson.Deserialize<ThemePack>(reader.ReadToEnd()) ?? throw new InvalidDataException("theme.json is empty.");
            if (theme.Format != 1) throw new InvalidDataException($"Unsupported theme format {theme.Format}.");
            Aether.Core.Configuration.ColorValue.Parse(theme.Palette.Accent);
            Aether.Core.Configuration.ColorValue.Parse(theme.Palette.Background);

            var id = NewId(theme.Name);
            var imported = theme with { Id = id, BuiltIn = false, CreatedAt = DateTimeOffset.UtcNow };
            if (theme.Wallpaper?.Path is { } wpName && zip.GetEntry(wpName) is { } wpEntry && Path.GetFileName(wpName) == wpName)
            {
                var ext = Path.GetExtension(wpName).ToLowerInvariant();
                if (ext is not (".jpg" or ".jpeg" or ".png" or ".bmp" or ".webp")) throw new InvalidDataException("Unsupported wallpaper type.");
                Directory.CreateDirectory(assetDirectory);
                var target = Path.Combine(assetDirectory, id + ext);
                wpEntry.ExtractToFile(target, overwrite: true);
                imported = imported with { Wallpaper = theme.Wallpaper with { Path = target, PerMonitor = null } };
            }
            else if (theme.Wallpaper is not null)
            {
                imported = imported with { Wallpaper = null };
            }
            return OperationResult<ThemePack>.Ok(Save(imported), $"Imported '{imported.Name}'.");
        }
        catch (Exception ex) when (ex is InvalidDataException or FormatException or System.Text.Json.JsonException or IOException)
        {
            return OperationResult<ThemePack>.Fail("The file is not a valid AETHER theme.", ex.Message);
        }
    }

    private string NewId(string name)
    {
        var slug = new string(name.ToLowerInvariant().Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');
        if (slug.Length == 0) slug = "theme";
        var id = slug;
        for (var i = 2; GetTheme(id) is not null; i++) id = $"{slug}-{i}";
        return id;
    }

    private sealed record ActiveThemeRecord(string Id);
}
