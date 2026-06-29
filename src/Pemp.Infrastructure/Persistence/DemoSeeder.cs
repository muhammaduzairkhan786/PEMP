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
        pay.AssignTester(TesterLee, dm);
        pay.CompleteAssessment("S. Lee");
        pay.SignSow(acme, reAuthenticated: true);
        db.Engagements.Add(EngagementRecord.FromDomain(pay, "Payments API", "Medium", "S. Lee"));

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

        db.SaveChanges();
    }
}
