using Aether.ModEngine.Manifest;

namespace Aether.ModEngine.Lifecycle;

/// <summary>Supplies the manifests of mods that ship inside AETHER.</summary>
public interface IBuiltinModCatalog
{
    IReadOnlyList<ModManifest> Manifests { get; }
}
