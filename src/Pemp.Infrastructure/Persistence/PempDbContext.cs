using Microsoft.EntityFrameworkCore;

namespace Pemp.Infrastructure.Persistence;

public sealed class PempDbContext(DbContextOptions<PempDbContext> options) : DbContext(options)
{
    public DbSet<EngagementRecord> Engagements => Set<EngagementRecord>();
    public DbSet<AuditEntryRow> AuditEntries => Set<AuditEntryRow>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        var eng = b.Entity<EngagementRecord>();
        eng.HasKey(e => e.Id);
        eng.HasIndex(e => e.Reference).IsUnique();
        eng.Property(e => e.Reference).IsRequired();
        eng.Property(e => e.AppName).IsRequired();
        // store enums as readable strings (audit/portability)
        eng.Property(e => e.Type).HasConversion<string>();
        eng.Property(e => e.CurrentStage).HasConversion<string>();

        var aud = b.Entity<AuditEntryRow>();
        aud.HasKey(a => a.Sequence);
        aud.Property(a => a.Sequence).ValueGeneratedNever(); // sequence is chain-assigned, not DB identity
        aud.HasIndex(a => a.EngagementId);
    }
}
