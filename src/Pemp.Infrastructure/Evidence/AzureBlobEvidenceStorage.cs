using Pemp.Infrastructure.Persistence;

namespace Pemp.Infrastructure.Evidence;

/// <summary>
/// PRODUCTION seam for evidence access (SEC-EVD-02) — the Blob counterpart of the Key Vault audit seam.
/// In production the browser must fetch the encrypted artifact DIRECTLY from private Blob storage via a
/// short-lived, read-only, user-delegation SAS, so the app never proxies the bytes. The download is
/// still object-level-scoped, re-auth-gated and audited by the app at mint time
/// (<see cref="EngagementStore.RecordEvidenceDownloadAsync"/>) before this URL is handed out.
///
/// Not wired into DI yet (the local SQLite/dev demo uses <see cref="LocalEvidenceStorage"/>); this class
/// documents the contract and the exact production wiring TODO.
/// </summary>
public sealed class AzureBlobEvidenceStorage : IEvidenceStorage
{
    // TODO(prod, SEC-EVD-02): inject a BlobServiceClient created with the Web App's managed identity
    // (DefaultAzureCredential). To mint the URL:
    //   1. var udk = await blobService.GetUserDelegationKeyAsync(now, now.AddMinutes(5));
    //   2. build a BlobSasBuilder { BlobName = evidence.Id, Resource = "b", ExpiresOn = now.AddMinutes(5),
    //        ContentDisposition = $"attachment; filename=\"{evidence.FileName}\"" } with read-only perms;
    //   3. return blobClient.Uri + "?" + sas.ToSasQueryParameters(udk, accountName).
    // No app endpoint / token redemption is needed in prod (Redeem/Materialize are local-only).
    public string CreateDownloadUrl(EvidenceRecord evidence) =>
        throw new NotImplementedException(
            "SEC-EVD-02: wire an Azure Blob user-delegation SAS for production evidence download.");

    public EvidenceTicket? Redeem(string token) => null;            // prod fetches straight from Blob

    public (byte[] Bytes, string ContentType, string DownloadName) Materialize(EvidenceTicket ticket) =>
        throw new NotImplementedException("Prod streams the encrypted blob via SAS, not through the app.");
}
