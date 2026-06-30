using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Pemp.Domain.Audit;

namespace Pemp.Infrastructure.Persistence;

/// <summary>
/// DB-backed append-only hash chain (SEC-AUD-01). Bound to one <see cref="PempDbContext"/>
/// unit of work: it seeds its position from the last persisted entry, then chains new
/// appends in order, adding rows to the context. They persist when the caller saves —
/// keeping the append atomic with the engagement state change it records.
/// </summary>
public sealed class EfAuditChain(PempDbContext db, byte[] key, ILogger? logger = null) : IAuditChain
{
    private readonly ILogger _log = logger ?? NullLogger.Instance;
    private bool _init;
    private string _prev = HashChain.GenesisHash;

    private void EnsureInit()
    {
        if (_init) return;
        var last = db.AuditEntries.OrderByDescending(e => e.Sequence).FirstOrDefault();
        if (last is not null) { _prev = last.Hash; }
        _init = true;
    }

    public AuditEntry Append(Guid engagementId, string actor, string action, string before, string after, string source, DateTimeOffset at)
    {
        EnsureInit();
        // Sequence 0 → the DB assigns the position (IDENTITY); the chain link is the PrevHash, so a
        // concurrent append cannot collide on a client-assigned primary key (rank 3).
        var entry = HashChain.Next(0, _prev, engagementId, actor, action, before, after, source, at, key);
        _prev = entry.Hash;
        db.AuditEntries.Add(AuditEntryRow.From(entry));
        return entry;
    }

    public bool Verify()
    {
        var prev = HashChain.GenesisHash;
        foreach (var r in db.AuditEntries.AsNoTracking().OrderBy(e => e.Sequence))
        {
            var e = r.ToEntry();
            if (e.PrevHash != prev || e.Hash != HashChain.ComputeHash(e.Canonical(), key))
            {
                // ACTIVE tamper signal (SEC-AUD-01): a broken/forged chain is a high-severity security
                // event, never a silent false. When App Insights is configured, this ILogger record flows
                // to it automatically (no hard dependency). The audit ROW detail is not logged here (it may
                // reference secrets/labels) — only the position + correlation id.
                _log.LogCritical(
                    "SEC-AUD-01: audit chain verification FAILED at sequence {Sequence} — possible tampering. CorrelationId={CorrelationId}",
                    r.Sequence, Activity.Current?.Id);
                return false;
            }
            prev = e.Hash;
        }
        return true;
    }

    public IReadOnlyList<AuditEntry> Entries =>
        db.AuditEntries.AsNoTracking().OrderBy(e => e.Sequence).Select(r => r.ToEntry()).ToList();
}
