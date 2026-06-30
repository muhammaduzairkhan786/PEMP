using Microsoft.AspNetCore.Identity;

namespace Pemp.Infrastructure.Persistence;

/// <summary>
/// Local Identity user (dev). Password-hashed by ASP.NET Core Identity; authenticator
/// TOTP via Identity's built-in 2FA. Carries the PEMP role + display name. In production
/// this is replaced by Entra identities (the role comes from group claims).
/// </summary>
public sealed class ApplicationUser : IdentityUser
{
    public string DisplayName { get; set; } = "";
    public string PempRole { get; set; } = ""; // one of DemoSession.Roles
}
