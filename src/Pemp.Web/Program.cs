using System.Security.Cryptography;
using System.Threading.RateLimiting;
using Azure.Identity;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Identity.Web;
using Microsoft.Identity.Web.UI;
using Pemp.Infrastructure;
using Pemp.Infrastructure.Persistence;
using Pemp.Web;
using Pemp.Web.Components;
using Pemp.Web.Components.Account;

var builder = WebApplication.CreateBuilder(args);

// ---- Azure Key Vault configuration provider (SEC-CRD-01 / SEC-AUD-01) -------
// Activated ONLY when a Key Vault URI is configured (Bicep sets KeyVault:Uri on the Web App).
// Secrets are read via the Web App's managed identity (DefaultAzureCredential). Secret names use
// "--" which the provider maps to ":" config keys (e.g. Audit--HmacKey → Audit:HmacKey). Local
// dev leaves KeyVault:Uri blank and keeps working with no vault and no Azure credentials.
var keyVaultUri = builder.Configuration["KeyVault:Uri"];
if (!string.IsNullOrWhiteSpace(keyVaultUri))
{
    builder.Configuration.AddAzureKeyVault(new Uri(keyVaultUri), new DefaultAzureCredential());
}

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Application Insights (NFR-MNT / SEC-INS-03). Activated only when a connection string is present
// (Bicep injects APPLICATIONINSIGHTS_CONNECTION_STRING on the Web App); skipped locally so dev runs
// without a telemetry endpoint. EF/SQL/HTTP dependency tracking is on by default, and the
// Activity/TraceId surfaced on Error.razor flows into the telemetry operation id.
var appInsightsConn = builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]
                      ?? builder.Configuration["ApplicationInsights:ConnectionString"];
if (!string.IsNullOrWhiteSpace(appInsightsConn))
{
    builder.Services.AddApplicationInsightsTelemetry();
}

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
            // Brute-force lockout (SEC-IAM): Login.razor already signs in with lockoutOnFailure:true.
            options.Lockout.MaxFailedAccessAttempts = 5;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            options.Lockout.AllowedForNewUsers = true;
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
// PROD sources it from Azure Key Vault (Audit--HmacKey) via the KV config provider above.
// The dev/default-key fallback is allowed ONLY for Development or the local SQLite demo (no vault);
// a real Azure SQL deployment (UseSqlite=false) FAILS CLOSED when the key is missing or equals the
// public default — see AuditHmacKey.FromConfig.
var auditHmacKey = builder.Configuration["Audit:HmacKey"];
var allowDefaultAuditKey = builder.Environment.IsDevelopment() || useSqlite;
builder.Services.AddPempInfrastructure(
    connectionString, useSqlite, auditHmacKey, allowDefaultAuditKey);

// Health/readiness (NFR-AVL / NFR-MNT): liveness = process up; readiness = DB reachable.
builder.Services.AddHealthChecks()
    .AddDbContextCheck<PempDbContext>("database", tags: ["ready"]);

// Rate limiting (SEC-IAM): a strict fixed window guards the auth + TOTP step-up surface
// (everything under /Account: sign-in, 2FA enrolment, login-with-2fa/recovery-code). All other
// requests — app pages, the SignalR circuit, static assets and the health endpoints — are
// unlimited here so the interactive UI and probes are never throttled.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
    {
        if (httpContext.Request.Path.StartsWithSegments("/Account"))
        {
            return RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 10,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                });
        }
        return RateLimitPartition.GetNoLimiter("unlimited");
    });
});

// Per-circuit signed-in context (role + actor). Populated from the authenticated user in
// MainLayout; carries the object-level scope used across the app (SEC-AZN / SEC-INS-01).
builder.Services.AddScoped<Pemp.Web.DemoSession>();

var app = builder.Build();

// Fail-closed startup assertion (SEC-AUD-01): a real Azure SQL deployment must never run with the
// public default audit key. FromConfig already throws for that path; this is a belt-and-braces
// guard plus a clear confirmation log. The local SQLite demo / Development keep the dev fallback.
if (!allowDefaultAuditKey)
{
    var auditKey = app.Services.GetRequiredService<AuditHmacKey>();
    if (auditKey.IsDefaultKey)
    {
        throw new InvalidOperationException(
            "SEC-AUD-01: refusing to start a production deployment with the public default audit HMAC key. " +
            "Configure the 'Audit--HmacKey' secret in Key Vault.");
    }
    app.Logger.LogInformation("Audit HMAC key resolved from configuration (not the compiled default).");
}

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

// ---- Security headers + Content-Security-Policy (SEC-NET / rank 13) ---------
// Enforcing CSP with a per-request nonce. The single inline <script> in App.razor (theme
// no-flash + download helper) carries this nonce; the Blazor framework loader (blazor.web.js)
// is same-origin and covered by 'self'. connect-src allows the SignalR circuit's ws:/wss:.
// style-src keeps 'unsafe-inline' for component scoped/inline styles and QR data: images.
// Verified: the theme script executes and the interactive circuit connects under this policy.
app.Use(async (context, next) =>
{
    var nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
    context.Items["csp-nonce"] = nonce;

    var headers = context.Response.Headers;
    headers["Content-Security-Policy"] =
        "default-src 'self'; " +
        $"script-src 'self' 'nonce-{nonce}'; " +
        "style-src 'self' 'unsafe-inline'; " +
        "img-src 'self' data:; " +
        "font-src 'self'; " +
        "connect-src 'self' ws: wss:; " +
        "base-uri 'self'; " +
        "form-action 'self'; " +
        "frame-ancestors 'none'; " +
        "object-src 'none'";
    headers["X-Content-Type-Options"] = "nosniff";
    headers["Referrer-Policy"] = "no-referrer";
    headers["X-Frame-Options"] = "DENY"; // belt-and-braces with frame-ancestors 'none'

    await next();
});

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

// Throttle the auth/step-up surface (policy defined above); health + static assets pass through.
app.UseRateLimiter();

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
                     || path.StartsWithSegments("/health")
                     || path.StartsWithSegments("/healthz")
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

// Health/readiness probes (NFR-AVL). Anonymous, exempt from the 2FA gate above, and not rate
// limited. /health = liveness (no checks run → 200 while the process is up); /healthz/ready =
// readiness (DB reachable). Bicep points siteConfig.healthCheckPath at /health.
app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = _ => false })
    .AllowAnonymous();
app.MapHealthChecks("/healthz/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
}).AllowAnonymous();

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
