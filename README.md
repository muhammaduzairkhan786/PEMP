# PEMP — Pentest Engagement Management Platform

PEMP is a secure web app that runs the end-to-end penetration-test engagement lifecycle
for **Acme Cyber Assurance**, built by **CloudKonsult Limited**. It replaces an ad-hoc mix
of email, spreadsheets, documents and a shared evidence folder with a single guarded system
of record. Its defining trait is **enforcement, not record-keeping**: an engagement
physically cannot advance past a stage until that stage's preconditions are met — testing
cannot begin without a signed Statement of Work and verified access. The engagement state
machine and its guards are the core of the product, and the append-only, hash-chained audit
trail makes every transition tamper-evident.

## Stack

- **.NET 10**, ASP.NET Core — **Blazor Web App (interactive Server)** front end
  (decision and trade-offs in `design/architecture.md §2`).
- **EF Core** — SQLite locally, **Azure SQL** in the cloud.
- **Microsoft Entra ID** (OIDC SSO) in production; **local ASP.NET Core Identity** for dev.
- Azure UK region only; Key Vault, Blob, Service Bus, App Insights per the SRS (§9).

## Build / test / run

```bash
export DOTNET_ROOT="/opt/homebrew/opt/dotnet/libexec"   # if dotnet can't find the runtime

dotnet build PEMP.sln                 # strict: code warnings are errors
dotnet test                           # 38 tests — 15 domain (guards + audit) + 23 infrastructure
dotnet run --project src/Pemp.Web     # the demo app → open the printed URL (SQLite, seeded)
```

No cloud is needed for the local demo. The app auto-creates and seeds a SQLite database via
real domain transitions (so the audit chain is genuine).

## Local dev sign-in (5 logins + TOTP)

When `AzureAd:ClientId` is not configured, PEMP falls back to local ASP.NET Core Identity:
email/password sign-in followed by **authenticator-app TOTP 2FA** (enrolled on first
sign-in; a direct GET of any app page before enrolment is redirected server-side to the
enrolment screen). The seeded dev logins (password `Pemp!2026`) are:

| Login | Role |
|-------|------|
| `acme@pemp.dev` | Acme CA Officer |
| `dm@pemp.dev` | Delivery Manager |
| `tester@pemp.dev` | Penetration Tester (A. Khan) |
| `stakeholder@pemp.dev` | Application Stakeholder (P. Devlin, scoped to Retail Web) |
| `admin@pemp.dev` | System Administrator |

To activate **Entra SSO + Azure SQL**, set `UseSqlite:false`, a SqlServer
`ConnectionStrings:Pemp`, and `AzureAd:*` — see `docs/azure-entra-setup.md`.

## Documentation

- `docs/DEMO.md` — 5-minute guided walkthrough of the enforcement story.
- `IMPLEMENTATION_PLAN.md` — SDLC phase tracker and exit criteria.
- `docs/azure-entra-setup.md` — Azure / Entra setup (app registration, consent, deploy).
- `design/` — Phase-2 design: `architecture.md`, `state-machine.md`, `tokens.css`,
  the clickable `prototype/`, and `compliance/` (traceability, DPIA, threat model).
- `CLAUDE.md` — orientation for the domain model, roles, lifecycle and security model.
