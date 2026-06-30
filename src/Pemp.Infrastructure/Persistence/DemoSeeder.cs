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

    public static void Seed(PempDbContext db, Func<DateTimeOffset> clock)
    {
        if (db.Engagements.Any()) return;
        var chain = new EfAuditChain(db);
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

        // Retail Web — Project, in the testing/findings window.
        var retail = Engagement.Raise("ENG-2026-0408", EngagementType.Project, "system", chain, clock);
        retail.RouteToDeliveryManager(dm);
        retail.AssignTester(TesterPatel, dm);
        retail.CompleteAssessment("R. Patel");
        retail.ReviewSowAsDeliveryManager(dm);
        retail.SignSow(acme, reAuthenticated: true);
        retail.VerifyAccess("R. Patel");
        retail.SendIrNotice("R. Patel");
        retail.EndTest("R. Patel");
        db.Engagements.Add(EngagementRecord.FromDomain(retail, "Retail Web", "High", "R. Patel"));

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

        // Mobile App — BAU, at Scoping (assessment questionnaire to be completed).
        var mobile = Engagement.Raise("ENG-2026-0421", EngagementType.Bau, "system", chain, clock);
        mobile.RouteToDeliveryManager(dm);
        mobile.AssignTester(TesterLee, dm);
        db.Engagements.Add(EngagementRecord.FromDomain(mobile, "Mobile App", "Medium", "S. Lee"));

        // Partner Portal — freshly raised, at Intake (DM to route).
        var intake = Engagement.Raise("ENG-2026-0422", EngagementType.Bau, "system", chain, clock);
        db.Engagements.Add(EngagementRecord.FromDomain(intake, "Partner Portal", "Medium", null));

        // Quote Engine — routed, awaiting tester assignment (DM's turn).
        var assign = Engagement.Raise("ENG-2026-0423", EngagementType.Project, "system", chain, clock);
        assign.RouteToDeliveryManager(dm);
        db.Engagements.Add(EngagementRecord.FromDomain(assign, "Quote Engine", "High", null));

        // Live register (FR-FND): findings for the engagements that have reached testing.
        FindingRecord F(Guid eng, string title, Severity sev, string cvss, string asset, FindingStatus status)
            => new() { Id = Guid.NewGuid(), EngagementId = eng, Title = title, Severity = sev, Cvss = cvss, Asset = asset, Status = status };
        var fSqli = F(retail.Id, "SQLi in /claims search", Severity.High, "8.1", "Web", FindingStatus.RetestPending);
        var fXss = F(retail.Id, "Stored XSS in note field", Severity.High, "7.4", "Web", FindingStatus.Open);
        db.Findings.AddRange(
            F(retail.Id, "Auth bypass via JWT confusion", Severity.Critical, "9.1", "API", FindingStatus.Open),
            fSqli, fXss,
            F(retail.Id, "Verbose error disclosure", Severity.Medium, "5.3", "API", FindingStatus.Remediated),
            F(retail.Id, "Missing cookie security flags", Severity.Low, "3.1", "Web", FindingStatus.Open),
            F(broker.Id, "Insecure direct object reference", Severity.High, "7.7", "Web", FindingStatus.RetestPending),
            F(broker.Id, "Weak password policy", Severity.Medium, "4.8", "Web", FindingStatus.Closed)
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

        // Tester checklist (Tab 4): Retail Web is mid-test — pre-reqs done, some during-test done.
        foreach (var code in new[] { "PRE-01", "PRE-02", "PRE-03", "PRE-04", "PRE-05", "PRE-06", "DUR-01", "DUR-02", "DUR-03" })
            db.ChecklistTicks.Add(new ChecklistTickRecord { Id = Guid.NewGuid(), EngagementId = retail.Id, Code = code, Done = true });

        db.SaveChanges();
    }
}
