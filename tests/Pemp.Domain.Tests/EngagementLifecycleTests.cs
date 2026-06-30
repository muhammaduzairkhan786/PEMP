using Pemp.Domain;
using Pemp.Domain.Audit;
using Xunit;

namespace Pemp.Domain.Tests;

/// <summary>
/// Encodes the guard table (design/state-machine.md, DESIGN_PLAN §8) as the
/// behavioural contract for the engagement state machine.
/// </summary>
public class EngagementLifecycleTests
{
    private static readonly Func<DateTimeOffset> Clock =
        () => new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.Zero);

    private static readonly Guid TesterA = Guid.NewGuid();
    private static readonly Guid TesterB = Guid.NewGuid();

    private static (Engagement e, InMemoryHashChain chain) New(EngagementType type = EngagementType.Project)
    {
        var chain = new InMemoryHashChain();
        var e = Engagement.Raise("ENG-2026-0412", type, "acme", chain, Clock);
        return (e, chain);
    }

    private static void Ok(Result r) => Assert.True(r.Ok, r.Error);

    /// <summary>Drive a Project engagement to just-before SoW signing.</summary>
    private static Engagement DriveToSow(out InMemoryHashChain chain, EngagementType type = EngagementType.Project)
    {
        var (e, c) = New(type);
        chain = c;
        Ok(e.RouteToDeliveryManager("acme"));
        Ok(e.AssignTester(TesterA, "dm"));
        Ok(e.CompleteAssessment("tester"));
        if (type == EngagementType.Project) Ok(e.ReviewSowAsDeliveryManager("dm"));
        Assert.Equal(Stage.Sow, e.CurrentStage);
        return e;
    }

    [Fact]
    public void Full_project_lifecycle_reaches_closed_with_intact_audit_chain()
    {
        var e = DriveToSow(out var chain);
        Ok(e.SignSow("acme", reAuthenticated: true));
        Assert.Equal(Stage.Access, e.CurrentStage);
        Ok(e.VerifyAccess("tester"));
        Assert.Equal(Stage.Execution, e.CurrentStage);
        Ok(e.SendIrNotice("tester"));
        Ok(e.EndTest("tester"));
        Assert.Equal(Stage.Findings, e.CurrentStage);
        Ok(e.GenerateDraft("tester"));
        Assert.Equal(Stage.Report, e.CurrentStage);
        Ok(e.PeerReview(TesterB, passed: true, "peer"));
        Ok(e.ReleaseFinal("acme", reAuthenticated: true));

        Assert.Equal(Stage.Closed, e.CurrentStage);
        Assert.True(chain.Verify());
    }

    [Fact]
    public void SignSow_blocked_without_reauthentication()  // SEC-IAM-04
    {
        var e = DriveToSow(out _);
        var r = e.SignSow("acme", reAuthenticated: false);
        Assert.False(r.Ok);
        Assert.Equal(Stage.Sow, e.CurrentStage);   // nothing changed
        Assert.False(e.SowSigned);
    }

    [Fact]
    public void Project_signSow_blocked_until_dm_review()   // FR-SOW-03
    {
        var (e, _) = New(EngagementType.Project);
        Ok(e.RouteToDeliveryManager("acme"));
        Ok(e.AssignTester(TesterA, "dm"));
        Ok(e.CompleteAssessment("tester"));
        // Skip DM review on purpose.
        var r = e.SignSow("acme", reAuthenticated: true);
        Assert.False(r.Ok);
        Assert.Equal(Stage.Sow, e.CurrentStage);
    }

    [Fact]
    public void Bau_signSow_does_not_require_dm_review()    // FR-SOW-04
    {
        var e = DriveToSow(out _, EngagementType.Bau);
        var r = e.SignSow("acme", reAuthenticated: true);
        Assert.True(r.Ok, r.Error);
        Assert.Equal(Stage.Access, e.CurrentStage);
    }

    [Fact]
    public void Testing_gate_blocks_access_verification_before_sow_signed()  // FR-SOW-06
    {
        var e = DriveToSow(out _);
        // Try to verify access while still at SoW (not signed) — wrong stage, blocked.
        var r = e.VerifyAccess("tester");
        Assert.False(r.Ok);
        Assert.Equal(Stage.Sow, e.CurrentStage);
    }

    [Fact]
    public void CompleteAssessment_is_blocked_when_no_assessment_data_is_present()  // FR-SCO-03 (data-derived gate)
    {
        var (e, _) = New();
        Ok(e.RouteToDeliveryManager("acme"));
        Ok(e.AssignTester(TesterA, "dm"));
        Assert.Equal(Stage.Scoping, e.CurrentStage);

        // No recorded answers → the gate refuses to advance; the engagement stays at Scoping.
        Assert.False(e.CompleteAssessment("tester", assessmentDataPresent: false).Ok);
        Assert.Equal(Stage.Scoping, e.CurrentStage);

        // With recorded answers it advances to SoW.
        Ok(e.CompleteAssessment("tester", assessmentDataPresent: true));
        Assert.Equal(Stage.Sow, e.CurrentStage);
    }

    [Fact]
    public void VerifyAccess_is_blocked_until_access_is_provisioned()  // FR-ACC-04 (data-derived gate)
    {
        var e = DriveToSow(out _);
        Ok(e.SignSow("acme", reAuthenticated: true));
        Assert.Equal(Stage.Access, e.CurrentStage);

        // Unprovisioned access → blocked, even with re-auth.
        Assert.False(e.VerifyAccess("tester", reAuthenticated: true, accessProvisioned: false).Ok);
        Assert.Equal(Stage.Access, e.CurrentStage);

        // Provisioned access → advances to Execution.
        Ok(e.VerifyAccess("tester", reAuthenticated: true, accessProvisioned: true));
        Assert.Equal(Stage.Execution, e.CurrentStage);
    }

    [Fact]
    public void Cannot_skip_stages()
    {
        var (e, _) = New();
        // Assigning a tester at Intake (before routing) is illegal.
        Assert.False(e.AssignTester(TesterA, "dm").Ok);
        Assert.Equal(Stage.Intake, e.CurrentStage);
    }

    [Fact]
    public void EndTest_blocked_until_ir_notice_sent()      // FR-EXE-01
    {
        var e = DriveToSow(out _);
        Ok(e.SignSow("acme", true));
        Ok(e.VerifyAccess("tester"));
        var r = e.EndTest("tester");   // no IR notice yet
        Assert.False(r.Ok);
        Assert.Equal(Stage.Execution, e.CurrentStage);
    }

    [Fact]
    public void Author_cannot_self_review()                // FR-REP-02
    {
        var e = DriveToReport(out _);
        var r = e.PeerReview(TesterA, passed: true, "tester"); // TesterA is the author
        Assert.False(r.Ok);
        Assert.False(e.PeerReviewPassed);
    }

    [Fact]
    public void Release_blocked_without_passing_peer_review()  // FR-REP-02
    {
        var e = DriveToReport(out _);
        var r = e.ReleaseFinal("acme", reAuthenticated: true);
        Assert.False(r.Ok);
        Assert.Equal(Stage.Report, e.CurrentStage);
    }

    [Fact]
    public void Peer_review_reject_returns_to_draft_then_can_recover()
    {
        var e = DriveToReport(out _);
        Ok(e.PeerReview(TesterB, passed: false, "peer"));   // reject
        Assert.Equal(Stage.Report, e.CurrentStage);
        Assert.False(e.DraftGenerated);                     // back to draft
        Assert.False(e.PeerReviewPassed);

        // Author addresses comments, regenerates, passes, releases.
        Ok(e.GenerateDraft("tester"));
        Ok(e.PeerReview(TesterB, passed: true, "peer"));
        Ok(e.ReleaseFinal("acme", true));
        Assert.Equal(Stage.Closed, e.CurrentStage);
    }

    [Fact]
    public void Retest_spawns_linked_child_engagement()    // FR-RET-01/02
    {
        var e = DriveToClosed(out var chain);
        var r = e.RequestRetest("ENG-2026-0488", "stakeholder", out var child);
        Assert.True(r.Ok, r.Error);
        Assert.True(e.RetestRequested);
        Assert.NotNull(child);
        Assert.Equal(e.Id, child!.ParentId);
        Assert.Equal(Stage.Retest, child.CurrentStage);
        Assert.True(chain.Verify());
    }

    [Fact]
    public void Retest_blocked_unless_closed()
    {
        var e = DriveToReport(out _);
        var r = e.RequestRetest("ENG-X", "stakeholder", out var child);
        Assert.False(r.Ok);
        Assert.Null(child);
    }

    // ---- drivers ----
    private static Engagement DriveToReport(out InMemoryHashChain chain)
    {
        var e = DriveToSow(out chain);
        Ok(e.SignSow("acme", true));
        Ok(e.VerifyAccess("tester"));
        Ok(e.SendIrNotice("tester"));
        Ok(e.EndTest("tester"));
        Ok(e.GenerateDraft("tester"));
        return e;
    }

    private static Engagement DriveToClosed(out InMemoryHashChain chain)
    {
        var e = DriveToReport(out chain);
        Ok(e.PeerReview(TesterB, passed: true, "peer"));
        Ok(e.ReleaseFinal("acme", true));
        return e;
    }
}
