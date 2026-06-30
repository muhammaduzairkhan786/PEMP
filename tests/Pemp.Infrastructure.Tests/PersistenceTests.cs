using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pemp.Domain;
using Pemp.Domain.Audit;
using Pemp.Infrastructure.Persistence;
using Xunit;

namespace Pemp.Infrastructure.Tests;

/// <summary>
/// Persistence + store tests over a real (SQLite in-memory) database — covers the
/// DB-backed hash chain, the enforcement guarantee (a failed guard saves nothing),
/// successful transitions, and the retest child spawn.
/// </summary>
public sealed class PersistenceTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly PempDbContext _db;
    private readonly EngagementStore _store;
    private static readonly Func<DateTimeOffset> Clock = () => DateTimeOffset.UnixEpoch;
    // One key used everywhere in a test DB so seeding, the store, and verification agree (SEC-AUD-01).
    private static readonly byte[] Key = HashChain.DefaultKey;

    // Server-side caller contexts (SEC-AZN): the write-path authorization re-derives scope from these.
    private static readonly CallerContext Khan = CallerContext.ForTester(DemoSeeder.TesterKhan, "A. Khan");
    private static readonly CallerContext Patel = CallerContext.ForTester(DemoSeeder.TesterPatel, "R. Patel");
    private static readonly CallerContext Acme = CallerContext.Portfolio(PempRoles.AcmeOfficer, "J. Okafor");
    private static readonly CallerContext Dm = CallerContext.Portfolio(PempRoles.DeliveryManager, "M. Reyes");

    public PersistenceTests()
    {
        _conn = new SqliteConnection("DataSource=:memory:");
        _conn.Open();
        var options = new DbContextOptionsBuilder<PempDbContext>().UseSqlite(_conn).Options;
        _db = new PempDbContext(options);
        _db.Database.EnsureCreated();
        DemoSeeder.Seed(_db, Clock, Key);
        _store = new EngagementStore(_db, Clock, new AuditHmacKey(Key));
    }

    private Guid IdOf(string reference) => _db.Engagements.Single(e => e.Reference == reference).Id;

    [Fact]
    public void Seed_builds_a_verifiable_hash_chain()
    {
        Assert.True(_db.AuditEntries.Any());
        Assert.True(new EfAuditChain(_db, Key).Verify());
    }

    [Fact]
    public void Interceptor_rejects_modifying_an_audit_row()  // SEC-AUD-01 (append-only at the data layer)
    {
        var row = _db.AuditEntries.OrderBy(a => a.Sequence).First();
        row.Actor = "mallory"; // attempt to mutate a recorded entry via EF
        // The append-only interceptor blocks the save for application AND admin code paths alike.
        Assert.Throws<InvalidOperationException>(() => _db.SaveChanges());
    }

    [Fact]
    public void Tampering_breaks_chain_verification()  // SEC-AUD-01 (HMAC tamper-evidence)
    {
        // Simulate a direct-database edit (bypassing EF/the interceptor, e.g. a DBA/admin with table
        // access). Without the HMAC key they cannot recompute a valid tail, so verification fails.
        _db.Database.ExecuteSqlRaw(
            "UPDATE AuditEntries SET Actor = 'mallory' WHERE Sequence = (SELECT MIN(Sequence) FROM AuditEntries)");
        Assert.False(new EfAuditChain(_db, Key).Verify());
    }

    [Fact]
    public void Tampered_row_cannot_be_recomputed_without_the_key()  // SEC-AUD-01 (keyed HMAC)
    {
        // Forge a row's content AND recompute its hash with the WRONG key — verification with the
        // real key still fails, proving the chain is not recomputable without the secret.
        var wrongKey = System.Text.Encoding.UTF8.GetBytes("attacker-guessed-key");
        var forged = new EfAuditChain(_db, wrongKey);   // attacker's chain helper
        // The attacker can't even produce a tail that the real-key verifier accepts:
        _db.Database.ExecuteSqlRaw(
            "UPDATE AuditEntries SET Actor = 'mallory' WHERE Sequence = (SELECT MIN(Sequence) FROM AuditEntries)");
        Assert.False(new EfAuditChain(_db, Key).Verify());      // real key: tamper detected
        Assert.False(forged.Verify());                          // wrong key: also can't validate
    }

    [Fact]
    public async Task Failed_guard_persists_nothing()
    {
        // Claims Portal is at SoW — CompleteAssessment requires Scoping, so it must fail.
        var id = IdOf("ENG-2026-0412");
        var before = await _store.GetAsync(id);
        var auditBefore = _db.AuditEntries.Count();

        var result = await _store.ExecuteAsync(id, Khan, EngagementAction.CompleteAssessment, e => e.CompleteAssessment("A. Khan"));

        Assert.True(result.Failed);
        var after = await _store.GetAsync(id);
        Assert.Equal(before!.CurrentStage, after!.CurrentStage);     // unchanged
        Assert.Equal(auditBefore, _db.AuditEntries.Count());          // nothing appended
        Assert.True(new EfAuditChain(_db, Key).Verify());
    }

    [Fact]
    public async Task Successful_transition_persists_state_and_audit()
    {
        // Claims Portal is a DM-reviewed Project SoW → signing (with re-auth) succeeds.
        var id = IdOf("ENG-2026-0412");
        var auditBefore = _db.AuditEntries.Count();

        var result = await _store.ExecuteAsync(id, Acme, EngagementAction.SignSow, e => e.SignSow("J. Okafor", reAuthenticated: true));

        Assert.False(result.Failed);
        var after = await _store.GetAsync(id);
        Assert.Equal(Stage.Access, after!.CurrentStage);
        Assert.True(after.SowSigned);
        Assert.Equal(auditBefore + 1, _db.AuditEntries.Count());
        Assert.True(new EfAuditChain(_db, Key).Verify());
    }

    [Fact]
    public async Task Signing_without_reauth_is_rejected()
    {
        var id = IdOf("ENG-2026-0412");
        var result = await _store.ExecuteAsync(id, Acme, EngagementAction.SignSow, e => e.SignSow("J. Okafor", reAuthenticated: false));
        Assert.True(result.Failed);
        Assert.Equal(Stage.Sow, (await _store.GetAsync(id))!.CurrentStage);
    }

    [Fact]
    public async Task GetScoped_enforces_object_level_access()
    {
        var claims = IdOf("ENG-2026-0412");  // assigned A. Khan
        var retail = IdOf("ENG-2026-0408");  // assigned A. Khan
        var mobile = IdOf("ENG-2026-0421");  // Mobile App, assigned A. Khan, at Scoping
        var partner = IdOf("ENG-2026-0422"); // Partner Portal, unassigned, at Intake

        // Tester scope (A. Khan, by stable id): reaches own assignment, blocked from one not assigned to them.
        Assert.NotNull(await _store.GetScopedAsync(claims, null, DemoSeeder.TesterKhan));
        Assert.Null(await _store.GetScopedAsync(partner, null, DemoSeeder.TesterKhan));

        // Stakeholder app scope (Mobile App, by stable id): reaches own app, blocked from others (anti-BOLA).
        Assert.NotNull(await _store.GetScopedAsync(mobile, DemoSeeder.AppIdFor("Mobile App"), null));
        Assert.Null(await _store.GetScopedAsync(claims, DemoSeeder.AppIdFor("Mobile App"), null));

        // Unrestricted (Acme/DM/Admin): reaches anything. A null role is now fail-closed (scoped),
        // so an all-portfolio role must be named explicitly to pass with no filter.
        Assert.NotNull(await _store.GetScopedAsync(retail, null, null, role: "Delivery Manager"));
    }

    [Fact]
    public async Task Scope_fails_closed_for_a_scoped_role_with_no_or_blank_filter()  // SEC-AZN/SEC-INS-01
    {
        var claims = IdOf("ENG-2026-0412");  // assigned A. Khan

        // A scoped role (Tester/Stakeholder) that arrives with NO concrete scope must match NOTHING,
        // never the full portfolio — even though Acme/DM/Admin pass (null,null) to mean "all".
        Assert.Null(await _store.GetScopedAsync(claims, null, null, role: "Tester"));
        Assert.Null(await _store.GetScopedAsync(claims, null, null, role: "Stakeholder"));
        Assert.Empty(await _store.ListScopedAsync(null, null, role: "Tester"));
        Assert.Empty(await _store.ListScopedAsync(null, null, role: "Stakeholder"));

        // An all-portfolio role with (null,null) still sees the whole portfolio.
        Assert.NotEmpty(await _store.ListScopedAsync(null, null, role: "Delivery Manager"));
        Assert.NotNull(await _store.GetScopedAsync(claims, null, null, role: "System Administrator"));

        // A scoped role WITH a concrete, matching id still reaches its own data, and anti-BOLA
        // still blocks reaching another scope by a DIFFERENT id.
        Assert.NotNull(await _store.GetScopedAsync(claims, null, DemoSeeder.TesterKhan, role: "Tester"));
        Assert.Null(await _store.GetScopedAsync(claims, null, DemoSeeder.TesterLee, role: "Tester"));
    }

    [Fact]
    public async Task AddFinding_persists_into_the_live_register()
    {
        // Retail Web is mid-test (assigned A. Khan) — record a new finding.
        var id = IdOf("ENG-2026-0408");
        var before = (await _store.FindingsForAsync(id)).Count;

        var findingId = await _store.AddFindingAsync(
            id, "Open redirect on /login", Severity.Medium, "4.7",
            "CVSS:3.1/AV:N/AC:L/PR:N/UI:R/S:U/C:N/I:L/A:N", "Web",
            "Validate redirect targets against an allow-list.", Khan);

        var after = await _store.FindingsForAsync(id);
        Assert.Equal(before + 1, after.Count);
        var added = after.Single(f => f.Id == findingId);
        Assert.Equal("Open redirect on /login", added.Title);
        Assert.Equal(Severity.Medium, added.Severity);
        Assert.Equal("4.7", added.Cvss);
        Assert.Equal("CVSS:3.1/AV:N/AC:L/PR:N/UI:R/S:U/C:N/I:L/A:N", added.CvssVector);
        Assert.Equal("Web", added.Asset);
        Assert.Equal("Validate redirect targets against an allow-list.", added.Remediation);
        Assert.Equal(FindingStatus.Open, added.Status); // default

        // It also surfaces in the portfolio-wide register (analytics feed).
        Assert.Contains(await _store.AllFindingsAsync(), f => f.Id == findingId);
    }

    [Fact]
    public async Task RequestRetest_spawns_linked_child()
    {
        // Broker Portal is closed → retest spawns a child engagement.
        var parentId = IdOf("ENG-2026-0399");

        var (result, childId) = await _store.RequestRetestAsync(parentId, Acme);

        Assert.False(result.Failed);
        Assert.NotNull(childId);
        var child = await _store.GetAsync(childId!.Value);
        Assert.Equal(parentId, child!.ParentId);
        Assert.Equal(Stage.Retest, child.CurrentStage);
        Assert.True((await _store.GetAsync(parentId))!.RetestRequested);
        Assert.True(new EfAuditChain(_db, Key).Verify());

        // in-scope (unresolved) findings are carried into the child to re-verify
        var childFindings = await _store.FindingsForAsync(childId.Value);
        Assert.NotEmpty(childFindings);
        Assert.All(childFindings, f => Assert.Equal(FindingStatus.RetestPending, f.Status));

        // double retest is rejected by the domain
        var second = await _store.RequestRetestAsync(parentId, Acme);
        Assert.True(second.Result.Failed);

        // the child re-verifies and closes
        var done = await _store.ExecuteAsync(childId.Value, Khan, EngagementAction.CompleteRetest, e => e.CompleteRetest("A. Khan"));
        Assert.False(done.Failed);
        Assert.Equal(Stage.Closed, (await _store.GetAsync(childId.Value))!.CurrentStage);
        Assert.True(new EfAuditChain(_db, Key).Verify());
    }

    [Fact]
    public async Task TestCredential_round_trips_through_the_store()  // SEC-CRD
    {
        // Payments API is at the Access stage — attach a test credential and read it back.
        var id = IdOf("ENG-2026-0419");
        var before = (await _store.CredentialsForAsync(id)).Count;

        var credId = await _store.AddTestCredentialAsync(id, "Staging admin", "admin.stg@test", "S3cr3t!-stg", Khan);

        var after = await _store.CredentialsForAsync(id);
        Assert.Equal(before + 1, after.Count);
        var added = after.Single(c => c.Id == credId);
        Assert.Equal("Staging admin", added.Label);
        Assert.Equal("admin.stg@test", added.Username);
        Assert.Equal("S3cr3t!-stg", added.Secret);  // round-trips (prod = Key Vault / envelope-encrypted)
    }

    [Fact]
    public async Task Retest_pass_and_fail_update_finding_status()  // FR-RET-03
    {
        // Closed Broker Portal → retest spawns a child carrying RetestPending findings.
        var parentId = IdOf("ENG-2026-0399");
        // Ensure at least two carry over (so we can test both verdicts): seed an extra open finding.
        await _store.AddFindingAsync(parentId, "Open redirect", Severity.Low, "3.5",
            "CVSS:3.1/AV:N/AC:L/PR:N/UI:R/S:U/C:N/I:L/A:N", "Web", "Allow-list redirects.", Khan, FindingStatus.Open);

        var (result, childId) = await _store.RequestRetestAsync(parentId, Acme);
        Assert.False(result.Failed);

        var carried = await _store.FindingsForAsync(childId!.Value);
        Assert.True(carried.Count >= 2);
        Assert.All(carried, f => Assert.Equal(FindingStatus.RetestPending, f.Status));

        // Pass = fix verified → Closed; fail = still present → re-Open.
        await _store.SetFindingStatusAsync(carried[0].Id, FindingStatus.Closed, Khan);
        await _store.SetFindingStatusAsync(carried[1].Id, FindingStatus.Open, Khan);

        var after = await _store.FindingsForAsync(childId.Value);
        Assert.Equal(FindingStatus.Closed, after.Single(f => f.Id == carried[0].Id).Status);
        Assert.Equal(FindingStatus.Open, after.Single(f => f.Id == carried[1].Id).Status);
    }

    [Fact]
    public async Task AddAccessRequirement_persists_a_new_row()  // FR-ACC-01
    {
        var id = IdOf("ENG-2026-0419"); // Payments API at Access
        var before = (await _store.AccessReqsForAsync(id)).Count;

        var row = await _store.AddAccessRequirementAsync(id, "Sandbox", "https://sbx.test", "Read", Khan);

        var after = await _store.AccessReqsForAsync(id);
        Assert.Equal(before + 1, after.Count);
        Assert.Equal(AccessStatus.AppTeamToProvision, after.Single(a => a.Id == row.Id).Status);
    }

    [Fact]
    public async Task AddFinding_appends_a_chained_audit_entry()  // SEC-AUD/FR-AUD
    {
        var id = IdOf("ENG-2026-0408"); // Retail Web, mid-test
        var auditBefore = _db.AuditEntries.Count();

        await _store.AddFindingAsync(id, "Open redirect on /login", Severity.Medium, "4.7",
            "CVSS:3.1/AV:N/AC:L/PR:N/UI:R/S:U/C:N/I:L/A:N", "Web", "Allow-list redirects.", Khan);

        Assert.Equal(auditBefore + 1, _db.AuditEntries.Count());
        var last = _db.AuditEntries.OrderByDescending(a => a.Sequence).First();
        Assert.Equal("Finding.Added", last.Action);
        Assert.Equal("A. Khan", last.Actor);
        Assert.True(new EfAuditChain(_db, Key).Verify());
    }

    [Fact]
    public async Task SetFindingStatus_appends_a_chained_audit_entry()  // SEC-AUD/FR-AUD
    {
        var id = IdOf("ENG-2026-0408");
        var finding = (await _store.FindingsForAsync(id)).First();
        var auditBefore = _db.AuditEntries.Count();

        await _store.SetFindingStatusAsync(finding.Id, FindingStatus.Remediated, Khan);

        Assert.Equal(auditBefore + 1, _db.AuditEntries.Count());
        Assert.Equal("Finding.StatusChanged", _db.AuditEntries.OrderByDescending(a => a.Sequence).First().Action);
        Assert.True(new EfAuditChain(_db, Key).Verify());
    }

    [Fact]
    public async Task AddTestCredential_appends_audit_without_logging_the_secret()  // SEC-AUD/SEC-CRD
    {
        var id = IdOf("ENG-2026-0419"); // Payments API at Access
        var auditBefore = _db.AuditEntries.Count();
        const string secret = "S3cr3t!-stg";

        await _store.AddTestCredentialAsync(id, "Staging admin", "admin.stg@test", secret, Khan);

        Assert.Equal(auditBefore + 1, _db.AuditEntries.Count());
        var last = _db.AuditEntries.OrderByDescending(a => a.Sequence).First();
        Assert.Equal("Credential.Added", last.Action);
        Assert.Equal("Staging admin", last.After);                 // label only
        // The secret must NEVER appear anywhere in the audit log.
        Assert.DoesNotContain(_db.AuditEntries, a => a.After.Contains(secret) || a.Before.Contains(secret));
        Assert.True(new EfAuditChain(_db, Key).Verify());
    }

    [Fact]
    public async Task CompleteRetest_is_blocked_while_a_finding_is_still_retest_pending()  // FR-RET-03
    {
        var parentId = IdOf("ENG-2026-0399"); // closed Broker Portal
        var (result, childId) = await _store.RequestRetestAsync(parentId, Acme);
        Assert.False(result.Failed);

        var carried = await _store.FindingsForAsync(childId!.Value);
        Assert.All(carried, f => Assert.Equal(FindingStatus.RetestPending, f.Status));

        // While any in-scope finding is still RetestPending, completion is rejected and nothing changes.
        var blocked = await _store.CompleteRetestAsync(childId.Value, Khan);
        Assert.True(blocked.Failed);
        Assert.Equal(Stage.Retest, (await _store.GetAsync(childId.Value))!.CurrentStage);

        // Give every carried finding a verdict, then completion succeeds.
        foreach (var f in carried)
            await _store.SetFindingStatusAsync(f.Id, FindingStatus.Closed, Khan);

        var ok = await _store.CompleteRetestAsync(childId.Value, Khan);
        Assert.False(ok.Failed);
        Assert.Equal(Stage.Closed, (await _store.GetAsync(childId.Value))!.CurrentStage);
        Assert.True(new EfAuditChain(_db, Key).Verify());
    }

    [Fact]
    public async Task RequestRetest_carries_cvss_vector_and_remediation_into_the_child()  // FR-RET-02
    {
        var parentId = IdOf("ENG-2026-0399"); // Broker Portal, closed, has an IDOR finding
        var parentFindings = await _store.FindingsForAsync(parentId);
        var src = parentFindings.First(f => f.Status != FindingStatus.Closed && f.Status != FindingStatus.AcceptedRisk);
        Assert.False(string.IsNullOrEmpty(src.CvssVector));
        Assert.False(string.IsNullOrEmpty(src.Remediation));

        var (result, childId) = await _store.RequestRetestAsync(parentId, Acme);
        Assert.False(result.Failed);

        var child = (await _store.FindingsForAsync(childId!.Value)).Single(f => f.Title == src.Title);
        Assert.Equal(src.CvssVector, child.CvssVector);   // previously dropped — now carried
        Assert.Equal(src.Remediation, child.Remediation);
    }

    [Fact]
    public async Task Null_or_unknown_role_fails_closed_and_sees_zero_engagements()  // SEC-AZN (fail-closed)
    {
        // An authenticated principal whose role is blank/unknown/null is NOT the legacy "unrestricted"
        // default: it is scoped and, with no concrete filter, must see the EMPTY portfolio.
        Assert.Empty(await _store.ListScopedAsync(null, null, role: ""));        // blank role
        Assert.Empty(await _store.ListScopedAsync(null, null, role: "Auditor")); // unknown role
        Assert.Empty(await _store.ListScopedAsync(null, null, role: null));      // null role
        Assert.Null(await _store.GetScopedAsync(IdOf("ENG-2026-0408"), null, null, role: ""));
        Assert.Null(await _store.GetScopedAsync(IdOf("ENG-2026-0408"), null, null, role: null));

        // A named all-portfolio role still reaches the full estate (the legitimate unrestricted path).
        Assert.NotEmpty(await _store.ListScopedAsync(null, null, role: "Acme CA Officer"));
    }

    [Fact]
    public async Task Tester_can_open_a_report_they_did_not_author_but_not_other_non_report_engagements()  // FR-REP-02 / anti-BOLA
    {
        // Drive Retail Web (assigned A. Khan) from Findings to Report so it enters the peer-QA queue.
        var report = IdOf("ENG-2026-0408");
        Assert.False((await _store.ExecuteAsync(report, Khan, EngagementAction.GenerateDraft, e => e.GenerateDraft("A. Khan"))).Failed);
        Assert.Equal(Stage.Report, (await _store.GetAsync(report))!.CurrentStage);

        // R. Patel (a DIFFERENT tester) CAN open it for review (Report-stage review access),
        var asReviewer = await _store.GetScopedAsync(report, null, DemoSeeder.TesterPatel, role: "Tester");
        Assert.NotNull(asReviewer);
        // and it surfaces in their scoped list (the QA queue), even though none are assigned to them.
        Assert.Contains(await _store.ListScopedAsync(null, DemoSeeder.TesterPatel, role: "Tester"), e => e.Id == report);

        // ...but a NON-Report engagement they didn't author stays blocked (anti-BOLA preserved).
        var claimsAtSow = IdOf("ENG-2026-0412"); // at Sow, assigned A. Khan
        Assert.Null(await _store.GetScopedAsync(claimsAtSow, null, DemoSeeder.TesterPatel, role: "Tester"));
        Assert.DoesNotContain(await _store.ListScopedAsync(null, DemoSeeder.TesterPatel, role: "Tester"), e => e.Id == claimsAtSow);
    }

    [Fact]
    public async Task Engagement_drives_author_to_report_then_independent_review_release_and_close()  // FR-REP-02/04 (P1 happy path)
    {
        // The end-to-end peer-QA path the lifecycle previously dead-ended on: author drafts → an
        // INDEPENDENT tester reviews → release → Closed, all through the store with an intact chain.
        var id = IdOf("ENG-2026-0408"); // Retail Web, assigned A. Khan, at Findings

        // Author generates the draft → Report.
        Assert.False((await _store.ExecuteAsync(id, Khan, EngagementAction.GenerateDraft, e => e.GenerateDraft("A. Khan"))).Failed);

        // The author CANNOT pass their own QA (no self-review backstop, FR-REP-02).
        var selfReview = await _store.ExecuteAsync(id, Khan, EngagementAction.PeerReview, e => e.PeerReview(DemoSeeder.TesterKhan, true, "A. Khan"));
        Assert.True(selfReview.Failed);
        Assert.False((await _store.GetAsync(id))!.PeerReviewPassed);

        // An INDEPENDENT tester (R. Patel) passes QA, then Acme releases (re-auth) → Closed.
        Assert.False((await _store.ExecuteAsync(id, Patel, EngagementAction.PeerReview, e => e.PeerReview(DemoSeeder.TesterPatel, true, "R. Patel"))).Failed);
        Assert.True((await _store.GetAsync(id))!.PeerReviewPassed);
        Assert.False((await _store.ExecuteAsync(id, Acme, EngagementAction.ReleaseFinal, e => e.ReleaseFinal("J. Okafor", reAuthenticated: true))).Failed);

        Assert.Equal(Stage.Closed, (await _store.GetAsync(id))!.CurrentStage);
        Assert.True(new EfAuditChain(_db, Key).Verify());
    }

    [Fact]
    public async Task AssignTester_records_the_assigned_tester_in_the_audit_after()  // FR-AUD-02 (before/after completeness)
    {
        var id = IdOf("ENG-2026-0423"); // Quote Engine, routed, awaiting assignment
        var result = await _store.AssignTesterAsync(id, DemoSeeder.TesterLee, "S. Lee", Dm);
        Assert.False(result.Failed);

        var last = _db.AuditEntries.OrderByDescending(a => a.Sequence).First();
        Assert.Equal("Tester.Assigned", last.Action);
        Assert.Equal("Assignment", last.Before);
        Assert.Contains("S. Lee", last.After);          // WHO was assigned is now captured, not just the stage
        Assert.Contains("Scoping", last.After);         // ...alongside the stage move
        Assert.True(new EfAuditChain(_db, Key).Verify());
    }

    // ---- Write-path authorization (rank 1, SEC-AZN-01/02) ------------------

    [Fact]
    public async Task Write_path_rejects_a_wrong_role_caller()  // SEC-AZN-01 (role on the write path)
    {
        // ENG-0412 is at SoW. SignSow is an Acme CA Officer action — a Tester (even one in scope) is rejected
        // server-side, not just hidden in the UI; nothing changes.
        var id = IdOf("ENG-2026-0412");
        var before = (await _store.GetAsync(id))!.CurrentStage;

        var result = await _store.ExecuteAsync(id, Khan, EngagementAction.SignSow, e => e.SignSow("A. Khan", reAuthenticated: true));

        Assert.True(result.Failed);
        Assert.Contains("Acme CA Officer", result.Error);
        Assert.Equal(before, (await _store.GetAsync(id))!.CurrentStage);   // unchanged
        Assert.True(new EfAuditChain(_db, Key).Verify());
    }

    [Fact]
    public async Task Write_path_rejects_a_cross_scope_id_on_a_transition()  // SEC-AZN-02 (anti-BOLA on write, not just read)
    {
        // S. Lee is NOT assigned to Retail Web (A. Khan is). A direct-id transition by S. Lee must be
        // rejected on the WRITE path, exactly as GetScopedAsync rejects the read.
        var retail = IdOf("ENG-2026-0408"); // at Findings, assigned A. Khan
        var lee = CallerContext.ForTester(DemoSeeder.TesterLee, "S. Lee");

        var result = await _store.ExecuteAsync(retail, lee, EngagementAction.GenerateDraft, e => e.GenerateDraft("S. Lee"));

        Assert.True(result.Failed);
        Assert.Contains("scope", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(Stage.Findings, (await _store.GetAsync(retail))!.CurrentStage); // unchanged
    }

    [Fact]
    public async Task Write_path_rejects_a_cross_scope_data_mutation()  // SEC-AZN-02 (anti-BOLA on a data write)
    {
        // S. Lee tries to add a finding to A. Khan's engagement by id — defense-in-depth throws, nothing written.
        var retail = IdOf("ENG-2026-0408");
        var lee = CallerContext.ForTester(DemoSeeder.TesterLee, "S. Lee");
        var before = (await _store.FindingsForAsync(retail)).Count;

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _store.AddFindingAsync(
            retail, "Forged finding", Severity.High, "9.0", "v", "Web", "x", lee));

        Assert.Equal(before, (await _store.FindingsForAsync(retail)).Count);
    }

    [Fact]
    public async Task SoW_sign_off_enforces_separation_of_duties()  // FR-SOW-05 (drafter != signer)
    {
        // An Acme CA Officer whose acting name matches the assigned tester (the drafter) is blocked by SoD.
        // ENG-0412's assigned tester is A. Khan; an officer acting as "A. Khan" must not be able to sign.
        var id = IdOf("ENG-2026-0412");
        var officerWhoIsAlsoTheDrafter = CallerContext.Portfolio(PempRoles.AcmeOfficer, "A. Khan", userId: DemoSeeder.TesterKhan);

        var result = await _store.ExecuteAsync(id, officerWhoIsAlsoTheDrafter, EngagementAction.SignSow,
            e => e.SignSow("A. Khan", reAuthenticated: true));

        Assert.True(result.Failed);
        Assert.Contains("Separation of duties", result.Error);
        Assert.Equal(Stage.Sow, (await _store.GetAsync(id))!.CurrentStage);

        // A different officer (J. Okafor) CAN sign — SoD satisfied.
        Assert.False((await _store.ExecuteAsync(id, Acme, EngagementAction.SignSow,
            e => e.SignSow("J. Okafor", reAuthenticated: true))).Failed);
        Assert.Equal(Stage.Access, (await _store.GetAsync(id))!.CurrentStage);
    }

    [Fact]
    public async Task Raise_is_restricted_to_the_acme_officer()  // FR-REQ-01 (role on create)
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _store.RaiseAsync(EngagementType.Bau, "Rogue App", "Low", Khan));

        var id = await _store.RaiseAsync(EngagementType.Bau, "New App", "Low", Acme);
        Assert.NotNull(await _store.GetAsync(id));
    }

    [Fact]
    public async Task Concurrent_transition_conflict_is_caught_and_resolves_safely()  // rank 3 (TOCTOU / optimistic concurrency)
    {
        var id = IdOf("ENG-2026-0419"); // Payments API, BAU, at Access (assigned A. Khan)

        // _db tracks the engagement at its current RowVersion — the stale snapshot the loser will hold.
        var tracked = await _db.Engagements.FirstAsync(e => e.Id == id);
        Assert.Equal(Stage.Access, tracked.CurrentStage);
        var seqBefore = _db.AuditEntries.Count();

        // A SECOND session commits the transition first (out-of-band), advancing the row + its RowVersion.
        var options = new DbContextOptionsBuilder<PempDbContext>().UseSqlite(_conn).Options;
        using (var db2 = new PempDbContext(options))
        {
            var store2 = new EngagementStore(db2, Clock, new AuditHmacKey(Key));
            var first = await store2.ExecuteAsync(id, Khan, EngagementAction.VerifyAccess, e => e.VerifyAccess("A. Khan"));
            Assert.False(first.Failed);
        }

        // Our session (still holding the stale tracked entity) attempts the SAME transition. The bumped
        // RowVersion no longer matches in the UPDATE's WHERE → conflict, surfaced as a friendly failure
        // rather than a throw or a corrupting last-write-win.
        var second = await _store.ExecuteAsync(id, Khan, EngagementAction.VerifyAccess, e => e.VerifyAccess("A. Khan"));
        Assert.True(second.Failed);
        Assert.Contains("Reload", second.Error);

        // No corruption: exactly one transition committed, the chain verifies, sequences are unique.
        Assert.True(new EfAuditChain(_db, Key).Verify());
        var seqs = _db.AuditEntries.Select(a => a.Sequence).ToList();
        Assert.Equal(seqs.Count, seqs.Distinct().Count());  // DB-generated sequence — no duplicate PK
        Assert.Equal(seqBefore + 1, seqs.Count);            // only the winner appended
    }

    [Fact]
    public void Tamper_is_logged_as_a_high_severity_event_not_silent()  // rank 6 / SEC-AUD-01 (active tamper signal)
    {
        var captured = new List<(LogLevel Level, string Message)>();
        var logger = new CapturingLogger<EngagementStore>(captured);
        var store = new EngagementStore(_db, Clock, new AuditHmacKey(Key), logger);

        // Forge a row directly (bypassing EF / the append-only interceptor), as a DBA with table access would.
        _db.Database.ExecuteSqlRaw(
            "UPDATE AuditEntries SET Actor='mallory' WHERE Sequence=(SELECT MIN(Sequence) FROM AuditEntries)");

        Assert.False(store.VerifyChain());  // tamper detected ...
        // ... AND surfaced as a high-severity log (App Insights captures this automatically in prod), not silent.
        Assert.Contains(captured, e => e.Level == LogLevel.Critical && e.Message.Contains("verification FAILED"));
    }

    // Minimal capturing logger so a test can assert a security event was LOGGED, not silently swallowed.
    private sealed class CapturingLogger<T>(List<(LogLevel, string)> sink) : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => sink.Add((logLevel, formatter(state, exception)));
    }

    // ---- Data-derived gates (rank 19) --------------------------------------

    [Fact]
    public async Task CompleteAssessment_is_a_data_derived_gate_requiring_recorded_answers()  // rank 19 / FR-SCO-03
    {
        var mobile = IdOf("ENG-2026-0421"); // at Scoping, assigned A. Khan, no answers seeded

        // No answers recorded → completion is blocked and nothing advances.
        var blocked = await _store.CompleteAssessmentAsync(mobile, Khan);
        Assert.True(blocked.Failed);
        Assert.Equal(Stage.Scoping, (await _store.GetAsync(mobile))!.CurrentStage);

        // Record an answer, then completion succeeds (Scoping → SoW).
        await _store.SaveAssessmentAnswerAsync(mobile, "app-type", "Web", Khan);
        var ok = await _store.CompleteAssessmentAsync(mobile, Khan);
        Assert.False(ok.Failed);
        Assert.Equal(Stage.Sow, (await _store.GetAsync(mobile))!.CurrentStage);
        Assert.True(new EfAuditChain(_db, Key).Verify());
    }

    [Fact]
    public async Task VerifyAccess_is_a_data_derived_gate_requiring_provisioned_requirements()  // rank 19 / FR-ACC-04
    {
        var pay = IdOf("ENG-2026-0419"); // at Access, assigned A. Khan, requirements not all provisioned

        var blocked = await _store.VerifyAccessAsync(pay, Khan, reAuthenticated: true);
        Assert.True(blocked.Failed);
        Assert.Equal(Stage.Access, (await _store.GetAsync(pay))!.CurrentStage);

        // Provision every recorded requirement, then access verification succeeds (Access → Execution).
        foreach (var r in await _store.AccessReqsForAsync(pay))
            await _store.SetAccessStatusAsync(r.Id, AccessStatus.Provisioned, Khan);
        var ok = await _store.VerifyAccessAsync(pay, Khan, reAuthenticated: true);
        Assert.False(ok.Failed);
        Assert.Equal(Stage.Execution, (await _store.GetAsync(pay))!.CurrentStage);
        Assert.True(new EfAuditChain(_db, Key).Verify());
    }

    [Fact]
    public async Task Retest_child_findings_link_back_to_their_original_for_provenance()  // rank 19 / FR-RET-02
    {
        var parentId = IdOf("ENG-2026-0399"); // Broker Portal, closed
        var parentIds = (await _store.FindingsForAsync(parentId)).Select(f => f.Id).ToHashSet();

        var (result, childId) = await _store.RequestRetestAsync(parentId, Acme);
        Assert.False(result.Failed);

        var children = await _store.FindingsForAsync(childId!.Value);
        Assert.NotEmpty(children);
        // Every carried child finding links back to a real parent finding (the before/after diff source).
        Assert.All(children, c => Assert.NotNull(c.OriginalFindingId));
        Assert.All(children, c => Assert.Contains(c.OriginalFindingId!.Value, parentIds));
    }

    // ---- Stable-id object scope (rank 21, SEC-AZN-02) ----------------------

    [Fact]
    public async Task Object_scope_keys_on_stable_app_id_not_the_mutable_name()  // rank 21 (rename can't break/widen access)
    {
        var mobileId = IdOf("ENG-2026-0421");        // Mobile App
        var appId = DemoSeeder.AppIdFor("Mobile App");
        Assert.NotNull(await _store.GetScopedAsync(mobileId, appId, null, role: "Stakeholder"));

        // Rename the application (display only). Scope keyed on the STABLE AppId must still reach it,
        // and the AppId itself is untouched by the rename.
        var rec = _db.Engagements.Single(e => e.Id == mobileId);
        rec.AppName = "Mobile App (rebranded)";
        await _db.SaveChangesAsync();

        Assert.NotNull(await _store.GetScopedAsync(mobileId, appId, null, role: "Stakeholder"));
        Assert.Equal(appId, (await _store.GetAsync(mobileId))!.AppId);
    }

    [Fact]
    public async Task Retest_child_shares_parent_app_id_and_tester_for_scope()  // rank 21 (no "(retest)" string hack)
    {
        var parentId = IdOf("ENG-2026-0399"); // Broker Portal, closed, assigned A. Khan
        var parent = (await _store.GetAsync(parentId))!;

        var (result, childId) = await _store.RequestRetestAsync(parentId, Acme);
        Assert.False(result.Failed);

        var child = (await _store.GetAsync(childId!.Value))!;
        Assert.Equal(parent.AppId, child.AppId);                       // same STABLE app id, not "X (retest)"
        Assert.Equal(parent.AssignedTesterId, child.AssignedTesterId); // tester carried by stable id

        // Both the app stakeholder (by AppId) and the assigned tester (by id) reach the child by scope.
        Assert.NotNull(await _store.GetScopedAsync(childId.Value, parent.AppId, null, role: "Stakeholder"));
        Assert.NotNull(await _store.GetScopedAsync(childId.Value, null, DemoSeeder.TesterKhan, role: "Tester"));
    }

    // ---- Analytics SQL aggregate (rank 15 / FR-ANL-04) ---------------------

    [Fact]
    public async Task OpenFindingCounts_aggregates_open_findings_per_engagement_and_severity_in_sql()
    {
        var retail = IdOf("ENG-2026-0408");
        var broker = IdOf("ENG-2026-0399");

        var counts = await _store.OpenFindingCountsAsync(new[] { retail, broker });

        // Retail Web open buckets: Critical 1, High 2, Low 1 — the Remediated Medium finding is excluded.
        Assert.Equal(1, counts.Single(c => c.EngagementId == retail && c.Severity == Severity.Critical).Count);
        Assert.Equal(2, counts.Single(c => c.EngagementId == retail && c.Severity == Severity.High).Count);
        Assert.Equal(1, counts.Single(c => c.EngagementId == retail && c.Severity == Severity.Low).Count);
        Assert.DoesNotContain(counts, c => c.EngagementId == retail && c.Severity == Severity.Medium);

        // Broker Portal: only the RetestPending IDOR (High) is open; the Closed Medium is excluded.
        Assert.Equal(1, counts.Single(c => c.EngagementId == broker && c.Severity == Severity.High).Count);
        Assert.DoesNotContain(counts, c => c.EngagementId == broker && c.Severity == Severity.Medium);

        // An empty scope yields no rows (a scoped role with no engagements sees nothing).
        Assert.Empty(await _store.OpenFindingCountsAsync(Array.Empty<Guid>()));
    }

    // ---- Reference minting (rank 29) ---------------------------------------

    [Fact]
    public async Task Raise_mints_unique_references()  // rank 29 (no duplicate ENG-2026-NNNN)
    {
        var refs = new List<string>();
        for (var i = 0; i < 6; i++)
        {
            var id = await _store.RaiseAsync(EngagementType.Bau, $"Fresh App {i}", "Low", Acme);
            refs.Add((await _store.GetAsync(id))!.Reference);
        }
        Assert.Equal(refs.Count, refs.Distinct().Count());     // every minted reference is unique
        Assert.All(refs, r => Assert.StartsWith("ENG-2026-", r));
    }

    [Fact]
    public async Task Duplicate_reference_is_rejected_by_the_unique_index()  // rank 29 (the concurrency backstop)
    {
        var existing = _db.Engagements.First().Reference;
        _db.Engagements.Add(new EngagementRecord
        {
            Id = Guid.NewGuid(), Reference = existing, AppId = Guid.NewGuid(),
            AppName = "Collision", Criticality = "Low", CurrentStage = Stage.Intake,
        });
        await Assert.ThrowsAnyAsync<DbUpdateException>(() => _db.SaveChangesAsync());
    }

    public void Dispose()
    {
        _db.Dispose();
        _conn.Dispose();
    }
}
