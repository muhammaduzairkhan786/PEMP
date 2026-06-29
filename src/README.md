# PEMP — source (Phase 3 implementation)

Layered .NET 10 solution (see `design/architecture.md §3`). A runnable demo vertical
slice over the domain state machine; run it with `dotnet run --project src/Pemp.Web`.

| Project | Layer | Status |
|---------|-------|--------|
| `Pemp.Domain` | Domain core — `Engagement` aggregate, `Stage` state machine + guards, hash-chained audit (`Audit/`) | ✅ built · 15 tests |
| `Pemp.Infrastructure` | EF Core (SQLite local / Azure SQL), `EfAuditChain`, `EngagementStore`, assessment/access/checklist/evidence stores, `DemoSeeder` | ✅ built · 6 tests |
| `Pemp.Web` | Blazor Web App (interactive server) — My-Turn, engagements, spine, gated actions, gate-ceremony modal, findings/evidence/checklist/access/analytics, Entra wiring (config-guarded) | ✅ built |
| `Pemp.Application` | Dedicated use-case layer (handlers, object-level authz) — currently folded into `EngagementStore` | ⬜ optional refactor |

The Application/API split from the architecture doc was collapsed for the demo:
`Pemp.Web` calls `EngagementStore` (Infrastructure) directly. Extracting a clean
`Pemp.Application` layer is the next architectural refactor (not demo-blocking).

## Build & test (.NET 10 SDK)

```bash
dotnet build PEMP.sln          # strict: code warnings are errors
dotnet test                    # 21 tests — 15 domain + 6 persistence
dotnet run --project src/Pemp.Web   # the demo app (SQLite, seeded, role switcher)
```

## What the domain enforces

`Engagement` is the state machine: every transition is guarded, an unmet guard returns
`Result.Fail` and mutates nothing, and each successful transition appends a hash-chained
`AuditEntry` atomically. `EngagementStore.ExecuteAsync` preserves that at the data layer
(a failed guard saves nothing). Tests encode the guard table from
`design/state-machine.md` — the SoW sign-off gate (re-auth + DM review for Project), the
access gate, the QA peer-review gate (reject→draft, no self-review), and the retest child.
