# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Current state

**Phase 2 (Design) substantially complete; Phase 3 (Implementation) in progress — at the domain core.** See `IMPLEMENTATION_PLAN.md` for the per-phase status and exit criteria.

| Phase | Stage | Status |
|-------|-------|--------|
| 1 | Requirements | ✅ `PEMP_SRS_v1.0_Master.docx` (released baseline) |
| 2 | Design | ✅ `DESIGN_PLAN.md` v0.3, `design/architecture.md`, `design/state-machine.md`, `design/prototype/`, `design/compliance/*` |
| 3 | Implementation | 🟡 domain core landed (`src/Pemp.Domain` + tests) → application → API → infrastructure next |
| 4–6 | Testing / Security / Deploy | ⬜ not started |

> ✅ **Domain core verified.** First `dotnet build` / `dotnet test` (2026-06-29, .NET 8.0.422) passes clean: 0 warnings (warnings-as-errors) and **15/15 guard tests green**. The first run surfaced — and fixed — a real dead-state bug in the peer-review reject loop (`GenerateDraft` can now re-draft from `Report` to recover a returned report). Next gate is adding the application layer (see `design/architecture.md §6`).

### Repository structure
```
PEMP_SRS_v1.0_Master.docx   Requirements baseline (Phase 1)
DESIGN_PLAN.md              Experience & UI design plan (v0.3)
IMPLEMENTATION_PLAN.md      SDLC phase tracker + exit criteria
design/                     Phase-2 artifacts
  architecture.md             layered backend, SPA decision, data model
  state-machine.md / .svg     the enforced engagement state machine (the core)
  tokens.css                  design tokens (dark-first) — SPA theme source
  prototype/index.html        self-contained clickable prototype (open in a browser)
  compliance/                 traceability.md · dpia.md · threat-model.md
PEMP.sln                    Solution
Directory.Build.props       Shared build settings (net8.0, C#12, nullable, warnings-as-errors)
src/Pemp.Domain/            Domain core: Engagement state machine + guards, hash-chained Audit, Result
tests/Pemp.Domain.Tests/    xUnit — encodes the guard table + audit invariants
```
The application, API, and infrastructure projects in the structure (`Pemp.Application/Api/Infrastructure`) are planned but **not yet created** — build order is Musts-first per `design/architecture.md §6`.

## Environment setup

- **.NET 8 SDK (LTS)** — required to build/test the C# solution. Not currently installed on this machine. macOS: `brew install --cask dotnet-sdk`, or the official installer at <https://dotnet.microsoft.com/download/dotnet/8.0>. Verify with `dotnet --version`.
- **A web browser** — the prototype (`design/prototype/index.html`) is fully self-contained; just open it. No Node/build step.
- **Python 3** — only for reading the SRS `.docx` (snippet below). Already present on macOS.

## Build / test / run

```bash
dotnet build PEMP.sln                 # strict: warnings are errors (Directory.Build.props)
dotnet test                           # runs Pemp.Domain.Tests — guard table + audit-chain invariants
dotnet run --project src/Pemp.Api     # once the API layer exists (not yet created)
open design/prototype/index.html      # the clickable prototype
```

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
- **Front end:** **SPA** — decided in Phase 2 (`design/architecture.md`, `DESIGN_PLAN §16`), driven by the optimistic-UI / ⌘K / drawer-heavy model. The React-vs-Blazor-WASM choice *within* the SPA posture is the one item still to ratify.
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
