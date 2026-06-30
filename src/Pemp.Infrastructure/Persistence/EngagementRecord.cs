using Pemp.Domain;
using Pemp.Domain.Audit;

namespace Pemp.Infrastructure.Persistence;

/// <summary>
/// EF persistence record for an engagement. Holds the domain state (stage + guard
/// flags) plus demo display metadata (app name, criticality, tester name). The domain
/// <see cref="Engagement"/> aggregate is rehydrated from this to run a transition,
/// then this record is updated from the aggregate and saved.
/// </summary>
public sealed class EngagementRecord
{
    public Guid Id { get; set; }
    public string Reference { get; set; } = "";
    public string AppName { get; set; } = "";
    public EngagementType Type { get; set; }
    public string Criticality { get; set; } = "Medium";
    public Stage CurrentStage { get; set; }
    public Guid? ParentId { get; set; }
    public Guid? AssignedTesterId { get; set; }
    public string? AssignedTesterName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Optimistic-concurrency token (rank 3 / TOCTOU). A provider-agnostic value-token: it is bumped on
    /// every state-changing save and included in the UPDATE's WHERE, so two transitions that both pass
    /// guards no longer last-write-win — the loser hits a <see cref="Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException"/>
    /// (surfaced as a friendly "reload and retry"). Works identically on SQLite and Azure SQL.
    /// </summary>
    public byte[] RowVersion { get; set; } = Guid.NewGuid().ToByteArray();

    // Guard flags (mirror the aggregate)
    public bool AssessmentComplete { get; set; }
    public bool SowReviewedByDm { get; set; }
    public bool SowSigned { get; set; }
    public bool AccessVerified { get; set; }
    public bool IrNoticeSent { get; set; }
    public bool DraftGenerated { get; set; }
    public bool PeerReviewPassed { get; set; }
    public bool RetestRequested { get; set; }

    public Engagement ToDomain(IAuditChain chain, Func<DateTimeOffset> clock) =>
        Engagement.Rehydrate(Id, Reference, Type, ParentId, CurrentStage, AssignedTesterId,
            AssessmentComplete, SowReviewedByDm, SowSigned, AccessVerified, IrNoticeSent,
            DraftGenerated, PeerReviewPassed, RetestRequested, chain, clock);

    /// <summary>Copy mutable state back from the aggregate after a successful transition.</summary>
    public void CopyFrom(Engagement e)
    {
        CurrentStage = e.CurrentStage;
        AssignedTesterId = e.AssignedTesterId;
        AssessmentComplete = e.AssessmentComplete;
        SowReviewedByDm = e.SowReviewedByDm;
        SowSigned = e.SowSigned;
        AccessVerified = e.AccessVerified;
        IrNoticeSent = e.IrNoticeSent;
        DraftGenerated = e.DraftGenerated;
        PeerReviewPassed = e.PeerReviewPassed;
        RetestRequested = e.RetestRequested;
    }

    public static EngagementRecord FromDomain(Engagement e, string appName, string criticality, string? testerName) => new()
    {
        Id = e.Id, Reference = e.Reference, AppName = appName, Type = e.Type, Criticality = criticality,
        CurrentStage = e.CurrentStage, ParentId = e.ParentId, AssignedTesterId = e.AssignedTesterId,
        AssignedTesterName = testerName, CreatedAt = DateTimeOffset.UtcNow,
        AssessmentComplete = e.AssessmentComplete, SowReviewedByDm = e.SowReviewedByDm, SowSigned = e.SowSigned,
        AccessVerified = e.AccessVerified, IrNoticeSent = e.IrNoticeSent, DraftGenerated = e.DraftGenerated,
        PeerReviewPassed = e.PeerReviewPassed, RetestRequested = e.RetestRequested,
    };
}
