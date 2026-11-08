using Aether.Core.Abstractions;
using Aether.Storage;

namespace Aether.ModEngine.Execution;

/// <summary>Original state of one registry value before any mod touched it.</summary>
public sealed class RegistryOriginal
{
    public required RegistryRoot Root { get; init; }
    public required string Path { get; init; }
    public required string Name { get; init; }
    public bool Existed { get; init; }
    public RegistryDataKind Kind { get; init; }
    /// <summary>Serialized data: string, number, base64 (binary) or newline-joined (multi-string).</summary>
    public string? Data { get; init; }
    public bool KeyExisted { get; init; } = true;

    public string Key => $"{Root}\\{Path}\\{Name}".ToLowerInvariant();

    public static RegistryOriginal Capture(IRegistryAccess registry, RegistryRoot root, string path, string name)
    {
        var keyExisted = registry.KeyExists(root, path);
        var value = keyExisted ? registry.GetValue(root, path, name) : null;
        return new RegistryOriginal
        {
            Root = root, Path = path, Name = name, Existed = value is not null, KeyExisted = keyExisted,
            Kind = value?.Kind ?? RegistryDataKind.String,
            Data = value is null ? null : Encode(value),
        };
    }

    public RegistryData? ToData() => Existed && Data is not null ? Decode(Kind, Data) : null;

    public void Restore(IRegistryAccess registry)
    {
        if (ToData() is { } data) registry.SetValue(Root, Path, Name, data);
        else
        {
            registry.DeleteValue(Root, Path, Name);
            if (!KeyExisted) registry.DeleteKeyIfEmpty(Root, Path);
        }
    }

    private static string Encode(RegistryData v) => v.Data switch
    {
        byte[] b => Convert.ToBase64String(b),
        string[] s => string.Join('\n', s),
        var o => Convert.ToString(o, System.Globalization.CultureInfo.InvariantCulture) ?? "",
    };

    private static RegistryData Decode(RegistryDataKind kind, string data) => kind switch
    {
        RegistryDataKind.DWord => RegistryData.Dword(int.Parse(data, System.Globalization.CultureInfo.InvariantCulture)),
        RegistryDataKind.QWord => RegistryData.Qword(long.Parse(data, System.Globalization.CultureInfo.InvariantCulture)),
        RegistryDataKind.Binary => RegistryData.Bin(Convert.FromBase64String(data)),
        RegistryDataKind.MultiString => new RegistryData(kind, data.Split('\n')),
        _ => new RegistryData(kind, data),
    };
}

public sealed class FlagOriginal
{
    public required SystemFlag Flag { get; init; }
    public bool? Value { get; init; }
}

/// <summary>Everything needed to return the system to how it was before a mod was enabled.</summary>
public sealed class ModBaseline
{
    public required string ModId { get; init; }
    public List<RegistryOriginal> Registry { get; init; } = [];
    public List<FlagOriginal> Flags { get; init; } = [];
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public bool IsEmpty => Registry.Count == 0 && Flags.Count == 0;
}

/// <summary>
/// Persists baselines write-ahead: the original value is saved to SQLite <em>before</em> a change is made,
/// so even a crash mid-apply leaves enough information to roll back.
/// </summary>
public sealed class BaselineStore(ISnapshotRepository snapshots)
{
    public const string Kind = "baseline";
    private readonly object _gate = new();

    private static string IdFor(string modId) => $"{modId}:{Kind}";

    public ModBaseline Load(string modId)
    {
        lock (_gate)
        {
            var rec = snapshots.Get(IdFor(modId));
            return rec is null ? new ModBaseline { ModId = modId } : AetherJson.Deserialize<ModBaseline>(rec.PayloadJson) ?? new ModBaseline { ModId = modId };
        }
    }

    public void Save(ModBaseline baseline)
    {
        lock (_gate)
        {
            if (baseline.IsEmpty) { snapshots.Delete(IdFor(baseline.ModId)); return; }
            snapshots.Save(new SnapshotRecord(IdFor(baseline.ModId), baseline.ModId, Kind, AetherJson.Serialize(baseline), baseline.CreatedAt));
        }
    }

    public void Delete(string modId)
    {
        lock (_gate) snapshots.Delete(IdFor(modId));
    }

    /// <summary>Captures the original value of a registry target if this mod hasn't recorded it yet.</summary>
    public void EnsureCaptured(string modId, IRegistryAccess registry, RegistryRoot root, string path, string name)
    {
        lock (_gate)
        {
            var baseline = Load(modId);
            var original = RegistryOriginal.Capture(registry, root, path, name);
            if (baseline.Registry.Any(r => r.Key == original.Key)) return;
            baseline.Registry.Add(original);
            Save(baseline);
        }
    }

    public void EnsureCaptured(string modId, ISystemSettings settings, SystemFlag flag)
    {
        lock (_gate)
        {
            var baseline = Load(modId);
            if (baseline.Flags.Any(f => f.Flag == flag)) return;
            baseline.Flags.Add(new FlagOriginal { Flag = flag, Value = settings.GetFlag(flag) });
            Save(baseline);
        }
    }
}
