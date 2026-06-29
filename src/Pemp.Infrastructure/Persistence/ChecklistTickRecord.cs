namespace Pemp.Infrastructure.Persistence;

/// <summary>A ticked tester-checklist item per engagement (workbook Tab 4).</summary>
public sealed class ChecklistTickRecord
{
    public Guid Id { get; set; }
    public Guid EngagementId { get; set; }
    public string Code { get; set; } = "";
    public bool Done { get; set; }
}
