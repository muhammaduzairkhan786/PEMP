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

        // ---- Engagement (the aggregate root) -------------------------------
        // Every persisted string is length-bounded so the Azure SQL provider emits nvarchar(N), not
        // nvarchar(max) — required so index keys stay <=900 bytes and the schema is valid on SQL Server
        // (rank 8). Relationships are declared explicitly with OnDelete(Restrict): an engagement (and its
        // crown-jewel children — findings, evidence, credentials, audit) is NEVER cascade-deleted, so an
        // accidental/forged delete cannot silently destroy evidence or the tamper-evident trail (SEC-AUD).
        var eng = b.Entity<EngagementRecord>();
        eng.HasKey(e => e.Id);
        eng.HasIndex(e => e.Reference).IsUnique();
        eng.Property(e => e.Reference).IsRequired().HasMaxLength(32);
        eng.Property(e => e.AppName).IsRequired().HasMaxLength(200);
        eng.Property(e => e.Criticality).HasMaxLength(20);
        eng.Property(e => e.AssignedTesterName).HasMaxLength(200);
        // store enums as readable strings (audit/portability)
        eng.Property(e => e.Type).HasConversion<string>().HasMaxLength(20);
        eng.Property(e => e.CurrentStage).HasConversion<string>().HasMaxLength(20);
        // Optimistic-concurrency token (rank 3): provider-agnostic — declared a concurrency token and
        // bumped by the store on each state-changing save, so it works on both SQLite and Azure SQL.
        eng.Property(e => e.RowVersion).IsConcurrencyToken();
        // Self-referencing parent → child link for retests (FR-RET-01). Optional, never cascades:
        // a retest child must outlive deletion attempts on its parent.
        eng.HasOne<EngagementRecord>().WithMany().HasForeignKey(e => e.ParentId).OnDelete(DeleteBehavior.Restrict);

        // ---- Append-only audit log -----------------------------------------
        var aud = b.Entity<AuditEntryRow>();
        aud.HasKey(a => a.Sequence);
        aud.Property(a => a.Sequence).ValueGeneratedOnAdd(); // DB assigns the position (IDENTITY) — no client PK race
        aud.HasIndex(a => a.EngagementId);
        aud.Property(a => a.Actor).HasMaxLength(200);
        aud.Property(a => a.Action).HasMaxLength(100);
        aud.Property(a => a.Before).HasMaxLength(1024);
        aud.Property(a => a.After).HasMaxLength(1024);
        aud.Property(a => a.Source).HasMaxLength(100);
        aud.Property(a => a.PrevHash).HasMaxLength(128);
        aud.Property(a => a.Hash).HasMaxLength(128);
        // The audit row references an engagement but is NEVER cascade-deleted with it (SEC-AUD-01):
        // the tamper-evident chain must survive even an engagement removal.
        aud.HasOne<EngagementRecord>().WithMany().HasForeignKey(a => a.EngagementId).OnDelete(DeleteBehavior.Restrict);

        // ---- Findings (live register, crown-jewel data) --------------------
        var fnd = b.Entity<FindingRecord>();
        fnd.HasKey(f => f.Id);
        fnd.HasIndex(f => f.EngagementId);
        fnd.Property(f => f.Severity).HasConversion<string>().HasMaxLength(20);
        fnd.Property(f => f.Status).HasConversion<string>().HasMaxLength(20);
        fnd.Property(f => f.Title).IsRequired().HasMaxLength(400);
        fnd.Property(f => f.Cvss).HasMaxLength(16);
        fnd.Property(f => f.CvssVector).HasMaxLength(128);
        fnd.Property(f => f.Asset).HasMaxLength(200);
        fnd.Property(f => f.Remediation).HasMaxLength(4000);
        fnd.HasOne<EngagementRecord>().WithMany().HasForeignKey(f => f.EngagementId).OnDelete(DeleteBehavior.Restrict);
        // Retest provenance: a child finding links back to the original it re-verifies (FR-RET-02).
        fnd.HasOne<FindingRecord>().WithMany().HasForeignKey(f => f.OriginalFindingId).OnDelete(DeleteBehavior.Restrict);

        var ans = b.Entity<AssessmentAnswerRecord>();
        ans.HasKey(a => a.Id);
        ans.HasIndex(a => new { a.EngagementId, a.QuestionId }).IsUnique();
        ans.Property(a => a.QuestionId).IsRequired().HasMaxLength(100);
        ans.Property(a => a.Value).HasMaxLength(4000);
        ans.HasOne<EngagementRecord>().WithMany().HasForeignKey(a => a.EngagementId).OnDelete(DeleteBehavior.Restrict);

        var acc = b.Entity<AccessRequirementRecord>();
        acc.HasKey(a => a.Id);
        acc.HasIndex(a => a.EngagementId);
        acc.Property(a => a.Status).HasConversion<string>().HasMaxLength(20);
        acc.Property(a => a.Environment).HasMaxLength(200);
        acc.Property(a => a.Url).HasMaxLength(1024);
        acc.Property(a => a.AccessType).HasMaxLength(50);
        acc.HasOne<EngagementRecord>().WithMany().HasForeignKey(a => a.EngagementId).OnDelete(DeleteBehavior.Restrict);

        var chk = b.Entity<ChecklistTickRecord>();
        chk.HasKey(c => c.Id);
        chk.HasIndex(c => new { c.EngagementId, c.Code }).IsUnique();
        chk.Property(c => c.Code).IsRequired().HasMaxLength(50);
        chk.HasOne<EngagementRecord>().WithMany().HasForeignKey(c => c.EngagementId).OnDelete(DeleteBehavior.Restrict);

        var ev = b.Entity<EvidenceRecord>();
        ev.HasKey(e => e.Id);
        ev.HasIndex(e => e.EngagementId);
        ev.HasIndex(e => e.FindingId);
        ev.Property(e => e.Kind).HasConversion<string>().HasMaxLength(20);
        ev.Property(e => e.FileName).IsRequired().HasMaxLength(400);
        ev.Property(e => e.Note).HasMaxLength(2000);
        ev.HasOne<EngagementRecord>().WithMany().HasForeignKey(e => e.EngagementId).OnDelete(DeleteBehavior.Restrict);
        ev.HasOne<FindingRecord>().WithMany().HasForeignKey(e => e.FindingId).OnDelete(DeleteBehavior.Restrict);

        // ---- Test credentials (vault-backed in prod, SEC-CRD) --------------
        var cred = b.Entity<TestCredentialRecord>();
        cred.HasKey(c => c.Id);
        cred.HasIndex(c => c.EngagementId);
        cred.Property(c => c.Label).HasMaxLength(200);
        cred.Property(c => c.Username).HasMaxLength(256);
        cred.Property(c => c.Secret).HasMaxLength(1024);
        cred.Property(c => c.AddedBy).HasMaxLength(200);
        cred.Property(c => c.KeyVaultSecretUri).HasMaxLength(1024);
        cred.HasOne<EngagementRecord>().WithMany().HasForeignKey(c => c.EngagementId).OnDelete(DeleteBehavior.Restrict);
    }
}
