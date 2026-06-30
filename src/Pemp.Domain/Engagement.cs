using Pemp.Domain.Audit;

namespace Pemp.Domain;

/// <summary>
/// The engagement aggregate and its enforced state machine. Every transition is
/// guarded; an unmet guard returns <see cref="Result.Fail"/> and changes nothing.
/// Each successful transition appends a hash-chained audit entry atomically
/// (architecture.md §3.1/§3.4). Guards mirror the 12-row table in
/// design/state-machine.md and DESIGN_PLAN §8.
/// </summary>
public sealed class Engagement
{
    private readonly IAuditChain _chain;
    private readonly Func<DateTimeOffset> _clock;

    public Guid Id { get; }
    public string Reference { get; }
    public EngagementType Type { get; }
    public Guid? ParentId { get; }
    public Stage CurrentStage { get; private set; }

    // Assignment / ownership
    public Guid? AssignedTesterId { get; private set; }

    // Guard flags (the preconditions the spine enforces)
    public bool AssessmentComplete { get; private set; }
    public bool SowReviewedByDm { get; private set; }
    public bool SowSigned { get; private set; }
    public bool AccessVerified { get; private set; }
    public bool IrNoticeSent { get; private set; }
    public bool DraftGenerated { get; private set; }
    public bool PeerReviewPassed { get; private set; }
    public bool RetestRequested { get; private set; }

    private Engagement(Guid id, string reference, EngagementType type, Guid? parentId, Stage stage,
        IAuditChain chain, Func<DateTimeOffset> clock)
    {
        Id = id;
        Reference = reference;
        Type = type;
        ParentId = parentId;
        CurrentStage = stage;
        _chain = chain;
        _clock = clock;
    }

    /// <summary>Raise a new request: reference minted, engagement enters Intake (FR-REQ-01/04).</summary>
    public static Engagement Raise(string reference, EngagementType type, string actor,
        IAuditChain chain, Func<DateTimeOffset> clock, string source = "api")
    {
        if (string.IsNullOrWhiteSpace(reference))
            throw new ArgumentException("Reference must be minted before raising.", nameof(reference));

        var e = new Engagement(Guid.NewGuid(), reference, type, parentId: null, Stage.Intake, chain, clock);
        chain.Append(e.Id, actor, "Engagement.Raised", "-", Stage.Intake.ToString(), source, clock());
        return e;
    }

    /// <summary>
    /// Reconstruct a persisted engagement so a further transition can run (persistence only).
    /// Restores prior state and writes no audit; later transitions append to the chain as normal.
    /// </summary>
    public static Engagement Rehydrate(
        Guid id, string reference, EngagementType type, Guid? parentId, Stage stage, Guid? assignedTesterId,
        bool assessmentComplete, bool sowReviewedByDm, bool sowSigned, bool accessVerified,
        bool irNoticeSent, bool draftGenerated, bool peerReviewPassed, bool retestRequested,
        IAuditChain chain, Func<DateTimeOffset> clock) =>
        new(id, reference, type, parentId, stage, chain, clock)
        {
            AssignedTesterId = assignedTesterId,
            AssessmentComplete = assessmentComplete,
            SowReviewedByDm = sowReviewedByDm,
            SowSigned = sowSigned,
            AccessVerified = accessVerified,
            IrNoticeSent = irNoticeSent,
            DraftGenerated = draftGenerated,
            PeerReviewPassed = peerReviewPassed,
            RetestRequested = retestRequested,
        };

    // ---- Transitions (guard table rows 2–12) -------------------------------

    /// <summary>Intake → Assignment: routed to the Delivery Manager queue (FR-REQ-03).</summary>
    public Result RouteToDeliveryManager(string actor, string source = "api") =>
        Guarded(Stage.Intake, () => Result.Success(), "Engagement.Routed", actor, source,
            () => CurrentStage = Stage.Assignment);

    /// <summary>
    /// Assignment → Scoping: at least one tester assigned (FR-ASG-03). The audit "after"
    /// records WHO was assigned (name if supplied, else id) alongside the stage move so the
    /// trail is before/after complete (FR-AUD-02), not just a bare stage transition.
    /// </summary>
    public Result AssignTester(Guid testerId, string actor, string source = "api", string? testerLabel = null) =>
        GuardedTransition(
            () => CurrentStage != Stage.Assignment
                ? Result.Fail($"Action 'Tester.Assigned' requires stage {Stage.Assignment} (currently {CurrentStage}).")
                : Result.Success(),
            "Tester.Assigned", actor, source, () =>
            {
                AssignedTesterId = testerId;
                CurrentStage = Stage.Scoping;
            },
            afterDetail: () => $"tester {(string.IsNullOrWhiteSpace(testerLabel) ? testerId.ToString() : testerLabel)}");

    /// <summary>Scoping → SoW: assessment marked complete (FR-SCO-03).</summary>
    public Result CompleteAssessment(string actor, string source = "api") =>
        Guarded(Stage.Scoping, () => Result.Success(), "Assessment.Completed", actor, source, () =>
        {
            AssessmentComplete = true;
            CurrentStage = Stage.Sow;
        });

    /// <summary>(Project only) Delivery Manager reviews the SoW before Acme sign-off (FR-SOW-03).</summary>
    public Result ReviewSowAsDeliveryManager(string actor, string source = "api") =>
        Guarded(Stage.Sow, () => Type == EngagementType.Project
                ? Result.Success()
                : Result.Fail("DM review applies to Project engagements only."),
            "Sow.DmReviewed", actor, source, () => SowReviewedByDm = true);

    /// <summary>
    /// SoW → Access — the signature gate (FR-SOW-05/06). Requires re-authentication
    /// (SEC-IAM-04); Project engagements additionally require prior DM review (FR-SOW-03).
    /// </summary>
    public Result SignSow(string actor, bool reAuthenticated, string source = "api") =>
        Guarded(Stage.Sow, () =>
        {
            if (!reAuthenticated) return Result.Fail("Re-authentication required to sign (SEC-IAM-04).");
            if (Type == EngagementType.Project && !SowReviewedByDm)
                return Result.Fail("Project SoW needs DM review before sign-off (FR-SOW-03).");
            return Result.Success();
        }, "Sow.Signed", actor, source, () =>
        {
            SowSigned = true;
            CurrentStage = Stage.Access;
        });

    /// <summary>
    /// Access → Execution: access verified before the start date (FR-ACC-04). Re-auth-gated
    /// privileged action (SEC-IAM-04); the param defaults true for non-UI callers.
    /// </summary>
    public Result VerifyAccess(string actor, bool reAuthenticated = true, string source = "api") =>
        Guarded(Stage.Access, () => reAuthenticated
                ? Result.Success()
                : Result.Fail("Re-authentication required to verify access (SEC-IAM-04)."),
            "Access.Verified", actor, source, () =>
        {
            AccessVerified = true;
            CurrentStage = Stage.Execution;
        });

    /// <summary>Execution start: IR advance notice sent before testing begins (FR-EXE-01).</summary>
    public Result SendIrNotice(string actor, string source = "api") =>
        Guarded(Stage.Execution, () => Result.Success(), "Exe.IrNoticeSent", actor, source,
            () => IrNoticeSent = true);

    /// <summary>Execution → Findings: end-of-test notice; requires the IR notice was sent (FR-EXE-03).</summary>
    public Result EndTest(string actor, string source = "api") =>
        Guarded(Stage.Execution, () => IrNoticeSent
                ? Result.Success()
                : Result.Fail("Send the IR advance notice before ending the test (FR-EXE-01)."),
            "Exe.Ended", actor, source, () => CurrentStage = Stage.Findings);

    /// <summary>
    /// Findings → Report: a draft is generated from the findings (FR-REP-01). Also the
    /// recovery path after a peer-review rejection — re-drafting from Report (where the
    /// reject path leaves the engagement with no current draft) so a returned report can
    /// be addressed and re-submitted (FR-REP-02).
    /// </summary>
    public Result GenerateDraft(string actor, string source = "api") =>
        GuardedTransition(() =>
            CurrentStage == Stage.Findings || (CurrentStage == Stage.Report && !DraftGenerated)
                ? Result.Success()
                : Result.Fail($"Report.Drafted requires stage Findings, or Report pending re-draft (currently {CurrentStage})."),
            "Report.Drafted", actor, source, () =>
        {
            DraftGenerated = true;
            CurrentStage = Stage.Report;
        });

    /// <summary>
    /// QA peer review (FR-REP-02). The reviewer must not be the author (no self-review).
    /// On pass, the report may be released; on reject it returns to draft (stage stays Report).
    /// </summary>
    public Result PeerReview(Guid reviewerId, bool passed, string actor, string source = "api") =>
        Guarded(Stage.Report, () =>
        {
            if (!DraftGenerated) return Result.Fail("No draft to review.");
            if (reviewerId == AssignedTesterId) return Result.Fail("Author cannot self-review (FR-REP-02).");
            return Result.Success();
        }, passed ? "Report.PeerApproved" : "Report.PeerReturnedToDraft", actor, source, () =>
        {
            if (passed)
            {
                PeerReviewPassed = true;
            }
            else
            {
                // Reject path: back to draft — must regenerate after addressing comments.
                DraftGenerated = false;
                PeerReviewPassed = false;
            }
        });

    /// <summary>Report → Closed: final report released and stored immutable (FR-REP-04). Re-auth required.</summary>
    public Result ReleaseFinal(string actor, bool reAuthenticated, string source = "api") =>
        Guarded(Stage.Report, () =>
        {
            if (!reAuthenticated) return Result.Fail("Re-authentication required to release (SEC-IAM-04).");
            if (!PeerReviewPassed) return Result.Fail("Peer QA review must pass before release (FR-REP-02).");
            return Result.Success();
        }, "Report.Released", actor, source, () => CurrentStage = Stage.Closed);

    /// <summary>
    /// Closed → Retest: spawns a linked child engagement that re-verifies remediated
    /// findings (FR-RET-01/02). Returns the child; the parent records the request.
    /// </summary>
    public Result RequestRetest(string childReference, string actor, out Engagement? child, string source = "api")
    {
        child = null;
        if (CurrentStage != Stage.Closed)
            return Result.Fail($"Retest can only be requested on a closed engagement (currently {CurrentStage}).");
        if (RetestRequested)
            return Result.Fail("A retest has already been requested for this engagement.");
        if (string.IsNullOrWhiteSpace(childReference))
            return Result.Fail("Child reference must be minted for the retest.");

        RetestRequested = true;
        _chain.Append(Id, actor, "Retest.Requested", Stage.Closed.ToString(), Stage.Closed.ToString(), source, _clock());

        var c = new Engagement(Guid.NewGuid(), childReference, Type, parentId: Id, Stage.Retest, _chain, _clock)
        {
            AssignedTesterId = AssignedTesterId
        };
        _chain.Append(c.Id, actor, "Engagement.RetestSpawned", "-", Stage.Retest.ToString(), source, _clock());
        child = c;
        return Result.Success();
    }

    /// <summary>
    /// Retest child: the tester has re-verified the in-scope findings (pass/fail each) →
    /// close the child with its retest report (FR-RET-03/04).
    /// </summary>
    public Result CompleteRetest(string actor, string source = "api") =>
        Guarded(Stage.Retest, () => Result.Success(), "Retest.Completed", actor, source,
            () => CurrentStage = Stage.Closed);

    // ---- guard helper ------------------------------------------------------

    // Fixed-stage transition: the action is legal only from a single required stage.
    private Result Guarded(Stage required, Func<Result> guard, string action, string actor, string source, Action apply) =>
        GuardedTransition(
            () => CurrentStage != required
                ? Result.Fail($"Action '{action}' requires stage {required} (currently {CurrentStage}).")
                : guard(),
            action, actor, source, apply);

    // Core transition: the guard fully decides legality (including any stage check),
    // the state change and the hash-chain append commit as one logical transaction.
    // <paramref name="afterDetail"/> (evaluated post-apply) appends extra context to the
    // audit "after" beyond the bare stage, for before/after completeness (FR-AUD-02).
    private Result GuardedTransition(Func<Result> guard, string action, string actor, string source, Action apply,
        Func<string>? afterDetail = null)
    {
        var g = guard();
        if (g.Failed) return g;

        var before = CurrentStage.ToString();
        apply();
        var after = afterDetail is null ? CurrentStage.ToString() : $"{CurrentStage} · {afterDetail()}";
        _chain.Append(Id, actor, action, before, after, source, _clock());
        return Result.Success();
    }
}
