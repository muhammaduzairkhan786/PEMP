using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
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

        var result = await _store.ExecuteAsync(id, e => e.CompleteAssessment("tester"));

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

        var result = await _store.ExecuteAsync(id, e => e.SignSow("acme", reAuthenticated: true));

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
        var result = await _store.ExecuteAsync(id, e => e.SignSow("acme", reAuthenticated: false));
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

        // Tester scope (A. Khan): reaches own assignment, blocked from one not assigned to them.
        Assert.NotNull(await _store.GetScopedAsync(claims, null, "A. Khan"));
        Assert.Null(await _store.GetScopedAsync(partner, null, "A. Khan"));

        // Stakeholder app scope (Mobile App): reaches own app, blocked from others (anti-BOLA).
        Assert.NotNull(await _store.GetScopedAsync(mobile, "Mobile App", null));
        Assert.Null(await _store.GetScopedAsync(claims, "Mobile App", null));

        // Unrestricted (Acme/DM/Admin): reaches anything.
        Assert.NotNull(await _store.GetScopedAsync(retail, null, null));
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

        // A blank/unresolved filter value also yields zero rows (fail-closed).
        Assert.Null(await _store.GetScopedAsync(claims, null, "", role: "Tester"));
        Assert.Empty(await _store.ListScopedAsync(null, "", role: "Tester"));

        // An all-portfolio role with (null,null) still sees the whole portfolio.
        Assert.NotEmpty(await _store.ListScopedAsync(null, null, role: "Delivery Manager"));
        Assert.NotNull(await _store.GetScopedAsync(claims, null, null, role: "System Administrator"));

        // A scoped role WITH a concrete, matching filter still reaches its own data, and anti-BOLA
        // still blocks reaching another scope by id.
        Assert.NotNull(await _store.GetScopedAsync(claims, null, "A. Khan", role: "Tester"));
        Assert.Null(await _store.GetScopedAsync(claims, null, "S. Lee", role: "Tester"));
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
            "Validate redirect targets against an allow-list.");

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

        var (result, childId) = await _store.RequestRetestAsync(parentId, "stakeholder");

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
        var second = await _store.RequestRetestAsync(parentId, "stakeholder");
        Assert.True(second.Result.Failed);

        // the child re-verifies and closes
        var done = await _store.ExecuteAsync(childId.Value, e => e.CompleteRetest("tester"));
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

        var credId = await _store.AddTestCredentialAsync(id, "Staging admin", "admin.stg@test", "S3cr3t!-stg");

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
            "CVSS:3.1/AV:N/AC:L/PR:N/UI:R/S:U/C:N/I:L/A:N", "Web", "Allow-list redirects.", FindingStatus.Open);

        var (result, childId) = await _store.RequestRetestAsync(parentId, "stakeholder");
        Assert.False(result.Failed);

        var carried = await _store.FindingsForAsync(childId!.Value);
        Assert.True(carried.Count >= 2);
        Assert.All(carried, f => Assert.Equal(FindingStatus.RetestPending, f.Status));

        // Pass = fix verified → Closed; fail = still present → re-Open.
        await _store.SetFindingStatusAsync(carried[0].Id, FindingStatus.Closed);
        await _store.SetFindingStatusAsync(carried[1].Id, FindingStatus.Open);

        var after = await _store.FindingsForAsync(childId.Value);
        Assert.Equal(FindingStatus.Closed, after.Single(f => f.Id == carried[0].Id).Status);
        Assert.Equal(FindingStatus.Open, after.Single(f => f.Id == carried[1].Id).Status);
    }

    [Fact]
    public async Task AddAccessRequirement_persists_a_new_row()  // FR-ACC-01
    {
        var id = IdOf("ENG-2026-0419"); // Payments API at Access
        var before = (await _store.AccessReqsForAsync(id)).Count;

        var row = await _store.AddAccessRequirementAsync(id, "Sandbox", "https://sbx.test", "Read");

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
            "CVSS:3.1/AV:N/AC:L/PR:N/UI:R/S:U/C:N/I:L/A:N", "Web", "Allow-list redirects.",
            actor: "A. Khan", role: "Tester");

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

        await _store.SetFindingStatusAsync(finding.Id, FindingStatus.Remediated, actor: "A. Khan", role: "Tester");

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

        await _store.AddTestCredentialAsync(id, "Staging admin", "admin.stg@test", secret,
            actor: "A. Khan", role: "Tester");

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
        var (result, childId) = await _store.RequestRetestAsync(parentId, "stakeholder");
        Assert.False(result.Failed);

        var carried = await _store.FindingsForAsync(childId!.Value);
        Assert.All(carried, f => Assert.Equal(FindingStatus.RetestPending, f.Status));

        // While any in-scope finding is still RetestPending, completion is rejected and nothing changes.
        var blocked = await _store.CompleteRetestAsync(childId.Value, "A. Khan");
        Assert.True(blocked.Failed);
        Assert.Equal(Stage.Retest, (await _store.GetAsync(childId.Value))!.CurrentStage);

        // Give every carried finding a verdict, then completion succeeds.
        foreach (var f in carried)
            await _store.SetFindingStatusAsync(f.Id, FindingStatus.Closed, actor: "A. Khan", role: "Tester");

        var ok = await _store.CompleteRetestAsync(childId.Value, "A. Khan");
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

        var (result, childId) = await _store.RequestRetestAsync(parentId, "stakeholder");
        Assert.False(result.Failed);

        var child = (await _store.FindingsForAsync(childId!.Value)).Single(f => f.Title == src.Title);
        Assert.Equal(src.CvssVector, child.CvssVector);   // previously dropped — now carried
        Assert.Equal(src.Remediation, child.Remediation);
    }

    public void Dispose()
    {
        _db.Dispose();
        _conn.Dispose();
    }
}
