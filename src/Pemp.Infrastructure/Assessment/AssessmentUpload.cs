namespace Pemp.Infrastructure.Assessment;

/// <summary>
/// Server-side validation for an uploaded assessment document (FR-SCO-01 / SEC robustness). Kept as a
/// pure, testable unit so the same rules apply wherever an upload arrives (the Blazor page today, an API
/// endpoint tomorrow). All untrusted: we accept ONLY a Word .docx or an Excel .xlsx by extension/content-type
/// and cap the size; the bytes are then parsed strictly as data and discarded — never stored or executed.
/// </summary>
public static class AssessmentUpload
{
    /// <summary>Maximum accepted upload size (5 MB) — a filled assessment is far smaller.</summary>
    public const long MaxBytes = 5 * 1024 * 1024;

    /// <summary>The OpenXML Word document content type.</summary>
    public const string DocxContentType =
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

    /// <summary>The OpenXML Excel workbook content type.</summary>
    public const string XlsxContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    /// <summary>True if the file name carries the Word .docx extension (case-insensitive).</summary>
    public static bool IsDocx(string? fileName) =>
        fileName is not null && fileName.EndsWith(".docx", StringComparison.OrdinalIgnoreCase);

    /// <summary>True if the file name carries the Excel .xlsx extension (case-insensitive).</summary>
    public static bool IsXlsx(string? fileName) =>
        fileName is not null && fileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Returns null if the upload is acceptable, otherwise a friendly, user-safe error message.
    /// <paramref name="contentType"/> is advisory (browsers vary); the .docx/.xlsx extension and size are
    /// the authoritative gate, with the bytes validated for real only when OpenXML opens them. The generic
    /// "application/octet-stream" content type is tolerated (some browsers send it for Office files).
    /// </summary>
    public static string? Validate(string fileName, string? contentType, long sizeBytes)
    {
        var isDocx = IsDocx(fileName);
        var isXlsx = IsXlsx(fileName);
        if (string.IsNullOrWhiteSpace(fileName) || (!isDocx && !isXlsx))
            return "Please upload a Word .docx or Excel .xlsx file (the structured assessment template).";

        // A legacy .doc/.xls or renamed file announcing a mismatched Office type is rejected early.
        var expectedType = isXlsx ? XlsxContentType : DocxContentType;
        if (!string.IsNullOrEmpty(contentType) &&
            !contentType.Equals(expectedType, StringComparison.OrdinalIgnoreCase) &&
            !contentType.Equals("application/octet-stream", StringComparison.OrdinalIgnoreCase))
            return isXlsx
                ? "That doesn't look like an .xlsx file. Export the assessment as an Excel .xlsx and try again."
                : "That doesn't look like a .docx file. Export the assessment as a Word .docx and try again.";

        if (sizeBytes <= 0)
            return "That file is empty.";
        if (sizeBytes > MaxBytes)
            return $"That file is too large ({sizeBytes / (1024 * 1024)} MB). The limit is {MaxBytes / (1024 * 1024)} MB.";

        return null;
    }
}
