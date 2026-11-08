using System.Text.Json.Nodes;
using Aether.Core.Abstractions;
using Aether.Core.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Aether.ModEngine.Execution;

/// <summary>
/// Context given to a running component. Access to system services is mediated: <see cref="Require"/>
/// throws unless the user granted the permission, and <see cref="Registry"/> is policy-guarded and baselined.
/// </summary>
public sealed class ModComponentContext
{
    private readonly IReadOnlySet<string> _granted;
    private readonly IServiceProvider _services;

    public ModComponentContext(string modId, JsonObject settings, IReadOnlySet<string> granted, IRegistryAccess guardedRegistry,
        IServiceProvider services, ILogger logger, BaselineStore baselines, ISystemSettings systemSettings)
    {
        ModId = modId;
        Settings = settings;
        _granted = granted;
        Registry = guardedRegistry;
        _services = services;
        Logger = logger;
        Baselines = baselines;
        SystemSettings = systemSettings;
    }

    public string ModId { get; }
    public JsonObject Settings { get; internal set; }
    public IRegistryAccess Registry { get; }
    public ILogger Logger { get; }
    internal BaselineStore Baselines { get; }
    private ISystemSettings SystemSettings { get; }

    public bool Has(string permission) => _granted.Contains(permission);

    public void Require(string permission)
    {
        if (!Has(permission)) throw new UnauthorizedAccessException($"The mod '{ModId}' was not granted '{permission}'.");
    }

    /// <summary>Resolves a host service after checking the permission that guards it.</summary>
    public T GetService<T>(string requiredPermission) where T : class
    {
        Require(requiredPermission);
        return (T?)_services.GetService(typeof(T)) ?? throw new InvalidOperationException($"Service {typeof(T).Name} is not available on this system.");
    }

    /// <summary>Sets a system flag, capturing its original value so disabling the mod restores it.</summary>
    public void SetFlag(SystemFlag flag, bool value)
    {
        Require(Manifest.ModPermissions.SystemParameters);
        Baselines.EnsureCaptured(ModId, SystemSettings, flag);
        SystemSettings.SetFlag(flag, value);
    }
}

/// <summary>Code-backed mod behaviour. Implementations must leave the system untouched after <see cref="StopAsync"/> + baseline revert.</summary>
public interface IModComponent : IAsyncDisposable
{
    Task StartAsync(ModComponentContext context, CancellationToken ct);
    Task OnSettingsChangedAsync(ModComponentContext context, CancellationToken ct);
    /// <summary>Stops background work. Registry/flag changes are reverted by the engine from the baseline afterwards.</summary>
    Task StopAsync(ModComponentContext context, CancellationToken ct);
    Task<OperationResult> InvokeActionAsync(ModComponentContext context, string actionId, CancellationToken ct);
    /// <summary>Optional status line shown on the mod card (e.g. "Next switch at 19:00").</summary>
    string? Status { get; }
}

/// <summary>Convenience base class with no-op defaults.</summary>
public abstract class ModComponentBase : IModComponent
{
    public virtual string? Status => null;
    public virtual Task StartAsync(ModComponentContext context, CancellationToken ct) => Task.CompletedTask;
    public virtual Task OnSettingsChangedAsync(ModComponentContext context, CancellationToken ct) => Task.CompletedTask;
    public virtual Task StopAsync(ModComponentContext context, CancellationToken ct) => Task.CompletedTask;
    public virtual Task<OperationResult> InvokeActionAsync(ModComponentContext context, string actionId, CancellationToken ct) =>
        Task.FromResult(OperationResult.Fail($"Unknown action '{actionId}'."));
    public virtual ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public interface IModComponentRegistry
{
    void Register(string componentId, Func<IServiceProvider, IModComponent> factory);
    bool IsRegistered(string componentId);
    IModComponent Create(string componentId, IServiceProvider services);
}

public sealed class ModComponentRegistry : IModComponentRegistry
{
    private readonly Dictionary<string, Func<IServiceProvider, IModComponent>> _factories = new(StringComparer.OrdinalIgnoreCase);

    public void Register(string componentId, Func<IServiceProvider, IModComponent> factory)
    {
        lock (_factories) _factories[componentId] = factory;
    }

    public bool IsRegistered(string componentId)
    {
        lock (_factories) return _factories.ContainsKey(componentId);
    }

    public IModComponent Create(string componentId, IServiceProvider services)
    {
        Func<IServiceProvider, IModComponent>? f;
        lock (_factories) _factories.TryGetValue(componentId, out f);
        return f is null ? throw new InvalidOperationException($"Component '{componentId}' is not available in this build.") : f(services);
    }
}
