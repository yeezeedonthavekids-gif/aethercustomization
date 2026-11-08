using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Aether.Core.Abstractions;
using Aether.Core.Diagnostics;
using Aether.ModEngine.Lifecycle;
using Aether.ModEngine.Packaging;
using Aether.Storage;
using Microsoft.Extensions.Logging;

namespace Aether.Services.Backup;

public enum BackupKind { Full, Settings, Themes, Mods, Widgets, Automation }

public sealed record BackupFileEntry(string Path, string Sha256, long Size);

public sealed record BackupManifest
{
    public int Format { get; init; } = 1;
    public required BackupKind Kind { get; init; }
    public required string Description { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required string AppVersion { get; init; }
    public required string MachineName { get; init; }
    public bool Automatic { get; init; }
    public List<BackupFileEntry> Files { get; init; } = [];
    public List<string> Notes { get; init; } = [];
}

public sealed record BackupInfo(string Path, BackupManifest Manifest, long SizeBytes)
{
    public string FileName => System.IO.Path.GetFileName(Path);
}

public sealed record BackupValidation(bool IsValid, IReadOnlyList<string> Problems, BackupManifest? Manifest);

/// <summary>
/// AETHER configuration backups (.aetherbackup zip files). These contain AETHER's own data — settings, themes,
/// widget layouts, automation rules and mod configurations — not a Windows system image. Windows restore points
/// are created separately through <see cref="Platform.IRestorePointService"/> and reported truthfully.
/// </summary>
public sealed class BackupService(IAppPaths paths, IDocumentStore documents, ModManager mods, IAuditLog audit, ILogger<BackupService> logger, string appVersion)
{
    public const string Extension = ".aetherbackup";

    private static readonly Dictionary<BackupKind, string[]> Collections = new()
    {
        [BackupKind.Settings] = ["app"],
        [BackupKind.Themes] = ["themes", "theme-state"],
        [BackupKind.Widgets] = ["widget-layouts", "widget-state"],
        [BackupKind.Automation] = ["automation-rules"],
        [BackupKind.Mods] = [],
    };

    public event Action? BackupsChanged;

    public IReadOnlyList<BackupInfo> List()
    {
        var list = new List<BackupInfo>();
        foreach (var file in Directory.GetFiles(paths.Backups, "*" + Extension))
        {
            try
            {
                using var zip = ZipFile.OpenRead(file);
                var manifest = ReadManifest(zip);
                if (manifest is not null) list.Add(new BackupInfo(file, manifest, new FileInfo(file).Length));
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException) { logger.LogWarning(ex, "Unreadable backup {File}", file); }
        }
        return list.OrderByDescending(b => b.Manifest.CreatedAt).ToList();
    }

    public async Task<OperationResult<BackupInfo>> CreateAsync(BackupKind kind, string description, bool automatic = false, CancellationToken ct = default)
    {
        var stamp = DateTimeOffset.Now;
        var path = Path.Combine(paths.Backups, $"aether-{kind.ToString().ToLowerInvariant()}-{stamp:yyyyMMdd-HHmmss-fff}{Extension}");
        for (var i = 2; File.Exists(path); i++)
            path = Path.Combine(paths.Backups, $"aether-{kind.ToString().ToLowerInvariant()}-{stamp:yyyyMMdd-HHmmss-fff}-{i}{Extension}");
        var temp = path + ".tmp";
        try
        {
            var files = new Dictionary<string, byte[]>();
            var notes = new List<string>();

            var exported = documents.ExportAll()
                .Where(d => kind == BackupKind.Full ? !d.Collection.StartsWith("wallpaper-memento", StringComparison.Ordinal) : Collections[kind].Contains(d.Collection))
                .Select(d => new DocumentRow(d.Collection, d.Id, d.Json)).ToList();
            files["documents.json"] = Encoding.UTF8.GetBytes(AetherJson.Serialize(exported, indented: true));

            if (kind is BackupKind.Full or BackupKind.Mods)
            {
                files["mods/configuration.json"] = Encoding.UTF8.GetBytes(mods.ExportConfiguration());
                foreach (var mod in mods.GetMods().Where(m => !m.IsBuiltIn && m.InstallPath is not null))
                {
                    if (!mod.Manifest.Redistributable) { notes.Add($"{mod.Manifest.Name} was not copied because its license doesn't allow redistribution."); continue; }
                    var package = ModPackage.ReadDirectory(mod.InstallPath!);
                    using var ms = new MemoryStream();
                    package.WriteZip(ms);
                    files[$"mods/packages/{mod.Id}{ModPackage.Extension}"] = ms.ToArray();
                }
            }

            if (kind is BackupKind.Full or BackupKind.Themes && Directory.Exists(paths.Themes))
                foreach (var asset in Directory.GetFiles(paths.Themes))
                    files[$"themes/assets/{Path.GetFileName(asset)}"] = await File.ReadAllBytesAsync(asset, ct).ConfigureAwait(false);

            if (kind == BackupKind.Full) notes.Add("Plugins are not included; reinstall them from their publisher.");

            var manifest = new BackupManifest
            {
                Kind = kind, Description = string.IsNullOrWhiteSpace(description) ? $"{kind} backup" : description.Trim(),
                CreatedAt = stamp, AppVersion = appVersion, MachineName = Environment.MachineName, Automatic = automatic, Notes = notes,
                Files = files.Select(f => new BackupFileEntry(f.Key, Convert.ToHexStringLower(SHA256.HashData(f.Value)), f.Value.Length)).OrderBy(f => f.Path).ToList(),
            };

            await using (var fs = File.Create(temp))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                foreach (var (name, bytes) in files)
                {
                    await using var s = zip.CreateEntry(name, CompressionLevel.Optimal).Open();
                    await s.WriteAsync(bytes, ct).ConfigureAwait(false);
                }
                await using var m = zip.CreateEntry("backup.json").Open();
                await m.WriteAsync(Encoding.UTF8.GetBytes(AetherJson.Serialize(manifest, indented: true)), ct).ConfigureAwait(false);
            }
            File.Move(temp, path, overwrite: false);

            var validation = Validate(path);
            if (!validation.IsValid) throw new InvalidDataException("The backup failed verification: " + string.Join("; ", validation.Problems));

            audit.Record(new AuditEntry(DateTimeOffset.UtcNow, "backups", "create", Path.GetFileName(path), true, manifest.Description));
            BackupsChanged?.Invoke();
            return OperationResult<BackupInfo>.Ok(new BackupInfo(path, manifest, new FileInfo(path).Length), $"Backup created ({manifest.Files.Count} items).");
        }
        catch (Exception ex)
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch (IOException) { }
            logger.LogError(ex, "Backup failed");
            audit.Record(new AuditEntry(DateTimeOffset.UtcNow, "backups", "create", kind.ToString(), false, ex.Message));
            return OperationResult<BackupInfo>.Fail("The backup could not be created.", ex.Message);
        }
    }

    public BackupValidation Validate(string path)
    {
        var problems = new List<string>();
        try
        {
            using var zip = ZipFile.OpenRead(path);
            var manifest = ReadManifest(zip);
            if (manifest is null) return new BackupValidation(false, ["backup.json is missing or unreadable."], null);
            if (manifest.Format != 1) problems.Add($"Unsupported backup format {manifest.Format}.");
            foreach (var f in manifest.Files)
            {
                var entry = zip.GetEntry(f.Path);
                if (entry is null) { problems.Add($"Missing: {f.Path}"); continue; }
                using var s = entry.Open();
                var hash = Convert.ToHexStringLower(SHA256.HashData(s));
                if (hash != f.Sha256) problems.Add($"Corrupted: {f.Path}");
            }
            return new BackupValidation(problems.Count == 0, problems, manifest);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            return new BackupValidation(false, [ex.Message], null);
        }
    }

    public async Task<OperationResult> RestoreAsync(string path, CancellationToken ct = default)
    {
        var validation = Validate(path);
        if (!validation.IsValid) return OperationResult.Fail("This backup can't be restored because it failed verification.", validation.Problems);
        var manifest = validation.Manifest!;

        var safety = await CreateAsync(BackupKind.Full, $"Automatic backup before restoring '{manifest.Description}'", automatic: true, ct).ConfigureAwait(false);
        if (!safety.Success) return OperationResult.Fail("Restore was cancelled because a safety backup of your current setup could not be created.", safety.Errors);

        var warnings = new List<string>();
        try
        {
            using var zip = ZipFile.OpenRead(path);
            string Read(string name) { using var r = new StreamReader(zip.GetEntry(name)!.Open()); return r.ReadToEnd(); }

            var rows = AetherJson.Deserialize<List<DocumentRow>>(Read("documents.json")) ?? [];
            if (manifest.Kind == BackupKind.Full)
                documents.ImportAll(rows.Select(r => (r.Collection, r.Id, r.Json)), replace: true);
            else
            {
                foreach (var collection in Collections[manifest.Kind])
                    foreach (var id in documents.GetIds(collection)) documents.Delete(collection, id);
                documents.ImportAll(rows.Select(r => (r.Collection, r.Id, r.Json)), replace: false);
            }

            foreach (var entry in zip.Entries.Where(e => e.FullName.StartsWith("themes/assets/", StringComparison.Ordinal)))
            {
                var name = Path.GetFileName(entry.FullName);
                if (ModPackage.NormalizePath(name) is null) continue;
                entry.ExtractToFile(Path.Combine(paths.Themes, name), overwrite: true);
            }

            foreach (var entry in zip.Entries.Where(e => e.FullName.StartsWith("mods/packages/", StringComparison.Ordinal)))
            {
                await using var s = entry.Open();
                using var ms = new MemoryStream();
                await s.CopyToAsync(ms, ct).ConfigureAwait(false);
                ms.Position = 0;
                var result = await mods.InstallAsync(ModPackage.ReadZip(ms), ct).ConfigureAwait(false);
                if (!result.Succeeded && mods.GetMod(Path.GetFileNameWithoutExtension(entry.Name)) is null) warnings.Add($"{entry.Name}: {result.Message}");
            }

            if (zip.GetEntry("mods/configuration.json") is not null)
                foreach (var (id, r) in await mods.ImportConfigurationAsync(Read("mods/configuration.json"), ct).ConfigureAwait(false))
                    if (!r.Succeeded) warnings.Add(r.Status == ModActionStatus.NeedsConsent ? $"{id}: grant its permissions to re-enable it." : $"{id}: {r.Message}");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Restore failed");
            audit.Record(new AuditEntry(DateTimeOffset.UtcNow, "backups", "restore", Path.GetFileName(path), false, ex.Message));
            return OperationResult.Fail($"Restore failed. Your previous setup is saved as '{safety.Value!.FileName}' and can be restored from Backups.", ex.Message);
        }

        audit.Record(new AuditEntry(DateTimeOffset.UtcNow, "backups", "restore", Path.GetFileName(path), true, string.Join("; ", warnings)));
        BackupsChanged?.Invoke();
        return warnings.Count == 0
            ? OperationResult.Ok("Backup restored. Some changes may need an AETHER restart to appear everywhere.")
            : new OperationResult(true, "Backup restored with warnings.", warnings);
    }

    /// <summary>Copies an external backup file into the backups folder after verifying it.</summary>
    public OperationResult Import(string sourcePath)
    {
        var v = Validate(sourcePath);
        if (!v.IsValid) return OperationResult.Fail("The file is not a valid AETHER backup.", v.Problems);
        var target = Path.Combine(paths.Backups, Path.GetFileName(sourcePath));
        if (File.Exists(target)) target = Path.Combine(paths.Backups, $"{Path.GetFileNameWithoutExtension(sourcePath)}-{DateTime.Now:HHmmss}{Extension}");
        File.Copy(sourcePath, target);
        BackupsChanged?.Invoke();
        return OperationResult.Ok("Backup imported.");
    }

    public OperationResult Export(string backupPath, string destination)
    {
        File.Copy(backupPath, destination, overwrite: true);
        return OperationResult.Ok("Backup exported.");
    }

    public OperationResult Delete(string backupPath)
    {
        File.Delete(backupPath);
        audit.Record(new AuditEntry(DateTimeOffset.UtcNow, "backups", "delete", Path.GetFileName(backupPath), true));
        BackupsChanged?.Invoke();
        return OperationResult.Ok("Backup deleted.");
    }

    /// <summary>Removes the oldest automatic backups beyond <paramref name="keep"/>.</summary>
    public void Prune(int keep)
    {
        foreach (var old in List().Where(b => b.Manifest.Automatic).Skip(Math.Max(1, keep)))
        {
            try { File.Delete(old.Path); } catch (IOException ex) { logger.LogWarning(ex, "Could not prune {File}", old.Path); }
        }
    }

    private static BackupManifest? ReadManifest(ZipArchive zip)
    {
        var entry = zip.GetEntry("backup.json");
        if (entry is null || entry.Length > 4 * 1024 * 1024) return null;
        using var r = new StreamReader(entry.Open());
        return AetherJson.Deserialize<BackupManifest>(r.ReadToEnd());
    }

    private sealed record DocumentRow(string Collection, string Id, string Json);
}
