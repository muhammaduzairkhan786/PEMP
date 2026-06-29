using Pemp.Domain.Audit;

namespace Pemp.Infrastructure.Persistence;

/// <summary>
/// EF row for one append-only audit entry (SEC-AUD-01). Mirrors the domain
/// <see cref="AuditEntry"/> 1:1; <see cref="Sequence"/> is the global chain position.
/// </summary>
public sealed class AuditEntryRow
{
    public long Sequence { get; set; }
    public Guid EngagementId { get; set; }
    public string Actor { get; set; } = "";
    public string Action { get; set; } = "";
    public string Before { get; set; } = "";
    public string After { get; set; } = "";
    public DateTimeOffset Timestamp { get; set; }
    public string Source { get; set; } = "";
    public string PrevHash { get; set; } = "";
    public string Hash { get; set; } = "";

    public static AuditEntryRow From(AuditEntry e) => new()
    {
        Sequence = e.Sequence, EngagementId = e.EngagementId, Actor = e.Actor, Action = e.Action,
        Before = e.Before, After = e.After, Timestamp = e.Timestamp, Source = e.Source,
        PrevHash = e.PrevHash, Hash = e.Hash,
    };

    public AuditEntry ToEntry() =>
        new(Sequence, EngagementId, Actor, Action, Before, After, Timestamp, Source, PrevHash, Hash);
}
