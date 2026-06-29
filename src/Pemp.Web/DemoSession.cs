namespace Pemp.Web;

/// <summary>
/// Stand-in for the signed-in identity during the demo — a role switcher in place of
/// Entra SSO (task 9 swaps this for Microsoft.Identity.Web claims). Per-circuit (scoped).
/// </summary>
public sealed class DemoSession
{
    public static readonly string[] Roles =
        { "Acme CA Officer", "Delivery Manager", "Tester", "Stakeholder", "System Administrator" };

    public string Role { get; set; } = "Acme CA Officer";

    public string Actor => Role switch
    {
        "Tester" => "A. Khan",
        "Acme CA Officer" => "J. Okafor",
        "Delivery Manager" => "M. Reyes",
        "Stakeholder" => "P. Devlin",
        _ => "S. Cole",
    };

    // ---- Object-level scope (SEC-AZN / SEC-INS-01) ----
    // A Stakeholder sees only their own application; a Tester only their assigned engagements.
    // Other roles (Acme, DM, Admin) see the whole portfolio.
    public string? AppScope => Role == "Stakeholder" ? "Mobile App" : null;
    public string? TesterScope => Role == "Tester" ? Actor : null;
}
