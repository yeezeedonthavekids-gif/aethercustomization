using System.Runtime.Versioning;
using Aether.Core.Abstractions;
using Microsoft.Win32;

namespace Aether.WindowsIntegration;

/// <summary>IRegistryAccess over Microsoft.Win32.Registry (64-bit view).</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsRegistryAccess : IRegistryAccess
{
    private static RegistryKey Base(RegistryRoot root) => RegistryKey.OpenBaseKey(
        root == RegistryRoot.CurrentUser ? RegistryHive.CurrentUser : RegistryHive.LocalMachine, RegistryView.Registry64);

    public RegistryData? GetValue(RegistryRoot root, string path, string name)
    {
        using var b = Base(root);
        using var key = b.OpenSubKey(path, writable: false);
        if (key is null) return null;
        var value = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        if (value is null) return null;
        return key.GetValueKind(name) switch
        {
            RegistryValueKind.DWord => RegistryData.Dword(Convert.ToInt32(value)),
            RegistryValueKind.QWord => RegistryData.Qword(Convert.ToInt64(value)),
            RegistryValueKind.Binary => RegistryData.Bin((byte[])value),
            RegistryValueKind.MultiString => new RegistryData(RegistryDataKind.MultiString, (string[])value),
            RegistryValueKind.ExpandString => new RegistryData(RegistryDataKind.ExpandString, (string)value),
            _ => new RegistryData(RegistryDataKind.String, value.ToString() ?? ""),
        };
    }

    public void SetValue(RegistryRoot root, string path, string name, RegistryData value)
    {
        using var b = Base(root);
        using var key = b.CreateSubKey(path, writable: true) ?? throw new IOException($"Could not open {path}.");
        var (data, kind) = value.Kind switch
        {
            RegistryDataKind.DWord => ((object)(int)value.Data, RegistryValueKind.DWord),
            RegistryDataKind.QWord => ((long)value.Data, RegistryValueKind.QWord),
            RegistryDataKind.Binary => (value.Data, RegistryValueKind.Binary),
            RegistryDataKind.MultiString => (value.Data, RegistryValueKind.MultiString),
            RegistryDataKind.ExpandString => (value.Data, RegistryValueKind.ExpandString),
            _ => (value.Data, RegistryValueKind.String),
        };
        key.SetValue(name, data, kind);
    }

    public bool DeleteValue(RegistryRoot root, string path, string name)
    {
        using var b = Base(root);
        using var key = b.OpenSubKey(path, writable: true);
        if (key?.GetValue(name) is null && key?.GetValueNames().Contains(name) != true) return false;
        key!.DeleteValue(name, throwOnMissingValue: false);
        return true;
    }

    public bool KeyExists(RegistryRoot root, string path)
    {
        using var b = Base(root);
        using var key = b.OpenSubKey(path, writable: false);
        return key is not null;
    }

    public void CreateKey(RegistryRoot root, string path)
    {
        using var b = Base(root);
        b.CreateSubKey(path, writable: true)?.Dispose();
    }

    public bool DeleteKeyIfEmpty(RegistryRoot root, string path)
    {
        using var b = Base(root);
        using (var key = b.OpenSubKey(path, writable: false))
        {
            if (key is null || key.ValueCount > 0 || key.SubKeyCount > 0) return false;
        }
        b.DeleteSubKey(path, throwOnMissingSubKey: false);
        return true;
    }

    public IReadOnlyList<string> GetValueNames(RegistryRoot root, string path)
    {
        using var b = Base(root);
        using var key = b.OpenSubKey(path, writable: false);
        return key?.GetValueNames() ?? [];
    }

    public IReadOnlyList<string> GetSubKeyNames(RegistryRoot root, string path)
    {
        using var b = Base(root);
        using var key = b.OpenSubKey(path, writable: false);
        return key?.GetSubKeyNames() ?? [];
    }
}
