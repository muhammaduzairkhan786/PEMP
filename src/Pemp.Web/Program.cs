using Pemp.Infrastructure;
using Pemp.Infrastructure.Persistence;
using Pemp.Web.Components;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Persistence: local demo uses SQLite; set "UseSqlite": false + a SqlServer
// connection string ("Pemp") to point at Azure SQL (§9 / task 10 setup guide).
var useSqlite = builder.Configuration.GetValue("UseSqlite", true);
var connectionString = builder.Configuration.GetConnectionString("Pemp")
                       ?? "Data Source=pemp-demo.db";
builder.Services.AddPempInfrastructure(connectionString, useSqlite);

// Demo session: in-app role switcher standing in for Entra sign-in (replaced in task 9).
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

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
