using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pemp.Domain;
using Pemp.Domain.Audit;
using Pemp.Infrastructure.Assessment;
using Pemp.Infrastructure.Persistence;
using Xunit;

namespace Pemp.Infrastructure.Tests;

/// <summary>
/// Tests for the .docx assessment importer (FR-SCO-01): label→question mapping (exact + fuzzy), junk
/// rejection, the blank-template / sample round-trip, upload validation, and that an import persists
/// through the scoped + hash-chain-audited store path without ever logging a secret/value or the bytes.
/// </summary>
public sealed class AssessmentDocImporterTests
{
    private static readonly DocxAssessmentImporter Importer = new();

    // Build a 2-column-table .docx in memory from (label, value) rows — the structure the parser reads.
    private static byte[] DocFromRows(params (string Label, string Value)[] rows)
    {
        using var ms = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            var table = new Table();
            foreach (var (label, value) in rows)
            {
                var row = new TableRow();
                row.Append(new TableCell(new Paragraph(new Run(new Text(label)))));
                row.Append(new TableCell(new Paragraph(new Run(new Text(value)))));
                table.Append(row);
            }
            main.Document = new Document(new Body(table));
            main.Document.Save();
        }
        return ms.ToArray();
    }

    private static byte[] DocFromParagraphs(params string[] paragraphs)
    {
        using var ms = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            var body = new Body();
            foreach (var p in paragraphs)
                body.Append(new Paragraph(new Run(new Text(p) { Space = SpaceProcessingModeValues.Preserve })));
            main.Document = new Document(body);
            main.Document.Save();
        }
        return ms.ToArray();
    }

    private static AssessmentImportResult Parse(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes);
        return Importer.Parse(ms);
    }

    [Fact]
    public void Maps_exact_template_labels_to_question_ids()
    {
        var bytes = DocFromRows(
            ("Application name", "Retail Web"),
            ("Contact name", "P. Devlin"),
            ("System is live in production and contains production data?", "Yes"),
            ("Data confidentiality", "Confidential"));

        var result = Parse(bytes);

        Assert.Equal("Retail Web", result.Matched.Single(m => m.QuestionId == "APPNAME").Value);
        Assert.Equal("P. Devlin", result.Matched.Single(m => m.QuestionId == "CONTACT").Value);
        Assert.Equal("Yes", result.Matched.Single(m => m.QuestionId == "Q11").Value);   // YesNo normalised
        Assert.Equal("Confidential", result.Matched.Single(m => m.QuestionId == "Q12").Value); // radio → option
        Assert.All(result.Matched, m => Assert.Equal(1.0, m.Confidence)); // exact-label matches
    }

    [Fact]
    public void Fuzzy_matches_a_slightly_different_label()
    {
        // A business doc that phrases the label differently than the template ("App name" / a trimmed
        // description label) still maps to the right question via contains / token-overlap.
        var bytes = DocFromRows(
            ("Name of the application", "Quote Engine"),
            ("Application description", "Internal quoting service"));

        var result = Parse(bytes);

        var appName = result.Matched.Single(m => m.QuestionId == "APPNAME");
        Assert.Equal("Quote Engine", appName.Value);
        Assert.True(appName.Confidence < 1.0);   // matched fuzzily, not exactly
        Assert.True(appName.Confidence >= 0.5);
        Assert.Equal("Internal quoting service", result.Matched.Single(m => m.QuestionId == "Q2").Value);
    }

    [Fact]
    public void Ignores_junk_and_unmatched_lines()
    {
        var bytes = DocFromRows(
            ("Application name", "Retail Web"),
            ("Quarterly revenue forecast", "£4m"),          // nothing in the template matches
            ("Favourite colour", "blue"));

        var result = Parse(bytes);

        Assert.Single(result.Matched);
        Assert.Equal("APPNAME", result.Matched[0].QuestionId);
        Assert.Contains(result.UnmatchedLines, l => l.Label == "Quarterly revenue forecast");
        Assert.Contains(result.UnmatchedLines, l => l.Label == "Favourite colour");
        Assert.Equal(result.TotalQuestions - 1, result.UnansweredQuestionIds.Count);
    }

    [Fact]
    public void Skips_blank_and_placeholder_values()
    {
        var bytes = DocFromRows(
            ("Application name", "Retail Web"),
            ("Contact name", "<To be filled by business>"), // workbook placeholder → skipped
            ("Project manager", ""));                        // blank → skipped

        var result = Parse(bytes);

        Assert.Contains(result.Matched, m => m.QuestionId == "APPNAME");
        Assert.DoesNotContain(result.Matched, m => m.QuestionId == "CONTACT");
        Assert.DoesNotContain(result.Matched, m => m.QuestionId == "PM");
    }

    [Fact]
    public void Reads_label_colon_value_paragraphs()
    {
        var bytes = DocFromParagraphs(
            "Application name: Mobile App",
            "This is just a sentence with no colon-delimited field");

        var result = Parse(bytes);

        Assert.Equal("Mobile App", result.Matched.Single(m => m.QuestionId == "APPNAME").Value);
    }

    [Fact]
    public void Maps_checkbox_value_to_nearest_options()
    {
        var bytes = DocFromRows(
            ("Application type", "Web application, API"),
            ("Network exposure", "External (Internet exposure)"));

        var result = Parse(bytes);

        var type = result.Matched.Single(m => m.QuestionId == "Q10").Value.Split('|');
        Assert.Contains("Web application", type);
        Assert.Contains("API", type);
        Assert.Contains("External (Internet exposure)", result.Matched.Single(m => m.QuestionId == "Q3").Value);
    }

    [Fact]
    public void Blank_template_round_trips_to_no_answers()
    {
        var result = Parse(Importer.BuildBlankTemplate());
        Assert.Empty(result.Matched);                       // an empty template fills nothing
        Assert.Equal(result.TotalQuestions, result.UnansweredQuestionIds.Count);
    }

    [Fact]
    public void Filled_document_round_trips_through_the_writer_and_parser()
    {
        var answers = new Dictionary<string, string>
        {
            ["APPNAME"] = "Retail Web",
            ["CONTACT"] = "P. Devlin",
            ["Q11"] = "Yes",
            ["Q12"] = "Confidential",
            ["Q10"] = "Web application|API",   // checkbox multi-value
            ["Q3"] = "External (Internet exposure)",
        };

        var result = Parse(Importer.BuildFilled(answers));

        Assert.Equal("Retail Web", result.Matched.Single(m => m.QuestionId == "APPNAME").Value);
        Assert.Equal("P. Devlin", result.Matched.Single(m => m.QuestionId == "CONTACT").Value);
        Assert.Equal("Yes", result.Matched.Single(m => m.QuestionId == "Q11").Value);
        Assert.Equal("Confidential", result.Matched.Single(m => m.QuestionId == "Q12").Value);
        var q10 = result.Matched.Single(m => m.QuestionId == "Q10").Value.Split('|');
        Assert.Contains("Web application", q10);
        Assert.Contains("API", q10);
        Assert.Equal("External (Internet exposure)", result.Matched.Single(m => m.QuestionId == "Q3").Value);
    }

    [Fact]
    public void Committed_sample_docx_round_trips_to_expected_answers()
    {
        // The committed sample under docs/samples — the user can upload it immediately and see fields fill.
        var path = SampleDocxPath();
        Assert.True(File.Exists(path), $"Sample assessment docx not found at {path}");

        var result = Parse(File.ReadAllBytes(path));

        Assert.NotEmpty(result.Matched);
        // A half-filled doc: some matched, some still to do.
        Assert.NotEmpty(result.UnansweredQuestionIds);
        Assert.Contains(result.Matched, m => m.QuestionId == "APPNAME");
    }

    [Fact]
    public void Corrupt_non_docx_input_throws_so_the_ui_can_handle_it_gracefully()
    {
        using var garbage = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("this is not a zip / docx"));
        Assert.ThrowsAny<Exception>(() => Importer.Parse(garbage));
    }

    [Theory]
    [InlineData("filled.docx", AssessmentUpload.DocxContentType, 1024, true)]
    [InlineData("filled.DOCX", null, 1024, true)]                       // case-insensitive ext, advisory type
    [InlineData("notes.txt", "text/plain", 1024, false)]               // wrong extension
    [InlineData("legacy.doc", "application/msword", 1024, false)]      // legacy .doc rejected
    [InlineData("filled.docx", AssessmentUpload.DocxContentType, 6 * 1024 * 1024, false)] // oversize
    [InlineData("filled.docx", AssessmentUpload.DocxContentType, 0, false)]               // empty
    public void Upload_validation_accepts_only_a_reasonable_docx(
        string name, string? contentType, long size, bool expectOk)
    {
        var error = AssessmentUpload.Validate(name, contentType, size);
        Assert.Equal(expectOk, error is null);
    }

    [Fact]
    public async Task Import_persists_answers_through_the_scoped_and_audited_store_path()
    {
        // Build a small in-memory store over the seeded demo (mirrors PersistenceTests setup).
        using var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        var options = new DbContextOptionsBuilder<PempDbContext>().UseSqlite(conn).Options;
        using var db = new PempDbContext(options);
        db.Database.EnsureCreated();
        Func<DateTimeOffset> clock = () => DateTimeOffset.UnixEpoch;
        DemoSeeder.Seed(db, clock, HashChain.DefaultKey);
        var store = new EngagementStore(db, clock, new AuditHmacKey(HashChain.DefaultKey));

        // Mobile App is at Scoping, assigned A. Khan, with no seeded answers.
        var id = db.Engagements.Single(e => e.Reference == "ENG-2026-0421").Id;
        var khan = CallerContext.ForTester(DemoSeeder.TesterKhan, "A. Khan");

        const string secretLike = "S3cr3t-test-account-value";
        var result = Parse(DocFromRows(
            ("Application name", "Mobile App"),
            ("Sample test data / test email to verify functionality", secretLike)));
        Assert.NotEmpty(result.Matched);
        var auditBefore = db.AuditEntries.Count();

        var written = await store.ImportAssessmentAsync(id, result.Matched, "vendor-form.docx", khan);

        // Every matched answer is persisted and readable back.
        Assert.Equal(result.Matched.Count, written);
        var saved = await store.AssessmentAnswersAsync(id);
        Assert.Equal("Mobile App", saved["APPNAME"]);

        // One audit entry per saved answer PLUS the import summary; the chain still verifies.
        Assert.Equal(auditBefore + written + 1, db.AuditEntries.Count());
        var summary = db.AuditEntries.OrderByDescending(a => a.Sequence).First();
        Assert.Equal("Assessment.Imported", summary.Action);
        Assert.Contains("vendor-form.docx", summary.After);
        Assert.True(new EfAuditChain(db, HashChain.DefaultKey).Verify());

        // The audit log records WHICH question changed, never the answer VALUE (input may be sensitive).
        Assert.DoesNotContain(db.AuditEntries, a => a.After.Contains(secretLike) || a.Before.Contains(secretLike));
        Assert.DoesNotContain(db.AuditEntries, a => a.After.Contains("Mobile App") && a.Action == "Assessment.AnswerSaved");
    }

    private static string SampleDocxPath()
    {
        // Walk up from the test bin dir to the repo root, then docs/samples.
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "PEMP.sln")))
            dir = Path.GetDirectoryName(dir);
        return Path.Combine(dir ?? ".", "docs", "samples", "assessment-sample-halffilled.docx");
    }
}
