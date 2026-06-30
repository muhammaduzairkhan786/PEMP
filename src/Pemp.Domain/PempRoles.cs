namespace Pemp.Domain;

/// <summary>
/// Canonical PEMP role names — the single source of truth for the five roles (CLAUDE.md domain model).
/// These EXACT strings are the Entra group / local-Identity role names, so every consumer derives from
/// them rather than repeating a literal that could drift: <c>[Authorize(Roles=…)]</c>, the seeded users
/// (UserSeeder), the per-circuit <c>DemoSession.Role</c> comparisons, the workflow owner labels, and the
/// store's portfolio-scope set (SEC-AZN / NFR-USA-01). Do NOT change the literal values — auth and the
/// seeded accounts must keep matching.
/// </summary>
public static class PempRoles
{
    public const string AcmeOfficer = "Acme CA Officer";
    public const string DeliveryManager = "Delivery Manager";
    public const string Tester = "Tester";
    public const string Stakeholder = "Stakeholder";
    public const string Administrator = "System Administrator";

    /// <summary>All five roles, in nav / seed order.</summary>
    public static readonly string[] All =
        { AcmeOfficer, DeliveryManager, Tester, Stakeholder, Administrator };

    /// <summary>
    /// Roles entitled to the WHOLE portfolio (SEC-AZN / SEC-INS-01). Every other role is "scoped":
    /// it must arrive with a concrete object filter, and a blank/unresolved filter matches NOTHING.
    /// </summary>
    public static readonly IReadOnlySet<string> Portfolio =
        new HashSet<string>(StringComparer.Ordinal) { AcmeOfficer, DeliveryManager, Administrator };

    /// <summary>True if <paramref name="role"/> is an all-portfolio role (named explicitly, never null/blank).</summary>
    public static bool IsPortfolio(string? role) => role is not null && Portfolio.Contains(role);
}
