using Microsoft.EntityFrameworkCore;
using Pemp.Domain.Audit;

namespace Pemp.Infrastructure.Persistence;

/// <summary>
/// DB-backed append-only hash chain (SEC-AUD-01). Bound to one <see cref="PempDbContext"/>
/// unit of work: it seeds its position from the last persisted entry, then chains new
/// appends in order, adding rows to the context. They persist when the caller saves —
/// keeping the append atomic with the engagement state change it records.
/// </summary>
public sealed class EfAuditChain(PempDbContext db, byte[] key) : IAuditChain
{
    private bool _init;
    private long _seq;
    private string _prev = HashChain.GenesisHash;

    private void EnsureInit()
    {
        if (_init) return;
        var last = db.AuditEntries.OrderByDescending(e => e.Sequence).FirstOrDefault();
        if (last is not null) { _seq = last.Sequence; _prev = last.Hash; }
        _init = true;
    }

    public AuditEntry Append(Guid engagementId, string actor, string action, string before, string after, string source, DateTimeOffset at)
    {
        EnsureInit();
        var entry = HashChain.Next(_seq + 1, _prev, engagementId, actor, action, before, after, source, at, key);
        _seq = entry.Sequence;
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
            if (e.PrevHash != prev) return false;
            if (e.Hash != HashChain.ComputeHash(e.Canonical(), key)) return false;
            prev = e.Hash;
        }
        return true;
    }

    public IReadOnlyList<AuditEntry> Entries =>
        db.AuditEntries.AsNoTracking().OrderBy(e => e.Sequence).Select(r => r.ToEntry()).ToList();
}
