using Pemp.Domain.Audit;
using System.Text;

namespace Pemp.Infrastructure.Persistence;

/// <summary>
/// DI-injected holder for the audit hash-chain HMAC key (SEC-AUD-01). The key seals every
/// chain link so a row edited directly in the database cannot be made to verify without it.
/// In PRODUCTION the value MUST come from Azure Key Vault (managed identity → the
/// <c>Audit--HmacKey</c> secret surfaced as <c>Audit:HmacKey</c>); the local
/// <c>Audit:HmacKey</c> config setting is for dev only.
/// </summary>
public sealed class AuditHmacKey
{
    public byte[] Value { get; }

    public AuditHmacKey(byte[] value) => Value = value;

    /// <summary>True when the resolved key is the public compiled fallback (never valid in prod).</summary>
    public bool IsDefaultKey => Value.AsSpan().SequenceEqual(HashChain.DefaultKey);

    /// <summary>
    /// Build from a config string. FAIL CLOSED on real deployments (SEC-AUD-01): when
    /// <paramref name="allowDefaultKey"/> is false, a missing key — or one that resolves to the
    /// public <see cref="HashChain.DefaultKey"/> — throws rather than silently sealing the audit
    /// chain with a key an attacker already knows. The caller sets <paramref name="allowDefaultKey"/>
    /// true only for Development or the local SQLite demo (which has no vault); a production
    /// Azure SQL deployment passes false and must supply the key from Key Vault.
    /// </summary>
    public static AuditHmacKey FromConfig(string? configured, bool allowDefaultKey)
    {
        if (string.IsNullOrWhiteSpace(configured))
        {
            if (!allowDefaultKey)
            {
                throw new InvalidOperationException(
                    "SEC-AUD-01: no audit HMAC key configured. A production deployment MUST source the " +
                    "key from Azure Key Vault (secret 'Audit--HmacKey' → config 'Audit:HmacKey'). " +
                    "Refusing to start with the public default key.");
            }
            return new(HashChain.DefaultKey);
        }

        var key = new AuditHmacKey(Encoding.UTF8.GetBytes(configured));
        if (!allowDefaultKey && key.IsDefaultKey)
        {
            throw new InvalidOperationException(
                "SEC-AUD-01: the configured audit HMAC key equals the public compiled default key. " +
                "Supply a real secret from Azure Key Vault before deploying to production.");
        }
        return key;
    }
}
