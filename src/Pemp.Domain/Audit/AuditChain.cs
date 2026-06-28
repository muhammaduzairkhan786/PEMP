using System.Security.Cryptography;
using System.Text;

namespace Pemp.Domain.Audit;

/// <summary>
/// One append-only audit record. Captures actor, action, before/after state,
/// timestamp and source (SEC-AUD-01, FR-AUD-02). <see cref="Hash"/> chains it to
/// the previous entry, making the log tamper-evident.
/// </summary>
public sealed record AuditEntry(
    long Sequence,
    Guid EngagementId,
    string Actor,
    string Action,
    string Before,
    string After,
    DateTimeOffset Timestamp,
    string Source,
    string PrevHash,
    string Hash)
{
    /// <summary>Canonical, order-stable serialization used as the hashing input.</summary>
    internal string Canonical() =>
        $"{Sequence}|{EngagementId:N}|{Actor}|{Action}|{Before}|{After}|{Timestamp.UtcDateTime:O}|{Source}|{PrevHash}";
}

/// <summary>
/// Append-only, hash-chained audit log. The append MUST be atomic with the state
/// transition it records (architecture.md §3.4) — the in-memory implementation here
/// models the invariant; the production adapter wraps both in one DB transaction.
/// </summary>
public interface IAuditChain
{
    AuditEntry Append(Guid engagementId, string actor, string action, string before, string after, string source, DateTimeOffset at);

    /// <summary>Re-walks the chain and returns true iff every link is intact (FR-AUD-03 verify).</summary>
    bool Verify();

    IReadOnlyList<AuditEntry> Entries { get; }
}

public sealed class InMemoryHashChain : IAuditChain
{
    public const string GenesisHash = "0000000000000000000000000000000000000000000000000000000000000000";

    private readonly List<AuditEntry> _entries = new();
    public IReadOnlyList<AuditEntry> Entries => _entries;

    public AuditEntry Append(Guid engagementId, string actor, string action, string before, string after, string source, DateTimeOffset at)
    {
        var prevHash = _entries.Count == 0 ? GenesisHash : _entries[^1].Hash;
        var seq = _entries.Count + 1;
        // Build the entry with an empty hash, compute over the canonical form, then finalize.
        var draft = new AuditEntry(seq, engagementId, actor, action, before, after, at, source, prevHash, string.Empty);
        var hash = ComputeHash(draft.Canonical());
        var entry = draft with { Hash = hash };
        _entries.Add(entry);
        return entry;
    }

    public bool Verify()
    {
        var prev = GenesisHash;
        foreach (var e in _entries)
        {
            if (e.PrevHash != prev) return false;
            var expected = ComputeHash(e.Canonical());
            if (e.Hash != expected) return false;
            prev = e.Hash;
        }
        return true;
    }

    private static string ComputeHash(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
