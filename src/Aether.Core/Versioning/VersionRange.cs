namespace Aether.Core.Versioning;

/// <summary>
/// A space separated list of comparators that must all match, e.g. "&gt;=1.2.0 &lt;2.0.0".
/// Supports =, &gt;, &gt;=, &lt;, &lt;=, ^ (compatible) and ~ (patch-level). "*" matches everything.
/// </summary>
public sealed class VersionRange
{
    private readonly List<(string Op, SemVersion Version)> _comparators;

    private VersionRange(string text, List<(string, SemVersion)> comparators)
    {
        Text = text;
        _comparators = comparators;
    }

    public string Text { get; }

    public static VersionRange Any { get; } = new("*", []);

    public static bool TryParse(string? text, out VersionRange range)
    {
        range = Any;
        if (string.IsNullOrWhiteSpace(text) || text.Trim() == "*") return true;
        var list = new List<(string, SemVersion)>();
        foreach (var token in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var op = token.StartsWith(">=") || token.StartsWith("<=") ? token[..2]
                : token[0] is '>' or '<' or '=' or '^' or '~' ? token[..1] : "=";
            var verText = op == "=" && token[0] != '=' ? token : token[op.Length..];
            if (!SemVersion.TryParse(verText, out var v)) return false;
            switch (op)
            {
                case "^":
                    list.Add((">=", v));
                    list.Add(("<", v.Major > 0 ? new SemVersion(v.Major + 1) : new SemVersion(0, v.Minor + 1)));
                    break;
                case "~":
                    list.Add((">=", v));
                    list.Add(("<", new SemVersion(v.Major, v.Minor + 1)));
                    break;
                default:
                    list.Add((op, v));
                    break;
            }
        }
        range = new VersionRange(text.Trim(), list);
        return true;
    }

    public static VersionRange Parse(string text) =>
        TryParse(text, out var r) ? r : throw new FormatException($"'{text}' is not a valid version range.");

    public bool IsSatisfiedBy(SemVersion version) => _comparators.All(c => c.Op switch
    {
        ">=" => version >= c.Version,
        "<=" => version <= c.Version,
        ">" => version > c.Version,
        "<" => version < c.Version,
        _ => version.Equals(c.Version),
    });

    public override string ToString() => Text;
}
