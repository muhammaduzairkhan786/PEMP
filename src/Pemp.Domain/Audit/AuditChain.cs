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
    public string Canonical() =>
        $"{Sequence}|{EngagementId:N}|{Actor}|{Action}|{Before}|{After}|{Timestamp.UtcDateTime:O}|{Source}|{PrevHash}";
}

/// <summary>
/// Shared hash-chain primitives so any <see cref="IAuditChain"/> implementation
/// (in-memory or DB-backed) chains identically (SEC-AUD-01). The chain link is an
/// HMAC-SHA256 keyed by a secret (<c>Audit:HmacKey</c>, in PROD from Azure Key Vault via
/// managed identity) — so anyone who edits a persisted row cannot recompute a valid tail
/// without the key, even with full database access. Genesis/sequence semantics are unchanged.
/// </summary>
public static class HashChain
{
    public const string GenesisHash = "0000000000000000000000000000000000000000000000000000000000000000";

    /// <summary>
    /// Dev/test fallback HMAC key used only when no <c>Audit:HmacKey</c> is configured.
    /// PRODUCTION MUST supply a real secret from Azure Key Vault — never rely on this default.
    /// </summary>
    public static readonly byte[] DefaultKey =
        Encoding.UTF8.GetBytes("PEMP-DEV-AUDIT-HMAC-KEY-not-for-production");

    public static string ComputeHash(string input, byte[] key) =>
        Convert.ToHexString(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(input))).ToLowerInvariant();

    /// <summary>Build the next chained entry from the previous hash + the new fields, keyed-HMAC sealed.</summary>
    public static AuditEntry Next(long sequence, string prevHash, Guid engagementId, string actor,
        string action, string before, string after, string source, DateTimeOffset at, byte[] key)
    {
        var draft = new AuditEntry(sequence, engagementId, actor, action, before, after, at, source, prevHash, string.Empty);
        return draft with { Hash = ComputeHash(draft.Canonical(), key) };
    }
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
    public const string GenesisHash = HashChain.GenesisHash;

    private readonly List<AuditEntry> _entries = new();
    private readonly byte[] _key;

    /// <param name="key">HMAC key; defaults to <see cref="HashChain.DefaultKey"/> for the in-memory model/tests.</param>
    public InMemoryHashChain(byte[]? key = null) => _key = key ?? HashChain.DefaultKey;

    public IReadOnlyList<AuditEntry> Entries => _entries;

    public AuditEntry Append(Guid engagementId, string actor, string action, string before, string after, string source, DateTimeOffset at)
    {
        var prevHash = _entries.Count == 0 ? HashChain.GenesisHash : _entries[^1].Hash;
        var entry = HashChain.Next(_entries.Count + 1, prevHash, engagementId, actor, action, before, after, source, at, _key);
        _entries.Add(entry);
        return entry;
    }

    public bool Verify()
    {
        var prev = HashChain.GenesisHash;
        foreach (var e in _entries)
        {
            if (e.PrevHash != prev) return false;
            if (e.Hash != HashChain.ComputeHash(e.Canonical(), _key)) return false;
            prev = e.Hash;
        }
        return true;
    }
}
