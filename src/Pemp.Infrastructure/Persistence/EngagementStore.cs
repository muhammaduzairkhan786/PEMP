using Microsoft.EntityFrameworkCore;
using Pemp.Domain;
using Pemp.Domain.Audit;

namespace Pemp.Infrastructure.Persistence;

/// <summary>
/// Application service the UI calls. Loads a record, rehydrates the domain aggregate
/// bound to a DB-backed audit chain, runs the requested guarded transition, and — only
/// on success — persists the new state and the appended audit entries together.
/// A failed guard changes and saves nothing (the enforcement guarantee).
/// </summary>
public sealed class EngagementStore(PempDbContext db, Func<DateTimeOffset> clock)
{
    public Task<List<EngagementRecord>> ListAsync() =>
        db.Engagements.AsNoTracking().OrderBy(e => e.Reference).ToListAsync();

    public Task<EngagementRecord?> GetAsync(Guid id) =>
        db.Engagements.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id);

    /// <summary>All findings across the portfolio (FR-ANL-04 analytics).</summary>
    public Task<List<FindingRecord>> AllFindingsAsync() =>
        db.Findings.AsNoTracking().ToListAsync();

    public Task<List<AuditEntryRow>> AuditForAsync(Guid id) =>
        db.AuditEntries.AsNoTracking().Where(a => a.EngagementId == id).OrderBy(a => a.Sequence).ToListAsync();

    public Task<List<FindingRecord>> FindingsForAsync(Guid id) =>
        db.Findings.AsNoTracking().Where(f => f.EngagementId == id)
          .OrderBy(f => f.Severity).ThenBy(f => f.Title).ToListAsync();

    // ---- Evidence (FR-FND-02 / SEC-EVD) ------------------------------------
    public Task<List<EvidenceRecord>> EvidenceForAsync(Guid id) =>
        db.Evidence.AsNoTracking().Where(e => e.EngagementId == id).ToListAsync();

    public async Task AddEvidenceAsync(Guid engagementId, Guid findingId, string fileName, EvidenceKind kind, string note)
    {
        db.Evidence.Add(new EvidenceRecord
        {
            Id = Guid.NewGuid(), EngagementId = engagementId, FindingId = findingId,
            FileName = fileName, Kind = kind, Note = note, EncryptedAtRest = true,
        });
        await db.SaveChangesAsync();
    }

    public bool VerifyChain() => new EfAuditChain(db).Verify();

    // ---- Assessment answers (workbook Tab 1 / FR-SCO) ----------------------
    public Task<Dictionary<string, string>> AssessmentAnswersAsync(Guid id) =>
        db.AssessmentAnswers.AsNoTracking().Where(a => a.EngagementId == id)
          .ToDictionaryAsync(a => a.QuestionId, a => a.Value);

    public async Task SaveAssessmentAnswerAsync(Guid id, string questionId, string value)
    {
        var row = await db.AssessmentAnswers.FirstOrDefaultAsync(a => a.EngagementId == id && a.QuestionId == questionId);
        if (row is null)
            db.AssessmentAnswers.Add(new AssessmentAnswerRecord { Id = Guid.NewGuid(), EngagementId = id, QuestionId = questionId, Value = value });
        else
            row.Value = value;
        await db.SaveChangesAsync();
    }

    // ---- Access requirements (workbook Tab 3 / FR-ACC-01) ------------------
    public Task<List<AccessRequirementRecord>> AccessReqsForAsync(Guid id) =>
        db.AccessRequirements.AsNoTracking().Where(a => a.EngagementId == id).OrderBy(a => a.Environment).ToListAsync();

    public async Task SetAccessStatusAsync(Guid reqId, AccessStatus status)
    {
        var row = await db.AccessRequirements.FirstOrDefaultAsync(a => a.Id == reqId);
        if (row is null) return;
        row.Status = status;
        await db.SaveChangesAsync();
    }

    // ---- Tester checklists (workbook Tab 4) --------------------------------
    public async Task<HashSet<string>> ChecklistDoneAsync(Guid id) =>
        (await db.ChecklistTicks.AsNoTracking().Where(c => c.EngagementId == id && c.Done).Select(c => c.Code).ToListAsync()).ToHashSet();

    public async Task SetChecklistAsync(Guid id, string code, bool done)
    {
        var row = await db.ChecklistTicks.FirstOrDefaultAsync(c => c.EngagementId == id && c.Code == code);
        if (row is null)
            db.ChecklistTicks.Add(new ChecklistTickRecord { Id = Guid.NewGuid(), EngagementId = id, Code = code, Done = done });
        else
            row.Done = done;
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Request a retest on a closed engagement (FR-RET-01/02): spawns a linked child that
    /// re-verifies remediated findings. Persists the parent (RetestRequested) + the new
    /// child record + the audit entries together. Returns the child id on success.
    /// </summary>
    public async Task<(Result Result, Guid? ChildId)> RequestRetestAsync(Guid parentId, string actor)
    {
        var parent = await db.Engagements.FirstOrDefaultAsync(e => e.Id == parentId);
        if (parent is null) return (Result.Fail("Engagement not found."), null);

        var chain = new EfAuditChain(db);
        var aggregate = parent.ToDomain(chain, clock);
        var childRef = $"{parent.Reference}-RT";

        var result = aggregate.RequestRetest(childRef, actor, out var child);
        if (result.Failed || child is null) return (result, null);

        parent.CopyFrom(aggregate);
        var childRec = EngagementRecord.FromDomain(child, $"{parent.AppName} (retest)", parent.Criticality, parent.AssignedTesterName);
        db.Engagements.Add(childRec);
        await db.SaveChangesAsync();
        return (result, child.Id);
    }

    /// <summary>
    /// Run a guarded transition on the aggregate. The action invokes a domain method
    /// (e.g. <c>e =&gt; e.SignSow(actor, reAuth: true)</c>). State + audit persist only if it succeeds.
    /// </summary>
    public async Task<Result> ExecuteAsync(Guid id, Func<Engagement, Result> action)
    {
        var rec = await db.Engagements.FirstOrDefaultAsync(e => e.Id == id);
        if (rec is null) return Result.Fail("Engagement not found.");

        var chain = new EfAuditChain(db);
        var aggregate = rec.ToDomain(chain, clock);

        var result = action(aggregate);
        if (result.Failed) return result; // guard rejected — nothing appended, nothing saved

        rec.CopyFrom(aggregate);
        await db.SaveChangesAsync();
        return result;
    }
}
