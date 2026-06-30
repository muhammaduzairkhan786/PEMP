using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Pemp.Domain;
using Pemp.Domain.Audit;

namespace Pemp.Infrastructure.Persistence;

/// <summary>
/// Per-engagement communications (FR-NOT-01/03): the home for the cross-role journeys — the same-day
/// findings summary, post-test notes, status updates. Kept separate from <see cref="EngagementStore"/>
/// so that store doesn't grow further, but it reuses the SAME object-level scope decision (delegating to
/// <see cref="EngagementStore.GetScopedAsync"/> / <see cref="EngagementStore.ListScopedAsync"/>, which
/// fail closed) and the SAME hash-chained, atomic audit pattern. Posting is scoped + authorized + audited;
/// reads are scoped; a fail-closed scope means an out-of-scope caller can neither post nor read.
/// </summary>
public sealed class CommsStore(PempDbContext db, Func<DateTimeOffset> clock, AuditHmacKey auditKey,
    EngagementStore engagements, ILogger<CommsStore>? logger = null)
{
    private readonly ILogger _log = logger ?? NullLogger<CommsStore>.Instance;
    private static string? Corr => Activity.Current?.Id;

    /// <summary>
    /// Messages on one engagement's timeline (newest first), scoped: an out-of-scope caller (anti-BOLA)
    /// gets an empty list, never another engagement's comms. Scope is the canonical fail-closed predicate.
    /// </summary>
    public async Task<List<CommsMessageRecord>> MessagesForAsync(Guid engagementId, CallerContext caller)
    {
        var rec = await engagements.GetScopedAsync(engagementId, caller.AppScope, caller.TesterScope, caller.Role);
        if (rec is null) return new();
        // Order newest-first in memory: the SQLite provider can't translate a DateTimeOffset ORDER BY
        // (same reason RecentCountAsync compares the window client-side). The per-engagement set is small.
        var rows = await db.CommsMessages.AsNoTracking()
            .Where(m => m.EngagementId == engagementId)
            .ToListAsync();
        return rows.OrderByDescending(m => m.CreatedAt).ToList();
    }

    /// <summary>
    /// Post a message (FR-NOT-01): a scoped, authorized, AUDITED action. Object-level scope is enforced
    /// (an out-of-scope post is rejected and writes nothing). A hash-chained <c>Comms.Posted</c> entry is
    /// appended atomically with the message; the KIND is recorded, never the body (it may be sensitive).
    /// </summary>
    public async Task<Result> PostMessageAsync(Guid engagementId, CommsKind kind, string body, CallerContext caller)
    {
        var rec = await engagements.GetScopedAsync(engagementId, caller.AppScope, caller.TesterScope, caller.Role);
        if (rec is null)
        {
            _log.LogWarning(
                "FR-NOT: comms post REJECTED on engagement {EngagementId} for actor={Actor} role={Role}: outside scope. CorrelationId={CorrelationId}",
                engagementId, caller.Actor, caller.Role, Corr);
            return Result.Fail("Not authorized for this engagement — outside your scope (SEC-AZN-02).");
        }
        if (string.IsNullOrWhiteSpace(body)) return Result.Fail("Enter a message.");

        var now = clock();
        db.CommsMessages.Add(new CommsMessageRecord
        {
            Id = Guid.NewGuid(), EngagementId = engagementId,
            AuthorName = caller.Actor, AuthorRole = caller.Role ?? "",
            CreatedAt = now, Kind = kind, Body = body.Trim(),
        });
        // Audit the post (kind only — never the body) on the same unit of work as the message.
        new EfAuditChain(db, auditKey.Value, _log).Append(engagementId, caller.Actor, "Comms.Posted",
            "-", kind.ToString(), string.IsNullOrEmpty(caller.Role) ? "ui" : $"ui:{caller.Role}", now);
        await db.SaveChangesAsync();
        return Result.Success();
    }

    /// <summary>
    /// A scoped "recent comms" count for the notification badge (FR-NOT-03). HONEST about what it counts:
    /// messages created on or after <paramref name="since"/> across the caller's in-scope engagements —
    /// a simple recency count, not per-user read receipts. Fails closed (a scoped role with no scope = 0).
    /// </summary>
    public async Task<int> RecentCountAsync(CallerContext caller, DateTimeOffset since)
    {
        var engs = await engagements.ListScopedAsync(caller.AppScope, caller.TesterScope, caller.Role);
        if (engs.Count == 0) return 0;
        var ids = engs.Select(e => e.Id).ToList();
        // Filter to the scoped engagements in SQL; compare the recency window in memory (the SQLite
        // provider can't translate a DateTimeOffset comparison — same pattern as the credential expiry).
        var timestamps = await db.CommsMessages.AsNoTracking()
            .Where(m => ids.Contains(m.EngagementId))
            .Select(m => m.CreatedAt)
            .ToListAsync();
        return timestamps.Count(ts => ts >= since);
    }
}
