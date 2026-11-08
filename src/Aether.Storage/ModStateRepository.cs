using Microsoft.Data.Sqlite;

namespace Aether.Storage;

public sealed record ModStateRecord(
    string Id,
    string Version,
    bool Enabled,
    string SettingsJson,
    string Source,
    string Verification,
    string? InstallPath,
    DateTimeOffset InstalledAt,
    DateTimeOffset UpdatedAt,
    string? LastError);

public sealed record ModHistoryRecord(long Sequence, string ModId, string Version, string Action, bool Success, string? Details, DateTimeOffset Timestamp);

public interface IModStateRepository
{
    ModStateRecord? Get(string id);
    IReadOnlyList<ModStateRecord> GetAll();
    void Upsert(ModStateRecord record);
    void Delete(string id);
    void AddHistory(string modId, string version, string action, bool success, string? details);
    IReadOnlyList<ModHistoryRecord> GetHistory(string? modId = null, int limit = 100);
}

public sealed class ModStateRepository(AetherDatabase db) : IModStateRepository
{
    public ModStateRecord? Get(string id)
    {
        using var c = db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT * FROM mod_state WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        using var r = cmd.ExecuteReader();
        return r.Read() ? Read(r) : null;
    }

    public IReadOnlyList<ModStateRecord> GetAll()
    {
        using var c = db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT * FROM mod_state ORDER BY id";
        using var r = cmd.ExecuteReader();
        var list = new List<ModStateRecord>();
        while (r.Read()) list.Add(Read(r));
        return list;
    }

    public void Upsert(ModStateRecord m)
    {
        using var c = db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            INSERT INTO mod_state (id, version, enabled, settings_json, source, verification, install_path, installed_at, updated_at, last_error)
            VALUES ($id, $v, $e, $s, $src, $ver, $p, $ia, $ua, $err)
            ON CONFLICT(id) DO UPDATE SET version = excluded.version, enabled = excluded.enabled,
                settings_json = excluded.settings_json, source = excluded.source, verification = excluded.verification,
                install_path = excluded.install_path, updated_at = excluded.updated_at, last_error = excluded.last_error
            """;
        cmd.Parameters.AddWithValue("$id", m.Id);
        cmd.Parameters.AddWithValue("$v", m.Version);
        cmd.Parameters.AddWithValue("$e", m.Enabled ? 1 : 0);
        cmd.Parameters.AddWithValue("$s", m.SettingsJson);
        cmd.Parameters.AddWithValue("$src", m.Source);
        cmd.Parameters.AddWithValue("$ver", m.Verification);
        cmd.Parameters.AddWithValue("$p", (object?)m.InstallPath ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$ia", m.InstalledAt.ToString("O"));
        cmd.Parameters.AddWithValue("$ua", m.UpdatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("$err", (object?)m.LastError ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public void Delete(string id)
    {
        using var c = db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "DELETE FROM mod_state WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    public void AddHistory(string modId, string version, string action, bool success, string? details)
    {
        using var c = db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "INSERT INTO mod_history (mod_id, version, action, success, details, timestamp) VALUES ($m, $v, $a, $s, $d, $t)";
        cmd.Parameters.AddWithValue("$m", modId);
        cmd.Parameters.AddWithValue("$v", version);
        cmd.Parameters.AddWithValue("$a", action);
        cmd.Parameters.AddWithValue("$s", success ? 1 : 0);
        cmd.Parameters.AddWithValue("$d", (object?)details ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$t", DateTimeOffset.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    public IReadOnlyList<ModHistoryRecord> GetHistory(string? modId = null, int limit = 100)
    {
        using var c = db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = modId is null
            ? "SELECT seq, mod_id, version, action, success, details, timestamp FROM mod_history ORDER BY seq DESC LIMIT $l"
            : "SELECT seq, mod_id, version, action, success, details, timestamp FROM mod_history WHERE mod_id = $m ORDER BY seq DESC LIMIT $l";
        cmd.Parameters.AddWithValue("$l", limit);
        if (modId is not null) cmd.Parameters.AddWithValue("$m", modId);
        using var r = cmd.ExecuteReader();
        var list = new List<ModHistoryRecord>();
        while (r.Read())
            list.Add(new ModHistoryRecord(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetInt32(4) == 1,
                r.IsDBNull(5) ? null : r.GetString(5), DateTimeOffset.Parse(r.GetString(6))));
        return list;
    }

    private static ModStateRecord Read(SqliteDataReader r) => new(
        r.GetString(r.GetOrdinal("id")),
        r.GetString(r.GetOrdinal("version")),
        r.GetInt32(r.GetOrdinal("enabled")) == 1,
        r.GetString(r.GetOrdinal("settings_json")),
        r.GetString(r.GetOrdinal("source")),
        r.GetString(r.GetOrdinal("verification")),
        r.IsDBNull(r.GetOrdinal("install_path")) ? null : r.GetString(r.GetOrdinal("install_path")),
        DateTimeOffset.Parse(r.GetString(r.GetOrdinal("installed_at"))),
        DateTimeOffset.Parse(r.GetString(r.GetOrdinal("updated_at"))),
        r.IsDBNull(r.GetOrdinal("last_error")) ? null : r.GetString(r.GetOrdinal("last_error")));
}
