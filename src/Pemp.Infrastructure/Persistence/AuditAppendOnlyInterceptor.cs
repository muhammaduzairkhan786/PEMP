using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Pemp.Infrastructure.Persistence;

/// <summary>
/// Enforces the append-only audit guarantee at the data layer (SEC-AUD-01): any attempt to
/// UPDATE or DELETE an <see cref="AuditEntryRow"/> through EF Core is rejected before it
/// reaches the database — for application AND administrator code paths alike. New rows
/// (Added) are the only legal change. Combined with the HMAC-keyed chain, this means the
/// audit log can be neither rewritten via the app nor silently forged via direct edits.
/// </summary>
public sealed class AuditAppendOnlyInterceptor : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData, InterceptionResult<int> result)
    {
        Guard(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Guard(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private static void Guard(DbContext? context)
    {
        if (context is null) return;
        foreach (var entry in context.ChangeTracker.Entries<AuditEntryRow>())
        {
            if (entry.State is EntityState.Modified or EntityState.Deleted)
            {
                throw new InvalidOperationException(
                    "Audit log is append-only and tamper-evident (SEC-AUD-01): " +
                    "existing audit entries cannot be modified or deleted.");
            }
        }
    }
}
