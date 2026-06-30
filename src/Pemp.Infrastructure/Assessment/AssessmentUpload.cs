namespace Pemp.Infrastructure.Assessment;

/// <summary>
/// Server-side validation for an uploaded assessment document (FR-SCO-01 / SEC robustness). Kept as a
/// pure, testable unit so the same rules apply wherever an upload arrives (the Blazor page today, an API
/// endpoint tomorrow). All untrusted: we accept ONLY a .docx by extension/content-type and cap the size;
/// the bytes are then parsed strictly as data and discarded — never stored or executed.
/// </summary>
public static class AssessmentUpload
{
    /// <summary>Maximum accepted upload size (5 MB) — a filled assessment is far smaller.</summary>
    public const long MaxBytes = 5 * 1024 * 1024;

    /// <summary>The OpenXML Word document content type.</summary>
    public const string DocxContentType =
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

    /// <summary>
    /// Returns null if the upload is acceptable, otherwise a friendly, user-safe error message.
    /// <paramref name="contentType"/> is advisory (browsers vary); the .docx extension and size are
    /// the authoritative gate, with the bytes validated for real only when OpenXML opens them.
    /// </summary>
    public static string? Validate(string fileName, string? contentType, long sizeBytes)
    {
        if (string.IsNullOrWhiteSpace(fileName) ||
            !fileName.EndsWith(".docx", StringComparison.OrdinalIgnoreCase))
            return "Please upload a Word .docx file (the structured assessment template).";

        // A legacy .doc / renamed file announcing a non-Word type is rejected early.
        if (!string.IsNullOrEmpty(contentType) &&
            !contentType.Equals(DocxContentType, StringComparison.OrdinalIgnoreCase) &&
            !contentType.Equals("application/octet-stream", StringComparison.OrdinalIgnoreCase))
            return "That doesn't look like a .docx file. Export the assessment as a Word .docx and try again.";

        if (sizeBytes <= 0)
            return "That file is empty.";
        if (sizeBytes > MaxBytes)
            return $"That file is too large ({sizeBytes / (1024 * 1024)} MB). The limit is {MaxBytes / (1024 * 1024)} MB.";

        return null;
    }
}
