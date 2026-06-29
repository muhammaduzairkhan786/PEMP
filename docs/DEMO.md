# PEMP — demo walkthrough

A 5-minute script that shows the defining trait — **enforcement, not record-keeping** —
plus the real AXA SOP workbook wired into the lifecycle.

## Run it

```bash
export DOTNET_ROOT="/opt/homebrew/opt/dotnet/libexec"   # if dotnet can't find the runtime
dotnet run --project src/Pemp.Web
```
Open the printed URL. It auto-creates a SQLite DB and seeds demo engagements via **real
domain transitions** (so the audit chain is genuine). No cloud needed — the top-right
**role switcher** stands in for Entra SSO (which activates from config in the cloud).

Seeded engagements:

| Reference | App | Stage | Use it to show |
|-----------|-----|-------|----------------|
| ENG-2026-0421 | Mobile App | Scoping | the **assessment questionnaire** |
| ENG-2026-0412 | Claims Portal | SoW | the **sign-off gate ceremony** |
| ENG-2026-0419 | Payments API | Access | the **access requirements matrix** |
| ENG-2026-0408 | Retail Web | Testing/Findings | **findings register + evidence + checklist** |
| ENG-2026-0399 | Broker Portal | Closed | **retest** child + audit verify |

---

## The script

### 1. "Whose turn is it?" — the My-Turn home
Land as **Acme CA Officer**. The home splits into **Your turn** (action required) and
**Waiting on others** — computed live from the state machine. Note the portfolio tiles.

### 2. The gate ceremony (the signature moment)
Open **Claims Portal**. The **spine** shows SoW current and everything downstream
**locked**. Click **Review & Sign SoW** → a full-focus **attestation modal**: plain-language
summary, the hash-chain trust line, and an **Entra MFA re-auth** step. Confirm → the spine
**advances to Access**, and a new row appears in the **audit trail**.
> Talking point: this isn't a status field — testing literally cannot start until the SoW is
> signed. Every action routes through the domain guards; a failed guard changes nothing.

### 3. The assessment questionnaire (workbook Tab 1)
Switch role to **Tester** (or Stakeholder). Open **Mobile App** → **Open assessment form**.
Pick an **Application type** (e.g. *API*, *Thick client*, *AI*) and a **hosting**/**network
exposure** — watch the matching **conditional sections appear**. The completeness meter
updates as you answer; everything auto-saves. **Complete & confirm** advances Scoping → SoW.

### 4. Access requirements (workbook Tab 3)
As **Tester**, open **Payments API** (Access stage). The **access matrix** lists each
environment/asset with a provisioning **status** you can change (App team to provision →
In progress → Provisioned).

### 5. Findings, evidence, checklist (workbook Tab 4 + register)
Open **Retail Web** (mid-test):
- **Live register** — filter findings by severity; see CVSS + status.
- **Evidence** — artifacts per finding with an **encrypted ✓** badge and a **signed link**
  (re-auth-gated); attach a new one as Tester.
- **Tester checklist** — pre-reqs / during / end-of-test items with progress.

### 6. Retest (child engagement)
As **Acme** (or Stakeholder), open **Broker Portal** (closed) → **Request a retest**. A linked
**child engagement** is created (badged "RETEST CHILD") and you land on it.

### 7. Trust — the tamper-evident audit
On any engagement, scroll to **Audit trail** → **Verify chain** → "🔗 verified". Every
transition is hash-chained (actor, action, before→after, time).

### 8. Portfolio analytics
Open **Analytics** — active engagements, open/critical findings, the **severity
distribution**, and per-application worst severity.

---

## What's real vs demo-mode
- **Real:** the engagement state machine + guards (15 unit tests), EF persistence, the
  hash-chained audit (6 persistence tests), the conditional assessment logic, the full UI.
- **Demo-mode:** SQLite (vs Azure SQL), role switcher (vs Entra SSO), evidence metadata +
  "signed link" mock (vs Blob + real signed URLs). All swap on via config —
  see `docs/azure-entra-setup.md` and `infra/main.bicep`.
