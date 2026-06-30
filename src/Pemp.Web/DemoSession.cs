namespace Pemp.Web;

/// <summary>
/// Per-circuit signed-in context. In the dev/local-Identity path it is populated in
/// MainLayout from the authenticated user's role + display name; in production the same
/// values come from Entra group / name claims. Pages read <see cref="Role"/> and
/// <see cref="Actor"/> exactly as before. Per-circuit (scoped).
/// </summary>
public sealed class DemoSession
{
    public static readonly string[] Roles =
        { "Acme CA Officer", "Delivery Manager", "Tester", "Stakeholder", "System Administrator" };

    public string Role { get; set; } = "Acme CA Officer";

    public string Actor { get; set; } = "J. Okafor";

    // ---- Object-level scope (SEC-AZN / SEC-INS-01) ----
    // A Stakeholder sees only their own application; a Tester only their assigned engagements.
    // Other roles (Acme, DM, Admin) see the whole portfolio.
    public string? AppScope => Role == "Stakeholder" ? "Mobile App" : null;
    public string? TesterScope => Role == "Tester" ? Actor : null;
}
