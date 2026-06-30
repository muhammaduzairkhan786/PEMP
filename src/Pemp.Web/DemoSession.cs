using Microsoft.AspNetCore.Components.Authorization;
using Pemp.Domain;

namespace Pemp.Web;

/// <summary>
/// Per-circuit signed-in context. Role + display name are derived from the authenticated
/// <see cref="System.Security.Claims.ClaimsPrincipal"/> (local-Identity roles in dev; Entra
/// group / name claims in production) via <see cref="EnsureInitializedAsync"/>, which every
/// page MUST await before reading <see cref="Role"/>, <see cref="Actor"/>, or the object-level
/// scope. This closes the SSR ordering hole where a page queried the store with the default
/// identity before the layout had populated it (leaking the full, unscoped portfolio in the
/// server-rendered HTML). Per-circuit (scoped); resolves once, then cached.
/// </summary>
public sealed class DemoSession(AuthenticationStateProvider authProvider)
{
    public static readonly string[] Roles = PempRoles.All;

    private bool _initialized;

    // No misleading defaults: empty until resolved from the principal, so a page that forgot
    // to call EnsureInitializedAsync gets a no-privilege state, never another role's data.
    public string Role { get; private set; } = "";

    public string Actor { get; private set; } = "";

    /// <summary>True once the user has enrolled an authenticator (local Identity "tfa" claim).</summary>
    public bool TfaEnrolled { get; private set; }

    /// <summary>
    /// Resolve role/actor/scope from the authenticated principal. Idempotent and synchronous
    /// after the first await. Called at the very start of each page's lifecycle so the store is
    /// queried with the correct scope during the SSR/prerender pass — not after re-render.
    /// </summary>
    public async Task EnsureInitializedAsync()
    {
        if (_initialized) return;
        _initialized = true;

        var user = (await authProvider.GetAuthenticationStateAsync()).User;
        if (user.Identity?.IsAuthenticated == true)
        {
            Actor = user.FindFirst("DisplayName")?.Value ?? user.Identity.Name ?? "";
            // No recognised role → no-privilege state (empty), not a misleading default that
            // would expose role-gated nav (e.g. New Request) to an unmapped user.
            Role = Array.Find(Roles, user.IsInRole) ?? "";
            TfaEnrolled = user.FindFirst("tfa")?.Value == "true";
        }
    }

    // ---- Object-level scope (SEC-AZN / SEC-INS-01) ----
    // A Stakeholder sees only their own application; a Tester only their assigned engagements. Scope now
    // keys on STABLE ids (app id / tester id), never display names. The authenticated principal is mapped
    // to its stable id at this boundary (demo: by display name → seeded id; prod: the Entra object id).
    // Other roles (Acme, DM, Admin) see the whole portfolio.
    public Guid? AppScope => Role == PempRoles.Stakeholder
        ? Pemp.Infrastructure.Persistence.DemoSeeder.AppIdFor("Retail Web")  // P. Devlin owns Retail Web
        : (Guid?)null;

    private Guid? ResolvedTesterId =>
        Array.Find(Pemp.Infrastructure.Persistence.DemoSeeder.Testers, t => t.Name == Actor)?.Id;

    public Guid? TesterScope => Role == PempRoles.Tester ? ResolvedTesterId : null;

    /// <summary>
    /// The server-side authorization context for a WRITE (SEC-AZN). Carries the SAME role + scope used
    /// for reads (stable ids), so the store re-derives and enforces object-level scope + role/SoD on
    /// every mutation. <see cref="ResolvedTesterId"/> doubles as the SoD user id for testers.
    /// </summary>
    public Pemp.Infrastructure.Persistence.CallerContext Caller =>
        new(Role, Actor, AppScope, TesterScope, ResolvedTesterId);
}
