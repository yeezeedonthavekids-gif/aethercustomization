using Aether.Core.Abstractions;

namespace Aether.Tests.Fakes;

public sealed class InMemoryRegistry : IRegistryAccess
{
    private readonly Dictionary<string, Dictionary<string, RegistryData>> _keys = new(StringComparer.OrdinalIgnoreCase);
    public int Writes { get; private set; }
    /// <summary>When set, a write to a value with this name throws.</summary>
    public string? FailOnName { get; set; }

    private static string K(RegistryRoot root, string path) => $"{root}\\{path.Trim('\\')}";

    public RegistryData? GetValue(RegistryRoot root, string path, string name) =>
        _keys.TryGetValue(K(root, path), out var v) && v.TryGetValue(name, out var d) ? d : null;

    public void SetValue(RegistryRoot root, string path, string name, RegistryData value)
    {
        if (FailOnName == name) throw new IOException($"Simulated failure writing {name}");
        CreateKey(root, path);
        _keys[K(root, path)][name] = value;
        Writes++;
    }

    public bool DeleteValue(RegistryRoot root, string path, string name) =>
        _keys.TryGetValue(K(root, path), out var v) && v.Remove(name);

    public bool KeyExists(RegistryRoot root, string path) => _keys.ContainsKey(K(root, path));

    public void CreateKey(RegistryRoot root, string path)
    {
        if (!_keys.ContainsKey(K(root, path))) _keys[K(root, path)] = new(StringComparer.OrdinalIgnoreCase);
    }

    public bool DeleteKeyIfEmpty(RegistryRoot root, string path)
    {
        var k = K(root, path);
        if (!_keys.TryGetValue(k, out var v) || v.Count > 0) return false;
        if (_keys.Keys.Any(other => other.StartsWith(k + "\\", StringComparison.OrdinalIgnoreCase))) return false;
        return _keys.Remove(k);
    }

    public IReadOnlyList<string> GetValueNames(RegistryRoot root, string path) =>
        _keys.TryGetValue(K(root, path), out var v) ? v.Keys.ToList() : [];

    public IReadOnlyList<string> GetSubKeyNames(RegistryRoot root, string path)
    {
        var prefix = K(root, path) + "\\";
        return _keys.Keys.Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(k => k[prefix.Length..].Split('\\')[0]).Distinct().ToList();
    }
}
