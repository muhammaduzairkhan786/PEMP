using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Pemp.Infrastructure.Persistence;

public sealed class PempDbContext(DbContextOptions<PempDbContext> options) : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<EngagementRecord> Engagements => Set<EngagementRecord>();
    public DbSet<AuditEntryRow> AuditEntries => Set<AuditEntryRow>();
    public DbSet<FindingRecord> Findings => Set<FindingRecord>();
    public DbSet<AssessmentAnswerRecord> AssessmentAnswers => Set<AssessmentAnswerRecord>();
    public DbSet<AccessRequirementRecord> AccessRequirements => Set<AccessRequirementRecord>();
    public DbSet<ChecklistTickRecord> ChecklistTicks => Set<ChecklistTickRecord>();
    public DbSet<EvidenceRecord> Evidence => Set<EvidenceRecord>();
    public DbSet<TestCredentialRecord> TestCredentials => Set<TestCredentialRecord>();

    // Append-only enforcement for the audit log at the data layer (SEC-AUD-01). Registered here
    // (rather than only in DI) so EVERY context — app, seeder, and tests — gets it, regardless of
    // how its options were built.
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);
        optionsBuilder.AddInterceptors(new AuditAppendOnlyInterceptor());
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b); // Identity tables (AspNetUsers, etc.)

        var eng = b.Entity<EngagementRecord>();
        eng.HasKey(e => e.Id);
        eng.HasIndex(e => e.Reference).IsUnique();
        eng.Property(e => e.Reference).IsRequired();
        eng.Property(e => e.AppName).IsRequired();
        // store enums as readable strings (audit/portability)
        eng.Property(e => e.Type).HasConversion<string>();
        eng.Property(e => e.CurrentStage).HasConversion<string>();
        // Optimistic-concurrency token (rank 3): provider-agnostic — declared a concurrency token and
        // bumped by the store on each state-changing save, so it works on both SQLite and Azure SQL.
        eng.Property(e => e.RowVersion).IsConcurrencyToken();

        var aud = b.Entity<AuditEntryRow>();
        aud.HasKey(a => a.Sequence);
        aud.Property(a => a.Sequence).ValueGeneratedOnAdd(); // DB assigns the position (IDENTITY) — no client PK race
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

        var cred = b.Entity<TestCredentialRecord>();
        cred.HasKey(c => c.Id);
        cred.HasIndex(c => c.EngagementId);
    }
}
