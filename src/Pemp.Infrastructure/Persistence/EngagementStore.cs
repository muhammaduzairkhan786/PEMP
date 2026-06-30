using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Pemp.Domain;
using Pemp.Domain.Audit;

namespace Pemp.Infrastructure.Persistence;

/// <summary>
/// Application service the UI calls. Loads a record, rehydrates the domain aggregate
/// bound to a DB-backed audit chain, runs the requested guarded transition, and — only
/// on success — persists the new state and the appended audit entries together.
/// A failed guard changes and saves nothing (the enforcement guarantee).
///
/// EVERY mutation first re-derives the caller's object-level scope (the SAME predicate as
/// <see cref="GetScopedAsync"/>) and applies the action's role / separation-of-duties policy
/// server-side (SEC-AZN-01/02). A cross-scope id, a wrong role, or an SoD violation is rejected
/// before any record is touched — so anti-BOLA / anti-priv-esc holds even when these become API
/// endpoints, not only behind the UI gates.
/// </summary>
public sealed class EngagementStore(PempDbContext db, Func<DateTimeOffset> clock, AuditHmacKey auditKey,
    ILogger<EngagementStore>? logger = null)
{
    // Structured logging (rank 6 / NFR-MNT). Never logs secrets, TOTP codes, or audit detail that may
    // carry a credential label/value — only ids, actor, role, action, and a correlation id. Optional so
    // the seeder/tests can construct the store without DI; production gets the DI ILogger.
    private readonly ILogger _log = logger ?? NullLogger<EngagementStore>.Instance;
    private static string? Corr => Activity.Current?.Id;
    /// <summary>
    /// Roles entitled to the WHOLE portfolio (SEC-AZN/SEC-INS-01). Every other role is "scoped":
    /// it must arrive with a concrete object filter and a blank/unresolved filter must match
    /// NOTHING — never the full set. These strings mirror the canonical PEMP role names.
    /// </summary>
    private static readonly IReadOnlySet<string> AllPortfolioRoles = PempRoles.Portfolio;

    // ---- Write-path authorization policy (SEC-AZN-01) -----------------------
    // The single source of truth for which role(s) may perform each mutation and the separation-of-duties
    // rules the lifecycle requires. Co-located with the store so it is authoritative server-side.
    private static readonly IReadOnlyDictionary<EngagementAction, ActionAuth> Policy =
        new Dictionary<EngagementAction, ActionAuth>
        {
            [EngagementAction.RouteToDeliveryManager] = new(ActionAuth.Roles(PempRoles.DeliveryManager, PempRoles.AcmeOfficer)),
            [EngagementAction.AssignTester]           = new(ActionAuth.Roles(PempRoles.DeliveryManager)),
            [EngagementAction.CompleteAssessment]     = new(ActionAuth.Roles(PempRoles.Tester, PempRoles.Stakeholder)),
            [EngagementAction.ReviewSow]              = new(ActionAuth.Roles(PempRoles.DeliveryManager)),
            // SoW sign-off is an Acme CA Officer action AND the signer must differ from the tester who
            // drafted it (separation of duties, FR-SOW-05).
            [EngagementAction.SignSow]                = new(ActionAuth.Roles(PempRoles.AcmeOfficer), MustDifferFromAssignedTester: true),
            [EngagementAction.VerifyAccess]           = new(ActionAuth.Roles(PempRoles.Tester), MustBeAssignedTester: true),
            [EngagementAction.SendIrNotice]           = new(ActionAuth.Roles(PempRoles.Tester), MustBeAssignedTester: true),
            [EngagementAction.EndTest]                = new(ActionAuth.Roles(PempRoles.Tester), MustBeAssignedTester: true),
            [EngagementAction.GenerateDraft]          = new(ActionAuth.Roles(PempRoles.Tester), MustBeAssignedTester: true),
            // Peer review's no-self-review SoD is enforced inside the domain (reviewer != author, FR-REP-02);
            // here we only require the actor be a tester in scope (the peer-QA review queue).
            [EngagementAction.PeerReview]             = new(ActionAuth.Roles(PempRoles.Tester)),
            [EngagementAction.ReleaseFinal]           = new(ActionAuth.Roles(PempRoles.AcmeOfficer, PempRoles.Tester)),
            [EngagementAction.RequestRetest]          = new(ActionAuth.Roles(PempRoles.AcmeOfficer, PempRoles.Stakeholder)),
            [EngagementAction.CompleteRetest]         = new(ActionAuth.Roles(PempRoles.Tester), MustBeAssignedTester: true),
            // Crown-jewel data mutations — only the assigned tester may write tester-owned data.
            [EngagementAction.AddFinding]             = new(ActionAuth.Roles(PempRoles.Tester), MustBeAssignedTester: true),
            [EngagementAction.SetFindingStatus]       = new(ActionAuth.Roles(PempRoles.Tester), MustBeAssignedTester: true),
            [EngagementAction.AddEvidence]            = new(ActionAuth.Roles(PempRoles.Tester), MustBeAssignedTester: true),
            [EngagementAction.AddTestCredential]      = new(ActionAuth.Roles(PempRoles.Tester), MustBeAssignedTester: true),
            [EngagementAction.AddAccessRequirement]   = new(ActionAuth.Roles(PempRoles.Tester), MustBeAssignedTester: true),
            [EngagementAction.SetAccessStatus]        = new(ActionAuth.Roles(PempRoles.Tester), MustBeAssignedTester: true),
            // Assessment input may also come from the application stakeholder (async).
            [EngagementAction.SaveAssessmentAnswer]   = new(ActionAuth.Roles(PempRoles.Tester, PempRoles.Stakeholder)),
            [EngagementAction.SetChecklist]           = new(ActionAuth.Roles(PempRoles.Tester), MustBeAssignedTester: true),
        };

    private EfAuditChain NewChain() => new(db, auditKey.Value, _log);

    /// <summary>
    /// The object-level scope predicate (SEC-AZN-02). Used identically for reads (<see cref="GetScopedAsync"/>)
    /// and writes (<see cref="Authorize"/>), so a record reachable for read is reachable for an authorized
    /// write and nothing else. Fails CLOSED: a scoped role with no concrete filter matches nothing.
    /// </summary>
    private static bool InScope(EngagementRecord rec, Guid? appScope, Guid? testerScope, string? role)
    {
        var scoped = !AllPortfolioRoles.Contains(role ?? "");
        if (scoped && appScope is null && testerScope is null) return false;
        // App scope keys on the STABLE app id (retest children share their parent's AppId), so the old
        // mutable-name + "(retest)" string hack is gone — a rename can't widen or break access.
        if (appScope is Guid app && rec.AppId != app) return false;
        if (testerScope is Guid tid)
        {
            var isAssigned = rec.AssignedTesterId == tid;
            // Peer-QA review access: a Report-stage engagement authored by ANOTHER tester is in scope.
            var isPeerReviewable = role == PempRoles.Tester
                && rec.CurrentStage == Stage.Report
                && rec.AssignedTesterId != null && rec.AssignedTesterId != tid;
            if (!isAssigned && !isPeerReviewable) return false;
        }
        return true;
    }

    /// <summary>
    /// The full write-path authorization decision (SEC-AZN-01/02): object-level scope + the action's
    /// role allow-list + separation of duties. Returns a friendly <see cref="Result.Fail"/> on denial so
    /// the UI can surface it; nothing is mutated.
    /// </summary>
    private static Result Authorize(EngagementRecord rec, CallerContext c, EngagementAction action)
    {
        var auth = Policy[action];
        if (!InScope(rec, c.AppScope, c.TesterScope, c.Role))
            return Result.Fail("Not authorized for this engagement — outside your scope (SEC-AZN-02).");
        if (auth.AllowedRoles is not null && (c.Role is null || !auth.AllowedRoles.Contains(c.Role)))
            return Result.Fail($"This action is restricted to: {string.Join(" / ", auth.AllowedRoles)} (SEC-AZN-01).");
        // SoD / ownership checks key on the STABLE assigned-tester id vs the caller's user id — never names.
        if (auth.MustBeAssignedTester && (c.UserId is null || rec.AssignedTesterId != c.UserId))
            return Result.Fail("Only the assigned tester may perform this action (SEC-AZN-02).");
        if (auth.MustDifferFromAssignedTester && c.UserId is Guid uid && rec.AssignedTesterId == uid)
            return Result.Fail("Separation of duties: the SoW signer must differ from the tester who drafted it (FR-SOW-05).");
        return Result.Success();
    }

    // Load + authorize a tracked engagement for a data mutation. Throws on denial (defense-in-depth: the
    // UI already gates these, so a denial here is a forged/BOLA attempt). Returns the tracked record.
    private async Task<EngagementRecord> AuthorizeTrackedAsync(Guid engagementId, CallerContext c, EngagementAction action)
    {
        var rec = await db.Engagements.FirstOrDefaultAsync(e => e.Id == engagementId)
                  ?? throw new InvalidOperationException("Engagement not found.");
        var result = Authorize(rec, c, action);
        if (result.Failed)
        {
            _log.LogWarning(
                "SEC-AZN: data mutation {Action} REJECTED on engagement {EngagementId} for actor={Actor} role={Role}: {Reason}. CorrelationId={CorrelationId}",
                action, engagementId, c.Actor, c.Role, result.Error, Corr);
            throw new UnauthorizedAccessException(result.Error);
        }
        return rec;
    }

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

    /// <summary>
    /// Unscoped full listing — INTERNAL only. Crosses every scope boundary, so it must never be reachable
    /// from a request path; callers use <see cref="ListScopedAsync"/>. Kept internal for the seeder/tests.
    /// </summary>
    internal Task<List<EngagementRecord>> ListAsync() =>
        db.Engagements.AsNoTracking().OrderBy(e => e.Reference).ToListAsync();

    /// <summary>
    /// Object-level scoped list (SEC-AZN/SEC-INS-01). Fails CLOSED: only an explicit all-portfolio
    /// <paramref name="role"/> (Acme/DM/Admin) gets an unrestricted listing. EVERY other role — a
    /// scoped role (Tester, Stakeholder), an unknown/blank role, AND a null role — MUST supply a
    /// concrete filter; with no filter, or a blank one, the result is empty, never the full portfolio.
    /// (Null role is treated as fail-closed too: an authenticated-but-unmapped principal sees nothing,
    /// not the whole estate.) A Tester additionally sees Report-stage engagements authored by a
    /// DIFFERENT tester — the independent peer-QA review queue (FR-REP-02) — without losing anti-BOLA
    /// elsewhere. Optional <paramref name="skip"/>/<paramref name="take"/> page the result.
    /// </summary>
    public Task<List<EngagementRecord>> ListScopedAsync(
        Guid? appScope, Guid? testerScope, string? role = null, int? skip = null, int? take = null)
    {
        var scoped = !AllPortfolioRoles.Contains(role ?? "");
        // Fail closed: a scoped role with no concrete filter at all sees nothing.
        if (scoped && appScope is null && testerScope is null)
            return Task.FromResult(new List<EngagementRecord>());

        var q = db.Engagements.AsNoTracking().AsQueryable();
        if (appScope is Guid app) q = q.Where(e => e.AppId == app);
        if (testerScope is Guid tid)
        {
            // A Tester's scope = own assignments ∪ Report-stage engagements authored by another tester
            // (the peer-QA queue, FR-REP-02), keyed on the stable AssignedTesterId.
            if (role == PempRoles.Tester)
            {
                q = q.Where(e => e.AssignedTesterId == tid
                    || (e.CurrentStage == Stage.Report && e.AssignedTesterId != null && e.AssignedTesterId != tid));
            }
            else
            {
                q = q.Where(e => e.AssignedTesterId == tid);
            }
        }
        q = q.OrderBy(e => e.Reference);
        if (skip is int s) q = q.Skip(s);
        if (take is int t) q = q.Take(t);
        return q.ToListAsync();
    }

    /// <summary>
    /// Unscoped fetch by id — INTERNAL only (crosses scope). Request paths use <see cref="GetScopedAsync"/>.
    /// </summary>
    internal Task<EngagementRecord?> GetAsync(Guid id) =>
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
    public async Task<EngagementRecord?> GetScopedAsync(Guid id, Guid? appScope, Guid? testerScope, string? role = null)
    {
        var rec = await db.Engagements.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id);
        if (rec is null) return null;
        return InScope(rec, appScope, testerScope, role) ? rec : null;
    }

    /// <summary>All findings across the portfolio (FR-ANL-04 analytics).</summary>
    public Task<List<FindingRecord>> AllFindingsAsync() =>
        db.Findings.AsNoTracking().ToListAsync();

    /// <summary>
    /// DB-side scoped analytics feed (FR-ANL-04): the count of OPEN findings (Open | RetestPending)
    /// per (engagement, severity) for the supplied — already scope-filtered — engagement ids. The
    /// filter + GROUP BY run in SQL, so only the compact per-bucket counts come back, never the full
    /// portfolio of finding rows. The dashboard derives totals, the severity distribution, and each
    /// app's worst severity from this small set (worst = enum-min, computed correctly client-side —
    /// Severity is persisted as a string, so a SQL MIN would order alphabetically, not by severity).
    /// An empty id set short-circuits to an empty result (a scoped role with no scope sees nothing).
    /// </summary>
    public Task<List<OpenFindingCount>> OpenFindingCountsAsync(IReadOnlyCollection<Guid> engagementIds)
    {
        if (engagementIds.Count == 0) return Task.FromResult(new List<OpenFindingCount>());
        var ids = engagementIds.ToList();
        return db.Findings.AsNoTracking()
            .Where(f => ids.Contains(f.EngagementId)
                        && (f.Status == FindingStatus.Open || f.Status == FindingStatus.RetestPending))
            .GroupBy(f => new { f.EngagementId, f.Severity })
            .Select(g => new OpenFindingCount(g.Key.EngagementId, g.Key.Severity, g.Count()))
            .ToListAsync();
    }

    /// <summary>
    /// The worst (highest) OPEN-finding CVSS base score per engagement (FR-ANL-04), for the supplied
    /// — already scope-filtered — engagement ids. The score is a real numeric column (decimal(3,1) on
    /// Azure SQL; REAL on SQLite), so the "worst" is a true NUMERIC maximum: a string MAX would rank
    /// "10.0" below "9.1" lexicographically. The compact per-engagement score rows are pulled and the
    /// max taken in memory (provider-agnostic — SQLite has no native decimal aggregate). An empty id
    /// set short-circuits (a scoped role with no engagements sees nothing).
    /// </summary>
    public async Task<Dictionary<Guid, decimal>> WorstOpenCvssAsync(IReadOnlyCollection<Guid> engagementIds)
    {
        if (engagementIds.Count == 0) return new();
        var ids = engagementIds.ToList();
        var rows = await db.Findings.AsNoTracking()
            .Where(f => ids.Contains(f.EngagementId)
                        && (f.Status == FindingStatus.Open || f.Status == FindingStatus.RetestPending)
                        && f.CvssScore != null)
            .Select(f => new { f.EngagementId, Score = f.CvssScore!.Value })
            .ToListAsync();
        return rows.GroupBy(r => r.EngagementId)
            .ToDictionary(g => g.Key, g => g.Max(r => r.Score));
    }

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
        string asset, string remediation, CallerContext caller,
        FindingStatus status = FindingStatus.Open, Guid? originalFindingId = null, decimal? cvssScore = null)
    {
        await AuthorizeTrackedAsync(engagementId, caller, EngagementAction.AddFinding);
        var finding = new FindingRecord
        {
            Id = Guid.NewGuid(),
            EngagementId = engagementId,
            Title = title,
            Severity = severity,
            Cvss = cvss,
            CvssScore = cvssScore,
            CvssVector = cvssVector,
            Asset = asset,
            Remediation = remediation,
            Status = status,
            OriginalFindingId = originalFindingId,
        };
        db.Findings.Add(finding);
        StageAudit(engagementId, caller.Actor, caller.Role, "Finding.Added", "-", $"{severity}: {title}");
        await db.SaveChangesAsync();
        return finding.Id;
    }

    // ---- Evidence (FR-FND-02 / SEC-EVD) ------------------------------------
    public Task<List<EvidenceRecord>> EvidenceForAsync(Guid id) =>
        db.Evidence.AsNoTracking().Where(e => e.EngagementId == id).ToListAsync();

    public async Task AddEvidenceAsync(Guid engagementId, Guid findingId, string fileName, EvidenceKind kind, string note,
        CallerContext caller)
    {
        await AuthorizeTrackedAsync(engagementId, caller, EngagementAction.AddEvidence);
        db.Evidence.Add(new EvidenceRecord
        {
            Id = Guid.NewGuid(), EngagementId = engagementId, FindingId = findingId,
            FileName = fileName, Kind = kind, Note = note, EncryptedAtRest = true,
        });
        StageAudit(engagementId, caller.Actor, caller.Role, "Evidence.Added", "-", fileName);
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Authorize + AUDIT an evidence download (SEC-EVD-02/03), atomically appending a hash-chained
    /// <c>Evidence.Downloaded</c> entry. Object-level scope is enforced with the SAME predicate as a
    /// scoped read (anti-BOLA, fail-closed) — viewing an engagement in scope implies the right to
    /// download its evidence, so this is open to any in-scope role (not just the assigned tester),
    /// unlike a data WRITE. The re-auth step-up is enforced by the caller (the ReauthDialog ceremony)
    /// BEFORE this is invoked, and the short-lived signed URL is minted only after this succeeds. The
    /// file LABEL is recorded — never the bytes, a path, or any secret. Nothing is audited on failure.
    /// </summary>
    public async Task<Result> RecordEvidenceDownloadAsync(Guid engagementId, Guid evidenceId, string fileLabel, CallerContext caller)
    {
        var rec = await db.Engagements.FirstOrDefaultAsync(e => e.Id == engagementId);
        if (rec is null) return Result.Fail("Engagement not found.");
        if (!InScope(rec, caller.AppScope, caller.TesterScope, caller.Role))
        {
            _log.LogWarning(
                "SEC-EVD: evidence download REJECTED on engagement {EngagementId} for actor={Actor} role={Role}: outside scope. CorrelationId={CorrelationId}",
                engagementId, caller.Actor, caller.Role, Corr);
            return Result.Fail("Not authorized for this engagement — outside your scope (SEC-AZN-02).");
        }
        var ev = await db.Evidence.AsNoTracking().FirstOrDefaultAsync(e => e.Id == evidenceId && e.EngagementId == engagementId);
        if (ev is null) return Result.Fail("Evidence not found.");
        StageAudit(engagementId, caller.Actor, caller.Role, "Evidence.Downloaded", "-", fileLabel);
        await db.SaveChangesAsync();
        _log.LogInformation(
            "Evidence.Downloaded on {EngagementId} by actor={Actor} role={Role}. CorrelationId={CorrelationId}",
            engagementId, caller.Actor, caller.Role, Corr);
        return Result.Success();
    }

    public bool VerifyChain() => NewChain().Verify();

    /// <summary>Global audit log for the admin console (FR-AUD-03), newest first; optional paging.</summary>
    public Task<List<AuditEntryRow>> AllAuditAsync(int? skip = null, int? take = null)
    {
        var q = db.AuditEntries.AsNoTracking().OrderByDescending(a => a.Sequence).AsQueryable();
        if (skip is int s) q = q.Skip(s);
        if (take is int t) q = q.Take(t);
        return q.ToListAsync();
    }

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
    public async Task<(List<AuditEntryRow> Rows, bool ChainIntact)> AllAuditVerifiedAsync(int? skip = null, int? take = null)
    {
        var rows = await AllAuditAsync(skip, take);
        return (rows, VerifyChain());
    }

    // ---- Assessment answers (workbook Tab 1 / FR-SCO) ----------------------
    public Task<Dictionary<string, string>> AssessmentAnswersAsync(Guid id) =>
        db.AssessmentAnswers.AsNoTracking().Where(a => a.EngagementId == id)
          .ToDictionaryAsync(a => a.QuestionId, a => a.Value);

    public async Task SaveAssessmentAnswerAsync(Guid id, string questionId, string value, CallerContext caller)
    {
        await AuthorizeTrackedAsync(id, caller, EngagementAction.SaveAssessmentAnswer);
        var row = await db.AssessmentAnswers.FirstOrDefaultAsync(a => a.EngagementId == id && a.QuestionId == questionId);
        if (row is null)
            db.AssessmentAnswers.Add(new AssessmentAnswerRecord { Id = Guid.NewGuid(), EngagementId = id, QuestionId = questionId, Value = value });
        else
            row.Value = value;
        // Log the question answered, not the answer value (assessment input may be sensitive).
        StageAudit(id, caller.Actor, caller.Role, "Assessment.AnswerSaved", "-", questionId);
        await db.SaveChangesAsync();
    }

    // ---- Access requirements (workbook Tab 3 / FR-ACC-01) ------------------
    public Task<List<AccessRequirementRecord>> AccessReqsForAsync(Guid id) =>
        db.AccessRequirements.AsNoTracking().Where(a => a.EngagementId == id).OrderBy(a => a.Environment).ToListAsync();

    public async Task SetAccessStatusAsync(Guid reqId, AccessStatus status, CallerContext caller)
    {
        var row = await db.AccessRequirements.FirstOrDefaultAsync(a => a.Id == reqId);
        if (row is null) return;
        await AuthorizeTrackedAsync(row.EngagementId, caller, EngagementAction.SetAccessStatus);
        var before = $"{row.Environment}: {row.Status}";
        row.Status = status;
        StageAudit(row.EngagementId, caller.Actor, caller.Role, "Access.StatusChanged", before, $"{row.Environment}: {status}");
        await db.SaveChangesAsync();
    }

    // ---- Tester checklists (workbook Tab 4) --------------------------------
    public async Task<HashSet<string>> ChecklistDoneAsync(Guid id) =>
        (await db.ChecklistTicks.AsNoTracking().Where(c => c.EngagementId == id && c.Done).Select(c => c.Code).ToListAsync()).ToHashSet();

    public async Task SetChecklistAsync(Guid id, string code, bool done, CallerContext caller)
    {
        await AuthorizeTrackedAsync(id, caller, EngagementAction.SetChecklist);
        var row = await db.ChecklistTicks.FirstOrDefaultAsync(c => c.EngagementId == id && c.Code == code);
        if (row is null)
            db.ChecklistTicks.Add(new ChecklistTickRecord { Id = Guid.NewGuid(), EngagementId = id, Code = code, Done = done });
        else
            row.Done = done;
        StageAudit(id, caller.Actor, caller.Role, "Checklist.Updated", code, $"{code}={(done ? "done" : "cleared")}");
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Raise a new engagement request (FR-REQ-01/04): mints a reference, enters Intake,
    /// and writes the first audit entry. Returns the new engagement id. The reference is minted
    /// inside the same transaction and backstopped by the unique index, so a duplicate cannot be
    /// committed under concurrency.
    /// </summary>
    public async Task<Guid> RaiseAsync(EngagementType type, string appName, string criticality, CallerContext caller)
    {
        if (caller.Role != PempRoles.AcmeOfficer)
        {
            _log.LogWarning(
                "SEC-AZN: Raise REJECTED for actor={Actor} role={Role} (Acme CA Officer only). CorrelationId={CorrelationId}",
                caller.Actor, caller.Role, Corr);
            throw new UnauthorizedAccessException("Raising requests is the Acme CA Officer's action (FR-REQ-01).");
        }

        // Retry on the unique-index backstop: two concurrent raises can compute the same next number;
        // the loser's SaveChanges violates the unique Reference index and we recompute + retry.
        for (var attempt = 0; ; attempt++)
        {
            var reference = await NextReferenceAsync();
            var chain = NewChain();
            var engagement = Engagement.Raise(reference, type, caller.Actor, chain, clock);
            // Engagements for the same application share one stable AppId (deterministic from the name),
            // so stakeholder app-scope is consistent across an app's engagements.
            db.Engagements.Add(EngagementRecord.FromDomain(engagement, DemoSeeder.AppIdFor(appName), appName, criticality, null));
            try
            {
                await db.SaveChangesAsync();
                return engagement.Id;
            }
            catch (DbUpdateException) when (attempt < 5)
            {
                // Drop the failed tracked entities and retry with a freshly minted reference.
                foreach (var entry in db.ChangeTracker.Entries().Where(e => e.State == EntityState.Added).ToList())
                    entry.State = EntityState.Detached;
            }
        }
    }

    // Compute the next ENG-2026-NNNN reference in the DB (max numeric suffix + 1). Computed at mint
    // time and protected by the unique index; the racy "load all references into memory" path is gone.
    private async Task<string> NextReferenceAsync()
    {
        var refs = await db.Engagements.AsNoTracking().Select(e => e.Reference).ToListAsync();
        var maxNum = refs
            .Select(r => int.TryParse(r.Split('-').Last(), out var n) ? n : 0)
            .DefaultIfEmpty(420).Max();
        return $"ENG-2026-{maxNum + 1:D4}";
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
    /// <summary>
    /// Credentials for an engagement, filtered for the caller (SEC-CRD): revoked or expired secrets
    /// are never returned. The lifecycle metadata stays so the UI can show "revoked"/"expired" rows
    /// without the secret value.
    /// </summary>
    public async Task<List<TestCredentialRecord>> CredentialsForAsync(Guid id)
    {
        var now = clock();
        var rows = await db.TestCredentials.AsNoTracking().Where(c => c.EngagementId == id).OrderBy(c => c.Label).ToListAsync();
        foreach (var c in rows)
        {
            if (c.Revoked || (c.ExpiresAt is DateTimeOffset exp && exp <= now))
                c.Secret = ""; // crypto-shred from the read projection (don't surface revoked/expired secrets)
        }
        return rows;
    }

    /// <summary>
    /// Attach a test-account credential to an engagement (SEC-CRD). The secret is persisted for the
    /// demo; production stores it in Key Vault / envelope-encrypted. Records the credential lifecycle
    /// (created/added-by, optional expiry). Returns the new credential id.
    /// </summary>
    public async Task<Guid> AddTestCredentialAsync(Guid engagementId, string label, string username, string secret,
        CallerContext caller, DateTimeOffset? expiresAt = null)
    {
        await AuthorizeTrackedAsync(engagementId, caller, EngagementAction.AddTestCredential);
        var cred = new TestCredentialRecord
        {
            Id = Guid.NewGuid(), EngagementId = engagementId,
            Label = label, Username = username, Secret = secret,
            CreatedAt = clock(), AddedBy = caller.Actor, ExpiresAt = expiresAt,
        };
        db.TestCredentials.Add(cred);
        // Log the credential's LABEL only — never the secret (SEC-CRD: secrets are never logged).
        StageAudit(engagementId, caller.Actor, caller.Role, "Credential.Added", "-", label);
        await db.SaveChangesAsync();
        return cred.Id;
    }

    /// <summary>
    /// Set a finding's status in the live register (FR-FND-04 / FR-RET-03): used by the retest
    /// pass/fail flow (pass → Closed, fail → Open) and remediation tracking. The change is a GUARDED
    /// transition (<see cref="FindingLifecycle.CanTransition"/>): an illegal jump (e.g. reviving a
    /// Closed finding) is rejected and nothing is changed or audited — the register can't be driven
    /// into a nonsensical state. A no-op (same status) is accepted silently.
    /// </summary>
    public async Task<Result> SetFindingStatusAsync(Guid findingId, FindingStatus status, CallerContext caller)
    {
        var row = await db.Findings.FirstOrDefaultAsync(f => f.Id == findingId);
        if (row is null) return Result.Fail("Finding not found.");
        await AuthorizeTrackedAsync(row.EngagementId, caller, EngagementAction.SetFindingStatus);
        if (row.Status == status) return Result.Success();   // idempotent no-op
        if (!FindingLifecycle.CanTransition(row.Status, status))
        {
            _log.LogInformation(
                "Guard rejected Finding.StatusChanged {From}->{To} on finding {FindingId} for actor={Actor}. CorrelationId={CorrelationId}",
                row.Status, status, findingId, caller.Actor, Corr);
            return Result.Fail($"A finding cannot move from {row.Status} to {status} (FR-FND-04).");
        }
        var before = row.Status.ToString();
        row.Status = status;
        StageAudit(row.EngagementId, caller.Actor, caller.Role, "Finding.StatusChanged", before, status.ToString());
        await db.SaveChangesAsync();
        return Result.Success();
    }

    /// <summary>
    /// Add a tester-defined access requirement row (FR-ACC-01): the owning tester adds the
    /// environment/asset they need at the Access stage. Returns the new record.
    /// </summary>
    public async Task<AccessRequirementRecord> AddAccessRequirementAsync(
        Guid engagementId, string environment, string url, string accessType, CallerContext caller)
    {
        await AuthorizeTrackedAsync(engagementId, caller, EngagementAction.AddAccessRequirement);
        var row = new AccessRequirementRecord
        {
            Id = Guid.NewGuid(), EngagementId = engagementId,
            Environment = environment, Url = url, AccessType = accessType,
            Status = AccessStatus.AppTeamToProvision,
        };
        db.AccessRequirements.Add(row);
        StageAudit(engagementId, caller.Actor, caller.Role, "Access.RequirementAdded", "-", environment);
        await db.SaveChangesAsync();
        return row;
    }

    /// <summary>
    /// Assign a tester (FR-ASG-03) and record the display name together, so the card/header
    /// don't show a stale "—" after assignment.
    /// </summary>
    public async Task<Result> AssignTesterAsync(Guid id, Guid testerId, string testerName, CallerContext caller)
    {
        var rec = await db.Engagements.FirstOrDefaultAsync(e => e.Id == id);
        if (rec is null) return Result.Fail("Engagement not found.");
        var authz = Authorize(rec, caller, EngagementAction.AssignTester);
        if (authz.Failed)
        {
            _log.LogWarning("SEC-AZN: AssignTester REJECTED on {EngagementId} for actor={Actor} role={Role}: {Reason}. CorrelationId={CorrelationId}",
                id, caller.Actor, caller.Role, authz.Error, Corr);
            return authz;
        }
        var chain = NewChain();
        var aggregate = rec.ToDomain(chain, clock);
        var result = aggregate.AssignTester(testerId, caller.Actor, testerLabel: testerName);
        if (result.Failed)
        {
            _log.LogWarning("Guard rejected AssignTester on {EngagementId}: {Reason}. CorrelationId={CorrelationId}", id, result.Error, Corr);
            return result;
        }
        rec.CopyFrom(aggregate);
        rec.AssignedTesterName = testerName;
        rec.RowVersion = Guid.NewGuid().ToByteArray();
        await db.SaveChangesAsync();
        return result;
    }

    /// <summary>
    /// Request a retest on a closed engagement (FR-RET-01/02): spawns a linked child that
    /// re-verifies remediated findings. Persists the parent (RetestRequested) + the new
    /// child record + the audit entries together. Returns the child id on success. Each carried
    /// finding records its <see cref="FindingRecord.OriginalFindingId"/> for before/after provenance.
    /// </summary>
    public async Task<(Result Result, Guid? ChildId)> RequestRetestAsync(Guid parentId, CallerContext caller)
    {
        var parent = await db.Engagements.FirstOrDefaultAsync(e => e.Id == parentId);
        if (parent is null) return (Result.Fail("Engagement not found."), null);
        var authz = Authorize(parent, caller, EngagementAction.RequestRetest);
        if (authz.Failed)
        {
            _log.LogWarning("SEC-AZN: RequestRetest REJECTED on {EngagementId} for actor={Actor} role={Role}: {Reason}. CorrelationId={CorrelationId}",
                parentId, caller.Actor, caller.Role, authz.Error, Corr);
            return (authz, null);
        }

        var chain = NewChain();
        var aggregate = parent.ToDomain(chain, clock);
        var childRef = $"{parent.Reference}-RT";

        var result = aggregate.RequestRetest(childRef, caller.Actor, out var child);
        if (result.Failed || child is null) return (result, null);

        parent.CopyFrom(aggregate);
        // The retest child shares the parent's STABLE AppId (so app-scope and tester-scope reach it
        // without any string hack); the "(retest)" suffix is now display-only.
        var childRec = EngagementRecord.FromDomain(child, parent.AppId, $"{parent.AppName} (retest)", parent.Criticality, parent.AssignedTesterName);
        db.Engagements.Add(childRec);

        // Carry the in-scope findings into the child to be re-verified (FR-RET-03): everything
        // not terminal — Open, RetestPending, AND Remediated (a retest verifies the fixes). Each child
        // finding links back to its origin (OriginalFindingId) so the report can show before/after.
        var inScope = await db.Findings.AsNoTracking()
            .Where(f => f.EngagementId == parentId
                        && f.Status != FindingStatus.Closed && f.Status != FindingStatus.AcceptedRisk)
            .ToListAsync();
        foreach (var f in inScope)
            db.Findings.Add(new FindingRecord
            {
                Id = Guid.NewGuid(), EngagementId = child.Id, Title = f.Title,
                Severity = f.Severity, Cvss = f.Cvss, CvssScore = f.CvssScore, CvssVector = f.CvssVector,
                Asset = f.Asset, Remediation = f.Remediation, Status = FindingStatus.RetestPending,
                OriginalFindingId = f.Id,
            });

        await db.SaveChangesAsync();
        return (result, child.Id);
    }

    /// <summary>
    /// Run a guarded transition on the aggregate (the lifecycle write path). Authorizes the caller for
    /// THIS engagement and action first (object-level scope + role + SoD, SEC-AZN), then invokes the
    /// domain method. State + audit persist only if both the authorization and the guard succeed; an
    /// optimistic-concurrency conflict surfaces as a friendly "reload and retry" failure, never a throw.
    /// </summary>
    public async Task<Result> ExecuteAsync(Guid id, CallerContext caller, EngagementAction action, Func<Engagement, Result> act)
    {
        var rec = await db.Engagements.FirstOrDefaultAsync(e => e.Id == id);
        if (rec is null) return Result.Fail("Engagement not found.");

        var authz = Authorize(rec, caller, action);
        if (authz.Failed)
        {
            _log.LogWarning(
                "SEC-AZN: transition {Action} REJECTED on {EngagementId} for actor={Actor} role={Role}: {Reason}. CorrelationId={CorrelationId}",
                action, id, caller.Actor, caller.Role, authz.Error, Corr);
            return authz; // not authorized — nothing appended, nothing saved
        }

        var chain = NewChain();
        var aggregate = rec.ToDomain(chain, clock);

        var result = act(aggregate);
        if (result.Failed)
        {
            // Guard rejection — the defining-trait enforcement firing. Logged so blocked actions are
            // observable (NFR-MNT); the reason text carries no secret.
            _log.LogInformation(
                "Guard rejected {Action} on {EngagementId} for actor={Actor} role={Role}: {Reason}. CorrelationId={CorrelationId}",
                action, id, caller.Actor, caller.Role, result.Error, Corr);
            return result; // nothing appended, nothing saved
        }

        rec.CopyFrom(aggregate);
        rec.RowVersion = Guid.NewGuid().ToByteArray(); // bump the concurrency token (checked in the UPDATE WHERE)

        // Auto-revoke test credentials the moment the engagement closes (SEC-CRD: time-boxed, revoked
        // after the engagement). Done in the SAME unit of work as the closing transition + its audit
        // append, with one chained audit entry — so a closed engagement never leaves live secrets behind.
        if (rec.CurrentStage == Stage.Closed)
        {
            var live = await db.TestCredentials.Where(c => c.EngagementId == id && !c.Revoked).ToListAsync();
            if (live.Count > 0)
            {
                var now = clock();
                foreach (var c in live) { c.Revoked = true; c.RevokedAt = now; }
                // Count only — never the labels/secrets.
                chain.Append(id, caller.Actor, "Credential.AutoRevoked", $"{live.Count} active",
                    "revoked on engagement close", string.IsNullOrEmpty(caller.Role) ? "system" : $"system:{caller.Role}", now);
            }
        }

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            // TOCTOU: another transition committed between our read and write. Don't corrupt or
            // last-write-win — drop the uncommitted audit append + state change so the context isn't
            // left dirty, and ask the caller to reload and retry against fresh state.
            foreach (var entry in db.ChangeTracker.Entries().Where(e => e.State == EntityState.Added).ToList())
                entry.State = EntityState.Detached;
            _log.LogWarning(
                "Optimistic-concurrency conflict on {Action} for {EngagementId} (actor={Actor}). CorrelationId={CorrelationId}",
                action, id, caller.Actor, Corr);
            return Result.Fail("This engagement changed in another session. Reload and try again.");
        }
        _log.LogInformation(
            "{Action} committed on {EngagementId} by actor={Actor} role={Role}. CorrelationId={CorrelationId}",
            action, id, caller.Actor, caller.Role, Corr);
        return result;
    }

    /// <summary>
    /// Complete the assessment (FR-SCO-03) as a DATA-DERIVED gate: the assessment can only be marked
    /// complete once at least one assessment answer has actually been recorded — the domain guard is
    /// fed the real fact, not a UI flag, so an empty questionnaire cannot advance the engagement.
    /// </summary>
    public async Task<Result> CompleteAssessmentAsync(Guid id, CallerContext caller)
    {
        var hasAnswers = await db.AssessmentAnswers.AnyAsync(a => a.EngagementId == id);
        return await ExecuteAsync(id, caller, EngagementAction.CompleteAssessment,
            e => e.CompleteAssessment(caller.Actor, assessmentDataPresent: hasAnswers));
    }

    /// <summary>
    /// Verify access (FR-ACC-04) as a DATA-DERIVED gate: access is "verified" only once at least one
    /// access requirement has been recorded AND every recorded requirement is provisioned (or marked
    /// not-required). The real provisioning state — not a click — is passed into the domain guard, so
    /// testing cannot begin while any requirement is still outstanding. Re-auth-gated (SEC-IAM-04).
    /// </summary>
    public async Task<Result> VerifyAccessAsync(Guid id, CallerContext caller, bool reAuthenticated)
    {
        var statuses = await db.AccessRequirements.AsNoTracking()
            .Where(a => a.EngagementId == id).Select(a => a.Status).ToListAsync();
        var provisioned = statuses.Count > 0
            && statuses.All(s => s == AccessStatus.Provisioned || s == AccessStatus.NotRequired);
        return await ExecuteAsync(id, caller, EngagementAction.VerifyAccess,
            e => e.VerifyAccess(caller.Actor, reAuthenticated, accessProvisioned: provisioned));
    }

    /// <summary>
    /// Complete a retest child (FR-RET-03): the tester must have given every in-scope finding a
    /// pass/fail verdict first — completion is BLOCKED while any finding is still RetestPending.
    /// The verdict-recording happens via <see cref="SetFindingStatusAsync"/> (pass → Closed,
    /// fail → Open); only then may the child close, with its transition + audit appended atomically.
    /// </summary>
    public async Task<Result> CompleteRetestAsync(Guid id, CallerContext caller)
    {
        var pending = await db.Findings.AsNoTracking()
            .AnyAsync(f => f.EngagementId == id && f.Status == FindingStatus.RetestPending);
        if (pending)
            return Result.Fail("Every in-scope finding must be re-verified (pass/fail) before completing the retest (FR-RET-03).");
        return await ExecuteAsync(id, caller, EngagementAction.CompleteRetest, e => e.CompleteRetest(caller.Actor));
    }
}
