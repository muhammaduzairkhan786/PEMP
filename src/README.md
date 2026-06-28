# PEMP — source (Phase 3 implementation)

Layered ASP.NET Core solution (see `design/architecture.md §3`). Build order is
Musts-first; this is the start of the **domain core**.

| Project | Layer | Status |
|---------|-------|--------|
| `Pemp.Domain` | Domain core — `Engagement` aggregate, `Stage` state machine + guards, hash-chained audit (`Audit/`) | ✅ written (build-pending in this sandbox) |
| `Pemp.Application` | Use-cases, object-level authz, re-auth, audit composition | ⬜ next |
| `Pemp.Api` | ASP.NET Core endpoints, RBAC policies, Entra/MSAL | ⬜ |
| `Pemp.Infrastructure` | EF Core, isolation query filter, Key Vault/Blob/Bus | ⬜ |

## Build & test (needs the .NET 8 SDK)

```bash
dotnet test PEMP.sln          # runs Pemp.Domain.Tests
```

> The cloud sandbox used to author this has **no .NET SDK** and the egress policy
> blocks installing one, so these projects are written review-grade but not yet
> compiled here. They build under CI / any machine with the SDK.

## What the domain enforces

`Engagement` is the state machine: every transition is guarded, an unmet guard
returns `Result.Fail` and mutates nothing, and each successful transition appends a
hash-chained `AuditEntry` atomically. The tests in `tests/Pemp.Domain.Tests` encode
the guard table from `design/state-machine.md` — including the SoW sign-off gate
(re-auth + DM review for Project), the access gate, the QA peer-review gate (with the
reject→draft path and no self-review), and the retest child engagement.
