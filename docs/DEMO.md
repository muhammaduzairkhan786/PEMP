# PEMP — demo walkthrough

A 5-minute script that shows the defining trait — **enforcement, not record-keeping** —
plus the real AXA SOP workbook wired into the lifecycle.

## Run it

```bash
export DOTNET_ROOT="/opt/homebrew/opt/dotnet/libexec"   # if dotnet can't find the runtime
dotnet run --project src/Pemp.Web
```
Open the printed URL. It auto-creates a SQLite DB and seeds demo engagements via **real
domain transitions** (so the audit chain is genuine). No cloud needed — local ASP.NET Core
Identity (email + password + authenticator-app 2FA) stands in for Entra SSO, which activates
from config in the cloud. Sign in as one of the dev logins below; each enrols an authenticator
on first sign-in.

Seeded engagements:

| Reference | App | Stage | Assigned | Use it to show |
|-----------|-----|-------|----------|----------------|
| ENG-2026-0421 | Mobile App | Scoping | A. Khan | the **assessment questionnaire** |
| ENG-2026-0412 | Claims Portal | SoW | A. Khan | the **sign-off gate ceremony** |
| ENG-2026-0419 | Payments API | Access | A. Khan | the **access requirements matrix** |
| ENG-2026-0408 | Retail Web | Testing/Findings | A. Khan | **findings register + record-finding form + evidence + checklist** |
| ENG-2026-0399 | Broker Portal | Closed | A. Khan | **retest** child + audit verify |

The seeded **tester login** is `tester@pemp.dev` = **A. Khan**, who owns all five showcase
engagements above (one per stage) — so a tester signing in reaches rich content at every
stage, and sees *only* their own assignments, never the full portfolio. The **stakeholder login**
`stakeholder@pemp.dev` = **P. Devlin** is scoped to **Retail Web** and so can view that
app's findings. Acme / Delivery Manager / Admin see the whole portfolio.

Dev logins (local Identity, password `Pemp!2026`): `acme@pemp.dev`, `dm@pemp.dev`,
`tester@pemp.dev`, `stakeholder@pemp.dev`, `admin@pemp.dev`. Each enrols an authenticator
app (TOTP) on first sign-in — a direct GET of any app page before enrolment is **redirected
server-side (302)** to the enrolment screen.

---

## The script

### 1. "Whose turn is it?" — the My-Turn home
Sign in as **Acme CA Officer** (`acme@pemp.dev`). The home splits into **Your turn** (action
required) and **Waiting on others** — computed live from the state machine. Note the
portfolio tiles (Acme sees every engagement).

### 2. The gate ceremony (the signature moment)
Open **Claims Portal**. The **spine** shows SoW current and everything downstream
**locked**. Click **Review & Sign SoW** → a full-focus **attestation modal**: plain-language
summary, the hash-chain trust line, and an **Entra MFA re-auth** step. Confirm → the spine
**advances to Access**, and a new row appears in the **audit trail**.
> Talking point: this isn't a status field — testing literally cannot start until the SoW is
> signed. Every action routes through the domain guards; a failed guard changes nothing.

### 3. The assessment questionnaire (workbook Tab 1)
Sign in as **Tester** (`tester@pemp.dev` = A. Khan). Open **Mobile App** → **Open assessment
form**. Pick an **Application type** (e.g. *API*, *Thick client*, *AI*) and a **hosting**/**network
exposure** — watch the matching **conditional sections appear**. The completeness meter
updates as you answer; everything auto-saves. **Complete & confirm** advances Scoping → SoW.

### 4. Access requirements (workbook Tab 3)
As the **Tester**, open **Payments API** (Access stage). The **access matrix** lists each
environment/asset with a provisioning **status** you can change (App team to provision →
In progress → Provisioned).

### 5. Findings, evidence, checklist (workbook Tab 4 + register)
As the **Tester**, open **Retail Web** (mid-test):
- **Live register** — filter findings by severity; see CVSS + status.
- **Record a finding** — the assigned tester gets an add-finding form (severity, CVSS vector +
  score, asset, title, remediation, status); it flows straight into the live register.
- **Evidence** — artifacts per finding with an **encrypted ✓** badge and a **signed link**
  (re-auth-gated); attach a new one as Tester.
- **Tester checklist** — pre-reqs / during / end-of-test items with progress.

> Scope check: signed in as the tester you see only A. Khan's engagements — never the full
> portfolio. The stakeholder (`stakeholder@pemp.dev`) sees only **Retail Web** and its findings.

### 6. Retest (child engagement)
As **Acme** (or the Retail Web Stakeholder), open **Broker Portal** (closed) → **Request a
retest**. A linked **child engagement** is created (badged "RETEST CHILD") and you land on it.

### 7. Trust — the tamper-evident audit
On any engagement, scroll to **Audit trail** → **Verify chain** → "🔗 verified". Every
transition is hash-chained (actor, action, before→after, time).

### 8. Portfolio analytics
Open **Analytics** — active engagements, open/critical findings, the **severity
distribution**, and per-application worst severity.

---

## What's real vs demo-mode
- **Real:** the engagement state machine + guards (15 unit tests), EF persistence + the store
  (23 infrastructure tests), the hash-chained audit, server-side RBAC + object-level scoping +
  the 2FA-enrolment gate, local Identity sign-in with authenticator 2FA, the conditional
  assessment logic, the full UI.
- **Demo-mode:** SQLite (vs Azure SQL), local Identity (vs Entra SSO), evidence metadata +
  "signed link" mock (vs Blob + real signed URLs). All swap on via config —
  see `docs/azure-entra-setup.md` and `infra/main.bicep`.
