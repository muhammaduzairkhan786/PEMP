using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pemp.Domain;
using Pemp.Domain.Audit;
using Pemp.Infrastructure.Assessment;
using Pemp.Infrastructure.Persistence;
using Xunit;

namespace Pemp.Infrastructure.Tests;

/// <summary>
/// Tests for the .xlsx assessment importer (FR-SCO-01). The Excel reader feeds the SAME shared
/// <see cref="AssessmentMapper"/> as the .docx reader, so these focus on the xlsx-specific concerns:
/// shared-string resolution, the 2-column layout, an optional header row, blank cells, the committed
/// sample round-trip, upload validation, and that an xlsx import persists through the scoped +
/// hash-chain-audited store path without ever logging a value or the bytes.
/// </summary>
public sealed class AssessmentXlsxImporterTests
{
    private static readonly XlsxAssessmentImporter Importer = new();

    // Build a 2-column .xlsx in memory from (label, value) rows, stored via the SHARED-STRING table (the
    // path the reader must resolve). A null/empty cell is omitted (so we test blank-cell handling). When
    // <paramref name="includeHeader"/> a "Question / Answer" header row is written first.
    private static byte[] XlsxFromRows(bool includeHeader, params (string? Label, string? Value)[] rows)
    {
        using var ms = new MemoryStream();
        using (var doc = SpreadsheetDocument.Create(ms, SpreadsheetDocumentType.Workbook))
        {
            var wbPart = doc.AddWorkbookPart();
            wbPart.Workbook = new Workbook();
            var wsPart = wbPart.AddNewPart<WorksheetPart>();
            var sheetData = new SheetData();
            wsPart.Worksheet = new Worksheet(sheetData);

            var sstPart = wbPart.AddNewPart<SharedStringTablePart>();
            sstPart.SharedStringTable = new SharedStringTable();
            var sst = sstPart.SharedStringTable;
            var map = new Dictionary<string, int>(StringComparer.Ordinal);

            int Intern(string t)
            {
                if (map.TryGetValue(t, out var i)) return i;
                sst.AppendChild(new SharedStringItem(new Text(t) { Space = SpaceProcessingModeValues.Preserve }));
                var idx = map.Count;
                map[t] = idx;
                return idx;
            }

            uint r = 1;
            Cell SCell(string reference, string t) => new()
            {
                CellReference = reference,
                DataType = CellValues.SharedString,
                CellValue = new CellValue(Intern(t).ToString()),
            };
            void Add(string? a, string? b)
            {
                var row = new Row { RowIndex = r };
                if (!string.IsNullOrEmpty(a)) row.Append(SCell($"A{r}", a));
                if (!string.IsNullOrEmpty(b)) row.Append(SCell($"B{r}", b));
                sheetData.Append(row);
                r++;
            }

            if (includeHeader) Add("Question", "Answer");
            foreach (var (label, value) in rows) Add(label, value);

            var sheets = wbPart.Workbook.AppendChild(new Sheets());
            sheets.Append(new Sheet { Id = wbPart.GetIdOfPart(wsPart), SheetId = 1U, Name = "Assessment" });
            wsPart.Worksheet.Save();
            wbPart.Workbook.Save();
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
        var bytes = XlsxFromRows(includeHeader: true,
            ("Application name", "Retail Web"),
            ("Contact name", "P. Devlin"),
            ("System is live in production and contains production data?", "Yes"),
            ("Data confidentiality", "Confidential"));

        var result = Parse(bytes);

        Assert.Equal("Retail Web", result.Matched.Single(m => m.QuestionId == "APPNAME").Value);
        Assert.Equal("P. Devlin", result.Matched.Single(m => m.QuestionId == "CONTACT").Value);
        Assert.Equal("Yes", result.Matched.Single(m => m.QuestionId == "Q11").Value);    // YesNo normalised
        Assert.Equal("Confidential", result.Matched.Single(m => m.QuestionId == "Q12").Value); // radio → option
        Assert.All(result.Matched, m => Assert.Equal(1.0, m.Confidence)); // exact-label matches
    }

    [Fact]
    public void Fuzzy_matches_a_slightly_different_label()
    {
        var bytes = XlsxFromRows(includeHeader: true,
            ("Name of the application", "Quote Engine"),
            ("Application description", "Internal quoting service"));

        var result = Parse(bytes);

        var appName = result.Matched.Single(m => m.QuestionId == "APPNAME");
        Assert.Equal("Quote Engine", appName.Value);
        Assert.True(appName.Confidence is < 1.0 and >= 0.5); // matched fuzzily, not exactly
        Assert.Equal("Internal quoting service", result.Matched.Single(m => m.QuestionId == "Q2").Value);
    }

    [Fact]
    public void Maps_checkbox_value_to_nearest_options()
    {
        var bytes = XlsxFromRows(includeHeader: true,
            ("Application type", "Web application, API"),
            ("Network exposure", "External (Internet exposure)"));

        var result = Parse(bytes);

        var type = result.Matched.Single(m => m.QuestionId == "Q10").Value.Split('|');
        Assert.Contains("Web application", type);
        Assert.Contains("API", type);
        Assert.Contains("External (Internet exposure)", result.Matched.Single(m => m.QuestionId == "Q3").Value);
    }

    [Fact]
    public void Header_row_is_skipped_but_real_first_row_is_not()
    {
        // With a header: the "Question/Answer" row is dropped, the data still maps.
        var withHeader = Parse(XlsxFromRows(includeHeader: true, ("Application name", "Retail Web")));
        Assert.Equal("Retail Web", withHeader.Matched.Single(m => m.QuestionId == "APPNAME").Value);
        Assert.DoesNotContain(withHeader.UnmatchedLines, l => l.Label == "Question"); // header consumed, not surfaced

        // Without a header row, the first row is genuine data and must NOT be dropped.
        var noHeader = Parse(XlsxFromRows(includeHeader: false, ("Application name", "Retail Web")));
        Assert.Equal("Retail Web", noHeader.Matched.Single(m => m.QuestionId == "APPNAME").Value);
    }

    [Fact]
    public void Blank_cells_are_ignored()
    {
        var bytes = XlsxFromRows(includeHeader: true,
            ("Application name", "Retail Web"),
            ("Contact name", null),          // blank value cell → CONTACT left unfilled
            (null, "orphan value"));          // blank label cell → whole row ignored

        var result = Parse(bytes);

        Assert.Contains(result.Matched, m => m.QuestionId == "APPNAME");
        Assert.DoesNotContain(result.Matched, m => m.QuestionId == "CONTACT");
        Assert.DoesNotContain(result.Matched, m => m.Value == "orphan value");
        Assert.DoesNotContain(result.UnmatchedLines, l => l.Value == "orphan value");
    }

    [Fact]
    public void Blank_template_round_trips_to_no_answers()
    {
        var result = Parse(Importer.BuildBlankTemplate());
        Assert.Empty(result.Matched);                       // an empty template fills nothing
        Assert.Equal(result.TotalQuestions, result.UnansweredQuestionIds.Count);
    }

    [Fact]
    public void Filled_workbook_round_trips_through_the_writer_and_parser()
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
    public void Committed_sample_xlsx_round_trips_to_expected_answers()
    {
        var path = SampleXlsxPath();
        Assert.True(File.Exists(path), $"Sample assessment xlsx not found at {path}");

        var result = Parse(File.ReadAllBytes(path));

        Assert.NotEmpty(result.Matched);
        Assert.NotEmpty(result.UnansweredQuestionIds); // half-filled: some matched, some still to do
        Assert.Equal("Retail Web", result.Matched.Single(m => m.QuestionId == "APPNAME").Value);
        Assert.Equal("Confidential", result.Matched.Single(m => m.QuestionId == "Q12").Value);
    }

    [Fact]
    public void Committed_sample_xlsx_and_docx_yield_identical_answers()
    {
        // The two sample files must auto-fill the form the same way (FR-SCO-01 parity).
        var xlsx = Parse(File.ReadAllBytes(SampleXlsxPath()));
        using var ms = new MemoryStream(File.ReadAllBytes(SampleDocxPath()));
        var docx = new DocxAssessmentImporter().Parse(ms);

        static string Key(AssessmentImportResult r) =>
            string.Join("\n", r.Matched.OrderBy(m => m.QuestionId).Select(m => $"{m.QuestionId}={m.Value}"));

        Assert.Equal(Key(docx), Key(xlsx));
    }

    [Fact]
    public void Corrupt_non_xlsx_input_throws_so_the_ui_can_handle_it_gracefully()
    {
        using var garbage = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("this is not a zip / xlsx"));
        Assert.ThrowsAny<Exception>(() => Importer.Parse(garbage));
    }

    [Theory]
    [InlineData("filled.xlsx", AssessmentUpload.XlsxContentType, 1024, true)]
    [InlineData("filled.XLSX", null, 1024, true)]                               // case-insensitive ext, advisory type
    [InlineData("filled.xlsx", "application/octet-stream", 1024, true)]         // generic type tolerated
    [InlineData("filled.xlsx", AssessmentUpload.DocxContentType, 1024, false)]  // mismatched Office type announced
    [InlineData("book.xls", "application/vnd.ms-excel", 1024, false)]           // legacy .xls rejected
    [InlineData("notes.txt", "text/plain", 1024, false)]                        // wrong extension
    [InlineData("filled.xlsx", AssessmentUpload.XlsxContentType, 6 * 1024 * 1024, false)] // oversize
    [InlineData("filled.xlsx", AssessmentUpload.XlsxContentType, 0, false)]               // empty
    public void Upload_validation_accepts_only_a_reasonable_xlsx(
        string name, string? contentType, long size, bool expectOk)
    {
        var error = AssessmentUpload.Validate(name, contentType, size);
        Assert.Equal(expectOk, error is null);
    }

    [Fact]
    public async Task Xlsx_import_persists_answers_through_the_scoped_and_audited_store_path()
    {
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
        var result = Parse(XlsxFromRows(includeHeader: true,
            ("Application name", "Mobile App"),
            ("Sample test data / test email to verify functionality", secretLike)));
        Assert.NotEmpty(result.Matched);
        var auditBefore = db.AuditEntries.Count();

        var written = await store.ImportAssessmentAsync(id, result.Matched, "vendor-form.xlsx", khan);

        // Every matched answer is persisted and readable back.
        Assert.Equal(result.Matched.Count, written);
        var saved = await store.AssessmentAnswersAsync(id);
        Assert.Equal("Mobile App", saved["APPNAME"]);

        // One audit entry per saved answer PLUS the import summary; the chain still verifies.
        Assert.Equal(auditBefore + written + 1, db.AuditEntries.Count());
        var summary = db.AuditEntries.OrderByDescending(a => a.Sequence).First();
        Assert.Equal("Assessment.Imported", summary.Action);
        Assert.Contains("vendor-form.xlsx", summary.After);
        Assert.True(new EfAuditChain(db, HashChain.DefaultKey).Verify());

        // The audit log records WHICH question changed, never the answer VALUE (input may be sensitive).
        Assert.DoesNotContain(db.AuditEntries, a => a.After.Contains(secretLike) || a.Before.Contains(secretLike));
        Assert.DoesNotContain(db.AuditEntries, a => a.After.Contains("Mobile App") && a.Action == "Assessment.AnswerSaved");
    }

    private static string SampleXlsxPath() => SamplePath("assessment-sample-halffilled.xlsx");
    private static string SampleDocxPath() => SamplePath("assessment-sample-halffilled.docx");

    private static string SamplePath(string fileName)
    {
        // Walk up from the test bin dir to the repo root, then docs/samples.
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "PEMP.sln")))
            dir = Path.GetDirectoryName(dir);
        return Path.Combine(dir ?? ".", "docs", "samples", fileName);
    }
}
