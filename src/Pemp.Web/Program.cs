using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Identity.Web;
using Microsoft.Identity.Web.UI;
using Pemp.Infrastructure;
using Pemp.Infrastructure.Persistence;
using Pemp.Web.Components;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Entra SSO (SEC-IAM / FR-AUTH) — activated only when AzureAd:ClientId is configured
// (the cloud deployment, see docs/azure-entra-setup.md). Locally it's absent, so the
// app falls back to the in-app DemoSession role switcher — the demo runs with no Entra.
var entraConfigured = !string.IsNullOrWhiteSpace(builder.Configuration["AzureAd:ClientId"]);
if (entraConfigured)
{
    builder.Services.AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
        .AddMicrosoftIdentityWebApp(builder.Configuration.GetSection("AzureAd"));
    builder.Services.AddAuthorization();
    builder.Services.AddCascadingAuthenticationState();
    builder.Services.AddControllersWithViews().AddMicrosoftIdentityUI(); // sign-in/out endpoints
}

// Persistence: local demo uses SQLite; set "UseSqlite": false + a SqlServer
// connection string ("Pemp") to point at Azure SQL (§9 / task 10 setup guide).
var useSqlite = builder.Configuration.GetValue("UseSqlite", true);
var connectionString = builder.Configuration.GetConnectionString("Pemp")
                       ?? "Data Source=pemp-demo.db";
builder.Services.AddPempInfrastructure(connectionString, useSqlite);

// Demo session: in-app role switcher — the fallback used when Entra is not configured.
builder.Services.AddScoped<Pemp.Web.DemoSession>();

var app = builder.Build();

// Create the schema and seed demo data (real domain transitions → genuine audit chain).
using (var scope = app.Services.CreateScope())
{
    var sp = scope.ServiceProvider;
    var db = sp.GetRequiredService<PempDbContext>();
    db.Database.EnsureCreated();
    DemoSeeder.Seed(db, sp.GetRequiredService<Func<DateTimeOffset>>());
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

if (entraConfigured)
{
    app.UseAuthentication();
    app.UseAuthorization();
}

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

if (entraConfigured)
{
    app.MapControllers(); // MicrosoftIdentity/Account sign-in & sign-out
}

app.Run();
