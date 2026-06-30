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

    /// <summary>Privileged gate actions that open the attestation + re-auth ceremony (SEC-IAM-04).</summary>
    public static bool NeedsReauth(ActionKind k) =>
        k is ActionKind.SignSow or ActionKind.ReleaseFinal or ActionKind.VerifyAccess;

    public static (ActionKind Kind, string Label, string OwnerRole) Next(EngagementRecord r) => r.CurrentStage switch
    {
        Stage.Intake => (ActionKind.RouteToDm, "Send to Delivery Manager", PempRoles.DeliveryManager),
        Stage.Assignment => (ActionKind.AssignTester, "Assign a tester", PempRoles.DeliveryManager),
        // The assigned Tester drives + confirms the assessment (FR-SCO-03); stakeholders supply input
        // async (offered as an "assist" on the page). Owner reflects who can actually advance it.
        Stage.Scoping => (ActionKind.CompleteAssessment, "Complete the assessment", PempRoles.Tester),
        Stage.Sow when r.Type == EngagementType.Project && !r.SowReviewedByDm
            => (ActionKind.ReviewSow, "Review Project SoW", PempRoles.DeliveryManager),
        Stage.Sow => (ActionKind.SignSow, "Review & Sign SoW", PempRoles.AcmeOfficer),
        Stage.Access => (ActionKind.VerifyAccess, "Verify access", PempRoles.Tester),
        Stage.Execution when !r.IrNoticeSent => (ActionKind.SendIr, "Send IR notice & start test", PempRoles.Tester),
        Stage.Execution => (ActionKind.EndTest, "End test & send notice", PempRoles.Tester),
        Stage.Findings => (ActionKind.GenerateDraft, "Generate draft report", PempRoles.Tester),
        Stage.Report when !r.DraftGenerated => (ActionKind.GenerateDraft, "Re-draft report (address QA comments)", PempRoles.Tester),
        Stage.Report when !r.PeerReviewPassed => (ActionKind.PeerReview, "Peer QA review", PempRoles.Tester),
        Stage.Report => (ActionKind.ReleaseFinal, "Release final report", PempRoles.Tester),
        // Closed is terminal/"done" — a retest is an OPTIONAL action offered on the page,
        // not a mandatory My-Turn item. Retest child has its own owned action.
        Stage.Retest => (ActionKind.CompleteRetest, "Re-verify findings & complete retest", PempRoles.Tester),
        _ => (ActionKind.None, "—", ""),
    };
}
