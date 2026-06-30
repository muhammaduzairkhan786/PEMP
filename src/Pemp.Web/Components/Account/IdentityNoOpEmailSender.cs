using Microsoft.AspNetCore.Identity;
using Pemp.Infrastructure.Persistence;

namespace Pemp.Web.Components.Account;

/// <summary>No-op email sender for the dev/local-Identity path — PEMP uses authenticator-app
/// 2FA and confirmed accounts are seeded, so no transactional mail is sent. Production uses
/// Entra and never touches this.</summary>
internal sealed class IdentityNoOpEmailSender : IEmailSender<ApplicationUser>
{
    public Task SendConfirmationLinkAsync(ApplicationUser user, string email, string confirmationLink) => Task.CompletedTask;
    public Task SendPasswordResetLinkAsync(ApplicationUser user, string email, string resetLink) => Task.CompletedTask;
    public Task SendPasswordResetCodeAsync(ApplicationUser user, string email, string resetCode) => Task.CompletedTask;
}
