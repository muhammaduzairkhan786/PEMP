# PEMP — SDLC Implementation Plan (Phase 2 → Phase 3+)

Tracks the project through the SDLC defined in `CLAUDE.md` / SRS §1.5. Each phase
lists its deliverables and exit criteria. The **engagement state machine is the
core** — built and tested first (CLAUDE.md: "treat guard logic as a correctness
requirement").

> **Build status:** the Phase-3 vertical slice is **built and runs** on **.NET 10**
> (`Directory.Build.props` targets `net10.0`, still LTS). `dotnet test` is green —
> **38 tests** (15 domain guard/audit + 23 infrastructure persistence). The
> TypeScript prototype (`design/prototype/`) remains as the static design reference.

---

## Phase status

| Phase | SDLC stage | Status |
|-------|-----------|--------|
| 1 | Requirements | ✅ complete — `PEMP_SRS_v1.0_Master.docx` (released baseline) |
| 2 | Design | ✅ complete — `DESIGN_PLAN.md` v0.3, `design/architecture.md`, `design/state-machine.md`, `design/prototype/`, `design/compliance/*` |
| 3 | Implementation | 🟢 **vertical slice built (.NET 10)** — domain core, EF persistence, Blazor Server UI, config-guarded Entra wiring, Bicep IaC. `Pemp.Application` / `Pemp.Api` layers are **planned-only** (not yet built; see below) |
| 4 | Testing & QA | 🟡 **38 tests green** (domain guard table + audit, infra persistence/scope/audit-chain); integration/authz matrix + DAST/SAST gates still to add |
| 5 | Security review & hardening | ⬜ threat-model verification, pen-test (dogfood) |
| 6 | Deployment | ⬜ Bicep authored (`infra/`); Azure UK-region deploy + CI/CD still to run |

---

## Phase 2 close-out (remaining design tasks)

- [x] Front-end stack ratified: **Blazor Web App (interactive Server)** — see
  `design/architecture.md §2`. (The earlier SPA / React-vs-Blazor-WASM question is closed.)
- [ ] Wireframe set (`DESIGN_PLAN §16`) — can derive from `design/prototype/`.
- [ ] DPO sign-off on `design/compliance/dpia.md`.

## Phase 3 — Implementation (layered, Musts first)

Build order mirrors `design/architecture.md §6`. **Built so far** (✅) vs **planned** (⬜):

1. ✅ **Domain core** — `src/Pemp.Domain`: the `Engagement` aggregate, the `Stage`
   state machine with explicit transition **guards** (mirrors `design/state-machine.md`),
   the hash-chained **audit** append, `Result` type.
   Tests: `tests/Pemp.Domain.Tests` encode the guard table + audit invariants (15).
2. ✅ **Infrastructure** — `src/Pemp.Infrastructure`: EF Core (SQLite local / Azure SQL),
   `EngagementStore` + `EfAuditChain`, object-level scoping for engagement isolation
   (`SEC-AZN-03`), assessment/access/checklist/evidence stores, `DemoSeeder`.
   Tests: `tests/Pemp.Infrastructure.Tests` cover persistence, scope and the audit chain (23).
3. ✅ **Web (UI + identity)** — `src/Pemp.Web`: Blazor Web App (interactive Server) —
   My-Turn home, engagement spine, gated actions + gate-ceremony modal, findings register,
   evidence, masked credentials, retest child, analytics; local ASP.NET Core Identity
   (email/password + authenticator TOTP) for dev with **Entra SSO activated by config**.
   For the demo, `Pemp.Web` calls `EngagementStore` (Infrastructure) directly.
4. ⬜ **Application layer** — `src/Pemp.Application` *(planned-only, not built)*: extract
   use-case handlers (SignSoW, VerifyAccess, AddFinding…), the object-level authorization
   filter (`SEC-AZN-02`) and re-auth assertion (`SEC-IAM-04`) out of `EngagementStore`.
5. ⬜ **API layer** — `src/Pemp.Api` *(planned-only, not built)*: ASP.NET Core endpoints,
   per-endpoint RBAC policies (`SEC-AZN-01`), MSAL/Entra auth, validation, rate limits.

## Phase 4 — Testing & QA

- Unit (domain, ≥ guard table coverage) · integration (API + EF, in-memory/Testcontainers)
- **Authorization test matrix** (RBAC × object-level) as a CI gate — the dominant risk
- SAST/DAST/dependency/container scans as CI quality gates (`SEC-SDL-01`)

## Build & run

```bash
dotnet build PEMP.sln                # strict: code warnings are errors
dotnet test                          # 38 tests — domain guard table + audit, infra persistence/scope
dotnet run --project src/Pemp.Web    # the demo app → open the printed URL (SQLite, seeded)
```

Target: **.NET 10 (LTS)**. See `Directory.Build.props` for shared settings, and
`docs/DEMO.md` for the local sign-in (5 dev logins + authenticator TOTP) and walkthrough.
