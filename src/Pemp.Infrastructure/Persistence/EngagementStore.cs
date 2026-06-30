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
public sealed class EngagementStore(PempDbContext db, Func<DateTimeOffset> clock, AuditHmacKey auditKey)
{
    /// <summary>
    /// Roles entitled to the WHOLE portfolio (SEC-AZN/SEC-INS-01). Every other role is "scoped":
    /// it must arrive with a concrete object filter and a blank/unresolved filter must match
    /// NOTHING — never the full set. These strings mirror the canonical PEMP role names.
    /// </summary>
    private static readonly IReadOnlySet<string> AllPortfolioRoles = PempRoles.Portfolio;

    private EfAuditChain NewChain() => new(db, auditKey.Value);

    /// <summary>
    /// Stage the chained audit entry for a crown-jewel DATA mutation onto the current unit of work
    /// (SEC-AUD/FR-AUD): it is added to the context here and persists atomically with the data row
    /// in the caller's single SaveChangesAsync — exactly as a stage transition does. The acting
    /// user's name (<paramref name="actor"/>) and role are recorded; the role is carried in the
    /// source channel. NEVER pass a credential secret as <paramref name="after"/> — labels/ids only.
    /// </summary>
    private void StageAudit(Guid engagementId, string actor, string? role, string action, string before, string after) =>
        NewChain().Append(engagementId, actor, action, before, after,
            string.IsNullOrEmpty(role) ? "ui" : $"ui:{role}", clock());

    public Task<List<EngagementRecord>> ListAsync() =>
        db.Engagements.AsNoTracking().OrderBy(e => e.Reference).ToListAsync();

    /// <summary>
    /// Object-level scoped list (SEC-AZN/SEC-INS-01). Fails CLOSED: only an explicit all-portfolio
    /// <paramref name="role"/> (Acme/DM/Admin) gets an unrestricted listing. EVERY other role — a
    /// scoped role (Tester, Stakeholder), an unknown/blank role, AND a null role — MUST supply a
    /// concrete filter; with no filter, or a blank one, the result is empty, never the full portfolio.
    /// (Null role is treated as fail-closed too: an authenticated-but-unmapped principal sees nothing,
    /// not the whole estate.) A Tester additionally sees Report-stage engagements authored by a
    /// DIFFERENT tester — the independent peer-QA review queue (FR-REP-02) — without losing anti-BOLA
    /// elsewhere.
    /// </summary>
    public Task<List<EngagementRecord>> ListScopedAsync(string? appName, string? assignedToName, string? role = null)
    {
        var scoped = !AllPortfolioRoles.Contains(role ?? "");
        // Fail closed: a scoped role with no concrete filter at all sees nothing.
        if (scoped && appName is null && assignedToName is null)
            return Task.FromResult(new List<EngagementRecord>());

        var q = db.Engagements.AsNoTracking().AsQueryable();
        if (appName is not null) q = q.Where(e => e.AppName == appName || e.AppName == appName + " (retest)");
        if (assignedToName is not null)
        {
            // A Tester's scope = own assignments ∪ Report-stage engagements authored by another tester
            // (the peer-QA queue, FR-REP-02). A blank name still matches nothing (fail-closed) because
            // the union only opens for a concrete, non-empty tester name.
            if (role == PempRoles.Tester && !string.IsNullOrEmpty(assignedToName))
            {
                var name = assignedToName;
                q = q.Where(e => e.AssignedTesterName == name
                    || (e.CurrentStage == Stage.Report && e.AssignedTesterName != null && e.AssignedTesterName != name));
            }
            else
            {
                q = q.Where(e => e.AssignedTesterName == assignedToName);
            }
        }
        return q.OrderBy(e => e.Reference).ToListAsync();
    }

    public Task<EngagementRecord?> GetAsync(Guid id) =>
        db.Engagements.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id);

    /// <summary>
    /// Object-level authorized fetch (anti-BOLA/IDOR, SEC-AZN-02): returns null if the record is
    /// outside the caller's scope, so a direct URL can't reach another app's or another tester's
    /// engagement. Fails CLOSED — see <see cref="ListScopedAsync"/>: only an all-portfolio
    /// <paramref name="role"/> may pass with no filter; every other role (scoped, unknown, OR null)
    /// with a missing/blank filter gets nothing. A Tester may additionally open a Report-stage
    /// engagement authored by a DIFFERENT tester (the peer-QA review path, FR-REP-02); they are
    /// STILL blocked from non-Report engagements they didn't author.
    /// </summary>
    public async Task<EngagementRecord?> GetScopedAsync(Guid id, string? appName, string? assignedToName, string? role = null)
    {
        var scoped = !AllPortfolioRoles.Contains(role ?? "");
        // Fail closed: a scoped role that supplied no concrete filter at all reaches nothing.
        if (scoped && appName is null && assignedToName is null) return null;

        var rec = await db.Engagements.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id);
        if (rec is null) return null;
        if (appName is not null && rec.AppName != appName && rec.AppName != appName + " (retest)") return null;
        if (assignedToName is not null)
        {
            var isAssigned = rec.AssignedTesterName == assignedToName;
            // Peer-QA review access: a Report-stage engagement authored by another tester is in scope.
            var isPeerReviewable = role == PempRoles.Tester && !string.IsNullOrEmpty(assignedToName)
                && rec.CurrentStage == Stage.Report
                && rec.AssignedTesterName != null && rec.AssignedTesterName != assignedToName;
            if (!isAssigned && !isPeerReviewable) return null;
        }
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

    /// <summary>
    /// Record a new finding into the live register (FR-FND-01/03): entered once by the assigned
    /// tester during Execution/Findings, it flows straight into the consolidated register and
    /// analytics. Returns the new finding id.
    /// </summary>
    public async Task<Guid> AddFindingAsync(
        Guid engagementId, string title, Severity severity, string cvss, string cvssVector,
        string asset, string remediation, FindingStatus status = FindingStatus.Open,
        string actor = "system", string? role = null)
    {
        var finding = new FindingRecord
        {
            Id = Guid.NewGuid(),
            EngagementId = engagementId,
            Title = title,
            Severity = severity,
            Cvss = cvss,
            CvssVector = cvssVector,
            Asset = asset,
            Remediation = remediation,
            Status = status,
        };
        db.Findings.Add(finding);
        StageAudit(engagementId, actor, role, "Finding.Added", "-", $"{severity}: {title}");
        await db.SaveChangesAsync();
        return finding.Id;
    }

    // ---- Evidence (FR-FND-02 / SEC-EVD) ------------------------------------
    public Task<List<EvidenceRecord>> EvidenceForAsync(Guid id) =>
        db.Evidence.AsNoTracking().Where(e => e.EngagementId == id).ToListAsync();

    public async Task AddEvidenceAsync(Guid engagementId, Guid findingId, string fileName, EvidenceKind kind, string note,
        string actor = "system", string? role = null)
    {
        db.Evidence.Add(new EvidenceRecord
        {
            Id = Guid.NewGuid(), EngagementId = engagementId, FindingId = findingId,
            FileName = fileName, Kind = kind, Note = note, EncryptedAtRest = true,
        });
        StageAudit(engagementId, actor, role, "Evidence.Added", "-", fileName);
        await db.SaveChangesAsync();
    }

    public bool VerifyChain() => NewChain().Verify();

    /// <summary>Global audit log for the admin console (FR-AUD-03), newest first.</summary>
    public Task<List<AuditEntryRow>> AllAuditAsync() =>
        db.AuditEntries.AsNoTracking().OrderByDescending(a => a.Sequence).ToListAsync();

    /// <summary>
    /// Engagement audit trail with the tamper-evidence check performed on READ (SEC-AUD-01) — so a
    /// broken/forged chain is surfaced automatically, not only when someone presses a "Verify" button.
    /// </summary>
    public async Task<(List<AuditEntryRow> Rows, bool ChainIntact)> AuditForVerifiedAsync(Guid id)
    {
        var rows = await AuditForAsync(id);
        return (rows, VerifyChain());
    }

    /// <summary>Global audit log + automatic chain verification on read (FR-AUD-03 / SEC-AUD-01).</summary>
    public async Task<(List<AuditEntryRow> Rows, bool ChainIntact)> AllAuditVerifiedAsync()
    {
        var rows = await AllAuditAsync();
        return (rows, VerifyChain());
    }

    // ---- Assessment answers (workbook Tab 1 / FR-SCO) ----------------------
    public Task<Dictionary<string, string>> AssessmentAnswersAsync(Guid id) =>
        db.AssessmentAnswers.AsNoTracking().Where(a => a.EngagementId == id)
          .ToDictionaryAsync(a => a.QuestionId, a => a.Value);

    public async Task SaveAssessmentAnswerAsync(Guid id, string questionId, string value,
        string actor = "system", string? role = null)
    {
        var row = await db.AssessmentAnswers.FirstOrDefaultAsync(a => a.EngagementId == id && a.QuestionId == questionId);
        if (row is null)
            db.AssessmentAnswers.Add(new AssessmentAnswerRecord { Id = Guid.NewGuid(), EngagementId = id, QuestionId = questionId, Value = value });
        else
            row.Value = value;
        // Log the question answered, not the answer value (assessment input may be sensitive).
        StageAudit(id, actor, role, "Assessment.AnswerSaved", "-", questionId);
        await db.SaveChangesAsync();
    }

    // ---- Access requirements (workbook Tab 3 / FR-ACC-01) ------------------
    public Task<List<AccessRequirementRecord>> AccessReqsForAsync(Guid id) =>
        db.AccessRequirements.AsNoTracking().Where(a => a.EngagementId == id).OrderBy(a => a.Environment).ToListAsync();

    public async Task SetAccessStatusAsync(Guid reqId, AccessStatus status,
        string actor = "system", string? role = null)
    {
        var row = await db.AccessRequirements.FirstOrDefaultAsync(a => a.Id == reqId);
        if (row is null) return;
        var before = $"{row.Environment}: {row.Status}";
        row.Status = status;
        StageAudit(row.EngagementId, actor, role, "Access.StatusChanged", before, $"{row.Environment}: {status}");
        await db.SaveChangesAsync();
    }

    // ---- Tester checklists (workbook Tab 4) --------------------------------
    public async Task<HashSet<string>> ChecklistDoneAsync(Guid id) =>
        (await db.ChecklistTicks.AsNoTracking().Where(c => c.EngagementId == id && c.Done).Select(c => c.Code).ToListAsync()).ToHashSet();

    public async Task SetChecklistAsync(Guid id, string code, bool done,
        string actor = "system", string? role = null)
    {
        var row = await db.ChecklistTicks.FirstOrDefaultAsync(c => c.EngagementId == id && c.Code == code);
        if (row is null)
            db.ChecklistTicks.Add(new ChecklistTickRecord { Id = Guid.NewGuid(), EngagementId = id, Code = code, Done = done });
        else
            row.Done = done;
        StageAudit(id, actor, role, "Checklist.Updated", code, $"{code}={(done ? "done" : "cleared")}");
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

        var chain = NewChain();
        var engagement = Engagement.Raise(reference, type, actor, chain, clock);
        db.Engagements.Add(EngagementRecord.FromDomain(engagement, appName, criticality, null));
        await db.SaveChangesAsync();
        return engagement.Id;
    }

    /// <summary>
    /// Capacity board (FR-ASG-02): active (non-closed) engagement count per tester name, so the
    /// Delivery Manager can see relative load before assigning. Derived live from the store.
    /// </summary>
    public async Task<Dictionary<string, int>> ActiveAssignmentCountsAsync()
    {
        var rows = await db.Engagements.AsNoTracking()
            .Where(e => e.AssignedTesterName != null && e.CurrentStage != Stage.Closed)
            .GroupBy(e => e.AssignedTesterName!)
            .Select(g => new { Name = g.Key, Count = g.Count() })
            .ToListAsync();
        return rows.ToDictionary(x => x.Name, x => x.Count);
    }

    // ---- Test credentials (SEC-CRD) ----------------------------------------
    public Task<List<TestCredentialRecord>> CredentialsForAsync(Guid id) =>
        db.TestCredentials.AsNoTracking().Where(c => c.EngagementId == id).OrderBy(c => c.Label).ToListAsync();

    /// <summary>
    /// Attach a test-account credential to an engagement (SEC-CRD). The secret is persisted for the
    /// demo; production stores it in Key Vault / envelope-encrypted. Returns the new credential id.
    /// </summary>
    public async Task<Guid> AddTestCredentialAsync(Guid engagementId, string label, string username, string secret,
        string actor = "system", string? role = null)
    {
        var cred = new TestCredentialRecord
        {
            Id = Guid.NewGuid(), EngagementId = engagementId,
            Label = label, Username = username, Secret = secret,
        };
        db.TestCredentials.Add(cred);
        // Log the credential's LABEL only — never the secret (SEC-CRD: secrets are never logged).
        StageAudit(engagementId, actor, role, "Credential.Added", "-", label);
        await db.SaveChangesAsync();
        return cred.Id;
    }

    /// <summary>
    /// Set a finding's status in the live register (FR-FND-04 / FR-RET-03): used by the retest
    /// pass/fail flow (pass → Closed, fail → Open) and remediation tracking.
    /// </summary>
    public async Task SetFindingStatusAsync(Guid findingId, FindingStatus status,
        string actor = "system", string? role = null)
    {
        var row = await db.Findings.FirstOrDefaultAsync(f => f.Id == findingId);
        if (row is null) return;
        var before = row.Status.ToString();
        row.Status = status;
        StageAudit(row.EngagementId, actor, role, "Finding.StatusChanged", before, status.ToString());
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Add a tester-defined access requirement row (FR-ACC-01): the owning tester adds the
    /// environment/asset they need at the Access stage. Returns the new record.
    /// </summary>
    public async Task<AccessRequirementRecord> AddAccessRequirementAsync(
        Guid engagementId, string environment, string url, string accessType,
        string actor = "system", string? role = null)
    {
        var row = new AccessRequirementRecord
        {
            Id = Guid.NewGuid(), EngagementId = engagementId,
            Environment = environment, Url = url, AccessType = accessType,
            Status = AccessStatus.AppTeamToProvision,
        };
        db.AccessRequirements.Add(row);
        StageAudit(engagementId, actor, role, "Access.RequirementAdded", "-", environment);
        await db.SaveChangesAsync();
        return row;
    }

    /// <summary>
    /// Assign a tester (FR-ASG-03) and record the display name together, so the card/header
    /// don't show a stale "—" after assignment.
    /// </summary>
    public async Task<Result> AssignTesterAsync(Guid id, Guid testerId, string testerName, string actor)
    {
        var rec = await db.Engagements.FirstOrDefaultAsync(e => e.Id == id);
        if (rec is null) return Result.Fail("Engagement not found.");
        var chain = NewChain();
        var aggregate = rec.ToDomain(chain, clock);
        var result = aggregate.AssignTester(testerId, actor, testerLabel: testerName);
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

        var chain = NewChain();
        var aggregate = parent.ToDomain(chain, clock);
        var childRef = $"{parent.Reference}-RT";

        var result = aggregate.RequestRetest(childRef, actor, out var child);
        if (result.Failed || child is null) return (result, null);

        parent.CopyFrom(aggregate);
        var childRec = EngagementRecord.FromDomain(child, $"{parent.AppName} (retest)", parent.Criticality, parent.AssignedTesterName);
        db.Engagements.Add(childRec);

        // Carry the in-scope findings into the child to be re-verified (FR-RET-03): everything
        // not terminal — Open, RetestPending, AND Remediated (a retest verifies the fixes).
        var inScope = await db.Findings.AsNoTracking()
            .Where(f => f.EngagementId == parentId
                        && f.Status != FindingStatus.Closed && f.Status != FindingStatus.AcceptedRisk)
            .ToListAsync();
        foreach (var f in inScope)
            db.Findings.Add(new FindingRecord
            {
                Id = Guid.NewGuid(), EngagementId = child.Id, Title = f.Title,
                Severity = f.Severity, Cvss = f.Cvss, CvssVector = f.CvssVector, Asset = f.Asset,
                Remediation = f.Remediation, Status = FindingStatus.RetestPending,
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

        var chain = NewChain();
        var aggregate = rec.ToDomain(chain, clock);

        var result = action(aggregate);
        if (result.Failed) return result; // guard rejected — nothing appended, nothing saved

        rec.CopyFrom(aggregate);
        await db.SaveChangesAsync();
        return result;
    }

    /// <summary>
    /// Complete a retest child (FR-RET-03): the tester must have given every in-scope finding a
    /// pass/fail verdict first — completion is BLOCKED while any finding is still RetestPending.
    /// The verdict-recording happens via <see cref="SetFindingStatusAsync"/> (pass → Closed,
    /// fail → Open); only then may the child close, with its transition + audit appended atomically.
    /// </summary>
    public async Task<Result> CompleteRetestAsync(Guid id, string actor)
    {
        var pending = await db.Findings.AsNoTracking()
            .AnyAsync(f => f.EngagementId == id && f.Status == FindingStatus.RetestPending);
        if (pending)
            return Result.Fail("Every in-scope finding must be re-verified (pass/fail) before completing the retest (FR-RET-03).");
        return await ExecuteAsync(id, e => e.CompleteRetest(actor));
    }
}
