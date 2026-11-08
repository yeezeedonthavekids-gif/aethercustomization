using Aether.Core.Diagnostics;

namespace Aether.Storage;

public sealed class SqliteAuditLog(AetherDatabase db) : IAuditLog
{
    public event Action<AuditEntry>? Recorded;

    public void Record(AuditEntry e)
    {
        using (var c = db.Open())
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "INSERT INTO audit_log (timestamp, category, action, subject, success, details) VALUES ($t, $c, $a, $s, $ok, $d)";
            cmd.Parameters.AddWithValue("$t", e.Timestamp.ToString("O"));
            cmd.Parameters.AddWithValue("$c", e.Category);
            cmd.Parameters.AddWithValue("$a", e.Action);
            cmd.Parameters.AddWithValue("$s", e.Subject);
            cmd.Parameters.AddWithValue("$ok", e.Success ? 1 : 0);
            cmd.Parameters.AddWithValue("$d", (object?)e.Details ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }
        Recorded?.Invoke(e);
    }

    public IReadOnlyList<AuditEntry> Query(string? category = null, string? subject = null, int limit = 200)
    {
        using var c = db.Open();
        using var cmd = c.CreateCommand();
        var where = new List<string>();
        if (category is not null) { where.Add("category = $c"); cmd.Parameters.AddWithValue("$c", category); }
        if (subject is not null) { where.Add("subject = $s"); cmd.Parameters.AddWithValue("$s", subject); }
        cmd.CommandText = "SELECT timestamp, category, action, subject, success, details FROM audit_log"
            + (where.Count > 0 ? " WHERE " + string.Join(" AND ", where) : "")
            + " ORDER BY seq DESC LIMIT $l";
        cmd.Parameters.AddWithValue("$l", limit);
        using var r = cmd.ExecuteReader();
        var list = new List<AuditEntry>();
        while (r.Read())
            list.Add(new AuditEntry(DateTimeOffset.Parse(r.GetString(0)), r.GetString(1), r.GetString(2), r.GetString(3), r.GetInt32(4) == 1, r.IsDBNull(5) ? null : r.GetString(5)));
        return list;
    }
}
