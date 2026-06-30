# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Current state

**Phase 2 (Design) complete; Phase 3 (Implementation) — the Blazor app is built, hardened, and furnished, all merged to `main`.** See `IMPLEMENTATION_PLAN.md` for the per-phase status.

| Phase | Stage | Status |
|-------|-------|--------|
| 1 | Requirements | ✅ `PEMP_SRS_v1.0_Master.docx` (released baseline) |
| 2 | Design | ✅ `DESIGN_PLAN.md` v0.3, `design/*`, prototype, compliance |
| 3 | Implementation | ✅ domain + EF persistence + Blazor Server UI + Entra wiring + Bicep IaC. Findings register/record, evidence, retest pass/fail child, masked credentials, peer-review QA gate, light/dark theme, drawer-rail engagement view, ⌘K palette, role rollup tabs, portfolio search, live CVSS scoring, signed-URL evidence download, comms log — all built |
| 4–6 | Testing / Security / Deploy | 🟡 75 tests green (18 domain + 57 infra); 2 QA sweeps + a 12-dimension fleet review applied. **Deferred to prod phase:** EF migrations, CI pipeline, private-endpoint network posture, scale-out (DbContextFactory + Redis/SignalR), full integration/authz matrix |

> ✅ **Built on .NET 10** (Homebrew `dotnet` is 10.x; **net10.0**, LTS). `dotnet test` = **75 tests green** (18 domain guard/audit + 57 infrastructure persistence/scope/schema/evidence/comms), 0 code warnings. Runs end-to-end on SQLite; sign-in is **local ASP.NET Core Identity (email/password + authenticator-app TOTP 2FA)** for dev, and **Entra SSO + Azure SQL activate via config** (see below).
>
> **Defining trait stays enforcement:** all actions route through the domain guards in `src/Pemp.Domain/Engagement.cs` — a failed guard changes and persists nothing. Enforcement now reaches the **write path** too: every mutating store method re-derives object/role scope server-side via `CallerContext` (anti-BOLA + separation-of-duties), not just the UI. The HMAC-keyed hash-chained audit append is atomic with each transition, append-only at the data layer (EF interceptor), verified on read, and the key is Key-Vault-sourced + fail-closed outside Development.

### Repository structure
```
PEMP_SRS_v1.0_Master.docx   Requirements baseline (Phase 1)
DESIGN_PLAN.md              Experience & UI design plan (v0.3)
IMPLEMENTATION_PLAN.md      SDLC phase tracker + exit criteria
README.md                   Human entry point: what PEMP is, build/test/run, dev sign-in, doc links
design/                     Phase-2 artifacts
  architecture.md             layered backend, Blazor-Server front-end decision, data model
  state-machine.md / .svg     the enforced engagement state machine (the core)
  tokens.css                  design tokens (dark-first) — UI theme source of truth
  prototype/index.html        self-contained clickable prototype (open in a browser)
  compliance/                 traceability.md · dpia.md · threat-model.md
infra/                      Bicep IaC (UK Azure) + sample parameters
docs/DEMO.md                The 5-minute walkthrough + local sign-in (5 dev logins + TOTP)
docs/azure-entra-setup.md   The user-only Azure/Entra setup steps (app reg, consent, deploy)
PEMP.sln                    Solution
Directory.Build.props       Shared build settings (net10.0, latest C#, nullable, warnings-as-errors)
src/Pemp.Domain/            Domain core: Engagement state machine + guards, hash-chained Audit, Result
src/Pemp.Infrastructure/    EF Core (SQLite local / Azure SQL), EfAuditChain, EngagementStore, DemoSeeder
src/Pemp.Web/               Blazor Web App (interactive server): My-Turn, engagements, spine, gated actions
tests/Pemp.Domain.Tests/    xUnit — encodes the guard table + audit invariants (18)
tests/Pemp.Infrastructure.Tests/  xUnit — EF persistence, object-level scope, schema, audit-chain, evidence, comms (57)
```
`Pemp.Web` calls `EngagementStore` / `CommsStore` (in Infrastructure) directly. A dedicated `Pemp.Application` use-case layer remains the clean next refactor per `design/architecture.md §6` (deferred — see the fleet backlog in memory).

## Environment setup

- **.NET 10 SDK** — installed via Homebrew (`brew install dotnet`). `dotnet` is on PATH; if a tool can't find the runtime, `export DOTNET_ROOT="/opt/homebrew/opt/dotnet/libexec"`. Verify with `dotnet --version`.
- **A web browser** — for the running app and the self-contained `design/prototype/index.html`.
- **(Cloud only)** `az` CLI for the Bicep deploy; an Azure subscription + Entra tenant admin — see `docs/azure-entra-setup.md`.

## Build / test / run

```bash
dotnet build PEMP.sln                 # strict: code warnings are errors (NuGet audit advisories stay warnings)
dotnet test                           # 75 tests — domain guard table + audit (18), infra persistence/scope/schema/evidence/comms (57)
dotnet run --project src/Pemp.Web     # the app → open the printed URL (SQLite, seeded; run in Development)
open design/prototype/index.html      # the static clickable prototype
```
Local demo needs no cloud: sign in with one of the seeded dev logins (local ASP.NET Core Identity, email/password + authenticator-app TOTP — see `docs/DEMO.md`). For Entra SSO + Azure SQL, set `UseSqlite:false`, a SqlServer `ConnectionStrings:Pemp`, and `AzureAd:*` (per the setup guide); the app auto-falls back to **local Identity** when `AzureAd:ClientId` is blank.

To read the SRS as text (it is a Word `.docx`, i.e. a zip of XML):
```bash
unzip -p PEMP_SRS_v1.0_Master.docx word/document.xml \
  | python3 -c "import sys,re; x=re.sub(r'</w:p>','\n',sys.stdin.read()); print(''.join(re.findall(r'<w:t(?: [^>]*)?>(.*?)</w:t>',x)))"
```

## What PEMP is

The **Pentest Engagement Management Platform (PEMP)** is a secure web app that runs the end-to-end penetration-test engagement lifecycle for **Acme Cyber Assurance** (the client), built by **CloudKonsult Limited**. It replaces an ad-hoc mix of email, spreadsheets, manual documents, and a shared evidence folder with a single guarded system of record.

**The defining product characteristic is enforcement, not record-keeping.** An engagement physically cannot advance past a stage until that stage's preconditions are met — e.g. testing cannot begin without a signed Statement of Work and verified access. The engagement state machine with its guard conditions is the core of the product; treat guard logic as a correctness requirement, not a nicety.

**PEMP holds exactly the data an attacker most wants** — test credentials, evidence, and live unremediated vulnerabilities for client applications. Security and auditability are first-order requirements that drive the design, not features bolted on later.

## Confirmed tech stack (baselined in SRS §9, do not re-litigate)

- **Hosting:** Microsoft Azure, **UK region only** — no data egress outside the approved boundary.
- **App / API:** ASP.NET Core (C#).
- **Front end:** **Blazor Web App (interactive Server)** — ratified in Phase 3 and shipped (`design/architecture.md §2`). Keeps one language (C#) end-to-end and shares the domain types with no separate API; the design tokens (`design/tokens.css`) drive the theme. (Supersedes the earlier "SPA, React-vs-Blazor-WASM TBD" framing.)
- **Data:** Azure SQL Database with Entity Framework Core.
- **Identity:** Microsoft Entra ID (Acme's tenant) via Microsoft Identity / MSAL, OIDC SSO, **no local password store**. Acme staff are native identities; CloudKonsult delivery staff (Delivery Manager, testers) and external stakeholders are **Entra B2B guests** with scoped least-privilege access. Enforced MFA via Conditional Access.
- **Secrets/keys:** Azure Key Vault with managed identities — no secrets in code or config.
- **Evidence/report storage:** Azure Blob Storage (private, server-side encrypted, no public access; signed short-lived download URLs).
- **Async:** Azure Service Bus / Functions or hosted background services (report generation and notifications run async).
- **Caching:** Azure Cache for Redis. **Observability:** Azure Monitor + Application Insights. **Edge:** WAF (App Gateway / Front Door).
- PEMP is a **standalone app and data plane** — no shared tenancy or data with CloudShieldSecure (CSS).

## Domain model: roles and navigation

Five roles, each restricted to **≤6 primary tabs** (a hard usability requirement, NFR-USA-01):

| Role | Responsibility |
|------|----------------|
| **Acme Cyber Assurance Officer** | Raises requests, tracks progress, signs off SoWs, receives reports. |
| **Delivery Manager** (contractor) | Receives requests, assigns testers from the capacity board, reviews Project SoWs. |
| **Penetration Tester** | Scoping, assessment, drafts SoW, verifies access, records findings, reports, retests. |
| **Application Stakeholder** | Dev/business contact: provides assessment input, views findings for *their own app only*, requests retests. |
| **System Administrator** | Manages users/roles (via Entra groups), templates, integrations, audit log. |

Every engagement carries a single guided **"flow" view** — a stage indicator that always shows the current stage, who owns the next action, and what it is. The same engagement tells a consistent story to every role while exposing only that role's controls.

## The engagement lifecycle (the state machine to implement)

15 steps, with the key enforced gates. Two paths exist: **BAU** (routine/recurring) and **Project** (release-tied, requires formal review). Retests spawn a **child engagement** linked to the original.

1. CA identifies need (BAU or Project) → request intake
2. CA asks Delivery Manager to assign a tester
3. Delivery Manager assigns from capacity board
4. Scoping call / app demo scheduled
5. Assessment form completed (conditional questions by app type; stakeholders fill async)
6. Tester drafts SoW + prerequisites/test-account access
7. **SoW sign-off gate** — Project: DM review → Acme sign-off; BAU: tester issues directly to Acme. **Progression to execution is blocked until SoW is signed** (FR-SOW-06)
8. Acme chases access tickets; advance notice + access check before start
9. **Access-verified gate** — tester verifies access, sends IR (Incident Response) advance notice, starts test
10. Testing (~7–14 days); evidence collected; end-of-test notice
11. Findings summary sent to Acme CA same day (auto-drafted from the register)
12. Draft report → peer-review/QA gate before release
13. Post-test call with dev/business
14. Final report + vulnerability register produced and stored (immutable final artifacts)
15. Retest requested after fixes → child engagement re-verifies only in-scope findings (pass/fail each) → retest report with before/after diff

**Findings → live vulnerability register:** each finding is entered once (severity, CVSS vector/score, asset, evidence, remediation) and flows automatically into a consolidated live register. Finding status: open / remediated / accepted-risk / retest-pending / closed.

## Security model (mandatory — §6, these drive the design)

- **Authorization is the dominant risk.** Enforce server-side RBAC on *every* endpoint and **object-level authorization on every record access** (anti-BOLA/IDOR). Engagement/tenant isolation must be enforced at the data layer, not just the UI. Testers see only their assigned engagements.
- **Test credentials:** vault-backed (Key Vault) or envelope-encrypted (AES-256-GCM, KMS keys); masked in UI; never logged; time-boxed with revocation after the engagement.
- **Re-authentication** required for privileged actions: credential view, report download, admin.
- **Audit log is append-only, tamper-evident (hash-chained)**, protected from modification by application *and* administrator roles alike. Captures actor, action, before/after state, timestamp, source.
- **Evidence/reports:** private encrypted blob storage, signed short-lived URLs, every download logged, re-auth gate on sensitive downloads.
- **Secure SDLC:** SAST/DAST/dependency/container scanning as CI quality gates; threat model maintained design→release; dogfood internal tooling (RedForce AI / Nuclei / Trivy) against the platform.
- **Compliance:** UK data residency, UK GDPR (data minimisation, DPIA support, subject rights), ISO/IEC 27001 alignment.

## Requirement ID conventions

The SRS uses stable IDs you should reference in code, commits, and design docs for traceability:
- Functional: `FR-<AREA>-NN` where AREA ∈ AUTH, REQ, ASG, SCO, SOW, ACC, EXE, FND, REP, RET, DOC, NOT, ANL, AUD, ADM
- Non-functional: `NFR-<AREA>-NN` (PER, AVL, USA, CMP, MNT)
- Security: `SEC-<AREA>-NN` (IAM, AZN, CRD, DAT, EVD, AUD, NET, SDL, INS, LIF)
- Constraints `CON-NN`, Assumptions `ASM-NN`

Priorities are **Must / Should / Could** — implement Musts first.
