using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pemp.Infrastructure.Persistence;

namespace Pemp.Infrastructure;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Register persistence. <paramref name="useSqlite"/> = local demo (SQLite file);
    /// otherwise Azure SQL via the SqlServer provider (production, SEC-DAT/§9).
    /// </summary>
    public static IServiceCollection AddPempInfrastructure(
        this IServiceCollection services, string connectionString, bool useSqlite,
        string? auditHmacKey = null, bool allowDefaultAuditKey = true)
    {
        services.AddDbContext<PempDbContext>(options =>
        {
            if (useSqlite) options.UseSqlite(connectionString);
            else options.UseSqlServer(connectionString);
        });
        services.AddSingleton<Func<DateTimeOffset>>(_ => () => DateTimeOffset.UtcNow);
        // Audit hash-chain HMAC key (SEC-AUD-01). PROD supplies this from Azure Key Vault; dev/local
        // reads Audit:HmacKey from config. FromConfig FAILS CLOSED on real (non-SQLite, non-Dev)
        // deployments when the key is missing or resolves to the public default — see AuditHmacKey.
        services.AddSingleton(AuditHmacKey.FromConfig(auditHmacKey, allowDefaultAuditKey));
        services.AddScoped<EngagementStore>();
        return services;
    }
}
