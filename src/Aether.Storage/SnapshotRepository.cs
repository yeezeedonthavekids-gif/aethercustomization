namespace Aether.Storage;

public sealed record SnapshotRecord(string Id, string Owner, string Kind, string PayloadJson, DateTimeOffset CreatedAt);

/// <summary>Rollback snapshots: the original state captured before AETHER changes anything.</summary>
public interface ISnapshotRepository
{
    void Save(SnapshotRecord snapshot);
    SnapshotRecord? Get(string id);
    SnapshotRecord? GetLatest(string owner, string kind);
    IReadOnlyList<SnapshotRecord> List(string? owner = null);
    void Delete(string id);
}

public sealed class SnapshotRepository(AetherDatabase db) : ISnapshotRepository
{
    public void Save(SnapshotRecord s)
    {
        using var c = db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            INSERT INTO snapshots (id, owner, kind, payload_json, created_at) VALUES ($i, $o, $k, $p, $t)
            ON CONFLICT(id) DO UPDATE SET payload_json = excluded.payload_json
            """;
        cmd.Parameters.AddWithValue("$i", s.Id);
        cmd.Parameters.AddWithValue("$o", s.Owner);
        cmd.Parameters.AddWithValue("$k", s.Kind);
        cmd.Parameters.AddWithValue("$p", s.PayloadJson);
        cmd.Parameters.AddWithValue("$t", s.CreatedAt.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    public SnapshotRecord? Get(string id) => Query("WHERE id = $a", ("$a", id)).FirstOrDefault();

    public SnapshotRecord? GetLatest(string owner, string kind) =>
        Query("WHERE owner = $a AND kind = $b ORDER BY created_at DESC LIMIT 1", ("$a", owner), ("$b", kind)).FirstOrDefault();

    public IReadOnlyList<SnapshotRecord> List(string? owner = null) => owner is null
        ? Query("ORDER BY created_at DESC")
        : Query("WHERE owner = $a ORDER BY created_at DESC", ("$a", owner));

    public void Delete(string id)
    {
        using var c = db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "DELETE FROM snapshots WHERE id = $i";
        cmd.Parameters.AddWithValue("$i", id);
        cmd.ExecuteNonQuery();
    }

    private List<SnapshotRecord> Query(string where, params (string Name, string Value)[] args)
    {
        using var c = db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT id, owner, kind, payload_json, created_at FROM snapshots " + where;
        foreach (var (n, v) in args) cmd.Parameters.AddWithValue(n, v);
        using var r = cmd.ExecuteReader();
        var list = new List<SnapshotRecord>();
        while (r.Read())
            list.Add(new SnapshotRecord(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), DateTimeOffset.Parse(r.GetString(4))));
        return list;
    }
}
