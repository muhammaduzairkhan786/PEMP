using Pemp.Domain;

namespace Pemp.Infrastructure.Persistence;

/// <summary>
/// Seeds demo engagements by running real domain transitions, so the spine positions
/// and the hash-chained audit trail are genuine (not hand-written rows).
/// </summary>
public static class DemoSeeder
{
    // Stable tester ids so auth/role mapping can line up later.
    public static readonly Guid TesterKhan = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid TesterLee = Guid.Parse("22222222-2222-2222-2222-222222222222");
    public static readonly Guid TesterPatel = Guid.Parse("33333333-3333-3333-3333-333333333333");

    /// <summary>A tester on the capacity board (FR-ASG): stable id + display name.</summary>
    public sealed record TesterInfo(Guid Id, string Name);

    /// <summary>
    /// The assignable testers shown on the capacity board picker (FR-ASG-02/03). A. Khan is the
    /// primary tester login; the others give the Delivery Manager a real choice.
    /// </summary>
    public static readonly TesterInfo[] Testers =
    {
        new(TesterKhan, "A. Khan"),
        new(TesterPatel, "R. Patel"),
        new(TesterLee, "S. Lee"),
    };

    public static void Seed(PempDbContext db, Func<DateTimeOffset> clock, byte[]? auditKey = null)
    {
        if (db.Engagements.Any()) return;
        var chain = new EfAuditChain(db, auditKey ?? Pemp.Domain.Audit.HashChain.DefaultKey);
        const string dm = "M. Reyes", acme = "J. Okafor";

        // Claims Portal — Project, parked at the SoW sign-off gate (DM-reviewed, awaiting Acme).
        var claims = Engagement.Raise("ENG-2026-0412", EngagementType.Project, "system", chain, clock);
        claims.RouteToDeliveryManager(dm);
        claims.AssignTester(TesterKhan, dm);
        claims.CompleteAssessment("A. Khan");
        claims.ReviewSowAsDeliveryManager(dm);
        db.Engagements.Add(EngagementRecord.FromDomain(claims, "Claims Portal", "High", "A. Khan"));

        // Payments API — BAU, signed, awaiting access verification.
        var pay = Engagement.Raise("ENG-2026-0419", EngagementType.Bau, "system", chain, clock);
        pay.RouteToDeliveryManager(dm);
        pay.AssignTester(TesterKhan, dm);                 // A. Khan (default Tester persona) → real work at Access
        pay.CompleteAssessment("A. Khan");
        pay.SignSow(acme, reAuthenticated: true);
        db.Engagements.Add(EngagementRecord.FromDomain(pay, "Payments API", "Medium", "A. Khan"));

        // Retail Web — Project, in the testing/findings window. Assigned to A. Khan (the only
        // seeded tester login) so the findings/evidence/checklist showcase is reachable as a
        // tester, and so the Retail Web stakeholder (P. Devlin) has findings to view.
        var retail = Engagement.Raise("ENG-2026-0408", EngagementType.Project, "system", chain, clock);
        retail.RouteToDeliveryManager(dm);
        retail.AssignTester(TesterKhan, dm);
        retail.CompleteAssessment("A. Khan");
        retail.ReviewSowAsDeliveryManager(dm);
        retail.SignSow(acme, reAuthenticated: true);
        retail.VerifyAccess("A. Khan");
        retail.SendIrNotice("A. Khan");
        retail.EndTest("A. Khan");
        db.Engagements.Add(EngagementRecord.FromDomain(retail, "Retail Web", "High", "A. Khan"));

        // Broker Portal — Project, fully closed (report released after peer QA).
        var broker = Engagement.Raise("ENG-2026-0399", EngagementType.Project, "system", chain, clock);
        broker.RouteToDeliveryManager(dm);
        broker.AssignTester(TesterKhan, dm);
        broker.CompleteAssessment("A. Khan");
        broker.ReviewSowAsDeliveryManager(dm);
        broker.SignSow(acme, reAuthenticated: true);
        broker.VerifyAccess("A. Khan");
        broker.SendIrNotice("A. Khan");
        broker.EndTest("A. Khan");
        broker.GenerateDraft("A. Khan");
        broker.PeerReview(TesterPatel, passed: true, "R. Patel");
        broker.ReleaseFinal(acme, reAuthenticated: true);
        db.Engagements.Add(EngagementRecord.FromDomain(broker, "Broker Portal", "Low", "A. Khan"));

        // Mobile App — BAU, at Scoping (assessment questionnaire to be completed). Assigned to
        // A. Khan so the assessment showcase is reachable from the single tester login.
        var mobile = Engagement.Raise("ENG-2026-0421", EngagementType.Bau, "system", chain, clock);
        mobile.RouteToDeliveryManager(dm);
        mobile.AssignTester(TesterKhan, dm);
        db.Engagements.Add(EngagementRecord.FromDomain(mobile, "Mobile App", "Medium", "A. Khan"));

        // Partner Portal — freshly raised, at Intake (DM to route).
        var intake = Engagement.Raise("ENG-2026-0422", EngagementType.Bau, "system", chain, clock);
        db.Engagements.Add(EngagementRecord.FromDomain(intake, "Partner Portal", "Medium", null));

        // Quote Engine — routed, awaiting tester assignment (DM's turn).
        var assign = Engagement.Raise("ENG-2026-0423", EngagementType.Project, "system", chain, clock);
        assign.RouteToDeliveryManager(dm);
        db.Engagements.Add(EngagementRecord.FromDomain(assign, "Quote Engine", "High", null));

        // Live register (FR-FND): findings for the engagements that have reached testing.
        FindingRecord F(Guid eng, string title, Severity sev, string cvss, string vector, string asset, string remediation, FindingStatus status)
            => new() { Id = Guid.NewGuid(), EngagementId = eng, Title = title, Severity = sev, Cvss = cvss, CvssVector = vector, Asset = asset, Remediation = remediation, Status = status };
        // Retail Web is mid-test and has NOT been retested, so its findings are Open (not RetestPending,
        // which is only valid once a retest child is carrying them for re-verification).
        var fSqli = F(retail.Id, "SQLi in /claims search", Severity.High, "8.1", "CVSS:3.1/AV:N/AC:L/PR:N/UI:N/S:U/C:H/I:H/A:N", "Web", "Use parameterised queries; validate and allow-list input.", FindingStatus.Open);
        var fXss = F(retail.Id, "Stored XSS in note field", Severity.High, "7.4", "CVSS:3.1/AV:N/AC:L/PR:L/UI:R/S:C/C:H/I:L/A:N", "Web", "Context-aware output encoding; CSP.", FindingStatus.Open);
        db.Findings.AddRange(
            F(retail.Id, "Auth bypass via JWT confusion", Severity.Critical, "9.1", "CVSS:3.1/AV:N/AC:L/PR:N/UI:N/S:U/C:H/I:H/A:N", "API", "Pin the signing algorithm; reject 'none'; verify 'kid'.", FindingStatus.Open),
            fSqli, fXss,
            F(retail.Id, "Verbose error disclosure", Severity.Medium, "5.3", "CVSS:3.1/AV:N/AC:L/PR:N/UI:N/S:U/C:L/I:N/A:N", "API", "Return generic errors; log details server-side only.", FindingStatus.Remediated),
            F(retail.Id, "Missing cookie security flags", Severity.Low, "3.1", "CVSS:3.1/AV:N/AC:H/PR:L/UI:N/S:U/C:L/I:N/A:N", "Web", "Set Secure, HttpOnly and SameSite on session cookies.", FindingStatus.Open),
            F(broker.Id, "Insecure direct object reference", Severity.High, "7.7", "CVSS:3.1/AV:N/AC:L/PR:L/UI:N/S:U/C:H/I:N/A:N", "Web", "Enforce per-object authorization server-side.", FindingStatus.RetestPending),
            F(broker.Id, "Weak password policy", Severity.Medium, "4.8", "CVSS:3.1/AV:N/AC:H/PR:N/UI:N/S:U/C:L/I:L/A:N", "Web", "Enforce length/complexity and breached-password checks.", FindingStatus.Closed)
        );

        // Evidence (FR-FND-02 / SEC-EVD): artifacts attached to findings.
        EvidenceRecord Ev(Guid eng, Guid find, string file, EvidenceKind kind, string note)
            => new() { Id = Guid.NewGuid(), EngagementId = eng, FindingId = find, FileName = file, Kind = kind, Note = note, EncryptedAtRest = true };
        db.Evidence.AddRange(
            Ev(retail.Id, fSqli.Id, "sqli-claims-poc.png", EvidenceKind.Screenshot, "UNION-based extraction PoC"),
            Ev(retail.Id, fSqli.Id, "sqli-request-response.txt", EvidenceKind.RequestResponse, "Injected payload + DB error"),
            Ev(retail.Id, fXss.Id, "stored-xss-note.png", EvidenceKind.Screenshot, "Payload fires on note view")
        );

        // Access requirements (FR-ACC-01) for Payments API (at the Access stage).
        AccessRequirementRecord A(Guid eng, string env, string url, string type, AccessStatus st)
            => new() { Id = Guid.NewGuid(), EngagementId = eng, Environment = env, Url = url, AccessType = type, Status = st };
        db.AccessRequirements.AddRange(
            A(pay.Id, "Production URLs", "https://payments.axaxl.example", "Read", AccessStatus.InProgress),
            A(pay.Id, "Pre-production", "https://preprod.payments.axaxl.example", "Read-Write", AccessStatus.AppTeamToProvision),
            A(pay.Id, "QA (lower env)", "https://qa.payments.axaxl.example", "Read-Write", AccessStatus.AppTeamToProvision),
            A(pay.Id, "APIs / Postman collection", "token + subscription key", "Other", AccessStatus.AppTeamToProvision)
        );

        // Test credentials (SEC-CRD): vault-backed in prod; here so the masked-reveal + re-auth
        // flow is exercisable by the assigned tester. Payments API is at Access; Retail Web mid-test.
        TestCredentialRecord C(Guid eng, string label, string user, string secret)
            => new() { Id = Guid.NewGuid(), EngagementId = eng, Label = label, Username = user, Secret = secret };
        db.TestCredentials.AddRange(
            C(pay.Id, "QA app login", "qa.tester@payments.test", "Q4-Sandbox!7xtR"),
            C(pay.Id, "API service account", "svc-pentest", "ak_live_8f3b2c1d9e7a4506"),
            C(retail.Id, "Retail admin (test)", "admin.test@retail.test", "R3tail!Demo-92Kp")
        );

        // Tester checklist (Tab 4): Retail Web is mid-test — pre-reqs done, some during-test done.
        foreach (var code in new[] { "PRE-01", "PRE-02", "PRE-03", "PRE-04", "PRE-05", "PRE-06", "DUR-01", "DUR-02", "DUR-03" })
            db.ChecklistTicks.Add(new ChecklistTickRecord { Id = Guid.NewGuid(), EngagementId = retail.Id, Code = code, Done = true });

        db.SaveChanges();
    }
}
