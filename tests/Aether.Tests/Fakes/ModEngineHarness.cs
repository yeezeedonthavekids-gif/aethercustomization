using Aether.Core.Abstractions;
using Aether.Core.Events;
using Aether.ModEngine.Execution;
using Aether.ModEngine.Lifecycle;
using Aether.ModEngine.Manifest;
using Aether.ModEngine.Security;
using Aether.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aether.Tests.Fakes;

public sealed class StaticCatalog(params ModManifest[] manifests) : IBuiltinModCatalog
{
    public IReadOnlyList<ModManifest> Manifests { get; } = manifests;
}

/// <summary>Wires a real ModManager against SQLite in a temp folder and in-memory system fakes.</summary>
public sealed class ModEngineHarness : IDisposable, IServiceProvider
{
    private readonly TempDirectory _temp = new();

    public ModEngineHarness(params ModManifest[] builtins)
    {
        Paths = new AppPaths(_temp.Path);
        Database = new AetherDatabase(Paths.DatabaseFile);
        State = new ModStateRepository(Database);
        Grants = new PermissionGrantRepository(Database);
        Documents = new SqliteDocumentStore(Database);
        Publishers = new TrustedPublisherStore(Documents);
        Snapshots = new SnapshotRepository(Database);
        Baselines = new BaselineStore(Snapshots);
        Audit = new SqliteAuditLog(Database);
        Executor = new OperationExecutor(Registry, SystemSettings, Shell, Baselines, NullLogger<OperationExecutor>.Instance);
        Catalog = new StaticCatalog(builtins);
        Rebuild();
    }

    public IAppPaths Paths { get; }
    public AetherDatabase Database { get; }
    public InMemoryRegistry Registry { get; } = new();
    public FakeSystemSettings SystemSettings { get; } = new();
    public FakeShell Shell { get; } = new();
    public FakeEnvironment Environment { get; } = new();
    public FakePowerShell PowerShell { get; } = new();
    public ModStateRepository State { get; }
    public PermissionGrantRepository Grants { get; }
    public SqliteDocumentStore Documents { get; }
    public TrustedPublisherStore Publishers { get; }
    public SnapshotRepository Snapshots { get; }
    public BaselineStore Baselines { get; }
    public SqliteAuditLog Audit { get; }
    public OperationExecutor Executor { get; }
    public ModComponentRegistry Components { get; } = new();
    public ModEngineOptions Options { get; } = new();
    public StaticCatalog Catalog { get; }
    public ModManager Manager { get; private set; } = null!;

    /// <summary>Recreates the manager (simulates an app restart) over the same database.</summary>
    public ModManager Rebuild()
    {
        Manager = new ModManager([Catalog], State, Grants, Publishers, Executor, Baselines, new PowerShellExecutor(PowerShell),
            Components, new CompatibilityChecker(Environment, Options), Environment, Registry, SystemSettings, Audit,
            new EventBus(), Paths, this, Options, NullLogger<ModManager>.Instance);
        return Manager;
    }

    public object? GetService(Type serviceType) => null;

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        _temp.Dispose();
    }
}
