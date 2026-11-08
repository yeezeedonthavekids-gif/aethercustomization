using Microsoft.Data.Sqlite;

namespace Aether.Storage;

/// <summary>
/// Owns the SQLite database file and its schema migrations. Every repository opens short-lived
/// connections (SQLite pooling makes this cheap) so the database is safe to use from any thread.
/// </summary>
public sealed class AetherDatabase
{
    private static readonly string[] Migrations =
    [
        // 1 — initial schema
        """
        CREATE TABLE IF NOT EXISTS documents (
            collection TEXT NOT NULL,
            id TEXT NOT NULL,
            json TEXT NOT NULL,
            updated_at TEXT NOT NULL,
            PRIMARY KEY (collection, id)
        );
        CREATE TABLE IF NOT EXISTS mod_state (
            id TEXT PRIMARY KEY,
            version TEXT NOT NULL,
            enabled INTEGER NOT NULL DEFAULT 0,
            settings_json TEXT NOT NULL DEFAULT '{}',
            source TEXT NOT NULL,
            verification TEXT NOT NULL,
            install_path TEXT,
            installed_at TEXT NOT NULL,
            updated_at TEXT NOT NULL,
            last_error TEXT
        );
        CREATE TABLE IF NOT EXISTS mod_history (
            seq INTEGER PRIMARY KEY AUTOINCREMENT,
            mod_id TEXT NOT NULL,
            version TEXT NOT NULL,
            action TEXT NOT NULL,
            success INTEGER NOT NULL,
            details TEXT,
            timestamp TEXT NOT NULL
        );
        CREATE INDEX IF NOT EXISTS ix_mod_history_mod ON mod_history (mod_id, seq DESC);
        CREATE TABLE IF NOT EXISTS snapshots (
            id TEXT PRIMARY KEY,
            owner TEXT NOT NULL,
            kind TEXT NOT NULL,
            payload_json TEXT NOT NULL,
            created_at TEXT NOT NULL
        );
        CREATE INDEX IF NOT EXISTS ix_snapshots_owner ON snapshots (owner, kind);
        CREATE TABLE IF NOT EXISTS permission_grants (
            subject TEXT NOT NULL,
            permission TEXT NOT NULL,
            granted_at TEXT NOT NULL,
            PRIMARY KEY (subject, permission)
        );
        CREATE TABLE IF NOT EXISTS audit_log (
            seq INTEGER PRIMARY KEY AUTOINCREMENT,
            timestamp TEXT NOT NULL,
            category TEXT NOT NULL,
            action TEXT NOT NULL,
            subject TEXT NOT NULL,
            success INTEGER NOT NULL,
            details TEXT
        );
        CREATE INDEX IF NOT EXISTS ix_audit_subject ON audit_log (subject, seq DESC);
        """,
    ];

    public AetherDatabase(string path)
    {
        Path = path;
        var dir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        ConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = true,
        }.ToString();
        Migrate();
    }

    public string Path { get; }
    public string ConnectionString { get; }
    public int SchemaVersion { get; private set; }

    public SqliteConnection Open()
    {
        var connection = new SqliteConnection(ConnectionString);
        connection.Open();
        using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 5000;";
        pragma.ExecuteNonQuery();
        return connection;
    }

    private void Migrate()
    {
        using var connection = Open();
        using (var wal = connection.CreateCommand())
        {
            wal.CommandText = "PRAGMA journal_mode = WAL;";
            wal.ExecuteNonQuery();
        }
        using (var init = connection.CreateCommand())
        {
            init.CommandText = "CREATE TABLE IF NOT EXISTS schema_version (version INTEGER NOT NULL);";
            init.ExecuteNonQuery();
        }
        int current;
        using (var read = connection.CreateCommand())
        {
            read.CommandText = "SELECT COALESCE(MAX(version), 0) FROM schema_version;";
            current = Convert.ToInt32(read.ExecuteScalar());
        }
        for (var i = current; i < Migrations.Length; i++)
        {
            using var tx = connection.BeginTransaction();
            using var cmd = connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = Migrations[i] + $"\nINSERT INTO schema_version (version) VALUES ({i + 1});";
            cmd.ExecuteNonQuery();
            tx.Commit();
        }
        SchemaVersion = Migrations.Length;
    }

    /// <summary>Copies the live database into <paramref name="destination"/> using SQLite's online backup API.</summary>
    public void BackupTo(string destination)
    {
        using var source = Open();
        using var target = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = destination, Pooling = false }.ToString());
        target.Open();
        source.BackupDatabase(target);
    }
}
