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
        var claims = IdOf("ENG-2026-0412"); // assigned A. Khan
        var retail = IdOf("ENG-2026-0408"); // assigned R. Patel
        var mobile = IdOf("ENG-2026-0421"); // Mobile App, at Scoping

        // Tester scope (A. Khan): reaches own assignment, blocked from another tester's.
        Assert.NotNull(await _store.GetScopedAsync(claims, null, "A. Khan"));
        Assert.Null(await _store.GetScopedAsync(retail, null, "A. Khan"));

        // Stakeholder app scope (Mobile App): reaches own app, blocked from others (anti-BOLA).
        Assert.NotNull(await _store.GetScopedAsync(mobile, "Mobile App", null));
        Assert.Null(await _store.GetScopedAsync(claims, "Mobile App", null));

        // Unrestricted (Acme/DM/Admin): reaches anything.
        Assert.NotNull(await _store.GetScopedAsync(retail, null, null));
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

    public void Dispose()
    {
        _db.Dispose();
        _conn.Dispose();
    }
}
