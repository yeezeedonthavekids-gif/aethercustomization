using System.Text.Json;
using Aether.Storage;

namespace Aether.ModEngine.Manifest;

public static class ManifestSerializer
{
    public static ModManifest Parse(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<ModManifest>(json, AetherJson.Options)
                   ?? throw new ManifestException("The manifest is empty.");
        }
        catch (JsonException ex)
        {
            throw new ManifestException($"The manifest is not valid JSON: {ex.Message}", ex);
        }
    }

    public static string Serialize(ModManifest manifest) => JsonSerializer.Serialize(manifest, AetherJson.Indented);
}

public sealed class ManifestException(string message, Exception? inner = null) : Exception(message, inner);
