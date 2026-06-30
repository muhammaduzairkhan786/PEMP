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
/// as a singleton). Extraction reads (a) "Label: value" paragraphs and (b) 2-column table rows; matching
/// is exact-id → exact-label → contains → token-overlap fuzzy, each template question consumed at most once
/// so identical labels (e.g. the repeated "sensitive/critical components" row) fill distinct questions in
/// document order. Choice answers are mapped to the nearest template option(s).
/// </summary>
public sealed class DocxAssessmentImporter : IAssessmentDocImporter
{
    // Below this combined match confidence a doc line is reported as "unmatched" rather than guessed.
    private const double MatchThreshold = 0.5;

    private static readonly AssessQuestion[] AllQuestions =
        AssessmentTemplate.Sections.SelectMany(s => s.Questions).ToArray();

    public AssessmentImportResult Parse(Stream docxStream)
    {
        var lines = ExtractLines(docxStream);

        var matched = new List<MatchedAnswer>();
        var unmatched = new List<DocLine>();
        var consumed = new HashSet<string>(StringComparer.Ordinal); // question ids already filled

        foreach (var line in lines)
        {
            var q = BestMatch(line.Label, consumed, out var confidence);
            if (q is null)
            {
                unmatched.Add(line);
                continue;
            }
            var value = MapValue(q, line.Value);
            if (string.IsNullOrWhiteSpace(value))
            {
                unmatched.Add(line);
                continue;
            }
            consumed.Add(q.Id);
            matched.Add(new MatchedAnswer(q.Id, q.Text, value, line.Label, confidence));
        }

        var unanswered = AllQuestions.Select(q => q.Id).Where(id => !consumed.Contains(id)).ToList();
        return new AssessmentImportResult(matched, unmatched, unanswered, AllQuestions.Length);
    }

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

    // ---- Matching ----------------------------------------------------------

    private static AssessQuestion? BestMatch(string docLabel, HashSet<string> consumed, out double confidence)
    {
        confidence = 0;
        var normLabel = Norm(docLabel);
        var labelKey = normLabel.Replace(" ", "");
        var labelTokens = Tokens(docLabel);
        AssessQuestion? best = null;

        foreach (var q in AllQuestions)
        {
            if (consumed.Contains(q.Id)) continue;

            double c;
            // 1. exact question id (case-insensitive, ignoring spaces) — the strongest anchor.
            if (labelKey == q.Id.ToLowerInvariant())
            {
                c = 1.0;
            }
            else
            {
                var normText = Norm(q.Text);
                if (normLabel.Length > 0 && normLabel == normText)
                {
                    c = 1.0; // 2. exact label
                }
                else if (normLabel.Length >= 4 &&
                         (normText.Contains(normLabel) || (normLabel.Length > normText.Length && normLabel.Contains(normText))))
                {
                    c = 0.8; // 3. one label contains the other
                }
                else
                {
                    c = Overlap(labelTokens, Tokens(q.Text)) * 0.75; // 4. token-overlap fuzzy (capped below "contains")
                }
            }

            if (c > confidence)
            {
                confidence = c;
                best = q;
            }
        }

        return confidence >= MatchThreshold ? best : null;
    }

    private static string Norm(string s)
    {
        var chars = s.ToLowerInvariant().Select(ch => char.IsLetterOrDigit(ch) ? ch : ' ');
        return string.Join(' ', new string(chars.ToArray()).Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static HashSet<string> Tokens(string s) =>
        Norm(s).Split(' ', StringSplitOptions.RemoveEmptyEntries)
               .Where(t => t.Length > 2) // drop noise words ("is", "to", "the", "of")
               .ToHashSet(StringComparer.Ordinal);

    private static double Overlap(HashSet<string> a, HashSet<string> b)
    {
        if (a.Count == 0 || b.Count == 0) return 0;
        var inter = a.Count(b.Contains);
        return (double)inter / Math.Min(a.Count, b.Count); // overlap coefficient
    }

    // ---- Value mapping (choice answers → template options) -----------------

    private static string MapValue(AssessQuestion q, string raw)
    {
        raw = raw.Trim();
        if (IsPlaceholder(raw)) return "";

        switch (q.Type)
        {
            case QType.YesNo:
                var l = raw.ToLowerInvariant();
                if (l.StartsWith('y') || l is "true" or "1") return "Yes";
                if (l.StartsWith('n') || l is "false" or "0") return "No";
                return ""; // unrecognised → leave unfilled (reviewer sets it)

            case QType.Radio:
                return NearestOption(q.Options!, raw) ?? "";

            case QType.Checkbox:
                var picks = raw.Split(new[] { ';', ',', '|', '\n', '/' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(part => NearestOption(q.Options!, part))
                    .Where(o => o is not null)
                    .Select(o => o!)
                    .Distinct()
                    .ToList();
                return string.Join('|', picks);

            default:
                return raw;
        }
    }

    private static string? NearestOption(string[] options, string value)
    {
        var normVal = Norm(value);
        if (normVal.Length == 0) return null;
        string? best = null;
        double bestScore = 0;
        foreach (var opt in options)
        {
            var normOpt = Norm(opt);
            double s;
            if (normVal == normOpt) s = 1.0;
            else if (normOpt.Contains(normVal) || normVal.Contains(normOpt)) s = 0.8;
            else s = Overlap(Tokens(value), Tokens(opt)) * 0.7;
            if (s > bestScore) { bestScore = s; best = opt; }
        }
        return bestScore >= 0.5 ? best : null;
    }

    private static bool IsPlaceholder(string v) =>
        v.Length == 0
        || v is "-" or "—" or "–" or "n/a" or "N/A" or "tbd" or "TBD"
        || (v.StartsWith('<') && v.EndsWith('>')); // workbook "<To be filled by business>" tokens

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
