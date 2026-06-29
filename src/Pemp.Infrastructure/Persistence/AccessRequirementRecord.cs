namespace Pemp.Infrastructure.Persistence;

/// <summary>Provisioning status of an access requirement (workbook Tab 3).</summary>
public enum AccessStatus { AppTeamToProvision, InProgress, Provisioned, NotRequired }

/// <summary>
/// A tester-defined access requirement per engagement (FR-ACC-01): the environment /
/// asset the tester needs, its URL, the access type, and provisioning status. Mirrors
/// the workbook's "Pentest – Access Requirements" matrix rows (Tab 3).
/// </summary>
public sealed class AccessRequirementRecord
{
    public Guid Id { get; set; }
    public Guid EngagementId { get; set; }
    public string Environment { get; set; } = "";   // e.g. "Production URLs", "Pre-production", "QA", "APIs"
    public string Url { get; set; } = "";
    public string AccessType { get; set; } = "";     // Admin / Read-Write / Read / Other
    public AccessStatus Status { get; set; } = AccessStatus.AppTeamToProvision;
}
