using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Pemp.Infrastructure.Assessment;

/// <summary>One template question matched from an uploaded document (FR-SCO-01).</summary>
/// <param name="QuestionId">The <see cref="AssessQuestion.Id"/> this doc line was mapped to.</param>
/// <param name="QuestionLabel">The template's human label (for the review UI).</param>
/// <param name="Value">The value to persist (choice values are normalised to template options).</param>
/// <param name="SourceLabel">The raw label the value came from in the document.</param>
/// <param name="Confidence">0..1 match confidence (1 = exact id/label, lower = fuzzy).</param>
public sealed record MatchedAnswer(
    string QuestionId, string QuestionLabel, string Value, string SourceLabel, double Confidence);

/// <summary>A raw label→value pair extracted from the document that matched no template question.</summary>
public sealed record DocLine(string Label, string Value);

/// <summary>
/// The structured outcome of parsing an uploaded assessment document (FR-SCO-01): the questions we
/// could confidently fill, the doc lines we could not place, and the template questions left unanswered.
/// The UI uses this to auto-fill the form and tell the reviewer "imported X of N; review these".
/// </summary>
public sealed record AssessmentImportResult(
    IReadOnlyList<MatchedAnswer> Matched,
    IReadOnlyList<DocLine> UnmatchedLines,
    IReadOnlyList<string> UnansweredQuestionIds,
    int TotalQuestions);

/// <summary>
/// Parses a half-filled Word (.docx) assessment into template answers, and generates the structured
/// blank/sample documents for the round-trip. A .docx is treated strictly as DATA — a zip of XML read
/// through OpenXML; nothing is executed and macros are ignored. All extracted text is untrusted: it is
/// only ever surfaced through Razor (HTML-encoded by default), never logged, and never interpreted.
/// </summary>
public interface IAssessmentDocImporter
{
    /// <summary>Extract candidate answers from a .docx stream and map them to template question ids.</summary>
    AssessmentImportResult Parse(Stream docxStream);

    /// <summary>A blank, structured .docx (2-column label/answer tables) for business teams to fill.</summary>
    byte[] BuildBlankTemplate();

    /// <summary>The same structured .docx pre-filled from <paramref name="answers"/> (questionId → value).</summary>
    byte[] BuildFilled(IReadOnlyDictionary<string, string> answers);
}

/// <summary>
/// OpenXML implementation of <see cref="IAssessmentDocImporter"/>. Stateless and thread-safe (registered
/// as a singleton). Extraction reads (a) "Label: value" paragraphs and (b) 2-column table rows, then hands
/// the raw label→value pairs to the shared <see cref="AssessmentMapper"/> — so docx and xlsx imports share
/// one matching algorithm (exact-id → exact-label → contains → token-overlap fuzzy, each template question
/// consumed at most once) and one choice-value mapping. This class owns only the .docx reading/writing.
/// </summary>
public sealed class DocxAssessmentImporter : IAssessmentDocImporter
{
    public AssessmentImportResult Parse(Stream docxStream) =>
        AssessmentMapper.MapLines(ExtractLines(docxStream));

    // ---- Extraction (untrusted DATA only) ----------------------------------

    private static List<DocLine> ExtractLines(Stream docxStream)
    {
        var lines = new List<DocLine>();
        using var doc = WordprocessingDocument.Open(docxStream, false);
        var body = doc.MainDocumentPart?.Document?.Body;
        if (body is null) return lines;

        // (b) 2-column tables: row = (label cell, value cell). The most reliable structure and what our
        // generated template uses.
        foreach (var table in body.Descendants<Table>())
        {
            foreach (var row in table.Elements<TableRow>())
            {
                var cells = row.Elements<TableCell>().ToList();
                if (cells.Count != 2) continue;
                var label = CellText(cells[0]);
                var value = CellText(cells[1]);
                if (!string.IsNullOrWhiteSpace(label))
                    lines.Add(new DocLine(label.Trim(), value.Trim()));
            }
        }

        // (a) top-level "Label: value" paragraphs (paragraphs inside tables are nested deeper, so
        // Elements<Paragraph> on the body excludes them — no double counting).
        foreach (var p in body.Elements<Paragraph>())
        {
            var text = p.InnerText;
            var idx = text.IndexOf(':');
            if (idx <= 0 || idx >= text.Length - 1) continue;
            var label = text[..idx].Trim();
            var value = text[(idx + 1)..].Trim();
            if (label.Length > 0)
                lines.Add(new DocLine(label, value));
        }

        return lines;
    }

    private static string CellText(TableCell cell) =>
        string.Join('\n', cell.Elements<Paragraph>().Select(p => p.InnerText)).Trim();

    // ---- Document generation (blank template + sample) ---------------------

    public byte[] BuildBlankTemplate() => BuildFilled(new Dictionary<string, string>());

    public byte[] BuildFilled(IReadOnlyDictionary<string, string> answers)
    {
        using var ms = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            var body = new Body();

            body.Append(Heading("PEMP — Application Assessment & Prerequisites", 1));
            body.Append(Para(
                "Application / business team to complete. Fill the right-hand 'Answer' column of each table, " +
                "then return the file. Leave a row blank if not applicable. (Generated from the live PEMP template.)"));

            foreach (var section in AssessmentTemplate.Sections)
            {
                body.Append(Heading(section.Name, 2));
                var table = NewTable();
                table.Append(HeaderRow());
                foreach (var q in section.Questions)
                {
                    var label = q.Type == QType.Checkbox || q.Type == QType.Radio
                        ? $"{q.Text} ({string.Join(" / ", q.Options ?? Array.Empty<string>())})"
                        : q.Text;
                    answers.TryGetValue(q.Id, out var raw);
                    // Stored checkbox values are '|'-joined; present them comma-separated in the doc.
                    var cellValue = string.IsNullOrEmpty(raw) ? "" : raw.Replace('|', ',');
                    table.Append(Row(label, cellValue));
                }
                body.Append(table);
            }

            main.Document = new Document(body);
            main.Document.Save();
        }
        return ms.ToArray();
    }

    private static Table NewTable()
    {
        var borders = new TableBorders(
            new TopBorder { Val = BorderValues.Single, Size = 4 },
            new BottomBorder { Val = BorderValues.Single, Size = 4 },
            new LeftBorder { Val = BorderValues.Single, Size = 4 },
            new RightBorder { Val = BorderValues.Single, Size = 4 },
            new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4 },
            new InsideVerticalBorder { Val = BorderValues.Single, Size = 4 });
        var props = new TableProperties(
            borders,
            new TableWidth { Width = "5000", Type = TableWidthUnitValues.Pct });
        var table = new Table();
        table.Append(props);
        return table;
    }

    private static TableRow HeaderRow()
    {
        var row = new TableRow();
        row.Append(Cell("Question", bold: true, widthPct: "65"));
        row.Append(Cell("Answer", bold: true, widthPct: "35"));
        return row;
    }

    private static TableRow Row(string label, string value)
    {
        var row = new TableRow();
        row.Append(Cell(label, bold: false, widthPct: "65"));
        row.Append(Cell(value, bold: false, widthPct: "35"));
        return row;
    }

    private static TableCell Cell(string text, bool bold, string widthPct)
    {
        var run = new Run(new Text(text) { Space = SpaceProcessingModeValues.Preserve });
        if (bold) run.RunProperties = new RunProperties(new Bold());
        var cellProps = new TableCellProperties(
            new TableCellWidth { Width = widthPct, Type = TableWidthUnitValues.Pct });
        return new TableCell(cellProps, new Paragraph(run));
    }

    private static Paragraph Heading(string text, int level)
    {
        var run = new Run(new Text(text) { Space = SpaceProcessingModeValues.Preserve });
        run.RunProperties = new RunProperties(new Bold(), new FontSize { Val = level == 1 ? "32" : "26" });
        return new Paragraph(new ParagraphProperties(new SpacingBetweenLines { Before = "200", After = "100" }), run);
    }

    private static Paragraph Para(string text) =>
        new(new Run(new Text(text) { Space = SpaceProcessingModeValues.Preserve }));
}
