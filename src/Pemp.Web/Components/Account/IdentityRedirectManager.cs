using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Components;

namespace Pemp.Web.Components.Account;

/// <summary>
/// Helper for the static-SSR Account pages: performs a redirect by navigating and then
/// throwing, and can carry a one-shot status message across the redirect via a short-lived
/// cookie (the standard Blazor Identity pattern).
/// </summary>
internal sealed class IdentityRedirectManager(NavigationManager navigationManager)
{
    public const string StatusCookieName = "Identity.StatusMessage";

    private static readonly CookieBuilder StatusCookieBuilder = new()
    {
        SameSite = SameSiteMode.Strict,
        HttpOnly = true,
        IsEssential = true,
        MaxAge = TimeSpan.FromSeconds(5),
    };

    [DoesNotReturn]
    public void RedirectTo(string? uri)
    {
        uri ??= "";
        // Prevent open redirects.
        if (!Uri.IsWellFormedUriString(uri, UriKind.Relative))
        {
            uri = navigationManager.ToBaseRelativePath(uri);
        }
        navigationManager.NavigateTo(uri);
        // This project sets BlazorDisableThrowNavigationException, so NavigateTo records the
        // redirect without throwing. Throw a NavigationException explicitly so control flow
        // stops here (the static-SSR pipeline catches it and issues the redirect).
        throw new NavigationException(navigationManager.ToAbsoluteUri(uri).AbsoluteUri);
    }

    [DoesNotReturn]
    public void RedirectTo(string uri, Dictionary<string, object?> queryParameters)
    {
        var uriWithoutQuery = navigationManager.ToAbsoluteUri(uri).GetLeftPart(UriPartial.Path);
        var newUri = navigationManager.GetUriWithQueryParameters(uriWithoutQuery, queryParameters);
        RedirectTo(newUri);
    }

    [DoesNotReturn]
    public void RedirectToWithStatus(string uri, string message, HttpContext context)
    {
        context.Response.Cookies.Append(StatusCookieName, message, StatusCookieBuilder.Build(context));
        RedirectTo(uri);
    }

    private string CurrentPath => navigationManager.ToAbsoluteUri(navigationManager.Uri).GetLeftPart(UriPartial.Path);

    [DoesNotReturn]
    public void RedirectToCurrentPage() => RedirectTo(CurrentPath);

    [DoesNotReturn]
    public void RedirectToCurrentPageWithStatus(string message, HttpContext context)
        => RedirectToWithStatus(CurrentPath, message, context);
}
