namespace Pemp.Infrastructure.Assessment;

public sealed record ChecklistItem(string Code, string Text);
public sealed record ChecklistGroup(string Name, ChecklistItem[] Items);

/// <summary>
/// Tester action checklists (workbook Tab 4 "CA &amp; Testers Only"): pre-requisites,
/// start/during test, and end-of-test. Ticked off by the tester across the execution
/// lifecycle. Faithful to docs/sop/pentest_sop_workbook.txt.
/// </summary>
public static class ChecklistTemplate
{
    public static readonly ChecklistGroup[] Groups =
    {
        new("Pre-requisites", new[]
        {
            new ChecklistItem("PRE-01", "Access for all environments/assets in scope — completed or outstanding (first check)"),
            new ChecklistItem("PRE-02", "Redacted copy of previous pentest findings shared to tester"),
            new ChecklistItem("PRE-03", "CA confirmed all URLs/environments in the previous report are in scope of this test"),
            new ChecklistItem("PRE-04", "SOW/PO is in place and updated in the Daily Tracker"),
            new ChecklistItem("PRE-05", "DPIDs captured for external-facing assets"),
            new ChecklistItem("PRE-06", "CA team provided Asset ID for the exact URLs & assets to the tester"),
        }),
        new("Start & during test", new[]
        {
            new ChecklistItem("DUR-01", "Advance Notice email — correct scope and accounts"),
            new ChecklistItem("DUR-02", "Access for all environments/assets re-checked at start of test (second check)"),
            new ChecklistItem("DUR-03", "Any scope/target changes updated in Advance Notice and confirmed with the business owner"),
            new ChecklistItem("DUR-04", "Vulnerability scan requested (Qualys WAS / Burp scan)"),
            new ChecklistItem("DUR-05", "Any critical or high findings escalated to CA leads via email"),
        }),
        new("End of test", new[]
        {
            new ChecklistItem("END-01", "Inform business to remove all original pentest access"),
            new ChecklistItem("END-02", "Post-test cleanup — remove any accounts testers created in the application"),
            new ChecklistItem("END-03", "Any new accounts → update Advance Notice email sent"),
            new ChecklistItem("END-04", "Remove all tester access (application servers, VDI, jump servers, etc.)"),
            new ChecklistItem("END-05", "Copy all test files and data to shared drives for report preparation"),
            new ChecklistItem("END-06", "End-of-test reply to the IDART (SOC) advance-notice email"),
            new ChecklistItem("END-07", "Summary-of-findings email to Cyber Assurance only"),
            new ChecklistItem("END-08", "Vulnerability scan performed (Burp scan)"),
            new ChecklistItem("END-09", "CA team provided Asset ID and DPID"),
            new ChecklistItem("END-10", "All files/POCs saved in the correct shared folder"),
        }),
    };

    public static int TotalItems => Groups.Sum(g => g.Items.Length);
}
