using Microsoft.EntityFrameworkCore;

namespace Pemp.Infrastructure.Persistence;

public sealed class PempDbContext(DbContextOptions<PempDbContext> options) : DbContext(options)
{
    public DbSet<EngagementRecord> Engagements => Set<EngagementRecord>();
    public DbSet<AuditEntryRow> AuditEntries => Set<AuditEntryRow>();
    public DbSet<FindingRecord> Findings => Set<FindingRecord>();
    public DbSet<AssessmentAnswerRecord> AssessmentAnswers => Set<AssessmentAnswerRecord>();
    public DbSet<AccessRequirementRecord> AccessRequirements => Set<AccessRequirementRecord>();
    public DbSet<ChecklistTickRecord> ChecklistTicks => Set<ChecklistTickRecord>();
    public DbSet<EvidenceRecord> Evidence => Set<EvidenceRecord>();

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

        var fnd = b.Entity<FindingRecord>();
        fnd.HasKey(f => f.Id);
        fnd.HasIndex(f => f.EngagementId);
        fnd.Property(f => f.Severity).HasConversion<string>();
        fnd.Property(f => f.Status).HasConversion<string>();

        var ans = b.Entity<AssessmentAnswerRecord>();
        ans.HasKey(a => a.Id);
        ans.HasIndex(a => new { a.EngagementId, a.QuestionId }).IsUnique();

        var acc = b.Entity<AccessRequirementRecord>();
        acc.HasKey(a => a.Id);
        acc.HasIndex(a => a.EngagementId);
        acc.Property(a => a.Status).HasConversion<string>();

        var chk = b.Entity<ChecklistTickRecord>();
        chk.HasKey(c => c.Id);
        chk.HasIndex(c => new { c.EngagementId, c.Code }).IsUnique();

        var ev = b.Entity<EvidenceRecord>();
        ev.HasKey(e => e.Id);
        ev.HasIndex(e => e.EngagementId);
        ev.HasIndex(e => e.FindingId);
        ev.Property(e => e.Kind).HasConversion<string>();
    }
}
