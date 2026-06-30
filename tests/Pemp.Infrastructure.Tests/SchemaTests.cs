using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Pemp.Infrastructure.Persistence;
using Xunit;

namespace Pemp.Infrastructure.Tests;

/// <summary>
/// Model-validity invariants for the Azure SQL target (rank 8). The model is built against the
/// SqlServer provider (no connection needed — model building is offline) and asserted to be SQL
/// Server-safe: every persisted string is length-bounded (so SqlServer emits nvarchar(N), never
/// nvarchar(max) — keeping index keys &lt;=900 bytes), and no relationship cascade-deletes our
/// crown-jewel data or the tamper-evident audit chain.
/// </summary>
public sealed class SchemaTests
{
    // The PEMP-owned persisted entities (excludes ASP.NET Core Identity tables, which the framework
    // already length-bounds and which we do not configure).
    private static readonly HashSet<Type> PempEntities = new()
    {
        typeof(EngagementRecord), typeof(AuditEntryRow), typeof(FindingRecord),
        typeof(AssessmentAnswerRecord), typeof(AccessRequirementRecord), typeof(ChecklistTickRecord),
        typeof(EvidenceRecord), typeof(TestCredentialRecord), typeof(CommsMessageRecord),
    };

    private static IModel SqlServerModel()
    {
        var options = new DbContextOptionsBuilder<PempDbContext>()
            .UseSqlServer("Server=none;Database=none;Trusted_Connection=False;") // model-only, never opened
            .Options;
        using var db = new PempDbContext(options);
        return db.Model;
    }

    [Fact]
    public void Every_persisted_string_is_length_bounded_for_sqlserver()  // rank 8 (no nvarchar(max), index keys <=900B)
    {
        var model = SqlServerModel();
        var offenders = new List<string>();
        foreach (var entity in model.GetEntityTypes().Where(e => PempEntities.Contains(e.ClrType)))
            foreach (var prop in entity.GetProperties().Where(p => p.ClrType == typeof(string)))
                if (prop.GetMaxLength() is null)
                    offenders.Add($"{entity.ClrType.Name}.{prop.Name}");

        Assert.True(offenders.Count == 0,
            "These string columns have no max length (would be nvarchar(max) on SQL Server): " + string.Join(", ", offenders));
    }

    [Fact]
    public void No_relationship_cascade_deletes_engagements_findings_or_audit()  // rank 8 / SEC-AUD (never cascade)
    {
        var model = SqlServerModel();
        var cascading = new List<string>();
        foreach (var entity in model.GetEntityTypes().Where(e => PempEntities.Contains(e.ClrType)))
            foreach (var fk in entity.GetForeignKeys())
                if (fk.DeleteBehavior == DeleteBehavior.Cascade)
                    cascading.Add($"{entity.ClrType.Name} -> {fk.PrincipalEntityType.ClrType.Name}");

        Assert.True(cascading.Count == 0,
            "These relationships cascade-delete (must be Restrict so evidence/audit can't be silently destroyed): "
            + string.Join(", ", cascading));
    }

    [Fact]
    public void Engagement_parent_link_is_a_self_referencing_restrict_fk()  // rank 8 (FR-RET self-ref)
    {
        var model = SqlServerModel();
        var eng = model.FindEntityType(typeof(EngagementRecord))!;
        var selfRef = eng.GetForeignKeys().Single(fk => fk.PrincipalEntityType.ClrType == typeof(EngagementRecord));
        Assert.Equal(nameof(EngagementRecord.ParentId), selfRef.Properties.Single().Name);
        Assert.Equal(DeleteBehavior.Restrict, selfRef.DeleteBehavior);
    }
}
