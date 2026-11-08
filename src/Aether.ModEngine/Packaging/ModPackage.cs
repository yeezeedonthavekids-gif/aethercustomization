using System.IO.Compression;
using System.Text;
using Aether.ModEngine.Manifest;

namespace Aether.ModEngine.Packaging;

/// <summary>An in-memory, validated view of a mod package (.aethermod zip or an extracted folder).</summary>
public sealed class ModPackage
{
    public const string Extension = ".aethermod";
    public const int MaxEntries = 2000;
    public const long MaxTotalBytes = 100L * 1024 * 1024;

    private ModPackage(ModManifest manifest, Dictionary<string, byte[]> files)
    {
        Manifest = manifest;
        Files = files;
    }

    public ModManifest Manifest { get; }
    public IReadOnlyDictionary<string, byte[]> Files { get; }

    public string? GetText(string relativePath) =>
        Files.TryGetValue(NormalizePath(relativePath) ?? "", out var b) ? Encoding.UTF8.GetString(b) : null;

    public static ModPackage FromFiles(Dictionary<string, byte[]> files)
    {
        if (!files.TryGetValue(ModManifest.FileName, out var manifestBytes))
            throw new ManifestException($"The package does not contain {ModManifest.FileName}.");
        var manifest = ManifestSerializer.Parse(Encoding.UTF8.GetString(manifestBytes));
        return new ModPackage(manifest, files);
    }

    public static ModPackage ReadZip(Stream stream)
    {
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        if (zip.Entries.Count > MaxEntries) throw new ManifestException($"The package has more than {MaxEntries} files.");
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        long total = 0;
        foreach (var entry in zip.Entries)
        {
            if (entry.FullName.EndsWith('/')) continue; // directory
            var path = NormalizePath(entry.FullName) ?? throw new ManifestException($"Unsafe path in package: '{entry.FullName}'.");
            total += entry.Length;
            if (entry.Length > MaxTotalBytes || total > MaxTotalBytes) throw new ManifestException("The package is too large.");
            using var s = entry.Open();
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            if (ms.Length != entry.Length) throw new ManifestException($"Corrupt entry '{entry.FullName}'.");
            if (!files.TryAdd(path, ms.ToArray())) throw new ManifestException($"Duplicate entry '{path}'.");
        }
        return FromFiles(files);
    }

    public static ModPackage ReadZip(string path)
    {
        using var fs = File.OpenRead(path);
        return ReadZip(fs);
    }

    public static ModPackage ReadDirectory(string directory)
    {
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        long total = 0;
        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            var rel = NormalizePath(Path.GetRelativePath(directory, file)) ?? throw new ManifestException($"Unsafe path '{file}'.");
            var bytes = File.ReadAllBytes(file);
            total += bytes.Length;
            if (files.Count >= MaxEntries || total > MaxTotalBytes) throw new ManifestException("The package is too large.");
            files[rel] = bytes;
        }
        return FromFiles(files);
    }

    public void WriteZip(Stream destination)
    {
        using var zip = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: true);
        foreach (var (path, bytes) in Files.OrderBy(f => f.Key, StringComparer.Ordinal))
        {
            var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
            entry.LastWriteTime = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
            using var s = entry.Open();
            s.Write(bytes);
        }
    }

    public void ExtractTo(string directory) => ExtractFiles(Files, directory);

    /// <summary>Writes files below <paramref name="directory"/>, refusing any path that escapes it (zip-slip).</summary>
    public static void ExtractFiles(IReadOnlyDictionary<string, byte[]> files, string directory)
    {
        Directory.CreateDirectory(directory);
        var root = Path.GetFullPath(directory);
        foreach (var (path, bytes) in files)
        {
            var full = Path.GetFullPath(Path.Combine(root, path));
            if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                throw new ManifestException($"Unsafe path '{path}'.");
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllBytes(full, bytes);
        }
    }

    /// <summary>Returns a forward-slash relative path, or null if the path escapes the package root.</summary>
    public static string? NormalizePath(string path)
    {
        var p = path.Replace('\\', '/').TrimStart('/');
        if (p.Length == 0 || p.Contains(':') || Path.IsPathRooted(path)) return null;
        var segments = p.Split('/');
        if (segments.Any(s => s is "" or "." or "..")) return null;
        return p;
    }
}
