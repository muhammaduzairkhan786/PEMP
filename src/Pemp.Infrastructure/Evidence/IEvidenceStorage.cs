using Pemp.Infrastructure.Persistence;

namespace Pemp.Infrastructure.Evidence;

/// <summary>
/// The descriptor a redeemed download token resolves to — enough to stream the artifact, never the
/// bytes themselves. Carries no secret; the engagement/evidence ids are opaque GUIDs.
/// </summary>
public sealed record EvidenceTicket(Guid EvidenceId, Guid EngagementId, string FileName, EvidenceKind Kind);

/// <summary>
/// Evidence artifact access (SEC-EVD-02/03). The caller MUST have already passed object-level scope
/// AND the re-auth step-up, and the download MUST have been audited, BEFORE a URL is minted — the
/// scope + audit live in <see cref="EngagementStore.RecordEvidenceDownloadAsync"/>; this seam only
/// produces the short-lived, single-use way to fetch the (already-authorized) bytes.
///
/// <para>LOCAL/dev: <see cref="LocalEvidenceStorage"/> mints an opaque HMAC-signed, time-boxed,
/// single-use token and the app streams a placeholder via the <c>/evidence/download/{token}</c>
/// endpoint — no raw file path is ever exposed.</para>
///
/// <para>PROD: <see cref="AzureBlobEvidenceStorage"/> (a documented seam, like the Key Vault audit
/// seam) returns a short-lived Azure Blob *user-delegation* SAS so the browser fetches the encrypted
/// artifact straight from private storage — the app never proxies the bytes.</para>
/// </summary>
public interface IEvidenceStorage
{
    /// <summary>
    /// Mint a short-lived download URL for one (already scope-checked, re-authed and audited) artifact.
    /// Local: an app-relative <c>/evidence/download/{opaque-token}</c>. Prod: a Blob SAS URL.
    /// </summary>
    string CreateDownloadUrl(EvidenceRecord evidence);

    /// <summary>
    /// Validate + consume a download token (single-use; rejected if expired, tampered, or already used).
    /// Returns the artifact descriptor, or null on any failure. Local-only — prod fetches straight from Blob.
    /// </summary>
    EvidenceTicket? Redeem(string token);

    /// <summary>
    /// Produce the bytes to stream for a redeemed ticket. Local: a clearly-marked placeholder (no real
    /// artifact bytes exist in the demo). Prod: not used (the SAS streams directly from Blob).
    /// </summary>
    (byte[] Bytes, string ContentType, string DownloadName) Materialize(EvidenceTicket ticket);
}
