namespace Aether.Storage;

/// <summary>Remembers which permissions the user explicitly granted to a mod or plugin.</summary>
public interface IPermissionGrantRepository
{
    IReadOnlySet<string> GetGrants(string subject);
    void Grant(string subject, IEnumerable<string> permissions);
    void RevokeAll(string subject);
}

public sealed class PermissionGrantRepository(AetherDatabase db) : IPermissionGrantRepository
{
    public IReadOnlySet<string> GetGrants(string subject)
    {
        using var c = db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT permission FROM permission_grants WHERE subject = $s";
        cmd.Parameters.AddWithValue("$s", subject);
        using var r = cmd.ExecuteReader();
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (r.Read()) set.Add(r.GetString(0));
        return set;
    }

    public void Grant(string subject, IEnumerable<string> permissions)
    {
        using var c = db.Open();
        using var tx = c.BeginTransaction();
        foreach (var p in permissions)
        {
            using var cmd = c.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT OR IGNORE INTO permission_grants (subject, permission, granted_at) VALUES ($s, $p, $t)";
            cmd.Parameters.AddWithValue("$s", subject);
            cmd.Parameters.AddWithValue("$p", p);
            cmd.Parameters.AddWithValue("$t", DateTimeOffset.UtcNow.ToString("O"));
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    public void RevokeAll(string subject)
    {
        using var c = db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "DELETE FROM permission_grants WHERE subject = $s";
        cmd.Parameters.AddWithValue("$s", subject);
        cmd.ExecuteNonQuery();
    }
}
