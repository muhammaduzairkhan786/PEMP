using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pemp.Domain;
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

    public PersistenceTests()
    {
        _conn = new SqliteConnection("DataSource=:memory:");
        _conn.Open();
        var options = new DbContextOptionsBuilder<PempDbContext>().UseSqlite(_conn).Options;
        _db = new PempDbContext(options);
        _db.Database.EnsureCreated();
        DemoSeeder.Seed(_db, Clock);
        _store = new EngagementStore(_db, Clock);
    }

    private Guid IdOf(string reference) => _db.Engagements.Single(e => e.Reference == reference).Id;

    [Fact]
    public void Seed_builds_a_verifiable_hash_chain()
    {
        Assert.True(_db.AuditEntries.Any());
        Assert.True(new EfAuditChain(_db).Verify());
    }

    [Fact]
    public void Tampering_breaks_chain_verification()
    {
        var row = _db.AuditEntries.OrderBy(a => a.Sequence).First();
        row.Actor = "mallory"; // mutate a recorded entry
        _db.SaveChanges();
        Assert.False(new EfAuditChain(_db).Verify());
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
        Assert.True(new EfAuditChain(_db).Verify());
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
        Assert.True(new EfAuditChain(_db).Verify());
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
        Assert.True(new EfAuditChain(_db).Verify());

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
        Assert.True(new EfAuditChain(_db).Verify());
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

    public void Dispose()
    {
        _db.Dispose();
        _conn.Dispose();
    }
}
