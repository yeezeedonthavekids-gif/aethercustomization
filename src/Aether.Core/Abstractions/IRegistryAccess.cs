namespace Aether.Core.Abstractions;

/// <summary>Registry hives AETHER is allowed to address. HKCR/HKU are intentionally absent.</summary>
public enum RegistryRoot
{
    CurrentUser,
    LocalMachine,
}

public enum RegistryDataKind
{
    String,
    ExpandString,
    DWord,
    QWord,
    Binary,
    MultiString,
}

/// <summary>An immutable registry value. <see cref="Data"/> is string, int, long, byte[] or string[].</summary>
public sealed record RegistryData(RegistryDataKind Kind, object Data)
{
    public static RegistryData Dword(int value) => new(RegistryDataKind.DWord, value);
    public static RegistryData Qword(long value) => new(RegistryDataKind.QWord, value);
    public static RegistryData Str(string value) => new(RegistryDataKind.String, value);
    public static RegistryData Bin(byte[] value) => new(RegistryDataKind.Binary, value);

    public bool ValueEquals(RegistryData? other)
    {
        if (other is null || other.Kind != Kind) return false;
        return (Data, other.Data) switch
        {
            (byte[] a, byte[] b) => a.AsSpan().SequenceEqual(b),
            (string[] a, string[] b) => a.SequenceEqual(b),
            _ => Equals(Data, other.Data),
        };
    }

    public override string ToString() => Data switch
    {
        byte[] b => Convert.ToHexString(b),
        string[] s => string.Join(" | ", s),
        _ => Data.ToString() ?? string.Empty,
    };
}

/// <summary>
/// Thin abstraction over the Windows registry so the mod engine can be tested and so every write
/// passes through a single, auditable choke point.
/// </summary>
public interface IRegistryAccess
{
    RegistryData? GetValue(RegistryRoot root, string path, string name);
    void SetValue(RegistryRoot root, string path, string name, RegistryData value);
    /// <returns>True when a value existed and was removed.</returns>
    bool DeleteValue(RegistryRoot root, string path, string name);
    bool KeyExists(RegistryRoot root, string path);
    void CreateKey(RegistryRoot root, string path);
    /// <summary>Deletes a key only if it has no values and no sub keys.</summary>
    bool DeleteKeyIfEmpty(RegistryRoot root, string path);
    IReadOnlyList<string> GetValueNames(RegistryRoot root, string path);
    IReadOnlyList<string> GetSubKeyNames(RegistryRoot root, string path);
}
