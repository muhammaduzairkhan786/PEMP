using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Pemp.Infrastructure.Persistence;

namespace Pemp.Web.Components.Account;

internal static class IdentityComponentsEndpointRouteBuilderExtensions
{
    /// <summary>Maps the non-Razor Account endpoints — currently the Logout POST.</summary>
    public static IEndpointConventionBuilder MapAdditionalIdentityEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var accountGroup = endpoints.MapGroup("/Account");

        accountGroup.MapPost("/Logout", async (
            SignInManager<ApplicationUser> signInManager,
            [FromForm] string? returnUrl) =>
        {
            await signInManager.SignOutAsync();
            return TypedResults.LocalRedirect($"~/{returnUrl ?? string.Empty}");
        });

        return accountGroup;
    }
}
