using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Text.Json.Nodes;
using Aether.Core.Abstractions;
using Aether.Core.Configuration;
using Aether.Core.Diagnostics;
using Aether.Core.Versioning;
using Aether.ModEngine.Packaging;
using Aether.ModEngine.Security;
using Aether.SDK;
using Aether.Storage;
using Microsoft.Extensions.Logging;

namespace Aether.PluginSystem;

public enum PluginState { Disabled, Loaded, Failed, Incompatible }

public sealed record PluginInfo(PluginManifest Manifest, string Directory, VerificationLevel Verification, PluginState State, bool Enabled,
    IReadOnlyList<string> MissingCapabilities, string? Error, JsonObject Settings);

public sealed record PluginCommandRegistration(string PluginId, PluginCommand Command);

/// <summary>
/// Discovers, installs, loads and unloads plugins. Each plugin gets its own collectible AssemblyLoadContext
/// (sharing only the SDK with the host) and a capability-checked <see cref="IPluginContext"/>.
/// Load contexts isolate dependencies, not privileges: plugins are .NET code, so AETHER only loads
/// plugins signed by a trusted publisher unless developer mode is on.
/// </summary>
public sealed class PluginHost : IAsyncDisposable
{
    private readonly IAppPaths _paths;
    private readonly IDocumentStore _store;
    private readonly IPermissionGrantRepository _grants;
    private readonly ITrustedPublisherStore _publishers;
    private readonly INotificationSink _notifications;
    private readonly IAuditLog _audit;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<PluginHost> _logger;
    private readonly Dictionary<string, LoadedPlugin> _loaded = new();
    private readonly List<PluginCommandRegistration> _commands = [];
    private readonly Dictionary<string, List<(string PluginId, Action<PluginEvent> Handler)>> _subscriptions = new();
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(20) };

    public PluginHost(IAppPaths paths, IDocumentStore store, IPermissionGrantRepository grants, ITrustedPublisherStore publishers,
        INotificationSink notifications, IAuditLog audit, ILoggerFactory loggerFactory, string hostVersion, Func<bool> developerMode)
    {
        _paths = paths; _store = store; _grants = grants; _publishers = publishers; _notifications = notifications;
        _audit = audit; _loggerFactory = loggerFactory; _logger = loggerFactory.CreateLogger<PluginHost>();
        HostVersion = hostVersion; DeveloperMode = developerMode;
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("AETHER-Plugin/" + hostVersion);
    }

    public string HostVersion { get; }
    public Func<bool> DeveloperMode { get; }
    public event Action? Changed;

    public static string Subject(string pluginId) => "plugin:" + pluginId;

    public IReadOnlyList<PluginCommandRegistration> Commands { get { lock (_commands) return [.. _commands]; } }

    public IReadOnlyList<PluginInfo> GetPlugins()
    {
        var list = new List<PluginInfo>();
        if (!Directory.Exists(_paths.Plugins)) return list;
        foreach (var dir in Directory.GetDirectories(_paths.Plugins))
        {
            var manifestPath = Path.Combine(dir, PluginManifest.FileName);
            if (!File.Exists(manifestPath)) continue;
            try
            {
                var manifest = AetherJson.Deserialize<PluginManifest>(File.ReadAllText(manifestPath))!;
                var package = ModPackageFiles(dir);
                var verification = PackageIntegrity.Verify(package, _publishers.GetAll()).Level;
                var state = _store.Get<PluginStateRecord>("plugin-state", manifest.Id) ?? new PluginStateRecord(false, null, []);
                var missing = manifest.Capabilities.Where(c => !_grants.GetGrants(Subject(manifest.Id)).Contains(c)).ToList();
                var compatible = SemVersion.TryParse(manifest.MinHostVersion, out var need) && SemVersion.Parse(HostVersion) >= need;
                PluginState s;
                lock (_loaded) s = !compatible ? PluginState.Incompatible : _loaded.ContainsKey(manifest.Id) ? PluginState.Loaded : state.Error is not null ? PluginState.Failed : PluginState.Disabled;
                list.Add(new PluginInfo(manifest, dir, verification, s, state.Enabled, missing, state.Error,
                    SettingValues.Resolve(manifest.Settings, state.Settings).Values));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Ignoring invalid plugin folder {Dir}", dir);
            }
        }
        return list;
    }

    public OperationResult Install(string packagePath)
    {
        try
        {
            using var fs = File.OpenRead(packagePath);
            using var zip = new System.IO.Compression.ZipArchive(fs);
            var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            long total = 0;
            foreach (var e in zip.Entries.Where(e => !e.FullName.EndsWith('/')))
            {
                var p = ModPackage.NormalizePath(e.FullName) ?? throw new InvalidDataException($"Unsafe path '{e.FullName}'.");
                total += e.Length;
                if (total > ModPackage.MaxTotalBytes || files.Count > ModPackage.MaxEntries) throw new InvalidDataException("The package is too large.");
                using var s = e.Open(); using var ms = new MemoryStream(); s.CopyTo(ms); files[p] = ms.ToArray();
            }
            if (!files.TryGetValue(PluginManifest.FileName, out var mb)) throw new InvalidDataException($"{PluginManifest.FileName} is missing.");
            var manifest = AetherJson.Deserialize<PluginManifest>(Encoding.UTF8.GetString(mb)) ?? throw new InvalidDataException("Empty manifest.");
            var errors = Validate(manifest, files);
            if (errors.Count > 0) return OperationResult.Fail("The plugin package is invalid.", errors);

            var verification = PackageIntegrity.Verify(files, _publishers.GetAll());
            if (verification.Level == VerificationLevel.Invalid) return OperationResult.Fail("Blocked: " + verification.Message);
            if (verification.Level != VerificationLevel.Trusted && !DeveloperMode())
                return OperationResult.Fail("Only plugins signed by a trusted publisher can be installed. Enable developer mode to load other plugins.", verification.Message);

            var target = Path.Combine(_paths.Plugins, manifest.Id);
            Unload(manifest.Id);
            if (Directory.Exists(target)) Directory.Delete(target, true);
            ModPackage.ExtractFiles(files, target);
            _audit.Record(new AuditEntry(DateTimeOffset.UtcNow, "plugins", "install", manifest.Id, true, verification.Message));
            Changed?.Invoke();
            return OperationResult.Ok($"Installed {manifest.Name} {manifest.Version}.");
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            return OperationResult.Fail("The plugin could not be installed.", ex.Message);
        }
    }

    public OperationResult Uninstall(string id)
    {
        Unload(id);
        var dir = Path.Combine(_paths.Plugins, id);
        try
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            _store.Delete("plugin-state", id);
            _grants.RevokeAll(Subject(id));
            _audit.Record(new AuditEntry(DateTimeOffset.UtcNow, "plugins", "uninstall", id, true));
            Changed?.Invoke();
            return OperationResult.Ok("Plugin removed.");
        }
        catch (Exception ex) { return OperationResult.Fail("The plugin files could not be removed.", ex.Message); }
    }

    public void GrantCapabilities(string id, IEnumerable<string> capabilities)
    {
        _grants.Grant(Subject(id), capabilities);
        Changed?.Invoke();
    }

    public async Task<OperationResult> EnableAsync(string id, CancellationToken ct = default)
    {
        var info = GetPlugins().FirstOrDefault(p => p.Manifest.Id == id);
        if (info is null) return OperationResult.Fail("Plugin not found.");
        if (info.State == PluginState.Incompatible) return OperationResult.Fail($"Requires AETHER {info.Manifest.MinHostVersion} or newer.");
        if (info.MissingCapabilities.Count > 0) return OperationResult.Fail("Grant the plugin's capabilities first.", info.MissingCapabilities.ToArray());
        if (info.Verification == VerificationLevel.Invalid) return OperationResult.Fail("The plugin files fail the integrity check.");
        if (info.Verification != VerificationLevel.Trusted && !DeveloperMode()) return OperationResult.Fail("The plugin is not from a trusted publisher. Enable developer mode to load it.");
        SaveState(id, s => s with { Enabled = true });
        var r = await LoadAsync(info, ct).ConfigureAwait(false);
        Changed?.Invoke();
        return r;
    }

    public async Task<OperationResult> DisableAsync(string id, CancellationToken ct = default)
    {
        SaveState(id, s => s with { Enabled = false, Error = null });
        await UnloadAsync(id, ct).ConfigureAwait(false);
        Changed?.Invoke();
        return OperationResult.Ok("Plugin disabled.");
    }

    public async Task UpdateSettingsAsync(string id, JsonObject settings, CancellationToken ct = default)
    {
        SaveState(id, s => s with { Settings = settings });
        LoadedPlugin? lp;
        lock (_loaded) _loaded.TryGetValue(id, out lp);
        if (lp is not null)
        {
            try { await lp.Instance.OnSettingsChangedAsync(settings, ct).ConfigureAwait(false); }
            catch (Exception ex) { _logger.LogError(ex, "Plugin {Id} failed to apply settings", id); }
        }
    }

    /// <summary>Loads every enabled plugin. Plugins are only loaded when enabled (on demand).</summary>
    public async Task StartAsync(CancellationToken ct = default)
    {
        foreach (var p in GetPlugins().Where(p => p.Enabled && p.State != PluginState.Incompatible))
            await LoadAsync(p, ct).ConfigureAwait(false);
    }

    public void Publish(string eventName, IReadOnlyDictionary<string, string> data)
    {
        List<(string, Action<PluginEvent>)> handlers;
        lock (_subscriptions) handlers = _subscriptions.TryGetValue(eventName, out var h) ? [.. h] : [];
        foreach (var (pluginId, handler) in handlers)
        {
            try { handler(new PluginEvent(eventName, data)); }
            catch (Exception ex) { _logger.LogError(ex, "Plugin {Id} event handler failed", pluginId); }
        }
    }

    private async Task<OperationResult> LoadAsync(PluginInfo info, CancellationToken ct)
    {
        lock (_loaded) if (_loaded.ContainsKey(info.Manifest.Id)) return OperationResult.Ok("Already loaded.");
        var alc = new PluginLoadContext(Path.Combine(info.Directory, info.Manifest.EntryAssembly));
        try
        {
            var assembly = alc.LoadFromAssemblyPath(Path.GetFullPath(Path.Combine(info.Directory, info.Manifest.EntryAssembly)));
            var type = assembly.GetType(info.Manifest.EntryType, throwOnError: true)!;
            if (!typeof(IAetherPlugin).IsAssignableFrom(type)) throw new InvalidOperationException($"{type.FullName} does not implement IAetherPlugin.");
            var instance = (IAetherPlugin)Activator.CreateInstance(type)!;
            var granted = _grants.GetGrants(Subject(info.Manifest.Id));
            var context = new PluginContext(this, info.Manifest, info.Settings, granted, _loggerFactory.CreateLogger("Plugin." + info.Manifest.Id));
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            await instance.InitializeAsync(context, timeout.Token).ConfigureAwait(false);
            lock (_loaded) _loaded[info.Manifest.Id] = new LoadedPlugin(instance, alc, context);
            SaveState(info.Manifest.Id, s => s with { Error = null });
            _audit.Record(new AuditEntry(DateTimeOffset.UtcNow, "plugins", "load", info.Manifest.Id, true));
            return OperationResult.Ok($"{info.Manifest.Name} loaded.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Plugin {Id} failed to load", info.Manifest.Id);
            RemoveRegistrations(info.Manifest.Id);
            alc.Unload();
            SaveState(info.Manifest.Id, s => s with { Error = ex.Message });
            _audit.Record(new AuditEntry(DateTimeOffset.UtcNow, "plugins", "load", info.Manifest.Id, false, ex.Message));
            return OperationResult.Fail($"{info.Manifest.Name} failed to load.", ex.Message);
        }
    }

    private void Unload(string id) => UnloadAsync(id, CancellationToken.None).GetAwaiter().GetResult();

    private async Task UnloadAsync(string id, CancellationToken ct)
    {
        LoadedPlugin? lp;
        lock (_loaded) _loaded.Remove(id, out lp);
        if (lp is null) return;
        try { await lp.Instance.ShutdownAsync(ct).ConfigureAwait(false); }
        catch (Exception ex) { _logger.LogWarning(ex, "Plugin {Id} shutdown failed", id); }
        RemoveRegistrations(id);
        lp.Context.Unload();
    }

    private void RemoveRegistrations(string id)
    {
        lock (_commands) _commands.RemoveAll(c => c.PluginId == id);
        lock (_subscriptions) foreach (var list in _subscriptions.Values) list.RemoveAll(s => s.PluginId == id);
    }

    internal IDisposable AddCommand(string pluginId, PluginCommand command)
    {
        var reg = new PluginCommandRegistration(pluginId, command);
        lock (_commands) _commands.Add(reg);
        Changed?.Invoke();
        return new Disposer(() => { lock (_commands) _commands.Remove(reg); Changed?.Invoke(); });
    }

    internal IDisposable AddSubscription(string pluginId, string eventName, Action<PluginEvent> handler)
    {
        var entry = (pluginId, handler);
        lock (_subscriptions)
        {
            if (!_subscriptions.TryGetValue(eventName, out var list)) _subscriptions[eventName] = list = [];
            list.Add(entry);
        }
        return new Disposer(() => { lock (_subscriptions) _subscriptions[eventName].Remove(entry); });
    }

    internal void NotifyFromPlugin(string pluginName, string title, string message) =>
        _notifications.Notify(new AppNotification($"{title}", $"{message}\n— {pluginName}"));

    internal HttpClient Http => _http;

    internal IPluginStorage StorageFor(string pluginId) => new DocumentPluginStorage(_store, pluginId);

    private void SaveState(string id, Func<PluginStateRecord, PluginStateRecord> change)
    {
        var current = _store.Get<PluginStateRecord>("plugin-state", id) ?? new PluginStateRecord(false, null, []);
        _store.Put("plugin-state", id, change(current));
    }

    public static IReadOnlyList<string> Validate(PluginManifest m, IReadOnlyDictionary<string, byte[]> files)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(m.Id) || m.Id.Any(c => !(char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c is '.' or '-')))
            errors.Add("Id must be lowercase letters, digits, dots and dashes.");
        if (!SemVersion.TryParse(m.Version, out _)) errors.Add($"'{m.Version}' is not a valid version.");
        if (!SemVersion.TryParse(m.MinHostVersion, out _)) errors.Add($"'{m.MinHostVersion}' is not a valid host version.");
        if (ModPackage.NormalizePath(m.EntryAssembly) is not { } entry || !files.ContainsKey(entry)) errors.Add($"Entry assembly '{m.EntryAssembly}' is missing.");
        foreach (var c in m.Capabilities)
            if (!PluginCapabilities.Descriptions.ContainsKey(c)) errors.Add($"Unknown capability '{c}'.");
        return errors;
    }

    private static Dictionary<string, byte[]> ModPackageFiles(string dir) =>
        Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
            .ToDictionary(f => Path.GetRelativePath(dir, f).Replace('\\', '/'), File.ReadAllBytes, StringComparer.Ordinal);

    public async ValueTask DisposeAsync()
    {
        string[] ids;
        lock (_loaded) ids = [.. _loaded.Keys];
        foreach (var id in ids) await UnloadAsync(id, CancellationToken.None).ConfigureAwait(false);
        _http.Dispose();
    }

    private sealed record LoadedPlugin(IAetherPlugin Instance, PluginLoadContext Context, PluginContext PluginContext);
    private sealed record PluginStateRecord(bool Enabled, string? Error, JsonObject Settings);

    private sealed class Disposer(Action action) : IDisposable
    {
        private Action? _action = action;
        public void Dispose() => Interlocked.Exchange(ref _action, null)?.Invoke();
    }

    private sealed class DocumentPluginStorage(IDocumentStore store, string pluginId) : IPluginStorage
    {
        private sealed record Entry(string? Value);
        public string? Get(string key) => store.Get<Entry>("plugin-data:" + pluginId, key)?.Value;
        public void Set(string key, string? value)
        {
            if (value is null) store.Delete("plugin-data:" + pluginId, key);
            else store.Put("plugin-data:" + pluginId, key, new Entry(value));
        }
    }
}

/// <summary>Collectible load context that resolves plugin dependencies locally but shares AETHER's SDK assemblies.</summary>
internal sealed class PluginLoadContext(string entryPath) : AssemblyLoadContext(isCollectible: true)
{
    private readonly AssemblyDependencyResolver _resolver = new(entryPath);
    private static readonly HashSet<string> Shared = ["Aether.SDK", "Aether.Core", "Microsoft.Extensions.Logging.Abstractions"];

    protected override Assembly? Load(AssemblyName name)
    {
        if (name.Name is { } n && Shared.Contains(n)) return null; // fall back to the host's copy
        var path = _resolver.ResolveAssemblyToPath(name);
        return path is null ? null : LoadFromAssemblyPath(path);
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        return path is null ? IntPtr.Zero : LoadUnmanagedDllFromPath(path);
    }
}

internal sealed class PluginContext(PluginHost host, PluginManifest manifest, JsonObject settings, IReadOnlySet<string> granted, ILogger logger) : IPluginContext
{
    private readonly HashSet<string> _effective = new(manifest.Capabilities.Where(granted.Contains), StringComparer.OrdinalIgnoreCase);

    public string PluginId => manifest.Id;
    public string HostVersion => host.HostVersion;
    public JsonObject Settings => settings;
    public IPluginLogger Log { get; } = new Logger(logger);
    public IPluginStorage Storage { get; } = host.StorageFor(manifest.Id);
    public bool HasCapability(string capability) => _effective.Contains(capability);

    private void Require(string capability)
    {
        if (!HasCapability(capability))
            throw new UnauthorizedAccessException($"Plugin '{manifest.Id}' does not have the '{capability}' capability.");
    }

    public IDisposable RegisterCommand(PluginCommand command) { Require(PluginCapabilities.Commands); return host.AddCommand(manifest.Id, command); }
    public IDisposable Subscribe(string eventName, Action<PluginEvent> handler) { Require(PluginCapabilities.Events); return host.AddSubscription(manifest.Id, eventName, handler); }
    public void Notify(string title, string message) { Require(PluginCapabilities.Notifications); host.NotifyFromPlugin(manifest.Name, title, message); }
    public HttpClient GetHttpClient() { Require(PluginCapabilities.Network); return host.Http; }

    private sealed class Logger(ILogger inner) : IPluginLogger
    {
        public void Info(string message) => inner.LogInformation("{Message}", message);
        public void Warn(string message) => inner.LogWarning("{Message}", message);
        public void Error(string message, Exception? exception = null) => inner.LogError(exception, "{Message}", message);
    }
}
