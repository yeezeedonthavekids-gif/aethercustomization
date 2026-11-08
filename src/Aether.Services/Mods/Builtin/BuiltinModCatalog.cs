using Aether.ModEngine.Lifecycle;
using Aether.ModEngine.Manifest;

namespace Aether.Services.Mods.Builtin;

/// <summary>Loads the built-in mod manifests embedded in this assembly.</summary>
public sealed class BuiltinModCatalog : IBuiltinModCatalog
{
    private static readonly Lazy<IReadOnlyList<ModManifest>> Loaded = new(Load);

    public IReadOnlyList<ModManifest> Manifests => Loaded.Value;

    private static IReadOnlyList<ModManifest> Load()
    {
        var assembly = typeof(BuiltinModCatalog).Assembly;
        var list = new List<ModManifest>();
        foreach (var name in assembly.GetManifestResourceNames().Where(n => n.EndsWith(".json", StringComparison.Ordinal)).Order())
        {
            using var stream = assembly.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream);
            list.Add(ManifestSerializer.Parse(reader.ReadToEnd()));
        }
        return list;
    }
}
