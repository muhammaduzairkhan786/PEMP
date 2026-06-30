# PEMP — Phase-2 Architecture & Design (draft)

> Companion to the SRS (`PEMP_SRS_v1.0_Master.docx`) and the Experience Plan
> (`DESIGN_PLAN.md`). The SRS says *what*; the Experience Plan says *how it feels*;
> this says *how it is built*. Stack is baselined in SRS §9 — not re-litigated here.
> Everything traces to SRS IDs. **[new]** marks proposals beyond the SRS.

---

## 1. Architectural principles

1. **Enforcement at the data layer, not the UI.** The engagement state machine,
   RBAC, object-level authorization and tenant isolation are enforced server-side on
   every request; the UI only *reflects* state (`SEC-AZN-02/03`, guard table
   DESIGN_PLAN §8). A bypassed UI must never bypass a guard.
2. **Least privilege everywhere.** Managed identities for Azure resources, scoped
   Entra B2B guests for contractors/stakeholders, no secrets in code/config
   (`SEC-DAT-03`), no local password store (`SEC-IAM-01`).
3. **Auditable by construction.** State transitions and sensitive reads are
   append-only, hash-chained, and atomic with the action they describe
   (`SEC-AUD-01`, `FR-AUD-02`).
4. **UK data residency.** All compute and data stay in a UK Azure region; no egress
   outside the approved boundary (`NFR-CMP-01`, `CON` boundary).
5. **Modular & layered**, documented APIs, IaC-reproducible (`NFR-MNT-01/03`).

---

## 2. Front end — decision: Blazor Web App (interactive Server)

Resolves the one open stack item (`ASM-01`, DESIGN_PLAN §16). **Status: ratified and
shipped.** The earlier draft kept a SPA / React-vs-Blazor-WASM choice open; the Phase-3
vertical slice was built as a **Blazor Web App running interactive Server components**,
and that is the decision of record.

**Rationale.** PEMP is an internal, enforcement-first system of record, not a
public-scale app, so the SPA trade-offs that mattered most landed on the C# side:

- **One language end-to-end** — the UI shares the `Pemp.Domain` types (state, guards,
  enums) with no DTO duplication and no client/server contract drift.
- **No separate API yet** — interactive Server runs component logic on the server over
  a SignalR circuit, so the demo needs no standalone `Pemp.Api`; pages call the
  store/domain directly. (A `Pemp.Api` can still be extracted later — §6, §3.)
- **Low-latency interactivity + secure-by-default** — server-side render keeps secrets
  (test credentials, evidence) and authorization on the server; the browser never holds
  domain state or long-lived tokens. Object-level authz runs where the data is read.
- **Theming unchanged** — `design/tokens.css` (dark-first, §12) remains the theme source
  of truth; light/dark toggle ships in the layout.

**Trade-offs / caveats.**
- **Circuit + scale-out:** interactive Server holds per-user SignalR circuits with
  server-side UI state, so horizontal scale needs sticky sessions or a backplane (Azure
  SignalR Service / Redis), and a dropped connection must reconnect/rehydrate gracefully.
- **Latency sensitivity:** every interaction is a round-trip, so it suits the internal,
  good-connectivity audience; the optimistic-UI ambition (DESIGN_PLAN) is softened to
  fast server reconciliation rather than client-only optimism.
- The richest "art-piece" motion libraries are JS-first; we lean on CSS tokens/transitions.

- **Auth:** Entra ID (OIDC) via Microsoft Identity Web, activated by config; local
  ASP.NET Core Identity (email/password + authenticator TOTP) stands in for dev.
  **Re-auth step-up** gates privileged actions (`SEC-IAM-04`).
- Heavy work (reports, notifications) stays async with in-progress states (`NFR-PER-01/03`).

---

## 3. Backend — layered ASP.NET Core (C#)

```
┌────────────────────────────────────────────────────────────┐
│ API layer  (ASP.NET Core minimal/controller endpoints)     │
│  · per-endpoint RBAC policy (SEC-AZN-01)                    │
│  · request validation · rate limits · re-auth assertion    │
├────────────────────────────────────────────────────────────┤
│ Application layer  (use-cases / CQRS handlers)             │
│  · EngagementStateMachine — guard predicates (FR-* guards)  │
│  · object-level authorization filter (SEC-AZN-02)           │
│  · audit-chain writer (SEC-AUD-01) — same tx as the change  │
├────────────────────────────────────────────────────────────┤
│ Domain layer  (entities, state, invariants)               │
│  · Engagement, Finding, SoW, Credential, AuditEntry …      │
├────────────────────────────────────────────────────────────┤
│ Infrastructure  (EF Core, Key Vault, Blob, Service Bus)    │
│  · global query filters for engagement isolation (SEC-AZN-03)│
└────────────────────────────────────────────────────────────┘
```

### 3.1 Authorization — the dominant risk (`SEC-AZN`)

Two gates on **every** record access, both server-side:

1. **RBAC policy** per endpoint (`SEC-AZN-01`) — role → allowed operations, role =
   Entra group membership (`FR-ADM-01`).
2. **Object-level authorization** (`SEC-AZN-02`, anti-BOLA/IDOR) — the actor must be
   entitled to *this* engagement/record. Enforced via an EF Core **global query
   filter** keyed to the caller's engagement scope so isolation holds at the data
   layer (`SEC-AZN-03`), not just the controller. Testers see only assigned
   engagements (`SEC-INS-01`); stakeholders only their own app (`FR-AUTH-06`).
   Field-level visibility (credentials vs IR-contact) follows DESIGN_PLAN §4 matrix
   (`SEC-AZN-04`).

```csharp
// Application-layer transition — illustrative, not final
public async Task<Result> SignSoW(Guid engagementId, Actor actor, ReAuthToken mfa)
{
    var eng = await _db.Engagements.FindScopedAsync(engagementId, actor); // SEC-AZN-02/03
    Authorize.Require(actor, eng, Op.SignSoW);                            // SEC-AZN-01
    ReAuth.Verify(mfa, actor);                                           // SEC-IAM-04
    Guard.Require(eng.State == SoW.AwaitSign && eng.SoW.IsSigned == false);// FR-SOW-05/06
    using var tx = await _db.BeginTransactionAsync();
        eng.SignSoW(actor);                       // domain invariant
        _audit.Append(eng, actor, "SoW.Signed", before, after); // SEC-AUD-01 (same tx)
        eng.Transition(to: Stage.Access);         // guard #5, DESIGN_PLAN §8
    await tx.CommitAsync();                        // atomic: state + chain
    return Result.Ok();
}
```

### 3.2 Credentials (`SEC-CRD`)

Vault-backed (Key Vault) or envelope-encrypted (AES-256-GCM, KMS keys); **masked in
UI, never logged**; reveal requires re-auth and is audited; time-boxed with automatic
revocation on engagement close (`SEC-CRD-01/02/03`).

### 3.3 Evidence & reports (`SEC-EVD`, `FR-DOC`)

Private, server-side-encrypted Blob Storage; **signed short-lived URLs** only; every
download logged; re-auth gate on sensitive download (`SEC-EVD-03`). Final report +
register are immutable artifacts (`FR-DOC-02`, `FR-REP-04`) with retention &
secure-deletion per data class (`FR-DOC-04`, `SEC-LIF-01/02`).

### 3.4 Audit chain (`SEC-AUD`, `FR-AUD`)

Append-only table; each row stores `hash = H(prev_hash ‖ canonical(entry))`. Append
is in the **same transaction** as the action, so the chain can never be half-written
(DESIGN_PLAN §10 error path). Protected from modification by application *and* admin
roles. Verification walks the chain; search/export per `FR-AUD-03`.

### 3.5 Async (`NFR-PER-03`)

Report generation and notifications run on Service Bus / Functions or hosted
background services; the UI shows in-progress state and reconciles on completion.

---

## 4. Data model (EF Core) — core entities

```
Engagement(id, ref, type{BAU|Project}, criticality, state, parentId?,
           appId, assignedTesterId, slaClocks…)            FR-REQ-04, FR-RET-02
 ├─ Assessment(engagementId, sections[], completeness)      FR-SCO
 ├─ SoW(engagementId, version, body, signedBy?, signedAt?)  FR-SOW
 ├─ AccessItem(engagementId, kind, ticketRef, status)       FR-ACC
 ├─ Credential(engagementId, ref, vaultUri, expiresAt)      SEC-CRD  (no secret in DB)
 ├─ Finding(engagementId, sev, cvssVector, score, asset,
 │          status, evidenceRefs[], remediation)            FR-FND
 ├─ Artifact(engagementId, kind, blobUri, immutable)        FR-DOC, SEC-EVD
 └─ AuditEntry(seq, engagementId?, actor, action,
              before, after, ts, source, prevHash, hash)    SEC-AUD-01
RegisterView  = consolidated live findings across engagements (FR-FND-03)
User/Role      = projected from Entra groups (no local store) FR-ADM-01, SEC-IAM-01
```

Engagement isolation is enforced by a **global query filter** on every
engagement-scoped entity; the retest child carries `parentId` and inherits scope.

---

## 5. Platform (Azure, UK region) — out of UI scope, in build scope

App Service / Container Apps behind WAF (App Gateway / Front Door, `SEC-NET-02`);
Azure SQL + EF Core; Key Vault w/ managed identity (`SEC-DAT-03`); Blob (private,
encrypted, `SEC-EVD-01`); Service Bus / Functions; Redis cache; Azure Monitor +
App Insights + Sentinel for security logging/alerting (`SEC-INS-03`). Private
networking / Private Endpoints (`SEC-NET-01`), TLS 1.2+ (`SEC-DAT-01`), encryption
at rest / TDE (`SEC-DAT-02`). IaC (Bicep/Terraform) for reproducible environments
(`NFR-MNT-03`). Secure SDLC: SAST/DAST/dependency/container scans as CI gates,
threat model maintained design→release, dogfood RedForce AI / Nuclei / Trivy
(`SEC-SDL-01/02/03`).

---

## 6. Build sequencing (Musts first)

1. Identity + RBAC + object-level auth skeleton (`SEC-AZN`, `SEC-IAM`) — the spine of safety.
2. Engagement state machine + audit chain (`FR-*` guards, `SEC-AUD-01`).
3. Intake → Assignment → Scoping → **SoW gate** → Access gate (the enforced core).
4. Findings + live register + evidence (`FR-FND`, `SEC-EVD`).
5. Reporting + QA gate + retest child (`FR-REP`, `FR-RET`).
6. Admin console + analytics/exceptions + notifications (`FR-ADM`, `FR-ANL`, `FR-NOT`).

See `state-machine.md` for the transition contract and `compliance/traceability.md`
for full SRS coverage.
