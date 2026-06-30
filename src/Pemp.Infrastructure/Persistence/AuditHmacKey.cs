using Pemp.Domain.Audit;
using System.Text;

namespace Pemp.Infrastructure.Persistence;

/// <summary>
/// DI-injected holder for the audit hash-chain HMAC key (SEC-AUD-01). The key seals every
/// chain link so a row edited directly in the database cannot be made to verify without it.
/// In PRODUCTION the value MUST come from Azure Key Vault (managed identity); the
/// <c>Audit:HmacKey</c> config setting is for local dev only. Falls back to
/// <see cref="HashChain.DefaultKey"/> when nothing is configured (dev convenience).
/// </summary>
public sealed class AuditHmacKey
{
    public byte[] Value { get; }

    public AuditHmacKey(byte[] value) => Value = value;

    /// <summary>Build from a config string; blank → dev fallback key.</summary>
    public static AuditHmacKey FromConfig(string? configured) =>
        new(string.IsNullOrWhiteSpace(configured)
            ? HashChain.DefaultKey
            : Encoding.UTF8.GetBytes(configured));
}
