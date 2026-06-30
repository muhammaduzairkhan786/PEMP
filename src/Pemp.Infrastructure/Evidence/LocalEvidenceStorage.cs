using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Pemp.Infrastructure.Persistence;

namespace Pemp.Infrastructure.Evidence;

/// <summary>
/// Local/dev <see cref="IEvidenceStorage"/> (SEC-EVD-02/03). Mints opaque, HMAC-signed, time-boxed,
/// SINGLE-USE tokens and serves a placeholder artifact through the app's download endpoint — exercising
/// the signed-short-lived-URL mechanic without exposing any raw file path or sensitive data in the URL.
///
/// The signing key is per-process and random: tokens are short-lived and single-use, so they never need
/// to survive a restart. The pending-token map enforces single-use (removed on first redemption); the
/// HMAC + expiry make a token unforgeable and self-expiring even before the map entry would lapse.
/// Registered as a singleton so the map is shared across requests.
/// </summary>
public sealed class LocalEvidenceStorage(Func<DateTimeOffset> clock) : IEvidenceStorage
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(2);
    private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);
    private readonly ConcurrentDictionary<string, Pending> _pending = new(StringComparer.Ordinal);

    private sealed record Pending(EvidenceTicket Ticket, DateTimeOffset ExpiresAt);

    public string CreateDownloadUrl(EvidenceRecord evidence)
    {
        var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var expires = clock().Add(Ttl);
        _pending[nonce] = new Pending(
            new EvidenceTicket(evidence.Id, evidence.EngagementId, evidence.FileName, evidence.Kind), expires);
        return $"/evidence/download/{Sign(nonce, expires)}";
    }

    public EvidenceTicket? Redeem(string token)
    {
        if (!TryParse(token, out var nonce, out var expires)) return null; // malformed / bad signature
        if (!_pending.TryRemove(nonce, out var pending)) return null;       // single-use: gone after first redemption
        // Reject if either the signed expiry or the stored expiry has passed.
        return clock() <= expires && clock() <= pending.ExpiresAt ? pending.Ticket : null;
    }

    public (byte[] Bytes, string ContentType, string DownloadName) Materialize(EvidenceTicket ticket)
    {
        // LOCAL/dev placeholder — no real artifact bytes exist in the demo. PROD streams the real,
        // server-side-encrypted blob via its SAS instead of this stand-in.
        var text =
            "PEMP demo evidence — placeholder artifact\n" +
            $"File:      {ticket.FileName}\n" +
            $"Kind:      {ticket.Kind}\n" +
            $"Evidence:  {ticket.EvidenceId}\n" +
            "(In production this is the encrypted artifact streamed from private Blob storage.)\n";
        return (Encoding.UTF8.GetBytes(text), "text/plain; charset=utf-8", ticket.FileName + ".txt");
    }

    // token = "{nonce}.{unixExpiry}.{hmacHex}" — all URL-safe; carries no sensitive data, only an opaque
    // nonce + expiry. The signature binds them so a client can neither forge nor extend a token.
    private string Sign(string nonce, DateTimeOffset expires)
    {
        var unix = expires.ToUnixTimeSeconds();
        return $"{nonce}.{unix}.{Mac(nonce, unix)}";
    }

    private bool TryParse(string token, out string nonce, out DateTimeOffset expires)
    {
        nonce = ""; expires = default;
        if (string.IsNullOrEmpty(token)) return false;
        var parts = token.Split('.');
        if (parts.Length != 3) return false;
        if (!long.TryParse(parts[1], out var unix)) return false;
        var expected = Mac(parts[0], unix);
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(parts[2]))) return false;
        nonce = parts[0];
        expires = DateTimeOffset.FromUnixTimeSeconds(unix);
        return true;
    }

    private string Mac(string nonce, long unix)
    {
        using var h = new HMACSHA256(_key);
        return Convert.ToHexString(h.ComputeHash(Encoding.ASCII.GetBytes($"{nonce}.{unix}")));
    }
}
