namespace Aether.Storage;

/// <summary>
/// Generic JSON document storage keyed by (collection, id). Used for themes, widget layouts,
/// automation rules, favorites, profiles and other aggregates that don't need relational queries.
/// </summary>
public interface IDocumentStore
{
    T? Get<T>(string collection, string id);
    IReadOnlyList<T> GetAll<T>(string collection);
    IReadOnlyList<string> GetIds(string collection);
    void Put<T>(string collection, string id, T document);
    bool Delete(string collection, string id);
    IReadOnlyList<(string Collection, string Id, string Json)> ExportAll();
    void ImportAll(IEnumerable<(string Collection, string Id, string Json)> documents, bool replace);
    event Action<string, string>? Changed;
}

public sealed class SqliteDocumentStore(AetherDatabase db) : IDocumentStore
{
    public event Action<string, string>? Changed;

    public T? Get<T>(string collection, string id)
    {
        using var c = db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT json FROM documents WHERE collection = $c AND id = $i";
        cmd.Parameters.AddWithValue("$c", collection);
        cmd.Parameters.AddWithValue("$i", id);
        return cmd.ExecuteScalar() is string json ? AetherJson.Deserialize<T>(json) : default;
    }

    public IReadOnlyList<T> GetAll<T>(string collection)
    {
        using var c = db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT json FROM documents WHERE collection = $c ORDER BY id";
        cmd.Parameters.AddWithValue("$c", collection);
        using var r = cmd.ExecuteReader();
        var list = new List<T>();
        while (r.Read())
        {
            var item = AetherJson.Deserialize<T>(r.GetString(0));
            if (item is not null) list.Add(item);
        }
        return list;
    }

    public IReadOnlyList<string> GetIds(string collection)
    {
        using var c = db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT id FROM documents WHERE collection = $c ORDER BY id";
        cmd.Parameters.AddWithValue("$c", collection);
        using var r = cmd.ExecuteReader();
        var list = new List<string>();
        while (r.Read()) list.Add(r.GetString(0));
        return list;
    }

    public void Put<T>(string collection, string id, T document)
    {
        using (var c = db.Open())
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = """
                INSERT INTO documents (collection, id, json, updated_at) VALUES ($c, $i, $j, $t)
                ON CONFLICT(collection, id) DO UPDATE SET json = excluded.json, updated_at = excluded.updated_at
                """;
            cmd.Parameters.AddWithValue("$c", collection);
            cmd.Parameters.AddWithValue("$i", id);
            cmd.Parameters.AddWithValue("$j", AetherJson.Serialize(document));
            cmd.Parameters.AddWithValue("$t", DateTimeOffset.UtcNow.ToString("O"));
            cmd.ExecuteNonQuery();
        }
        Changed?.Invoke(collection, id);
    }

    public bool Delete(string collection, string id)
    {
        int n;
        using (var c = db.Open())
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "DELETE FROM documents WHERE collection = $c AND id = $i";
            cmd.Parameters.AddWithValue("$c", collection);
            cmd.Parameters.AddWithValue("$i", id);
            n = cmd.ExecuteNonQuery();
        }
        if (n > 0) Changed?.Invoke(collection, id);
        return n > 0;
    }

    public IReadOnlyList<(string Collection, string Id, string Json)> ExportAll()
    {
        using var c = db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT collection, id, json FROM documents ORDER BY collection, id";
        using var r = cmd.ExecuteReader();
        var list = new List<(string, string, string)>();
        while (r.Read()) list.Add((r.GetString(0), r.GetString(1), r.GetString(2)));
        return list;
    }

    public void ImportAll(IEnumerable<(string Collection, string Id, string Json)> documents, bool replace)
    {
        using var c = db.Open();
        using var tx = c.BeginTransaction();
        if (replace)
        {
            using var del = c.CreateCommand();
            del.Transaction = tx;
            del.CommandText = "DELETE FROM documents";
            del.ExecuteNonQuery();
        }
        foreach (var (collection, id, json) in documents)
        {
            using var cmd = c.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO documents (collection, id, json, updated_at) VALUES ($c, $i, $j, $t)
                ON CONFLICT(collection, id) DO UPDATE SET json = excluded.json, updated_at = excluded.updated_at
                """;
            cmd.Parameters.AddWithValue("$c", collection);
            cmd.Parameters.AddWithValue("$i", id);
            cmd.Parameters.AddWithValue("$j", json);
            cmd.Parameters.AddWithValue("$t", DateTimeOffset.UtcNow.ToString("O"));
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
        Changed?.Invoke("*", "*");
    }
}
