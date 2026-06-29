namespace Pemp.Infrastructure.Assessment;

/// <summary>Field type for an assessment question (FR-SCO-01 conditional form).</summary>
public enum QType { Text, LongText, YesNo, Radio, Checkbox }

/// <summary>
/// One assessment question. <see cref="ShowIf"/> encodes the workbook's "Depend." logic:
/// "key:value" (OR-joined by '|') where key maps to a driver question —
/// net→Q3 (network exposure), host→Q7 (hosting), cloud→Q9 (cloud tenant),
/// type→Q10 (application type). The question shows only if that driver's answer
/// contains the value. Null = always shown.
/// </summary>
public sealed record AssessQuestion(string Id, string Text, QType Type, string[]? Options = null, string? ShowIf = null);

public sealed record AssessSection(string Name, AssessQuestion[] Questions, string? ShowIf = null);

/// <summary>
/// The "App Team to Complete" assessment (workbook Tab 1) as structured data, faithful
/// to docs/sop/pentest_sop_workbook.txt. Rendered as a conditional questionnaire on the
/// Scoping stage; the stakeholder/app team fills it (FR-SCO).
/// </summary>
public static class AssessmentTemplate
{
    public static readonly AssessSection[] Sections =
    {
        new("Business unit & contacts", new AssessQuestion[]
        {
            new("APPNAME", "Application name", QType.Text),
            new("CONTACT", "Contact name", QType.Text),
            new("PM", "Project manager", QType.Text),
            new("PMEMAIL", "Project manager email", QType.Text),
            new("ARCH", "Architect", QType.Text),
            new("ARCHEMAIL", "Architect email", QType.Text),
            new("DEV", "Developer", QType.Text),
            new("DEVEMAIL", "Developer email", QType.Text),
        }),

        new("High level information", new AssessQuestion[]
        {
            new("Q1", "Application HLD: provide a copy or URL link", QType.Text),
            new("Q2", "Application description: high-level summary", QType.LongText),
            new("Q3", "Network exposure", QType.Checkbox, new[]
                { "External (Internet exposure)", "Internal (AXA XL network only)", "Specific Subnetwork within AXA XL network" }),
            new("Q4", "External application — provide a digital permit ID (https://digitalhub.axa)", QType.Text, ShowIf: "net:External"),
            new("Q5", "Specific subnetwork — name of the subnetwork and details", QType.Text, ShowIf: "net:Specific Subnetwork within AXA XL network"),
            new("Q6", "CMDB listing?", QType.YesNo),
            new("Q7", "Application hosting", QType.Checkbox, new[] { "On prem", "Cloud", "Both" }),
            new("Q8", "Asset hosting type", QType.Checkbox, new[] { "In-house", "IaaS", "PaaS", "SaaS" }),
            new("Q9", "Cloud — application hosting details", QType.Checkbox, new[]
                { "POD (Point Of Delivery)", "MPI Azure Tenant", "Technical Azure Tenant", "Identity Azure Tenant" }, ShowIf: "host:Cloud|host:Both"),
            new("Q10", "Application type", QType.Checkbox, new[]
                { "Web application", "API", "Thick client", "Thin client", "Mobile application", "Platform", "AI (ML, LLM, Agent, …)", "Other" }),
            new("Q11", "System is live in production and contains production data?", QType.YesNo),
            new("Q12", "Data confidentiality", QType.Radio, new[] { "Public", "Internal", "Confidential", "Secret" }),
            new("Q13", "Sensitive/critical components to consider during the pentest", QType.LongText),
            new("Q14", "Identified risks or particular areas of concern", QType.LongText),
            new("Q15", "Is the application hosted by a 3rd-party provider?", QType.YesNo),
            new("Q16", "3rd-party hosted — evidence the vendor approved the pentest", QType.Text, ShowIf: "host3p:Yes"),
            new("Q17", "If servers present — raise a Qualys infrastructure scan request", QType.Text),
        }),

        new("Environment details", new AssessQuestion[]
        {
            new("Q18", "Production URLs (internal & external)", QType.Text),
            new("Q19", "Pre-production URL (reflective of prod)", QType.Text),
            new("Q20", "All other lower environments and their URLs", QType.LongText),
            new("Q21", "Hosting environments (server names / IPs)", QType.LongText),
            new("Q22", "Do any lower environments contain production data?", QType.YesNo),
            new("Q23", "DR environment present? Active or passive?", QType.Text),
        }),

        new("Application details", new AssessQuestion[]
        {
            new("Q24", "Any environments subject to IP whitelisting? Permit tester IP if so.", QType.Text),
            new("Q25", "AD groups that grant access to review access controls/roles", QType.Text),
            new("Q26", "Authentication method", QType.Checkbox, new[] { "SSO", "Local authentication" }),
            new("Q27", "External application — confirm any MFA (tester setup required if yes)", QType.Text, ShowIf: "net:External"),
            new("Q28", "How many user roles? Brief description (vertical segmentation test)", QType.LongText),
            new("Q29", "Different scopes, or all users see the same data? (horizontal segmentation)", QType.LongText),
            new("Q30", "Are there administrative roles?", QType.YesNo),
            new("Q31", "File upload features?", QType.YesNo),
            new("Q32", "User input features?", QType.YesNo),
            new("Q33", "File types supported by the upload feature", QType.Text, ShowIf: "upload:Yes"),
            new("Q34", "Sample test data / test email to verify functionality", QType.Text),
            new("Q35", "Dependencies/interactions with other AXA XL applications?", QType.LongText),
            new("Q36", "External inbound/outbound 3rd-party APIs used?", QType.YesNo),
            new("Q37", "Sensitive/critical components to consider during the pentest", QType.LongText),
            new("Q38", "Any Power BI dashboards that access this application's data?", QType.YesNo),
            new("Q39", "Power BI dashboards — datasource details", QType.Text, ShowIf: "powerbi:Yes"),
            new("Q40", "Location/path of integration code (connection strings, vault/key config)", QType.LongText),
            new("Q41", "Confirm lower environments are internal only, no external connections", QType.YesNo),
            new("Q42", "Any additional information not covered above", QType.LongText),
        }),

        new("Thick client", new AssessQuestion[]
        {
            new("Q43", "Deployed on a specific machine, or on the user's workstation?", QType.Text),
            new("Q44", "Is the thick client in kiosk mode?", QType.YesNo),
            new("Q45", "Can access be provided for test-tool installation on the client server?", QType.YesNo),
        }, ShowIf: "type:Thick client"),

        new("Thin client", new AssessQuestion[]
        {
            new("Q46", "How do you access the thin client?", QType.Text),
            new("Q47", "Is the thin client in kiosk mode?", QType.YesNo),
        }, ShowIf: "type:Thin client"),

        new("API", new AssessQuestion[]
        {
            new("Q48", "API endpoints — provide/confirm the full list", QType.LongText),
            new("Q49", "Inbound/outbound APIs used by this app?", QType.Text),
            new("Q50", "APIs connecting to other AXA XL applications (in scope)", QType.LongText),
            new("Q51", "How does the API authenticate with the API server?", QType.Text),
            new("Q52", "Provide API Postman collections (JSON or XML)", QType.Text),
        }, ShowIf: "type:API"),

        new("On prem", new AssessQuestion[]
        {
            new("Q53", "Hosting environments (server names / IPs)", QType.LongText),
            new("Q54", "Any servers (web, database, etc.)?", QType.YesNo),
            new("Q55", "Database server hostname for each environment", QType.Text),
            new("Q56", "Database names under each server and environment", QType.Text),
            new("Q57", "Can a vulnerability-assessment scan report for all DBs be provided?", QType.YesNo),
        }, ShowIf: "host:On prem|host:Both"),

        new("Cloud — MPI Azure Tenant", new AssessQuestion[]
        {
            new("Q61", "Name and IPs of the VMs the application is deployed on", QType.Text),
            new("Q62", "NSG ingress/egress rules attached to your VMs", QType.LongText),
            new("Q63", "Using a Key Vault to store application secrets?", QType.YesNo),
            new("Q64", "Using a NetApp file share to store data?", QType.YesNo),
            new("Q65", "Hosting a database on your VMs?", QType.YesNo),
            new("Q66", "Deploying via a CI/CD pipeline?", QType.YesNo),
            new("Q67", "If yes, describe the CI/CD architecture (repo, orchestrator, runners)", QType.LongText),
        }, ShowIf: "cloud:MPI Azure Tenant"),

        new("Cloud — Technical Azure Tenant", new AssessQuestion[]
        {
            new("Q68", "Deploying your infra through CPP?", QType.YesNo),
            new("Q69", "Deployed resources not in the CPP catalogue?", QType.YesNo),
            new("Q70", "Deploying on an OpenShift container?", QType.YesNo),
            new("Q71", "Provide command-line access to the container to the pentesters", QType.Text),
            new("Q72", "Provide the container's manifest", QType.Text),
            new("Q73", "Provide Reader (resource-group level) to the Cyber Assurance team", QType.Text),
        }, ShowIf: "cloud:Technical Azure Tenant"),

        new("Cloud — Identity Azure Tenant", new AssessQuestion[]
        {
            new("Q74", "Is the application to be migrated to MPI or Technical tenants?", QType.Text),
            new("Q75", "Provide Reader (resource-group level) to the Cyber Assurance team", QType.Text),
            new("Q76", "Deploying via a CI/CD pipeline?", QType.YesNo),
            new("Q77", "If yes, describe the CI/CD architecture (repo, orchestrator, runners)", QType.LongText),
        }, ShowIf: "cloud:Identity Azure Tenant"),

        new("AI questionnaire", new AssessQuestion[]
        {
            new("AI1", "AI system type", QType.Checkbox, new[] { "Machine Learning", "LLM / GenAI", "Agentic" }),
            new("AI2", "Describe the model(s), training data, and guardrails", QType.LongText),
            new("AI3", "Is the model exposed to untrusted user input (prompt injection surface)?", QType.YesNo),
        }, ShowIf: "type:AI (ML, LLM, Agent, …)"),
    };

    // Driver-question map for ShowIf keys → the question whose answer is checked.
    public static readonly Dictionary<string, string> Drivers = new()
    {
        ["net"] = "Q3", ["host"] = "Q7", ["cloud"] = "Q9", ["type"] = "Q10",
        ["host3p"] = "Q15", ["upload"] = "Q31", ["powerbi"] = "Q38",
    };
}
