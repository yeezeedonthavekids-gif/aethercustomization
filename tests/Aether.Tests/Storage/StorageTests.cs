using Aether.Core.Diagnostics;
using Aether.Storage;
using Aether.Tests.Fakes;

namespace Aether.Tests.Storage;

public class StorageTests
{
    private sealed record Doc(string Name, int Value);

    [Fact]
    public void Documents_round_trip_and_export()
    {
        using var t = new TempDirectory();
        var db = new AetherDatabase(Path.Combine(t.Path, "a.db"));
        var store = new SqliteDocumentStore(db);
        store.Put("things", "a", new Doc("A", 1));
        store.Put("things", "a", new Doc("A", 2));
        store.Put("things", "b", new Doc("B", 3));
        Assert.Equal(2, store.Get<Doc>("things", "a")!.Value);
        Assert.Equal(2, store.GetAll<Doc>("things").Count);

        var exported = store.ExportAll();
        Assert.True(store.Delete("things", "a"));
        store.ImportAll(exported, replace: true);
        Assert.Equal(2, store.GetAll<Doc>("things").Count);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
    }

    [Fact]
    public void Migrations_are_idempotent_and_audit_log_queries()
    {
        using var t = new TempDirectory();
        var path = Path.Combine(t.Path, "a.db");
        var db = new AetherDatabase(path);
        var again = new AetherDatabase(path);
        Assert.Equal(db.SchemaVersion, again.SchemaVersion);

        var audit = new SqliteAuditLog(db);
        audit.Record(new AuditEntry(DateTimeOffset.UtcNow, "mods", "enable", "x", true));
        audit.Record(new AuditEntry(DateTimeOffset.UtcNow, "themes", "apply", "y", false, "boom"));
        Assert.Single(audit.Query(category: "mods"));
        Assert.Equal("boom", audit.Query(subject: "y")[0].Details);

        var backup = Path.Combine(t.Path, "copy.db");
        db.BackupTo(backup);
        Assert.Equal(2, new SqliteAuditLog(new AetherDatabase(backup)).Query().Count);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
    }
}
