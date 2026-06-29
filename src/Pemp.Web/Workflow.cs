using Pemp.Domain;
using Pemp.Infrastructure.Persistence;

namespace Pemp.Web;

public enum ActionKind
{
    None, RouteToDm, AssignTester, CompleteAssessment, ReviewSow, SignSow,
    VerifyAccess, SendIr, EndTest, GenerateDraft, PeerReview, PeerReviewReject,
    ReleaseFinal, RequestRetest, CompleteRetest
}

/// <summary>
/// UI-side projection of the domain state machine: the human label for each stage and
/// the single next action + who owns it. The authority remains the domain guards — this
/// only predicts the next legal move so the UI can show "whose turn" (DESIGN_PLAN §1.1/§9).
/// </summary>
public static class Workflow
{
    /// <summary>The linear master spine shown in the UI (Retest is a child engagement).</summary>
    public static readonly Stage[] Spine =
        { Stage.Intake, Stage.Assignment, Stage.Scoping, Stage.Sow, Stage.Access,
          Stage.Execution, Stage.Findings, Stage.Report, Stage.Closed, Stage.Retest };

    public static string Label(Stage s) => s switch
    {
        Stage.Sow => "SoW",
        Stage.Execution => "Testing",
        _ => s.ToString(),
    };

    /// <summary>SOP target week per stage (workbook Tab 2 "Pentesting SOP Process").</summary>
    public static string Week(Stage s) => s switch
    {
        Stage.Scoping => "WK-1",
        Stage.Sow => "WK-2",
        Stage.Access => "WK-2/3",
        Stage.Execution => "WK-4–6",
        Stage.Findings => "WK-6",
        Stage.Report => "WK-8–10",
        Stage.Closed => "WK-11–12",
        _ => "",
    };

    /// <summary>Re-auth-gated privileged actions (SEC-IAM-04): credential view, sign, release.</summary>
    public static bool NeedsReauth(ActionKind k) => k is ActionKind.SignSow or ActionKind.ReleaseFinal;

    public static (ActionKind Kind, string Label, string OwnerRole) Next(EngagementRecord r) => r.CurrentStage switch
    {
        Stage.Intake => (ActionKind.RouteToDm, "Route to Delivery Manager", "Delivery Manager"),
        Stage.Assignment => (ActionKind.AssignTester, "Assign a tester", "Delivery Manager"),
        Stage.Scoping => (ActionKind.CompleteAssessment, "Complete the assessment", "Stakeholder"),
        Stage.Sow when r.Type == EngagementType.Project && !r.SowReviewedByDm
            => (ActionKind.ReviewSow, "Review Project SoW", "Delivery Manager"),
        Stage.Sow => (ActionKind.SignSow, "Review & Sign SoW", "Acme CA Officer"),
        Stage.Access => (ActionKind.VerifyAccess, "Verify access", "Tester"),
        Stage.Execution when !r.IrNoticeSent => (ActionKind.SendIr, "Send IR notice & start test", "Tester"),
        Stage.Execution => (ActionKind.EndTest, "End test & send notice", "Tester"),
        Stage.Findings => (ActionKind.GenerateDraft, "Generate draft report", "Tester"),
        Stage.Report when !r.DraftGenerated => (ActionKind.GenerateDraft, "Re-draft report (address QA comments)", "Tester"),
        Stage.Report when !r.PeerReviewPassed => (ActionKind.PeerReview, "Peer QA review", "Tester"),
        Stage.Report => (ActionKind.ReleaseFinal, "Release final report", "Tester"),
        Stage.Closed when !r.RetestRequested => (ActionKind.RequestRetest, "Request a retest", "Acme CA Officer"),
        Stage.Retest => (ActionKind.CompleteRetest, "Re-verify findings & complete retest", "Tester"),
        _ => (ActionKind.None, "—", ""),
    };
}
