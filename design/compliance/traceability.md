# PEMP — Requirement Traceability Matrix

Maps every SRS requirement area to where it is addressed across the design
artifacts. **D** = `DESIGN_PLAN.md` (experience), **A** = `architecture.md`,
**S** = `state-machine.md`, **P** = `design/prototype/`. This is the audit trail a
regulated insurer expects (`NFR-CMP-03` / ISO 27001 alignment).

> Coverage baseline: SRS has ~80 requirements (58 FR, 11 NFR, 11 SEC, 5 CON, 3 ASM).
> All functional/security areas are now traced. Infra/SDLC requirements are owned by
> the build/platform phase (A §5) and explicitly scoped out of the UX layer.

## Functional (FR)

| Area | Requirements | Addressed in |
|------|-------------|-------------|
| AUTH | 01–07 | D §3/§6.1 (Entra roles, B2B, CA cues) · A §2/§3.1 · P (re-auth) |
| REQ | 01–05 | D §5 Phase 1 · A §4 · S #1–2 |
| ASG | 01–05 | D §5 Phase 2 (capacity board) · S #3 |
| SCO | 01–05 | D §5 Phase 3 (two-lens assessment) · A §4 · S #4 |
| SOW | 01–06 | D §5 Phase 4 + §10 ceremony · A §3.1 (SignSoW) · S #5 · P (gate) |
| ACC | 01–05 | D §5 Phase 5 (prereq def, masked creds, auto-revoke) · A §3.2 · S #6 |
| EXE | 01–05 | D §5 Phase 6 (test cockpit, comms) · S #7–8 |
| FND | 01–06 | D §5 Phase 7 (capture + live register, CVSS) · A §4 · P (register) |
| REP | 01–06 | D §5 Phase 8 (QA gate) · A §3.3 · S #9–11 |
| RET | 01–04 | D §5 Phase 9 (request initiator + diff) · S #12 |
| DOC | 01–04 | D §6.2 (retention) · A §3.3 (immutable, secure-delete) |
| NOT | 01–03 | D §9 My-Turn (one attention system) · §7.1 |
| ANL | 01–05 | D §7 (exceptions, security-event monitor, portfolio analytics) |
| AUD | 01–03 | D §6.4 (search/export) · A §3.4 (hash chain) · S invariants |
| ADM | 01–04 | D §6 (users, config, integrations, audit) · A §3.1/§5 |

## Non-functional (NFR)

| Area | Requirements | Addressed in |
|------|-------------|-------------|
| PER | 01–03 | D §15 (optimistic UI, async) · A §3.5 |
| AVL | 01–02 | A §5 (HA, backups/DR — build phase) |
| USA | 01–04 | D §1/§3 (≤6 tabs, spine, My-Turn) · §13/§15 (WCAG AA) · P |
| CMP | 01–03 | A §1.4/§5 (UK residency) · `dpia.md` (UK GDPR) · this matrix (ISO 27001) |
| MNT | 01–03 | A §1.5/§5 (layered, IaC, observability) |

## Security (SEC)

| Area | Requirements | Addressed in |
|------|-------------|-------------|
| IAM | 01–04 | A §2/§3.1 (Entra SSO, MFA, CA, step-up re-auth) · D §8 |
| AZN | 01–04 | A §3.1 (RBAC + object-level + isolation) · D §4 matrix |
| CRD | 01–03 | A §3.2 · D §5 Phase 5 (masked, time-boxed, auto-revoke) |
| DAT | 01–03 | A §5 (TLS, encryption-at-rest, Key Vault) |
| EVD | 01–03 | A §3.3 · D §5 Phase 7 (encrypted, signed URLs, re-auth download) |
| AUD | 01–02 | A §3.4 · S invariants (atomic chain) · D §6.4 |
| NET | 01–03 | A §5 (Private Endpoints, WAF, hardened hosting) |
| SDL | 01–03 | A §5 (CI security gates, dogfooding, threat model) · `threat-model.md` |
| INS | 01–03 | A §3.1/§5 · D §7.2 (security-event monitor) |
| LIF | 01–02 | A §3.3 · D §6.2 (retention, crypto-shred) |

## Constraints & assumptions

| ID | Status |
|----|--------|
| CON-01..05 | Honoured (UK region, Entra, no CSS tenancy, ITSM via §6.3) |
| ASM-01 | **Resolved** → SPA (A §2, D §16) |
| ASM-02..03 | Carried into build phase |
