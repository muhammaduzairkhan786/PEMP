using Microsoft.AspNetCore.Identity;
using Pemp.Infrastructure.Persistence;

namespace Pemp.Web.Components.Account;

/// <summary>Loads the current <see cref="ApplicationUser"/> for a static-SSR Account page,
/// redirecting to an error if the principal can't be resolved.</summary>
internal sealed class IdentityUserAccessor(
    UserManager<ApplicationUser> userManager,
    IdentityRedirectManager redirectManager)
{
    public async Task<ApplicationUser> GetRequiredUserAsync(HttpContext context)
    {
        var user = await userManager.GetUserAsync(context.User);
        if (user is null)
        {
            redirectManager.RedirectToWithStatus(
                "Account/Login",
                $"Error: Unable to load user with ID '{userManager.GetUserId(context.User)}'.",
                context);
        }
        return user;
    }
}
