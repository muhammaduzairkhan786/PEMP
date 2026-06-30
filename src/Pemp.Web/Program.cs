using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Identity.Web;
using Microsoft.Identity.Web.UI;
using Pemp.Infrastructure;
using Pemp.Infrastructure.Persistence;
using Pemp.Web;
using Pemp.Web.Components;
using Pemp.Web.Components.Account;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Entra SSO (SEC-IAM / FR-AUTH) is the PRODUCTION identity path — activated only when
// AzureAd:ClientId is configured (see docs/azure-entra-setup.md). When it is absent (local
// dev), PEMP falls back to ASP.NET Core Identity with a local user store: email+password
// sign-in followed by authenticator-app TOTP 2FA. The local store intentionally overrides
// the SRS "no local password store" rule FOR DEV ONLY; prod remains Entra.
var entraConfigured = !string.IsNullOrWhiteSpace(builder.Configuration["AzureAd:ClientId"]);
if (entraConfigured)
{
    builder.Services.AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
        .AddMicrosoftIdentityWebApp(builder.Configuration.GetSection("AzureAd"));
    builder.Services.AddAuthorization();
    builder.Services.AddCascadingAuthenticationState();
    builder.Services.AddControllersWithViews().AddMicrosoftIdentityUI(); // sign-in/out endpoints
}
else
{
    // ---- Dev / local ASP.NET Core Identity (Blazor "Individual Accounts" pattern) ----
    builder.Services.AddCascadingAuthenticationState();
    builder.Services.AddScoped<IdentityUserAccessor>();
    builder.Services.AddScoped<IdentityRedirectManager>();
    builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();

    builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme)
        .AddIdentityCookies();

    builder.Services.AddIdentityCore<ApplicationUser>(options =>
        {
            options.SignIn.RequireConfirmedAccount = false;
            options.Password.RequiredLength = 8;
            options.Password.RequireDigit = true;
            options.Password.RequireLowercase = true;
            options.Password.RequireUppercase = true;
            options.Password.RequireNonAlphanumeric = true;
            options.User.RequireUniqueEmail = true;
        })
        .AddRoles<IdentityRole>()
        .AddEntityFrameworkStores<PempDbContext>()
        .AddSignInManager()
        .AddDefaultTokenProviders();

    builder.Services.AddScoped<
        IUserClaimsPrincipalFactory<ApplicationUser>, AdditionalUserClaimsPrincipalFactory>();
    builder.Services.AddSingleton<IEmailSender<ApplicationUser>, IdentityNoOpEmailSender>();
    builder.Services.AddAuthorization();
}

// Persistence: local demo uses SQLite; set "UseSqlite": false + a SqlServer
// connection string ("Pemp") to point at Azure SQL (§9 / task 10 setup guide).
var useSqlite = builder.Configuration.GetValue("UseSqlite", true);
var connectionString = builder.Configuration.GetConnectionString("Pemp")
                       ?? "Data Source=pemp-demo.db";
// Audit HMAC key (SEC-AUD-01): dev reads it from Audit:HmacKey (appsettings.Development.json);
// PROD MUST source it from Azure Key Vault via managed identity — never from config.
var auditHmacKey = builder.Configuration["Audit:HmacKey"];
builder.Services.AddPempInfrastructure(connectionString, useSqlite, auditHmacKey);

// Per-circuit signed-in context (role + actor). Populated from the authenticated user in
// MainLayout; carries the object-level scope used across the app (SEC-AZN / SEC-INS-01).
builder.Services.AddScoped<Pemp.Web.DemoSession>();

var app = builder.Build();

// Create the schema and seed demo data (real domain transitions → genuine audit chain).
using (var scope = app.Services.CreateScope())
{
    var sp = scope.ServiceProvider;
    var db = sp.GetRequiredService<PempDbContext>();
    db.Database.EnsureCreated();
    DemoSeeder.Seed(db, sp.GetRequiredService<Func<DateTimeOffset>>(),
        sp.GetRequiredService<AuditHmacKey>().Value);
    if (!entraConfigured)
    {
        await UserSeeder.SeedAsync(sp); // dev Identity roles + one user per PEMP role
    }
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

// Server-side 2FA-enrollment gate (LOCAL Identity path only). An authenticated user whose
// authenticator is not yet enrolled (no "tfa"=="true" claim) is hard-redirected (302) to the
// enrolment page before any app content can be served — closing the hole where a direct GET of
// an app page returned content instead of a redirect. Skipped entirely under Entra (production),
// which enforces MFA via Conditional Access and carries no "tfa" claim. The /Account/* pages
// (sign-in, enrolment, sign-out), the Blazor framework/SignalR endpoints, and static assets are
// exempt so there is no redirect loop and the enrolment UI itself stays reachable.
if (!entraConfigured)
{
    app.Use(async (context, next) =>
    {
        var user = context.User;
        var path = context.Request.Path;
        var exempt = path.StartsWithSegments("/Account")
                     || path.StartsWithSegments("/_framework")
                     || path.StartsWithSegments("/_blazor")
                     || path.StartsWithSegments("/_content")
                     || Path.HasExtension(path.Value); // static assets (css/js/png…)

        if (!exempt
            && user.Identity?.IsAuthenticated == true
            && user.FindFirst("tfa")?.Value != "true")
        {
            context.Response.Redirect("/Account/EnableAuthenticator");
            return;
        }

        await next();
    });
}

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

if (entraConfigured)
{
    app.MapControllers(); // MicrosoftIdentity/Account sign-in & sign-out
}
else
{
    app.MapAdditionalIdentityEndpoints(); // /Account/Logout
}

app.Run();
