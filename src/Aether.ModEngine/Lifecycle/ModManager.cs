using System.Text.Json.Nodes;
using Aether.Core.Abstractions;
using Aether.Core.Configuration;
using Aether.Core.Diagnostics;
using Aether.Core.Events;
using Aether.Core.Versioning;
using Aether.ModEngine.Execution;
using Aether.ModEngine.Manifest;
using Aether.ModEngine.Packaging;
using Aether.ModEngine.Security;
using Aether.Storage;
using Microsoft.Extensions.Logging;

namespace Aether.ModEngine.Lifecycle;

/// <summary>
/// Orchestrates the full mod lifecycle: install → consent → enable (transactional apply) → configure →
/// disable (baseline revert) → update/restore → uninstall. Every mutating operation is serialized,
/// audited, recorded in history and never fails silently.
/// </summary>
public sealed class ModManager : IAsyncDisposable
{
    private readonly IEnumerable<IBuiltinModCatalog> _catalogs;
    private readonly IModStateRepository _state;
    private readonly IPermissionGrantRepository _grants;
    private readonly ITrustedPublisherStore _publishers;
    private readonly OperationExecutor _executor;
    private readonly BaselineStore _baselines;
    private readonly PowerShellExecutor _powerShell;
    private readonly IModComponentRegistry _components;
    private readonly CompatibilityChecker _compat;
    private readonly ISystemEnvironment _environment;
    private readonly IRegistryAccess _registry;
    private readonly ISystemSettings _systemSettings;
    private readonly IAuditLog _audit;
    private readonly IEventBus _events;
    private readonly IAppPaths _paths;
    private readonly IServiceProvider _services;
    private readonly ModEngineOptions _options;
    private readonly ILogger<ModManager> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, (IModComponent Component, ModComponentContext Context)> _running = new();
    private Dictionary<string, (ModManifest Manifest, string Source, VerificationLevel Verification, string? Path)> _manifests = new();

    public ModManager(
        IEnumerable<IBuiltinModCatalog> catalogs, IModStateRepository state, IPermissionGrantRepository grants,
        ITrustedPublisherStore publishers, OperationExecutor executor, BaselineStore baselines, PowerShellExecutor powerShell,
        IModComponentRegistry components, CompatibilityChecker compat, ISystemEnvironment environment, IRegistryAccess registry,
        ISystemSettings systemSettings, IAuditLog audit, IEventBus events, IAppPaths paths, IServiceProvider services,
        ModEngineOptions options, ILogger<ModManager> logger)
    {
        _catalogs = catalogs; _state = state; _grants = grants; _publishers = publishers; _executor = executor;
        _baselines = baselines; _powerShell = powerShell; _components = components; _compat = compat;
        _environment = environment; _registry = registry; _systemSettings = systemSettings; _audit = audit;
        _events = events; _paths = paths; _services = services; _options = options; _logger = logger;
        LoadManifests();
    }

    public event Action<ModChangedEvent>? Changed;

    // ───────────────────────────── Queries ─────────────────────────────

    public IReadOnlyList<InstalledMod> GetMods()
    {
        lock (_manifests) return _manifests.Keys.Select(BuildView).OfType<InstalledMod>().OrderBy(m => m.Manifest.Name).ToList();
    }

    public InstalledMod? GetMod(string id)
    {
        lock (_manifests) return BuildView(id);
    }

    public IReadOnlyList<ModHistoryRecord> GetHistory(string? id = null, int limit = 100) => _state.GetHistory(id, limit);

    public IReadOnlyList<string> GetMissingPermissions(string id) =>
        TryGetManifest(id) is { } entry ? MissingPermissionsFor(entry.Manifest, entry.Path) : [];

    /// <summary>Human-readable list of the changes enabling the mod with these settings would make.</summary>
    public IReadOnlyList<string> PreviewChanges(string id, JsonObject? settings = null)
    {
        if (TryGetManifest(id) is not { } entry) return [];
        var resolved = settings is null ? CurrentSettings(entry.Manifest) : SettingValues.Resolve(entry.Manifest.Settings, settings).Values;
        try
        {
            var lines = PlanBuilder.Build(entry.Manifest, resolved).Describe().ToList();
            if (entry.Manifest.Implementation.Kind == ImplementationKind.Component) lines.Add("Runs the built-in AETHER component while enabled");
            if (entry.Manifest.Implementation.Kind == ImplementationKind.PowerShell) lines.Add($"Runs PowerShell script {entry.Manifest.Implementation.ApplyScript}");
            return lines;
        }
        catch (Exception ex) { return [$"Cannot preview: {ex.Message}"]; }
    }

    /// <summary>Returns the PowerShell scripts of a mod (shown in the consent dialog).</summary>
    public (string Apply, string Revert, string Hash)? GetScripts(string id)
    {
        if (TryGetManifest(id) is not { } e || e.Manifest.Implementation.Kind != ImplementationKind.PowerShell || e.Path is null) return null;
        var apply = File.ReadAllText(Path.Combine(e.Path, e.Manifest.Implementation.ApplyScript!));
        var revert = File.ReadAllText(Path.Combine(e.Path, e.Manifest.Implementation.RevertScript!));
        return (apply, revert, PowerShellExecutor.ScriptHash(apply, revert));
    }

    public IReadOnlyList<SemVersion> GetStoredVersions(string id)
    {
        var dir = Path.Combine(_paths.Mods, id);
        if (!Directory.Exists(dir)) return [];
        return Directory.GetDirectories(dir)
            .Select(d => SemVersion.TryParse(Path.GetFileName(d), out var v) ? v : null)
            .OfType<SemVersion>().OrderDescending().ToList();
    }

    // ───────────────────────────── Install ─────────────────────────────

    public PackageInspection Inspect(ModPackage package)
    {
        var m = package.Manifest;
        var errors = ManifestValidator.Validate(m).ToList();
        var verification = PackageIntegrity.Verify(package.Files, _publishers.GetAll());
        var compat = _compat.Check(m);
        var missingDeps = MissingDependencies(m);
        string? installed;
        lock (_manifests) installed = _manifests.TryGetValue(m.Id, out var e) ? e.Manifest.Version.ToString() : null;

        if (installed is not null && TryGetManifest(m.Id) is { Source: ModSources.BuiltIn })
            errors.Add("A built-in mod with this id already exists.");
        if (m.Implementation.Kind == ImplementationKind.Component && !_components.IsRegistered(m.Implementation.Component!))
            errors.Add($"This mod needs component '{m.Implementation.Component}', which is not part of this AETHER version.");
        if (m.Implementation.Kind == ImplementationKind.PowerShell)
            foreach (var script in new[] { m.Implementation.ApplyScript, m.Implementation.RevertScript })
                if (script is not null && package.GetText(script) is null) errors.Add($"Script '{script}' is missing from the package.");

        var trustOk = verification.Level switch
        {
            VerificationLevel.Invalid => false,
            VerificationLevel.Unsigned => _options.AllowUnsignedPackages(),
            _ => true,
        };
        var canInstall = errors.Count == 0 && trustOk && compat.IsCompatible && missingDeps.Count == 0;
        var summary = !trustOk
            ? verification.Level == VerificationLevel.Invalid
                ? $"Blocked: {verification.Message}"
                : "Blocked: unsigned packages are disabled. You can allow them in Settings ▸ Security."
            : errors.Count > 0 ? "The package is invalid."
            : !compat.IsCompatible ? "This mod is not compatible with your system."
            : missingDeps.Count > 0 ? "Install the missing dependencies first."
            : installed is null ? "Ready to install." : $"Ready to update from {installed} to {m.Version}.";

        return new PackageInspection(m, verification, compat, errors, missingDeps, installed, canInstall, summary);
    }

    public async Task<ModActionResult> InstallAsync(ModPackage package, CancellationToken ct = default)
    {
        var inspection = Inspect(package);
        if (!inspection.CanInstall)
            return ModActionResult.Blocked(inspection.Summary, [.. inspection.ValidationErrors, .. inspection.Compatibility.Problems, .. inspection.MissingDependencies.Select(d => "Missing dependency: " + d)]);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var m = package.Manifest;
            var existing = _state.Get(m.Id);
            var wasEnabled = existing?.Enabled == true;
            var oldSettings = existing is null ? null : ParseSettings(existing.SettingsJson);

            if (wasEnabled)
            {
                var off = await DisableCoreAsync(m.Id, ct).ConfigureAwait(false);
                if (!off.Succeeded) return ModActionResult.Failed($"Could not disable the current version before updating: {off.Message}", [.. off.Details]);
            }

            var target = Path.Combine(_paths.Mods, m.Id, m.Version.ToString());
            var staging = target + ".staging-" + Guid.NewGuid().ToString("N")[..8];
            try
            {
                package.ExtractTo(staging);
                if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
                Directory.Move(staging, target);
            }
            catch (Exception ex)
            {
                TryDeleteDirectory(staging);
                Record(m.Id, m.Version.ToString(), existing is null ? "install" : "update", false, ex.Message);
                return ModActionResult.Failed("Could not write the mod files.", ex.Message);
            }

            var now = DateTimeOffset.UtcNow;
            var settings = SettingValues.Resolve(m.Settings, oldSettings).Values;
            _state.Upsert(new ModStateRecord(m.Id, m.Version.ToString(), false, settings.ToJsonString(), ModSources.Package,
                inspection.Verification.Level.ToString(), target, existing?.InstalledAt ?? now, now, null));
            lock (_manifests) _manifests[m.Id] = (m, ModSources.Package, inspection.Verification.Level, target);
            PruneVersions(m.Id);

            var action = existing is null ? "install" : "update";
            Record(m.Id, m.Version.ToString(), action, true, $"{inspection.Verification.Message} Digest {inspection.Verification.Digest[..16]}…");
            Raise(m.Id, action);

            if (wasEnabled)
            {
                var on = await EnableCoreAsync(m.Id, ct).ConfigureAwait(false);
                if (!on.Succeeded)
                    return ModActionResult.Ok($"Updated {m.Name} to {m.Version}, but it could not be re-enabled: {on.Message}", on.MissingPermissions.Select(p => "Needs permission: " + p).ToList());
            }
            return ModActionResult.Ok(existing is null ? $"Installed {m.Name} {m.Version}." : $"Updated {m.Name} to {m.Version}.");
        }
        finally { _gate.Release(); }
    }

    public async Task<ModActionResult> InstallFromFileAsync(string path, CancellationToken ct = default)
    {
        ModPackage package;
        try { package = Directory.Exists(path) ? ModPackage.ReadDirectory(path) : ModPackage.ReadZip(path); }
        catch (Exception ex) when (ex is ManifestException or IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return ModActionResult.Blocked("The file is not a valid AETHER mod package.", ex.Message);
        }
        return await InstallAsync(package, ct).ConfigureAwait(false);
    }

    public async Task<ModActionResult> UninstallAsync(string id, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (TryGetManifest(id) is not { } entry) return ModActionResult.Blocked("This mod is not installed.");
            if (entry.Source == ModSources.BuiltIn) return ModActionResult.Blocked("Built-in mods can be disabled but not uninstalled.");
            var dependents = EnabledDependents(id);
            if (dependents.Count > 0) return ModActionResult.Blocked($"Disable the mods that depend on it first: {string.Join(", ", dependents)}.");

            if (_state.Get(id)?.Enabled == true)
            {
                var off = await DisableCoreAsync(id, ct).ConfigureAwait(false);
                if (!off.Succeeded) return ModActionResult.Failed($"Uninstall stopped because the mod could not be reverted: {off.Message}", [.. off.Details]);
            }

            _state.Delete(id);
            _grants.RevokeAll(id);
            _baselines.Delete(id);
            lock (_manifests) _manifests.Remove(id);
            TryDeleteDirectory(Path.Combine(_paths.Mods, id));
            Record(id, entry.Manifest.Version.ToString(), "uninstall", true, null);
            Raise(id, "uninstall");
            return ModActionResult.Ok($"Uninstalled {entry.Manifest.Name}. All of its changes were reverted.");
        }
        finally { _gate.Release(); }
    }

    public async Task<ModActionResult> RestoreVersionAsync(string id, SemVersion version, CancellationToken ct = default)
    {
        var dir = Path.Combine(_paths.Mods, id, version.ToString());
        if (!Directory.Exists(dir)) return ModActionResult.Blocked($"Version {version} is not stored locally.");
        var result = await InstallFromFileAsync(dir, ct).ConfigureAwait(false);
        if (result.Succeeded) Record(id, version.ToString(), "restore-version", true, null);
        return result;
    }

    // ───────────────────────────── Enable / disable ─────────────────────────────

    public void GrantPermissions(string id, IEnumerable<string> permissions)
    {
        var list = permissions.ToList();
        _grants.Grant(id, list);
        _audit.Record(new AuditEntry(DateTimeOffset.UtcNow, "permissions", "grant", id, true, string.Join(", ", list)));
        Raise(id, "permissions");
    }

    public async Task<ModActionResult> EnableAsync(string id, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try { return await EnableCoreAsync(id, ct).ConfigureAwait(false); }
        finally { _gate.Release(); }
    }

    public async Task<ModActionResult> DisableAsync(string id, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var dependents = EnabledDependents(id);
            if (dependents.Count > 0) return ModActionResult.Blocked($"Disable the mods that depend on it first: {string.Join(", ", dependents)}.");
            return await DisableCoreAsync(id, ct).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public async Task<ModActionResult> UpdateSettingsAsync(string id, JsonObject settings, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (TryGetManifest(id) is not { } entry) return ModActionResult.Blocked("This mod is not installed.");
            var (values, errors) = SettingValues.Resolve(entry.Manifest.Settings, settings);
            if (errors.Count > 0) return ModActionResult.Blocked("Some settings are invalid.", errors.Select(e => $"{e.Key}: {e.Message}").ToArray());

            var record = EnsureState(entry);
            var previous = ParseSettings(record.SettingsJson);
            _state.Upsert(record with { SettingsJson = values.ToJsonString(), UpdatedAt = DateTimeOffset.UtcNow });

            if (!record.Enabled)
            {
                Record(id, record.Version, "configure", true, null);
                Raise(id, "settings");
                return ModActionResult.Ok("Settings saved. They will apply when the mod is enabled.");
            }

            var applied = await ApplyEnabledAsync(entry, values, ct, isReconfigure: true).ConfigureAwait(false);
            if (!applied.Succeeded)
            {
                // Put the old configuration back so state matches the system.
                _state.Upsert(record with { SettingsJson = previous?.ToJsonString() ?? "{}", UpdatedAt = DateTimeOffset.UtcNow });
                await ApplyEnabledAsync(entry, SettingValues.Resolve(entry.Manifest.Settings, previous).Values, ct, isReconfigure: true).ConfigureAwait(false);
            }
            Record(id, record.Version, "configure", applied.Succeeded, applied.Succeeded ? null : applied.Message);
            Raise(id, "settings");
            return applied.Succeeded ? ModActionResult.Ok("Settings applied.", applied.Details) : applied;
        }
        finally { _gate.Release(); }
    }

    public Task<ModActionResult> ResetSettingsAsync(string id, CancellationToken ct = default) =>
        TryGetManifest(id) is { } e
            ? UpdateSettingsAsync(id, SettingValues.ResolveDefaults(e.Manifest.Settings), ct)
            : Task.FromResult(ModActionResult.Blocked("This mod is not installed."));

    public async Task<OperationResult> InvokeActionAsync(string id, string actionId, CancellationToken ct = default)
    {
        if (TryGetManifest(id) is not { } entry) return OperationResult.Fail("This mod is not installed.");
        if (entry.Manifest.Actions.All(a => a.Id != actionId)) return OperationResult.Fail($"Unknown action '{actionId}'.");

        (IModComponent Component, ModComponentContext Context) running;
        bool temporary = false;
        lock (_running)
        {
            if (!_running.TryGetValue(id, out running)) running = default;
        }
        if (running.Component is null)
        {
            // Actions may be used without enabling the mod, but still require granted permissions.
            var missing = MissingPermissionsFor(entry.Manifest, entry.Path);
            if (missing.Count > 0) return OperationResult.Fail("Grant this mod's permissions before using its actions.", missing.ToArray());
            if (entry.Manifest.Implementation.Component is not { } componentId) return OperationResult.Fail("This mod has no actions implementation.");
            running = (_components.Create(componentId, _services), CreateContext(entry.Manifest, CurrentSettings(entry.Manifest)));
            temporary = true;
        }

        try
        {
            var result = await running.Component.InvokeActionAsync(running.Context!, actionId, ct).ConfigureAwait(false);
            _audit.Record(new AuditEntry(DateTimeOffset.UtcNow, "mods", "action:" + actionId, id, result.Success, result.Describe()));
            Raise(id, "action");
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Action {Action} of {Mod} failed", actionId, id);
            _audit.Record(new AuditEntry(DateTimeOffset.UtcNow, "mods", "action:" + actionId, id, false, ex.Message));
            return OperationResult.Fail("The action failed.", ex.Message);
        }
        finally
        {
            if (temporary) await running.Component.DisposeAsync().ConfigureAwait(false);
        }
    }

    // ───────────────────────────── Startup / shutdown ─────────────────────────────

    /// <summary>Re-applies enabled declarative mods (repairing drift) and starts enabled components.</summary>
    public async Task<IReadOnlyList<(string Id, ModActionResult Result)>> StartAsync(CancellationToken ct = default)
    {
        var results = new List<(string, ModActionResult)>();
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            foreach (var record in _state.GetAll().Where(r => r.Enabled))
            {
                if (TryGetManifest(record.Id) is not { } entry) continue;
                if (entry.Verification == VerificationLevel.Invalid)
                {
                    SetError(record, "Installed files no longer match the package signature. The mod was not started.");
                    results.Add((record.Id, ModActionResult.Blocked("Integrity check failed.")));
                    continue;
                }
                var compat = _compat.Check(entry.Manifest);
                if (!compat.IsCompatible)
                {
                    SetError(record, string.Join(" ", compat.Problems));
                    results.Add((record.Id, ModActionResult.Blocked("No longer compatible.", [.. compat.Problems])));
                    continue;
                }
                var settings = SettingValues.Resolve(entry.Manifest.Settings, ParseSettings(record.SettingsJson)).Values;
                var isDeclarativeOnly = entry.Manifest.Implementation.Kind == ImplementationKind.Declarative;
                if (isDeclarativeOnly && !_options.ReapplyOnStartup()) continue;
                if (entry.Manifest.Implementation.Kind == ImplementationKind.PowerShell) continue; // scripts run on explicit enable only

                var result = await ApplyEnabledAsync(entry, settings, ct, isReconfigure: false).ConfigureAwait(false);
                if (!result.Succeeded) SetError(record, result.Message);
                results.Add((record.Id, result));
            }
        }
        finally { _gate.Release(); }
        return results;
    }

    /// <summary>Stops running components at app exit. Persistent changes stay in place.</summary>
    public async Task StopAsync()
    {
        List<(IModComponent Component, ModComponentContext Context)> running;
        lock (_running) { running = [.. _running.Values]; _running.Clear(); }
        foreach (var (component, context) in running)
        {
            try { await component.StopAsync(context, CancellationToken.None).ConfigureAwait(false); }
            catch (Exception ex) { _logger.LogWarning(ex, "Stopping {Mod} failed", context.ModId); }
            await component.DisposeAsync().ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _gate.Dispose();
    }

    // ───────────────────────────── Import / export ─────────────────────────────

    public string ExportConfiguration(IEnumerable<string>? ids = null)
    {
        var filter = ids?.ToHashSet();
        var export = new ModConfigurationExport
        {
            Mods = GetMods().Where(m => filter is null || filter.Contains(m.Id)).Select(m => new ModConfigurationEntry
            {
                Id = m.Id, Version = m.Manifest.Version.ToString(), Enabled = m.IsEnabled, Settings = (JsonObject)m.Settings.DeepClone(),
            }).ToList(),
        };
        return AetherJson.Serialize(export, indented: true);
    }

    /// <summary>Applies settings and enabled state from an export. Mods needing consent are reported, not forced.</summary>
    public async Task<IReadOnlyList<(string Id, ModActionResult Result)>> ImportConfigurationAsync(string json, CancellationToken ct = default)
    {
        ModConfigurationExport? export;
        try { export = AetherJson.Deserialize<ModConfigurationExport>(json); }
        catch (Exception ex) { return [("*", ModActionResult.Blocked("The file is not a valid mod configuration export.", ex.Message))]; }
        if (export is null) return [("*", ModActionResult.Blocked("The file is empty."))];

        var results = new List<(string, ModActionResult)>();
        foreach (var entry in export.Mods)
        {
            if (GetMod(entry.Id) is not { } mod) { results.Add((entry.Id, ModActionResult.Blocked("Not installed — install it first."))); continue; }
            var r = await UpdateSettingsAsync(entry.Id, entry.Settings, ct).ConfigureAwait(false);
            if (r.Succeeded && entry.Enabled != mod.IsEnabled)
                r = entry.Enabled ? await EnableAsync(entry.Id, ct).ConfigureAwait(false) : await DisableAsync(entry.Id, ct).ConfigureAwait(false);
            results.Add((entry.Id, r));
        }
        return results;
    }

    // ───────────────────────────── Core implementation ─────────────────────────────

    private async Task<ModActionResult> EnableCoreAsync(string id, CancellationToken ct)
    {
        if (TryGetManifest(id) is not { } entry) return ModActionResult.Blocked("This mod is not installed.");
        var m = entry.Manifest;
        var record = EnsureState(entry);
        bool isRunning;
        lock (_running) isRunning = _running.ContainsKey(id);
        if (record.Enabled && (isRunning || m.Implementation.Kind != ImplementationKind.Component))
            return ModActionResult.Ok($"{m.Name} is already enabled.");

        if (entry.Verification == VerificationLevel.Invalid)
            return ModActionResult.Blocked("The installed files fail the integrity check. Reinstall the mod.");

        var compat = _compat.Check(m);
        if (!compat.IsCompatible) return ModActionResult.Blocked($"{m.Name} is not compatible with this system.", [.. compat.Problems]);

        var needsAdmin = m.Compatibility.RequiresAdmin || m.Permissions.Contains(ModPermissions.RegistryMachine);
        if (needsAdmin && !_environment.IsAdministrator)
            return ModActionResult.Blocked("Administrator rights are required. Restart AETHER as administrator and try again.");

        foreach (var dep in m.Dependencies.Where(d => !d.Optional))
        {
            var depState = _state.Get(dep.Id);
            var depMod = TryGetManifest(dep.Id);
            if (depMod is null || depState?.Enabled != true)
                return ModActionResult.Blocked($"Enable the required mod '{dep.Id}' first.");
            if (!VersionRange.Parse(dep.VersionRange).IsSatisfiedBy(depMod.Value.Manifest.Version))
                return ModActionResult.Blocked($"Requires {dep.Id} {dep.VersionRange}, but {depMod.Value.Manifest.Version} is installed.");
        }

        var missing = MissingPermissionsFor(m, entry.Path);
        if (missing.Count > 0) return ModActionResult.Consent(missing);

        var settings = SettingValues.Resolve(m.Settings, ParseSettings(record.SettingsJson)).Values;
        var result = await ApplyEnabledAsync(entry, settings, ct, isReconfigure: false).ConfigureAwait(false);

        _state.Upsert(record with { Enabled = result.Succeeded, UpdatedAt = DateTimeOffset.UtcNow, LastError = result.Succeeded ? null : result.Message });
        Record(id, m.Version.ToString(), "enable", result.Succeeded, result.Succeeded ? string.Join("; ", result.Details) : result.Message);
        Raise(id, "enable");
        return result.Succeeded ? ModActionResult.Ok($"{m.Name} is now active.", result.Details) : result;
    }

    private async Task<ModActionResult> DisableCoreAsync(string id, CancellationToken ct)
    {
        if (TryGetManifest(id) is not { } entry) return ModActionResult.Blocked("This mod is not installed.");
        var m = entry.Manifest;
        var record = EnsureState(entry);
        var errors = new List<string>();

        (IModComponent Component, ModComponentContext Context) running;
        lock (_running)
        {
            _running.Remove(id, out running);
        }
        if (running.Component is not null)
        {
            try { await running.Component.StopAsync(running.Context, ct).ConfigureAwait(false); }
            catch (Exception ex) { errors.Add($"Component stop: {ex.Message}"); }
            await running.Component.DisposeAsync().ConfigureAwait(false);
        }

        if (m.Implementation.Kind == ImplementationKind.PowerShell && record.Enabled && entry.Path is not null)
        {
            var script = File.ReadAllText(Path.Combine(entry.Path, m.Implementation.RevertScript!));
            var r = await _powerShell.RunAsync(script, SettingValues.Resolve(m.Settings, ParseSettings(record.SettingsJson)).Values, ct).ConfigureAwait(false);
            if (!r.Succeeded) errors.Add($"Revert script exited with {r.ExitCode}: {r.StandardError.Trim()}");
        }

        ExecutionPlan? post = null;
        try { post = PlanBuilder.Build(m, CurrentSettings(m)); } catch { /* post actions are best effort */ }
        var outcome = await _executor.RevertAsync(id, post, ct).ConfigureAwait(false);
        if (!outcome.Success) errors.Add(outcome.Error!);

        var ok = errors.Count == 0;
        _state.Upsert(record with { Enabled = !ok && record.Enabled, UpdatedAt = DateTimeOffset.UtcNow, LastError = ok ? null : string.Join(" ", errors) });
        Record(id, m.Version.ToString(), "disable", ok, ok ? string.Join("; ", outcome.Applied) : string.Join("; ", errors));
        Raise(id, "disable");
        return ok
            ? ModActionResult.Ok($"{m.Name} was disabled and its changes were reverted.", outcome.Applied)
            : ModActionResult.Failed($"{m.Name} could not be fully reverted. Remaining changes are kept for another attempt.", [.. errors]);
    }

    /// <summary>Applies the mod's declarative operations / script / component with the given settings.</summary>
    private async Task<ModActionResult> ApplyEnabledAsync(
        (ModManifest Manifest, string Source, VerificationLevel Verification, string? Path) entry, JsonObject settings, CancellationToken ct, bool isReconfigure)
    {
        var m = entry.Manifest;
        var details = new List<string>();

        ExecutionPlan plan;
        try { plan = PlanBuilder.Build(m, settings); }
        catch (Exception ex) { return ModActionResult.Failed("The mod's configuration could not be resolved.", ex.Message); }

        if (plan.Steps.Count > 0 || plan.Post != PostActions.None)
        {
            var outcome = await _executor.ApplyAsync(m.Id, plan, ct).ConfigureAwait(false);
            details.AddRange(outcome.Applied);
            if (!outcome.Success)
                return ModActionResult.Failed($"Applying {m.Name} failed{(outcome.RolledBack ? " and all changes were rolled back" : "")}.", outcome.Error!);
        }

        switch (m.Implementation.Kind)
        {
            case ImplementationKind.PowerShell:
            {
                if (entry.Path is null) return ModActionResult.Failed("Script files are missing.");
                if (!_powerShell.IsAvailable) return ModActionResult.Blocked("PowerShell is not available on this system.");
                var script = File.ReadAllText(Path.Combine(entry.Path, m.Implementation.ApplyScript!));
                var r = await _powerShell.RunAsync(script, settings, ct).ConfigureAwait(false);
                if (!r.Succeeded)
                {
                    var revert = File.ReadAllText(Path.Combine(entry.Path, m.Implementation.RevertScript!));
                    await _powerShell.RunAsync(revert, settings, CancellationToken.None).ConfigureAwait(false);
                    await _executor.RevertAsync(m.Id, plan, CancellationToken.None).ConfigureAwait(false);
                    return ModActionResult.Failed($"The apply script failed (exit code {r.ExitCode}) and was reverted.", r.StandardError.Trim());
                }
                details.Add("Apply script completed");
                break;
            }
            case ImplementationKind.Component:
            {
                (IModComponent Component, ModComponentContext Context) running;
                lock (_running) _running.TryGetValue(m.Id, out running);
                try
                {
                    if (running.Component is not null)
                    {
                        running.Context.Settings = settings;
                        await running.Component.OnSettingsChangedAsync(running.Context, ct).ConfigureAwait(false);
                    }
                    else
                    {
                        var component = _components.Create(m.Implementation.Component!, _services);
                        var context = CreateContext(m, settings);
                        await component.StartAsync(context, ct).ConfigureAwait(false);
                        lock (_running) _running[m.Id] = (component, context);
                    }
                    details.Add("Component running");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Component {Component} failed", m.Implementation.Component);
                    lock (_running) _running.Remove(m.Id, out running);
                    if (running.Component is not null)
                    {
                        try { await running.Component.StopAsync(running.Context, CancellationToken.None).ConfigureAwait(false); } catch { }
                        await running.Component.DisposeAsync().ConfigureAwait(false);
                    }
                    await _executor.RevertAsync(m.Id, plan, CancellationToken.None).ConfigureAwait(false);
                    return ModActionResult.Failed($"{m.Name} failed to start and its changes were reverted.", ex.Message);
                }
                break;
            }
        }
        return ModActionResult.Ok("Applied.", details);
    }

    private ModComponentContext CreateContext(ModManifest m, JsonObject settings)
    {
        var granted = _grants.GetGrants(m.Id);
        var effective = new HashSet<string>(m.Permissions.Where(granted.Contains), StringComparer.OrdinalIgnoreCase);
        return new ModComponentContext(m.Id, settings, effective, new GuardedRegistry(m.Id, _registry, _baselines, effective),
            _services, _logger, _baselines, _systemSettings)
        {
            RequestDisable = () => Task.Run(async () =>
            {
                var r = await DisableAsync(m.Id).ConfigureAwait(false);
                if (!r.Succeeded) _logger.LogWarning("Self-disable of {Mod} failed: {Message}", m.Id, r.Message);
            }),
        };
    }

    private IReadOnlyList<string> MissingPermissionsFor(ModManifest m, string? path)
    {
        var granted = _grants.GetGrants(m.Id);
        var required = new List<string>(m.Permissions);
        if (m.Implementation.Kind == ImplementationKind.PowerShell && path is not null)
        {
            try
            {
                var apply = File.ReadAllText(Path.Combine(path, m.Implementation.ApplyScript!));
                var revert = File.ReadAllText(Path.Combine(path, m.Implementation.RevertScript!));
                required.Add(PowerShellExecutor.ConsentPermission(PowerShellExecutor.ScriptHash(apply, revert)));
            }
            catch (IOException) { required.Add(ModPermissions.PowerShell + ":missing-script"); }
        }
        return required.Where(p => !granted.Contains(p)).ToList();
    }

    private List<string> MissingDependencies(ModManifest m)
    {
        var missing = new List<string>();
        foreach (var d in m.Dependencies.Where(d => !d.Optional))
        {
            var dep = TryGetManifest(d.Id);
            if (dep is null) missing.Add($"{d.Id} {d.VersionRange}");
            else if (!VersionRange.Parse(d.VersionRange).IsSatisfiedBy(dep.Value.Manifest.Version)) missing.Add($"{d.Id} {d.VersionRange} (installed {dep.Value.Manifest.Version})");
        }
        return missing;
    }

    private List<string> EnabledDependents(string id)
    {
        lock (_manifests)
        {
            return _manifests.Values
                .Where(e => e.Manifest.Dependencies.Any(d => d.Id == id && !d.Optional) && _state.Get(e.Manifest.Id)?.Enabled == true)
                .Select(e => e.Manifest.Name).ToList();
        }
    }

    private void LoadManifests()
    {
        var map = new Dictionary<string, (ModManifest, string, VerificationLevel, string?)>(StringComparer.OrdinalIgnoreCase);
        foreach (var catalog in _catalogs)
            foreach (var m in catalog.Manifests)
            {
                var errors = ManifestValidator.Validate(m);
                if (errors.Count > 0) { _logger.LogError("Built-in mod {Id} is invalid: {Errors}", m.Id, string.Join("; ", errors)); continue; }
                map[m.Id] = (m, ModSources.BuiltIn, VerificationLevel.BuiltIn, null);
            }

        foreach (var record in _state.GetAll().Where(r => r.Source == ModSources.Package && r.InstallPath is not null))
        {
            try
            {
                var package = ModPackage.ReadDirectory(record.InstallPath!);
                var verification = PackageIntegrity.Verify(package.Files, _publishers.GetAll());
                map[record.Id] = (package.Manifest, ModSources.Package, verification.Level, record.InstallPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Could not load installed mod {Id}", record.Id);
                _state.Upsert(record with { LastError = $"Could not load: {ex.Message}", Enabled = record.Enabled });
            }
        }
        _manifests = map;
    }

    private (ModManifest Manifest, string Source, VerificationLevel Verification, string? Path)? TryGetManifest(string id)
    {
        lock (_manifests) return _manifests.TryGetValue(id, out var e) ? e : null;
    }

    private ModStateRecord EnsureState((ModManifest Manifest, string Source, VerificationLevel Verification, string? Path) entry)
    {
        var existing = _state.Get(entry.Manifest.Id);
        if (existing is not null) return existing;
        var now = DateTimeOffset.UtcNow;
        var record = new ModStateRecord(entry.Manifest.Id, entry.Manifest.Version.ToString(), false,
            SettingValues.ResolveDefaults(entry.Manifest.Settings).ToJsonString(), entry.Source, entry.Verification.ToString(), entry.Path, now, now, null);
        _state.Upsert(record);
        return record;
    }

    private JsonObject CurrentSettings(ModManifest m) =>
        SettingValues.Resolve(m.Settings, ParseSettings(_state.Get(m.Id)?.SettingsJson)).Values;

    private static JsonObject? ParseSettings(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonNode.Parse(json) as JsonObject; } catch { return null; }
    }

    private InstalledMod? BuildView(string id)
    {
        if (!_manifests.TryGetValue(id, out var e)) return null;
        var record = _state.Get(id);
        var compat = _compat.Check(e.Manifest);
        string? runtime = null;
        lock (_running) if (_running.TryGetValue(id, out var r)) runtime = r.Component.Status;
        var status = !compat.IsCompatible ? ModStatus.Incompatible
            : record?.LastError is not null && record.Enabled ? ModStatus.Error
            : record?.Enabled == true ? ModStatus.Enabled : ModStatus.Disabled;
        return new InstalledMod
        {
            Manifest = e.Manifest, Source = e.Source, Verification = e.Verification, InstallPath = e.Path,
            Status = status, Settings = SettingValues.Resolve(e.Manifest.Settings, ParseSettings(record?.SettingsJson)).Values,
            Compatibility = compat, MissingPermissions = MissingPermissionsFor(e.Manifest, e.Path),
            LastError = record?.LastError, RuntimeStatus = runtime,
            InstalledAt = record?.InstalledAt ?? DateTimeOffset.MinValue, UpdatedAt = record?.UpdatedAt ?? DateTimeOffset.MinValue,
        };
    }

    private void SetError(ModStateRecord record, string error)
    {
        _state.Upsert(record with { LastError = error, UpdatedAt = DateTimeOffset.UtcNow });
        Raise(record.Id, "error");
    }

    private void PruneVersions(string id)
    {
        foreach (var old in GetStoredVersions(id).Skip(_options.VersionsToKeep))
            TryDeleteDirectory(Path.Combine(_paths.Mods, id, old.ToString()));
    }

    private void Record(string id, string version, string action, bool success, string? details)
    {
        _state.AddHistory(id, version, action, success, details);
        _audit.Record(new AuditEntry(DateTimeOffset.UtcNow, "mods", action, id, success, details));
        if (!success) _logger.LogWarning("Mod {Id} {Action} failed: {Details}", id, action, details);
    }

    private void Raise(string id, string change)
    {
        var e = new ModChangedEvent(id, change);
        Changed?.Invoke(e);
        _events.Publish(e);
    }

    private void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch (Exception ex) { _logger.LogWarning(ex, "Could not delete {Path}", path); }
    }
}
