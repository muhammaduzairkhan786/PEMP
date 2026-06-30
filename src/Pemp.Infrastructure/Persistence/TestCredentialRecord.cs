namespace Pemp.Infrastructure.Persistence;

/// <summary>
/// A test-account credential attached to an engagement (SEC-CRD). In this demo the secret
/// is stored locally so the masked-reveal flow can be exercised; in PRODUCTION these are
/// held in Azure Key Vault or envelope-encrypted at rest (AES-256-GCM, KMS-wrapped keys),
/// masked in the UI, never logged, time-boxed and revoked on engagement close (SEC-CRD-01/02/03).
/// </summary>
public sealed class TestCredentialRecord
{
    public Guid Id { get; set; }
    public Guid EngagementId { get; set; }
    public string Label { get; set; } = "";       // e.g. "QA admin", "API service account"
    public string Username { get; set; } = "";
    public string Secret { get; set; } = "";       // demo only; prod = Key Vault / envelope-encrypted

    // ---- Credential lifecycle (SEC-CRD-01/02/03) ----
    public DateTimeOffset CreatedAt { get; set; }
    public string AddedBy { get; set; } = "";
    /// <summary>Time-box: after this instant the secret is never returned on read (auto-expiry).</summary>
    public DateTimeOffset? ExpiresAt { get; set; }
    /// <summary>Revoked credentials (and auto-revoked on engagement Close) never return their secret.</summary>
    public bool Revoked { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    /// <summary>Production reference to the Key Vault secret / envelope-wrapped key (placeholder for prod).</summary>
    public string? KeyVaultSecretUri { get; set; }
}
