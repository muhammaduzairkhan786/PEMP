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

    /// <summary>
    /// Object-level scoped list (SEC-AZN/SEC-INS-01): filter to one app (Stakeholder) or
    /// to engagements assigned to a tester. Null filters mean unrestricted (Acme/DM/Admin).
    /// </summary>
    public Task<List<EngagementRecord>> ListScopedAsync(string? appName, string? assignedToName)
    {
        var q = db.Engagements.AsNoTracking().AsQueryable();
        if (appName is not null) q = q.Where(e => e.AppName == appName || e.AppName == appName + " (retest)");
        if (assignedToName is not null) q = q.Where(e => e.AssignedTesterName == assignedToName);
        return q.OrderBy(e => e.Reference).ToListAsync();
    }

    public Task<EngagementRecord?> GetAsync(Guid id) =>
        db.Engagements.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id);

    /// <summary>
    /// Object-level authorized fetch (anti-BOLA/IDOR, SEC-AZN-02): returns null if the
    /// record is outside the caller's scope, so a direct URL can't reach another app's
    /// or another tester's engagement. Null filters = unrestricted (Acme/DM/Admin).
    /// </summary>
    public async Task<EngagementRecord?> GetScopedAsync(Guid id, string? appName, string? assignedToName)
    {
        var rec = await db.Engagements.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id);
        if (rec is null) return null;
        if (appName is not null && rec.AppName != appName && rec.AppName != appName + " (retest)") return null;
        if (assignedToName is not null && rec.AssignedTesterName != assignedToName) return null;
        return rec;
    }

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

    /// <summary>Global audit log for the admin console (FR-AUD-03), newest first.</summary>
    public Task<List<AuditEntryRow>> AllAuditAsync() =>
        db.AuditEntries.AsNoTracking().OrderByDescending(a => a.Sequence).ToListAsync();

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
    /// Raise a new engagement request (FR-REQ-01/04): mints a reference, enters Intake,
    /// and writes the first audit entry. Returns the new engagement id.
    /// </summary>
    public async Task<Guid> RaiseAsync(EngagementType type, string appName, string criticality, string actor)
    {
        var refs = await db.Engagements.AsNoTracking().Select(e => e.Reference).ToListAsync();
        var maxNum = refs
            .Select(r => int.TryParse(r.Split('-').Last(), out var n) ? n : 0)
            .DefaultIfEmpty(420).Max();
        var reference = $"ENG-2026-{maxNum + 1:D4}";

        var chain = new EfAuditChain(db);
        var engagement = Engagement.Raise(reference, type, actor, chain, clock);
        db.Engagements.Add(EngagementRecord.FromDomain(engagement, appName, criticality, null));
        await db.SaveChangesAsync();
        return engagement.Id;
    }

    /// <summary>
    /// Assign a tester (FR-ASG-03) and record the display name together, so the card/header
    /// don't show a stale "—" after assignment.
    /// </summary>
    public async Task<Result> AssignTesterAsync(Guid id, Guid testerId, string testerName, string actor)
    {
        var rec = await db.Engagements.FirstOrDefaultAsync(e => e.Id == id);
        if (rec is null) return Result.Fail("Engagement not found.");
        var chain = new EfAuditChain(db);
        var aggregate = rec.ToDomain(chain, clock);
        var result = aggregate.AssignTester(testerId, actor);
        if (result.Failed) return result;
        rec.CopyFrom(aggregate);
        rec.AssignedTesterName = testerName;
        await db.SaveChangesAsync();
        return result;
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

        // Carry the unresolved (in-scope) findings into the child to be re-verified (FR-RET-03).
        var inScope = await db.Findings.AsNoTracking()
            .Where(f => f.EngagementId == parentId && (f.Status == FindingStatus.Open || f.Status == FindingStatus.RetestPending))
            .ToListAsync();
        foreach (var f in inScope)
            db.Findings.Add(new FindingRecord
            {
                Id = Guid.NewGuid(), EngagementId = child.Id, Title = f.Title,
                Severity = f.Severity, Cvss = f.Cvss, Asset = f.Asset, Status = FindingStatus.RetestPending,
            });

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
