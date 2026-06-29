namespace Pemp.Infrastructure.Persistence;

/// <summary>Evidence artifact type (FR-FND-02).</summary>
public enum EvidenceKind { Screenshot, RequestResponse, Log, File }

/// <summary>
/// An evidence artifact attached to a finding (FR-FND-02, SEC-EVD-01/02). In the demo
/// this stores metadata + an "encrypted-at-rest" marker; production puts the bytes in
/// private, server-side-encrypted Blob storage and serves them via signed short-lived URLs.
/// </summary>
public sealed class EvidenceRecord
{
    public Guid Id { get; set; }
    public Guid EngagementId { get; set; }
    public Guid FindingId { get; set; }
    public string FileName { get; set; } = "";
    public EvidenceKind Kind { get; set; }
    public string Note { get; set; } = "";
    public bool EncryptedAtRest { get; set; } = true; // SEC-EVD-01 (server-side encryption)
}
