namespace Pemp.Infrastructure.Persistence;

/// <summary>One saved answer to an assessment question (workbook Tab 1 / FR-SCO).</summary>
public sealed class AssessmentAnswerRecord
{
    public Guid Id { get; set; }
    public Guid EngagementId { get; set; }
    public string QuestionId { get; set; } = "";
    public string Value { get; set; } = "";
}
