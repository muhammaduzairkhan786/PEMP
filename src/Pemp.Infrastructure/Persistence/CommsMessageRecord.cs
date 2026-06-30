namespace Pemp.Infrastructure.Persistence;

/// <summary>The kind of communication on an engagement (FR-NOT). Drives the badge/label, not behaviour.</summary>
public enum CommsKind
{
    Note,            // free-form cross-role note
    FindingsSummary, // the same-day findings summary sent to the CA (FR-FND / FR-NOT-01)
    StatusUpdate,    // a lifecycle status note (e.g. post-test call scheduled)
}

/// <summary>
/// A communication on an engagement's timeline (FR-NOT-01/03): the home for the cross-role journeys —
/// the auto-drafted same-day findings summary, post-test notes, status updates. Posting is a scoped,
/// authorized, audited action (see <see cref="CommsStore"/>). Crown-jewel-adjacent: the body may carry
/// sensitive detail, so it is never written to the audit log (only the kind is).
/// </summary>
public sealed class CommsMessageRecord
{
    public Guid Id { get; set; }
    public Guid EngagementId { get; set; }
    public string AuthorName { get; set; } = "";
    public string AuthorRole { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public CommsKind Kind { get; set; }
    public string Body { get; set; } = "";
}
