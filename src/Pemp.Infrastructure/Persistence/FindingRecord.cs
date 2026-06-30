using Pemp.Domain;

namespace Pemp.Infrastructure.Persistence;

/// <summary>Fixed 5-step severity scale (FR-FND-01, CVSS bands).</summary>
public enum Severity { Critical, High, Medium, Low, Info }

/// <summary>
/// A vulnerability finding in the live register (FR-FND-01/03/04). Entered once,
/// flows into the consolidated register; status tracked through remediation/retest.
/// </summary>
public sealed class FindingRecord
{
    public Guid Id { get; set; }
    public Guid EngagementId { get; set; }
    public string Title { get; set; } = "";
    public Severity Severity { get; set; }
    public string Cvss { get; set; } = "";           // CVSS base score (e.g. "8.1")
    public string CvssVector { get; set; } = "";      // CVSS vector string (e.g. "CVSS:3.1/AV:N/...")
    public string Asset { get; set; } = "";
    public string Remediation { get; set; } = "";
    public FindingStatus Status { get; set; }
}
