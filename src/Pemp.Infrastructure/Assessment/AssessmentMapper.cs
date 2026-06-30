namespace Pemp.Infrastructure.Assessment;

/// <summary>
/// The format-agnostic core of the assessment import (FR-SCO-01): given raw label→value pairs extracted
/// from <em>any</em> uploaded document (a Word .docx today, an Excel .xlsx now), map them to template
/// question ids and return the structured <see cref="AssessmentImportResult"/>. Both the docx and xlsx
/// readers feed their extracted <see cref="DocLine"/>s into <see cref="MapLines"/> so the question-matching
/// (exact-id → exact-label → contains → token-overlap fuzzy) and choice-value mapping are identical
/// regardless of the source file type — one matching algorithm, two front-end readers.
///
/// All input is untrusted DATA: labels/values are only ever surfaced through Razor (HTML-encoded), never
/// logged and never interpreted/executed.
/// </summary>
public static class AssessmentMapper
{
    // Below this combined match confidence a doc line is reported as "unmatched" rather than guessed.
    private const double MatchThreshold = 0.5;

    private static readonly AssessQuestion[] AllQuestions =
        AssessmentTemplate.Sections.SelectMany(s => s.Questions).ToArray();

    /// <summary>
    /// Map extracted label→value pairs to template answers. Each template question is consumed at most
    /// once, so identical labels (e.g. the repeated "sensitive/critical components" row) fill distinct
    /// questions in document order. Choice answers are normalised to the nearest template option(s).
    /// </summary>
    public static AssessmentImportResult MapLines(IReadOnlyList<DocLine> lines)
    {
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
}
