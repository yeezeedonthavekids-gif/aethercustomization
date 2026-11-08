namespace Aether.Core.Diagnostics;

public sealed record AuditEntry(
    DateTimeOffset Timestamp,
    string Category,
    string Action,
    string Subject,
    bool Success,
    string? Details = null);

/// <summary>Append-only record of every system change AETHER makes. Shown in Settings ▸ Logging.</summary>
public interface IAuditLog
{
    void Record(AuditEntry entry);
    IReadOnlyList<AuditEntry> Query(string? category = null, string? subject = null, int limit = 200);
}
