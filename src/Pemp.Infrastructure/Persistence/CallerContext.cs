using Pemp.Domain;

namespace Pemp.Infrastructure.Persistence;

/// <summary>
/// Server-side caller identity + object-level scope used for an authorization decision on the WRITE
/// path (SEC-AZN-01/02, anti-BOLA / anti-priv-esc). It mirrors exactly what the Web layer's DemoSession
/// resolves from the authenticated principal — the role, the acting display name, and the same scope
/// filters used for reads — so the SAME predicate that scopes reads (<see cref="EngagementStore.InScope"/>)
/// gates writes. Because the store re-derives scope from this context, the guarantee continues to hold
/// when these methods become API endpoints rather than in-process calls.
/// </summary>
public sealed record CallerContext(string? Role, string Actor, string? AppScope, string? TesterScope)
{
    /// <summary>A portfolio (Acme/DM/Admin) caller — no object filter, sees/acts on the whole estate.</summary>
    public static CallerContext Portfolio(string role, string actor) => new(role, actor, null, null);

    /// <summary>A Tester caller scoped to their own assignments (TesterScope = their display name).</summary>
    public static CallerContext ForTester(string actor) => new(PempRoles.Tester, actor, null, actor);

    /// <summary>A Stakeholder caller scoped to one application.</summary>
    public static CallerContext ForStakeholder(string actor, string appScope) =>
        new(PempRoles.Stakeholder, actor, appScope, null);
}

/// <summary>
/// The lifecycle / data mutations the store authorizes. The store owns the policy table that maps each
/// to its required role(s) and separation-of-duties rules, so the decision is server-side and authoritative
/// (a caller names an action; it cannot supply a laxer policy).
/// </summary>
public enum EngagementAction
{
    RouteToDeliveryManager, AssignTester, CompleteAssessment, ReviewSow, SignSow,
    VerifyAccess, SendIrNotice, EndTest, GenerateDraft, PeerReview, ReleaseFinal,
    RequestRetest, CompleteRetest, Raise,
    AddFinding, SetFindingStatus, AddEvidence, AddTestCredential,
    AddAccessRequirement, SetAccessStatus, SaveAssessmentAnswer, SetChecklist
}

/// <summary>
/// Authorization rule for one <see cref="EngagementAction"/> (SEC-AZN-01). <see cref="AllowedRoles"/> null
/// means "any in-scope role"; otherwise the caller's role must be in the set. The two SoD flags encode the
/// lifecycle's separation-of-duties: the action is restricted to the assigned tester, or (for SoW sign-off)
/// the actor must DIFFER from the assigned tester who drafted it.
/// </summary>
public sealed record ActionAuth(
    IReadOnlySet<string>? AllowedRoles = null,
    bool MustBeAssignedTester = false,
    bool MustDifferFromAssignedTester = false)
{
    public static IReadOnlySet<string> Roles(params string[] roles) =>
        new HashSet<string>(roles, StringComparer.Ordinal);
}
