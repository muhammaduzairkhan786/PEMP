namespace Pemp.Domain;

/// <summary>
/// The engagement lifecycle stages — the master spine (DESIGN_PLAN §1.1,
/// state-machine.md). An engagement advances one stage at a time, and only when
/// the transition's guard is satisfied.
/// </summary>
public enum Stage
{
    Intake,
    Assignment,
    Scoping,
    Sow,        // Statement of Work — sign-off gate
    Access,     // Access & prerequisites — verify gate
    Execution,  // Testing window
    Findings,
    Report,     // Draft + QA peer-review gate
    Closed,     // Final report released, register stored
    Retest      // Child engagement re-verifies remediated findings
}

/// <summary>BAU (routine) vs Project (release-tied, requires DM review of the SoW).</summary>
public enum EngagementType
{
    Bau,
    Project
}

/// <summary>Finding status in the live vulnerability register (FR-FND-04).</summary>
public enum FindingStatus
{
    Open,
    Remediated,
    AcceptedRisk,
    RetestPending,
    Closed
}

/// <summary>
/// The guarded finding-status lifecycle (FR-FND-04). A finding moves open → remediated / accepted-risk,
/// is carried into a retest as retest-pending, and resolves to closed (or re-opens on a failed retest).
/// Status changes route through <see cref="CanTransition"/> so the live register cannot be driven into a
/// nonsensical state (e.g. reviving a closed finding) by the free-form setter — the same enforcement
/// philosophy as the engagement spine, applied to findings.
/// </summary>
public static class FindingLifecycle
{
    private static readonly IReadOnlyDictionary<FindingStatus, FindingStatus[]> Allowed =
        new Dictionary<FindingStatus, FindingStatus[]>
        {
            [FindingStatus.Open]          = [FindingStatus.Remediated, FindingStatus.AcceptedRisk, FindingStatus.RetestPending, FindingStatus.Closed],
            [FindingStatus.Remediated]    = [FindingStatus.RetestPending, FindingStatus.Open, FindingStatus.Closed],
            [FindingStatus.AcceptedRisk]  = [FindingStatus.Open, FindingStatus.Closed],
            [FindingStatus.RetestPending] = [FindingStatus.Closed, FindingStatus.Open],
            [FindingStatus.Closed]        = [],   // terminal — a retest spawns a NEW child finding, never revives this one
        };

    /// <summary>True if a finding may move directly from <paramref name="from"/> to <paramref name="to"/>.</summary>
    public static bool CanTransition(FindingStatus from, FindingStatus to) =>
        Allowed.TryGetValue(from, out var next) && Array.IndexOf(next, to) >= 0;
}
