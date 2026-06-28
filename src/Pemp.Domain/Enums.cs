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
