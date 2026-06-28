# PEMP — Threat Model (skeleton)

> Maintained design→release per `SEC-SDL-03`. STRIDE over PEMP's trust boundaries.
> PEMP holds "exactly the data an attacker most wants" — credentials, evidence, live
> unremediated vulns — so authorization is the dominant risk class.

## 1. Assets (what we protect)

- Test credentials (`SEC-CRD`) — highest value.
- Findings / evidence / live vulnerability register (`SEC-EVD`, `FR-FND`).
- Final reports & immutable artifacts (`FR-DOC-02`).
- The audit chain's integrity (`SEC-AUD-01`).
- Engagement isolation (one engagement/tenant cannot see another, `SEC-AZN-03`).

## 2. Trust boundaries

```
Browser (SPA, MSAL) ──TLS──▶ WAF ──▶ API ──▶ App/Domain ──▶ SQL / KeyVault / Blob / Bus
   user + Entra token            (SEC-NET-02)   RBAC + obj-auth      isolation + encryption
```
External actors: Acme staff (native), contractor testers & DM (B2B guests),
application stakeholders (B2B guests, least-technical), System Admin. Each is a
distinct privilege boundary.

## 3. STRIDE

| Threat | Example against PEMP | Mitigation | Trace |
|--------|----------------------|-----------|-------|
| **S**poofing | stolen session / impersonate signer | Entra OIDC + enforced MFA + step-up re-auth on privileged actions | `SEC-IAM-02/04` |
| **T**ampering | edit a finding's severity / forge audit | append-only hash-chained audit atomic with action; immutable artifacts | `SEC-AUD-01`, `FR-DOC-02` |
| **R**epudiation | "I didn't release that report" | every action logged with actor/source/before-after; re-auth at gates | `FR-AUD-02`, `SEC-IAM-04` |
| **I**nfo disclosure | BOLA/IDOR to another engagement's vulns/creds | RBAC + object-level auth + data-layer isolation; field-level visibility; masked creds | `SEC-AZN-01/02/03/04`, `SEC-CRD-02` |
| **D**enial of service | flood API / exhaust report workers | WAF + DDoS, rate limits, async workers isolated from request path | `SEC-NET-02`, `NFR-PER-03` |
| **E**levation | guest escalates to admin via a missing check | server-side RBAC on every endpoint; roles = Entra groups, not client-set | `SEC-AZN-01`, `FR-ADM-01` |

## 4. Abuse cases (insider & misuse)

| Abuse case | Detection |
|------------|-----------|
| Tester bulk-exports evidence before leaving | bulk-export alert (`SEC-INS-02`, D §7.2) |
| Off-hours credential reveal spike | off-hours/credential-spike alert (`SEC-INS-02`) |
| Access attempt on an unassigned engagement | out-of-scope-access alert + blocked by isolation (`SEC-INS-01`, `SEC-AZN-03`) |
| Admin tries to alter the audit log | chain is admin-proof; integrity verify detects gaps (`SEC-AUD-01`) |
| Repeated auth failures (guest account) | auth-failure cluster alert (`FR-ANL-03`) |

## 5. Anti-patterns explicitly rejected

- UI-only authorization (must be enforced at data layer).
- Secrets in code/config (Key Vault + managed identity only, `SEC-DAT-03`).
- Logging credentials or signed URLs.
- Non-atomic audit writes (chain must never be half-written).

## 6. Living-document actions

- [ ] Per-endpoint authorization test matrix (RBAC × object-level) as CI gate.
- [ ] DAST/SAST/dependency/container scans wired as CI quality gates (`SEC-SDL-01`).
- [ ] Dogfood RedForce AI / Nuclei / Trivy against PEMP each release (`SEC-SDL-02`).
- [ ] Re-review this model at each phase boundary and on any new trust boundary.
