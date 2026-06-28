# PEMP — SDLC Implementation Plan (Phase 2 → Phase 3+)

Tracks the project through the SDLC defined in `CLAUDE.md` / SRS §1.5. Each phase
lists its deliverables and exit criteria. The **engagement state machine is the
core** — built and tested first (CLAUDE.md: "treat guard logic as a correctness
requirement").

> **Environment note:** this cloud sandbox has **no .NET SDK** and the egress policy
> blocks installing one, so C# here is written review-grade but **not compiled in
> this environment**. It builds and tests under `dotnet test` in Phase-3 CI (see
> commands below). The TypeScript prototype (`design/prototype/`) remains the only
> code runnable in this sandbox.

---

## Phase status

| Phase | SDLC stage | Status |
|-------|-----------|--------|
| 1 | Requirements | ✅ complete — `PEMP_SRS_v1.0_Master.docx` (released baseline) |
| 2 | Design | ✅ substantially complete — `DESIGN_PLAN.md` v0.3, `design/architecture.md`, `design/state-machine.md`, `design/prototype/`, `design/compliance/*` |
| 3 | Implementation | 🟡 **in progress** — domain core (this iteration) → application → API → infrastructure |
| 4 | Testing & QA | ⬜ unit (with domain), integration, authz test matrix, DAST/SAST gates |
| 5 | Security review & hardening | ⬜ threat-model verification, pen-test (dogfood) |
| 6 | Deployment | ⬜ IaC (Bicep), Azure UK region, CI/CD |

---

## Phase 2 close-out (remaining design tasks)

- [ ] Wireframe set (`DESIGN_PLAN §16`) — can derive from `design/prototype/`.
- [ ] Confirm front-end: React vs Blazor-WASM (design recommends SPA posture).
- [ ] DPO sign-off on `design/compliance/dpia.md`.

## Phase 3 — Implementation (layered, Musts first)

Build order mirrors `design/architecture.md §6`:

1. **Domain core** *(this iteration)* — `src/Pemp.Domain`: the `Engagement` aggregate,
   the `Stage` state machine with explicit transition **guards** (mirrors
   `design/state-machine.md`), the hash-chained **audit** append, `Result` type.
   Tests: `tests/Pemp.Domain.Tests` encode the 12-row guard table + audit invariants.
2. **Application layer** — `src/Pemp.Application`: use-case handlers (SignSoW,
   VerifyAccess, AddFinding…), object-level authorization filter (`SEC-AZN-02`),
   re-auth assertion (`SEC-IAM-04`), audit-writer composed atomically with each change.
3. **API layer** — `src/Pemp.Api`: ASP.NET Core endpoints, per-endpoint RBAC
   policies (`SEC-AZN-01`), MSAL/Entra auth, validation, rate limits.
4. **Infrastructure** — `src/Pemp.Infrastructure`: EF Core, global query filter for
   engagement isolation (`SEC-AZN-03`), Key Vault, Blob, Service Bus adapters.

## Phase 4 — Testing & QA

- Unit (domain, ≥ guard table coverage) · integration (API + EF, in-memory/Testcontainers)
- **Authorization test matrix** (RBAC × object-level) as a CI gate — the dominant risk
- SAST/DAST/dependency/container scans as CI quality gates (`SEC-SDL-01`)

## Build & run (Phase-3 CI / a machine with the SDK)

```bash
dotnet build PEMP.sln
dotnet test                       # runs Pemp.Domain.Tests (guard table + audit)
dotnet run --project src/Pemp.Api # once the API layer lands
```

Target: .NET 8 (LTS). See `Directory.Build.props` for shared settings.
