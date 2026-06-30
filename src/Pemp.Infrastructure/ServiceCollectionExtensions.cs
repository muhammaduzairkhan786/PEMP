using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pemp.Infrastructure.Assessment;
using Pemp.Infrastructure.Evidence;
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
        services.AddScoped<CommsStore>();
        // Evidence access (SEC-EVD-02/03): local dev streams a placeholder via short-lived, single-use,
        // HMAC-signed tokens (singleton so the pending-token map is shared). PROD swaps this for
        // AzureBlobEvidenceStorage (Blob user-delegation SAS) — see that class's documented seam.
        services.AddSingleton<IEvidenceStorage, LocalEvidenceStorage>();
        // Assessment import (FR-SCO-01): stateless OpenXML parsers/writers — safe to share. Word .docx and
        // Excel .xlsx readers both feed the shared AssessmentMapper, so the matching/result are identical.
        services.AddSingleton<IAssessmentDocImporter, DocxAssessmentImporter>();
        services.AddSingleton<IAssessmentXlsxImporter, XlsxAssessmentImporter>();
        return services;
    }
}
