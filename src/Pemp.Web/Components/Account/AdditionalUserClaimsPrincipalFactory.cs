using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Pemp.Infrastructure.Persistence;

namespace Pemp.Web.Components.Account;

/// <summary>
/// Adds PEMP-specific claims (DisplayName, PempRole) to the signed-in principal so the UI
/// can render the actor and the layout can resolve the role without an extra DB hit. Role
/// authorization continues to flow through Identity roles (ClaimTypes.Role).
/// </summary>
internal sealed class AdditionalUserClaimsPrincipalFactory(
    UserManager<ApplicationUser> userManager,
    RoleManager<IdentityRole> roleManager,
    IOptions<IdentityOptions> optionsAccessor)
    : UserClaimsPrincipalFactory<ApplicationUser, IdentityRole>(userManager, roleManager, optionsAccessor)
{
    public override async Task<ClaimsPrincipal> CreateAsync(ApplicationUser user)
    {
        var principal = await base.CreateAsync(user);
        if (principal.Identity is ClaimsIdentity identity)
        {
            if (!string.IsNullOrEmpty(user.DisplayName))
            {
                identity.AddClaim(new Claim("DisplayName", user.DisplayName));
            }
            if (!string.IsNullOrEmpty(user.PempRole))
            {
                identity.AddClaim(new Claim("PempRole", user.PempRole));
            }
            // 2FA enrollment gate (local Identity path): the layout blocks app pages until
            // the user has enrolled an authenticator. Re-issued on RefreshSignInAsync so the
            // cookie carries the up-to-date value the moment 2FA is enabled.
            identity.AddClaim(new Claim("tfa", user.TwoFactorEnabled ? "true" : "false"));
        }
        return principal;
    }
}
