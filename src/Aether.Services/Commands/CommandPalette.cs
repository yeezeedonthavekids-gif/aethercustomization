namespace Aether.Services.Commands;

public sealed record CommandItem(string Id, string Title, string Subtitle, string Category, string Glyph, Func<Task> Execute)
{
    public IReadOnlyList<string> Keywords { get; init; } = [];
    public int Boost { get; init; }
}

public interface ICommandSource
{
    IEnumerable<CommandItem> GetCommands();
}

public sealed record CommandMatch(CommandItem Item, int Score, IReadOnlyList<int> Highlights);

/// <summary>
/// Subsequence fuzzy matching with bonuses for prefix, word-start and consecutive matches (similar in spirit to
/// editor quick-open). Keywords match too, at a slight discount.
/// </summary>
public static class FuzzyMatcher
{
    public static (int Score, List<int> Positions)? Match(string query, string text)
    {
        if (query.Length == 0) return (0, []);
        if (text.Length == 0) return null;
        var positions = new List<int>(query.Length);
        var score = 0;
        var ti = 0;
        var prevMatch = -2;
        foreach (var qc in query)
        {
            if (char.IsWhiteSpace(qc)) continue;
            var found = false;
            while (ti < text.Length)
            {
                if (char.ToLowerInvariant(text[ti]) == char.ToLowerInvariant(qc))
                {
                    var bonus = 1;
                    if (ti == 0) bonus += 8;
                    else if (!char.IsLetterOrDigit(text[ti - 1]) || (char.IsUpper(text[ti]) && char.IsLower(text[ti - 1]))) bonus += 6;
                    if (prevMatch == ti - 1) bonus += 5;
                    if (text[ti] == qc) bonus += 1;
                    score += bonus;
                    positions.Add(ti);
                    prevMatch = ti;
                    ti++;
                    found = true;
                    break;
                }
                ti++;
            }
            if (!found) return null;
        }
        score -= Math.Max(0, text.Length - query.Length) / 8; // prefer shorter targets
        if (text.StartsWith(query, StringComparison.OrdinalIgnoreCase)) score += 15;
        return (score, positions);
    }

    public static IReadOnlyList<CommandMatch> Rank(string query, IEnumerable<CommandItem> items, int limit = 40)
    {
        query = query.Trim();
        if (query.Length == 0)
            return items.OrderByDescending(i => i.Boost).Take(limit).Select(i => new CommandMatch(i, i.Boost, [])).ToList();

        var results = new List<CommandMatch>();
        foreach (var item in items)
        {
            var best = Match(query, item.Title) is { } t ? (t.Score + 10, t.Positions) : ((int, List<int>)?)null;
            foreach (var text in item.Keywords.Append(item.Subtitle).Append(item.Category))
            {
                if (Match(query, text) is { } k && (best is null || k.Score - 4 > best.Value.Item1))
                    best = (k.Score - 4, []);
            }
            if (best is { } b) results.Add(new CommandMatch(item, b.Item1 + item.Boost, b.Item2));
        }
        return results.OrderByDescending(r => r.Score).ThenBy(r => r.Item.Title.Length).Take(limit).ToList();
    }
}

/// <summary>Aggregates every registered command source; sources are evaluated lazily on each query.</summary>
public sealed class CommandPaletteService(IEnumerable<ICommandSource> sources)
{
    private readonly List<ICommandSource> _extra = [];
    private readonly Dictionary<string, DateTimeOffset> _recent = new();

    public void AddSource(ICommandSource source) { lock (_extra) _extra.Add(source); }

    public IReadOnlyList<CommandMatch> Search(string query, int limit = 40)
    {
        List<ICommandSource> all;
        lock (_extra) all = [.. sources, .. _extra];
        var items = new List<CommandItem>();
        foreach (var source in all)
        {
            try { items.AddRange(source.GetCommands()); }
            catch (Exception) { /* a failing source must not break the palette */ }
        }
        lock (_recent)
            items = items.Select(i => _recent.TryGetValue(i.Id, out var at) ? i with { Boost = i.Boost + (DateTimeOffset.Now - at < TimeSpan.FromDays(1) ? 12 : 4) } : i).ToList();
        return FuzzyMatcher.Rank(query, items, limit);
    }

    public async Task ExecuteAsync(CommandItem item)
    {
        lock (_recent) _recent[item.Id] = DateTimeOffset.Now;
        await item.Execute().ConfigureAwait(false);
    }
}
