using Aether.Core.Abstractions;
using Aether.ModEngine.Security;

namespace Aether.ModEngine.Execution;

/// <summary>
/// The registry handed to mod components. Every write is checked against <see cref="RegistryPolicy"/> using
/// the mod's granted permissions and its original value is captured into the mod's baseline first, so
/// component changes are reverted exactly like declarative ones.
/// </summary>
public sealed class GuardedRegistry(string modId, IRegistryAccess inner, BaselineStore baselines, IReadOnlySet<string> granted) : IRegistryAccess
{
    private void Check(RegistryRoot root, string path)
    {
        var decision = RegistryPolicy.Evaluate(root, path, granted);
        if (!decision.Allowed) throw new UnauthorizedAccessException(decision.Reason);
    }

    public RegistryData? GetValue(RegistryRoot root, string path, string name) => inner.GetValue(root, path, name);
    public bool KeyExists(RegistryRoot root, string path) => inner.KeyExists(root, path);
    public IReadOnlyList<string> GetValueNames(RegistryRoot root, string path) => inner.GetValueNames(root, path);
    public IReadOnlyList<string> GetSubKeyNames(RegistryRoot root, string path) => inner.GetSubKeyNames(root, path);

    public void SetValue(RegistryRoot root, string path, string name, RegistryData value)
    {
        Check(root, path);
        baselines.EnsureCaptured(modId, inner, root, path, name);
        inner.SetValue(root, path, name, value);
    }

    public bool DeleteValue(RegistryRoot root, string path, string name)
    {
        Check(root, path);
        baselines.EnsureCaptured(modId, inner, root, path, name);
        return inner.DeleteValue(root, path, name);
    }

    public void CreateKey(RegistryRoot root, string path)
    {
        Check(root, path);
        inner.CreateKey(root, path);
    }

    public bool DeleteKeyIfEmpty(RegistryRoot root, string path)
    {
        Check(root, path);
        return inner.DeleteKeyIfEmpty(root, path);
    }
}
