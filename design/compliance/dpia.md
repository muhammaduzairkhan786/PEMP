# PEMP — Data Protection Impact Assessment (DPIA) Starter

> UK GDPR Art. 35 DPIA scaffold for `NFR-CMP-02`. This is a **starter** to be
> completed with Acme's DPO before processing begins; it captures the high-risk
> processing PEMP performs and the mitigations already designed in.

## 1. Why a DPIA is required

PEMP processes data that is high-risk by nature: **live, unremediated vulnerabilities
and test credentials for client applications** (CLAUDE.md: "exactly the data an
attacker most wants"), plus identifiable contacts (Acme staff, contractor testers,
application stakeholders). High-risk triggers met: large-scale sensitive/security
data, systematic processing, novel technology.

## 2. Data inventory

| Data category | Examples | Subjects | Sensitivity |
|---------------|----------|----------|-------------|
| Identity | name, email, Entra object id, role | Acme staff, testers, stakeholders | Personal |
| Engagement metadata | app, scope, dates, SLA | — | Business-confidential |
| Test credentials | service accounts, tokens | (system) | **Secret** |
| Findings/evidence | vulns, CVSS, screenshots, logs | indirectly subjects of tested apps | **High — security-sensitive** |
| Audit log | actor, action, before/after, source | all actors | Personal + security |

## 3. Lawful basis & principles

- **Lawful basis:** legitimate interests (security assurance) / contract with Acme;
  confirm per-category with DPO.
- **Data minimisation** (`NFR-CMP-02`): collect only what each role needs; field-level
  visibility matrix (DESIGN_PLAN §4) enforces need-to-know.
- **Storage limitation:** retention & secure-deletion per data class (`FR-DOC-04`,
  `SEC-LIF-01/02`); credentials purged on engagement close (`SEC-CRD-03`).
- **Residency:** all data in a UK Azure region, no egress (`NFR-CMP-01`).

## 4. Risks → mitigations (already designed)

| Risk | Likelihood/Impact | Mitigation | Trace |
|------|-------------------|-----------|-------|
| Unauthorized access to vulns/creds (BOLA/IDOR) | Med / **High** | RBAC + object-level auth + data-layer isolation | `SEC-AZN-01/02/03` · A §3.1 |
| Credential leakage | Low / **High** | vault-backed, masked, never logged, time-boxed, re-auth to reveal | `SEC-CRD-01/02/03` · A §3.2 |
| Evidence/report exfiltration | Med / High | private encrypted blob, signed short-lived URLs, re-auth download, every download logged | `SEC-EVD-01/02/03` · A §3.3 |
| Tampering with the record | Low / High | append-only hash-chained audit, atomic with action, admin-proof | `SEC-AUD-01` · A §3.4 |
| Insider misuse / anomalous access | Med / High | security-event monitor (bulk export, off-hours, out-of-scope) | `SEC-INS-02/03` · D §7.2 |
| Excessive data exposure to a role | Med / Med | least-privilege tabs, field-level visibility matrix | `SEC-AZN-04` · D §4 |
| Subject-rights handling | Low / Med | data inventory + retention enable access/erasure responses | `NFR-CMP-02` |

## 5. Data-subject rights

Access, rectification, erasure (subject to legal-hold on security records), and
restriction are supported via the data inventory (§2) and retention policy (§3).
Audit records may be retained under legitimate interest / legal obligation even where
other data is erased — document the balancing test with the DPO.

## 6. Outstanding actions (for DPO sign-off)

- [ ] Confirm lawful basis per data category.
- [ ] Set concrete retention periods per data class (feeds `FR-DOC-04` config, D §6.2).
- [ ] Confirm international-transfer posture = none (UK-only) and document.
- [ ] Define legal-hold rules for audit vs erasure requests.
- [ ] DPO + Acme sign-off before go-live.
