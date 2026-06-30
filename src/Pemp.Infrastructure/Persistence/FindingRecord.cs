using Pemp.Domain;

namespace Pemp.Infrastructure.Persistence;

/// <summary>Fixed 5-step severity scale (FR-FND-01, CVSS bands).</summary>
public enum Severity { Critical, High, Medium, Low, Info }

/// <summary>
/// A SQL-side analytics aggregate (FR-ANL-04): the number of OPEN findings of one <see cref="Severity"/>
/// for one engagement. Produced by a GROUP BY so the dashboard never materializes raw findings.
/// </summary>
public sealed record OpenFindingCount(Guid EngagementId, Severity Severity, int Count);

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

    /// <summary>
    /// When a retest child copies a parent finding to re-verify it, this links back to the original
    /// (FR-RET-02): the finding is entered ONCE and the child only carries provenance, driving the
    /// retest report's before/after diff. Null on originally-entered findings.
    /// </summary>
    public Guid? OriginalFindingId { get; set; }
}
