using System.Security.Cryptography;
using System.Text;
using Aether.ModEngine.Manifest;
using Aether.ModEngine.Security;
using Aether.Storage;

namespace Aether.ModEngine.Packaging;

/// <summary>Builds (and optionally signs) mod packages. Used by the SDK tooling and tests.</summary>
public sealed class ModPackageBuilder
{
    private readonly Dictionary<string, byte[]> _files = new(StringComparer.Ordinal);

    public ModPackageBuilder WithManifest(ModManifest manifest) =>
        WithFile(ModManifest.FileName, Encoding.UTF8.GetBytes(ManifestSerializer.Serialize(manifest)));

    public ModPackageBuilder WithManifestJson(string json) => WithFile(ModManifest.FileName, Encoding.UTF8.GetBytes(json));

    public ModPackageBuilder WithFile(string path, byte[] content)
    {
        var p = ModPackage.NormalizePath(path) ?? throw new ArgumentException($"Unsafe path '{path}'.", nameof(path));
        _files[p] = content;
        return this;
    }

    public ModPackageBuilder WithText(string path, string content) => WithFile(path, Encoding.UTF8.GetBytes(content));

    public ModPackageBuilder SignWith(ECDsa key, string publisherId, string publisherName)
    {
        _files.Remove(SignatureDocument.FileName);
        var sig = PackageIntegrity.Sign(_files, key, publisherId, publisherName);
        _files[SignatureDocument.FileName] = Encoding.UTF8.GetBytes(AetherJson.Serialize(sig, indented: true));
        return this;
    }

    public ModPackage Build() => ModPackage.FromFiles(new Dictionary<string, byte[]>(_files, StringComparer.Ordinal));

    public byte[] BuildZip()
    {
        using var ms = new MemoryStream();
        Build().WriteZip(ms);
        return ms.ToArray();
    }
}
