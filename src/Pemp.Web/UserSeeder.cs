using Microsoft.AspNetCore.Identity;
using Pemp.Infrastructure.Persistence;

namespace Pemp.Web;

/// <summary>
/// Idempotently seeds the five PEMP Identity roles and one demo user per role for the
/// dev/local-Identity path. Accounts are email-confirmed; 2FA is NOT pre-enabled — each
/// user enrolls an authenticator app on first sign-in (the standard PEMP flow).
/// Production uses Entra and never runs this.
/// </summary>
public static class UserSeeder
{
    public const string DevPassword = "Pemp!2026";

    private sealed record DemoUser(string Email, string Role, string DisplayName);

    // Role strings are the canonical DemoSession.Roles values so the app pages (which compare
    // Session.Role to e.g. "Stakeholder") and [Authorize(Roles=...)] line up exactly.
    private static readonly DemoUser[] Users =
    {
        new("acme@pemp.dev",        "Acme CA Officer",      "J. Okafor"),
        new("dm@pemp.dev",          "Delivery Manager",     "M. Reyes"),
        new("tester@pemp.dev",      "Tester",               "A. Khan"),
        new("stakeholder@pemp.dev", "Stakeholder",          "P. Devlin"),
        new("admin@pemp.dev",       "System Administrator", "S. Cole"),
    };

    private static readonly string[] RoleNames = DemoSession.Roles;

    public static async Task SeedAsync(IServiceProvider services)
    {
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();

        foreach (var role in RoleNames)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                var r = await roleManager.CreateAsync(new IdentityRole(role));
                if (!r.Succeeded)
                {
                    throw new InvalidOperationException(
                        $"Failed to seed role '{role}': {string.Join("; ", r.Errors.Select(e => e.Description))}");
                }
            }
        }

        foreach (var u in Users)
        {
            if (await userManager.FindByEmailAsync(u.Email) is not null)
            {
                continue;
            }

            var user = new ApplicationUser
            {
                UserName = u.Email,
                Email = u.Email,
                EmailConfirmed = true,
                DisplayName = u.DisplayName,
                PempRole = u.Role,
            };

            var created = await userManager.CreateAsync(user, DevPassword);
            if (!created.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Failed to seed user '{u.Email}': {string.Join("; ", created.Errors.Select(e => e.Description))}");
            }

            await userManager.AddToRoleAsync(user, u.Role);
        }
    }
}
