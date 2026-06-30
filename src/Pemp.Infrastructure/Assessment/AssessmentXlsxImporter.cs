using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace Pemp.Infrastructure.Assessment;

/// <summary>
/// Parses a half-filled Excel (.xlsx) assessment into template answers, and generates the structured
/// blank/sample workbooks for the round-trip (FR-SCO-01). An .xlsx is treated strictly as DATA — a zip of
/// XML read through OpenXML's <see cref="SpreadsheetDocument"/>; nothing is executed. Cell <em>formulas are
/// never evaluated</em> — only cached values and the shared-string table are read. All extracted text is
/// untrusted: it is only ever surfaced through Razor (HTML-encoded by default), never logged, never
/// interpreted. The actual question-matching/choice-mapping is the shared <see cref="AssessmentMapper"/>,
/// identical to the .docx path.
/// </summary>
public interface IAssessmentXlsxImporter
{
    /// <summary>Extract candidate answers from an .xlsx stream and map them to template question ids.</summary>
    AssessmentImportResult Parse(Stream xlsxStream);

    /// <summary>A blank, structured .xlsx (2-column Question/Answer sheet) for business teams to fill.</summary>
    byte[] BuildBlankTemplate();

    /// <summary>The same structured .xlsx pre-filled from <paramref name="answers"/> (questionId → value).</summary>
    byte[] BuildFilled(IReadOnlyDictionary<string, string> answers);
}

/// <summary>
/// OpenXML SpreadsheetDocument implementation of <see cref="IAssessmentXlsxImporter"/>. Stateless and
/// thread-safe (registered as a singleton). Reads the FIRST worksheet of a 2-column layout
/// (column A = label/question, column B = value), resolving shared strings and skipping an optional
/// "Question / Answer" header row and blank cells, then feeds the raw label→value pairs into the shared
/// <see cref="AssessmentMapper"/>. This class owns only the .xlsx reading/writing.
/// </summary>
public sealed class XlsxAssessmentImporter : IAssessmentXlsxImporter
{
    public AssessmentImportResult Parse(Stream xlsxStream) =>
        AssessmentMapper.MapLines(ExtractLines(xlsxStream));

    // ---- Extraction (untrusted DATA only; no formula evaluation) -----------

    private static List<DocLine> ExtractLines(Stream xlsxStream)
    {
        var lines = new List<DocLine>();
        using var doc = SpreadsheetDocument.Open(xlsxStream, false);
        var wbPart = doc.WorkbookPart;
        if (wbPart?.Workbook is null) return lines;

        // First worksheet only — the structured template is a single "Assessment" sheet.
        var firstSheet = wbPart.Workbook.Sheets?.Elements<Sheet>().FirstOrDefault();
        if (firstSheet?.Id?.Value is null) return lines;
        if (wbPart.GetPartById(firstSheet.Id!.Value!) is not WorksheetPart wsPart) return lines;
        var sheetData = wsPart.Worksheet.GetFirstChild<SheetData>();
        if (sheetData is null) return lines;

        var sst = wbPart.SharedStringTablePart?.SharedStringTable;

        var headerSkipped = false;
        foreach (var row in sheetData.Elements<Row>())
        {
            var label = "";
            var value = "";
            var positional = 0;
            foreach (var cell in row.Elements<Cell>())
            {
                var col = ColumnLetters(cell.CellReference?.Value, positional);
                var text = CellText(cell, sst);
                if (col == "A") label = text.Trim();
                else if (col == "B") value = text.Trim();
                positional++;
            }

            if (string.IsNullOrWhiteSpace(label)) continue; // blank row / blank label cell → ignore
            if (!headerSkipped && IsHeaderRow(label, value)) { headerSkipped = true; continue; }
            lines.Add(new DocLine(label, value));
        }

        return lines;
    }

    /// <summary>
    /// Read a cell's TEXT from its cached value / the shared-string table only. A <c>CellFormula</c> child
    /// is deliberately ignored — we never evaluate formulas (untrusted content is data, not code).
    /// </summary>
    private static string CellText(Cell cell, SharedStringTable? sst)
    {
        var raw = cell.CellValue?.InnerText;

        if (cell.DataType?.Value == CellValues.SharedString)
        {
            if (sst is not null && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var idx)
                && idx >= 0 && idx < sst.ChildElements.Count)
                return sst.ElementAt(idx).InnerText;
            return "";
        }

        if (cell.DataType?.Value == CellValues.InlineString)
            return cell.InlineString?.Text?.Text ?? cell.InnerText;

        return raw ?? "";
    }

    // Column letters from a cell reference ("B7" → "B"). Cells may be sparse (blanks omitted), so we rely
    // on the reference rather than position; a missing reference (rare) falls back to A/B by order.
    private static string ColumnLetters(string? cellRef, int positional)
    {
        if (!string.IsNullOrEmpty(cellRef))
            return new string(cellRef.TakeWhile(char.IsLetter).ToArray()).ToUpperInvariant();
        return positional == 0 ? "A" : positional == 1 ? "B" : "";
    }

    private static bool IsHeaderRow(string label, string value) =>
        label.Equals("Question", StringComparison.OrdinalIgnoreCase)
        && (value.Length == 0 || value.Equals("Answer", StringComparison.OrdinalIgnoreCase));

    // ---- Workbook generation (blank template + sample) ---------------------

    public byte[] BuildBlankTemplate() => BuildFilled(new Dictionary<string, string>());

    public byte[] BuildFilled(IReadOnlyDictionary<string, string> answers)
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
            var sstIndex = new Dictionary<string, int>(StringComparer.Ordinal);

            int Intern(string text)
            {
                if (sstIndex.TryGetValue(text, out var i)) return i;
                sst.AppendChild(new SharedStringItem(new Text(text) { Space = SpaceProcessingModeValues.Preserve }));
                var idx = sstIndex.Count;
                sstIndex[text] = idx;
                return idx;
            }

            Cell StrCell(string reference, string text) => new()
            {
                CellReference = reference,
                DataType = CellValues.SharedString,
                CellValue = new CellValue(Intern(text).ToString(CultureInfo.InvariantCulture)),
            };

            uint rowIndex = 1;
            void AddRow(string a, string? b)
            {
                var row = new Row { RowIndex = rowIndex };
                row.Append(StrCell($"A{rowIndex}", a));
                if (!string.IsNullOrEmpty(b)) row.Append(StrCell($"B{rowIndex}", b));
                sheetData.Append(row);
                rowIndex++;
            }

            AddRow("Question", "Answer");
            foreach (var section in AssessmentTemplate.Sections)
            {
                AddRow(section.Name, null); // section divider (blank answer → ignored on re-import)
                foreach (var q in section.Questions)
                {
                    var label = q.Type is QType.Checkbox or QType.Radio
                        ? $"{q.Text} ({string.Join(" / ", q.Options ?? Array.Empty<string>())})"
                        : q.Text;
                    answers.TryGetValue(q.Id, out var raw);
                    // Stored checkbox values are '|'-joined; present them comma-separated in the sheet.
                    var cellValue = string.IsNullOrEmpty(raw) ? "" : raw.Replace('|', ',');
                    AddRow(label, cellValue.Length == 0 ? null : cellValue);
                }
            }

            sst.Count = (uint)sstIndex.Count;
            sst.UniqueCount = (uint)sstIndex.Count;

            var sheets = wbPart.Workbook.AppendChild(new Sheets());
            sheets.Append(new Sheet { Id = wbPart.GetIdOfPart(wsPart), SheetId = 1U, Name = "Assessment" });

            wsPart.Worksheet.Save();
            wbPart.Workbook.Save();
        }
        return ms.ToArray();
    }
}
