using Aether.Core.Abstractions;
using Aether.Core.Diagnostics;
using Aether.Storage;

namespace Aether.Services.Explorer;

public sealed record CustomizedFolder(string Path, string IconResource, string? OriginalDesktopIni, bool HadSystemAttribute, DateTimeOffset At);

/// <summary>
/// Custom folder icons via the documented desktop.ini [.ShellClassInfo] IconResource mechanism. The original
/// desktop.ini (if any) and folder attributes are remembered so "Reset" restores the folder exactly.
/// </summary>
public sealed class FolderCustomizer(IDocumentStore store, ISystemSettings system, IAuditLog audit)
{
    private const string Collection = "custom-folders";

    public IReadOnlyList<CustomizedFolder> GetCustomized() => store.GetAll<CustomizedFolder>(Collection).Where(f => Directory.Exists(f.Path)).ToList();

    private static string Key(string path) => Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(path.ToLowerInvariant())))[..24];

    public OperationResult SetIcon(string folder, string iconFile, int index)
    {
        if (!Directory.Exists(folder)) return OperationResult.Fail("The folder doesn't exist.");
        if (!File.Exists(iconFile)) return OperationResult.Fail("The icon file doesn't exist.");
        var ini = Path.Combine(folder, "desktop.ini");
        try
        {
            var existing = store.Get<CustomizedFolder>(Collection, Key(folder));
            var original = existing?.OriginalDesktopIni ?? (File.Exists(ini) ? File.ReadAllText(ini) : null);
            var hadSystem = existing?.HadSystemAttribute ?? new DirectoryInfo(folder).Attributes.HasFlag(FileAttributes.System);

            var lines = (original ?? "").Split('\n').Select(l => l.TrimEnd('\r')).Where(l => !l.StartsWith("IconResource=", StringComparison.OrdinalIgnoreCase)).ToList();
            var section = lines.FindIndex(l => l.Trim().Equals("[.ShellClassInfo]", StringComparison.OrdinalIgnoreCase));
            if (section < 0) { lines.Insert(0, "[.ShellClassInfo]"); section = 0; }
            lines.Insert(section + 1, $"IconResource={iconFile},{index}");

            if (File.Exists(ini)) File.SetAttributes(ini, FileAttributes.Normal);
            File.WriteAllText(ini, string.Join("\r\n", lines.Where((l, i) => l.Length > 0 || i < lines.Count - 1)) + "\r\n", System.Text.Encoding.Unicode);
            File.SetAttributes(ini, FileAttributes.Hidden | FileAttributes.System);
            var dir = new DirectoryInfo(folder);
            dir.Attributes |= FileAttributes.ReadOnly; // Explorer only reads desktop.ini on read-only/system folders
            store.Put(Collection, Key(folder), new CustomizedFolder(folder, $"{iconFile},{index}", original, hadSystem, DateTimeOffset.UtcNow));
            system.RefreshShell();
            audit.Record(new AuditEntry(DateTimeOffset.UtcNow, "explorer", "folder-icon", folder, true, iconFile));
            return OperationResult.Ok($"Custom icon set for {Path.GetFileName(folder)}. Explorer may take a moment to refresh its icon cache.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return OperationResult.Fail("The folder icon could not be changed.", ex.Message);
        }
    }

    public OperationResult Reset(string folder)
    {
        var record = store.Get<CustomizedFolder>(Collection, Key(folder));
        if (record is null) return OperationResult.Fail("AETHER didn't customize this folder.");
        var ini = Path.Combine(folder, "desktop.ini");
        try
        {
            if (File.Exists(ini)) File.SetAttributes(ini, FileAttributes.Normal);
            if (record.OriginalDesktopIni is null) { if (File.Exists(ini)) File.Delete(ini); }
            else { File.WriteAllText(ini, record.OriginalDesktopIni, System.Text.Encoding.Unicode); File.SetAttributes(ini, FileAttributes.Hidden | FileAttributes.System); }
            if (record.OriginalDesktopIni is null && !record.HadSystemAttribute)
            {
                var dir = new DirectoryInfo(folder);
                dir.Attributes &= ~FileAttributes.ReadOnly;
            }
            store.Delete(Collection, Key(folder));
            system.RefreshShell();
            audit.Record(new AuditEntry(DateTimeOffset.UtcNow, "explorer", "folder-icon-reset", folder, true));
            return OperationResult.Ok($"{Path.GetFileName(folder)} uses its normal icon again.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return OperationResult.Fail("The folder could not be reset.", ex.Message);
        }
    }
}
