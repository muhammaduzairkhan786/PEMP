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
    public static IServiceCollection AddPempInfrastructure(this IServiceCollection services, string connectionString, bool useSqlite)
    {
        services.AddDbContext<PempDbContext>(options =>
        {
            if (useSqlite) options.UseSqlite(connectionString);
            else options.UseSqlServer(connectionString);
        });
        services.AddSingleton<Func<DateTimeOffset>>(_ => () => DateTimeOffset.UtcNow);
        services.AddScoped<EngagementStore>();
        return services;
    }
}
