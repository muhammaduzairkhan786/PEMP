# PEMP — Experience & UI Design Plan (v0.3 draft)

> Companion to `PEMP_SRS_v1.0_Master.docx`. The SRS says *what* the platform must do; this document proposes *how it should feel and look*. It is input to Phase 2 (Design) wireframes — not a final spec. Everything here is traceable back to SRS requirement IDs. Anything proposed beyond the SRS is tagged **[new — not in SRS]** with a justification.
>
> **v0.2 added:** per-phase ASCII wireframes (§5), the state-machine guard table, the My-Turn home spec, the gate-ceremony frame-by-frame, the component kit, design tokens, and the ⌘K command palette.
>
> **v0.3 adds:** the **Admin Console** (§6, `FR-ADM`/`FR-AUD-03`) and **Analytics, Exceptions & Security Monitoring** (§7, `FR-ANL-03/04/05`, `SEC-INS`) — two whole surfaces that had role tabs but no design; an elevated **"signature look"** visual system + dark-first tokens (§2/§12); the **field-level visibility matrix** (§4, `SEC-AZN-03/04`); evidence-upload, retest-request and prerequisite-definition detail in the phase layouts (§5); and the former open questions now **resolved** (§16). All v0.2 content is retained, renumbered where new sections were inserted.

---

## 0. North Star

> **"The engagement runs itself; the human just confirms the next move."**

PEMP manages a process that is genuinely complex — 15 lifecycle steps, 5 roles, hard security gates, credential vaulting, a live vulnerability register. The design goal is to make that complexity *invisible to the person in front of it*. Each user, at any moment, should feel like they are doing **one small, obvious thing** — while the system quietly enforces the rigour underneath.

The model is **professional-addictive** (Linear, Superhuman, Stripe, Vercel), not consumer-gamified (no badges, streaks, or confetti for its own sake). For security professionals and insurer staff, "addictive" means: *it never makes me think, it never makes me wait, and it never makes me wonder if I did it right.* That is the loop that brings people back daily.

Three promises every screen must keep:
1. **I always know whose turn it is and what the next action is.** (Never a blank "now what?")
2. **The hard part is handled for me.** Guards, audit, encryption, routing — automatic and felt as safety, not friction.
3. **Doing my part feels fast and final.** Optimistic, instant, with a clear "done" beat.

---

## 1. The Five Core Design Primitives

Everything in the UI is built from these five. If a screen doesn't use at least one, question why it exists.

### 1.1 The Engagement Spine (the "story") — `NFR-USA-02`
A single, persistent horizontal stage-rail at the top of every engagement, for every role.

```
 ●━━━━━━━●━━━━━━━●━━━━━━━◍─ ─ ─ ○ ─ ─ ─ ○ ─ ─ ─ ○
Intake  Assign  Scope   SoW    Access  Test   Findings  Report  Retest
  ✓       ✓       ✓     ▶ YOU    🔒                  (🔒 until SoW signed)
```
- **Done** = solid + ✓. **Current** = pulses softly, names the owner. **Future** = ghosted. **Gated** = 🔒 with the unmet precondition on hover ("Locked — needs signed SoW", `FR-SOW-06`).
- One consistent story for all roles; only the controls below change per role.
- Clicking a past stage = read-only replay drawn from the audit log (`FR-AUD-02`).

### 1.2 "My Turn" — the home inbox
No role lands on charts. Every role lands on a **two-pile inbox**: **Your turn** (action required — the only pile with buttons) and **Waiting on others** (blocked, with who/what/age, no buttons). Clearing "Your turn" to zero is the daily win. Full spec in §9.

### 1.3 One Primary Action per screen
Each view has exactly **one** visually dominant button — the next legal move in the state machine, predicted for you. Kids-easy = only ever one bright thing to press.

### 1.4 Progressive disclosure
Keep all the detail, hide it until needed. Summary rows → side-drawers for depth; "show advanced" for power controls; conditional, chunked forms (`FR-SCO-01`) with a live completeness meter.

### 1.5 Gate Ceremonies
The product's soul is enforcement, so gate moments (sign-off, access-verified, test-start, report-release) are deliberate, weighty ceremonies — full-focus modal, plain-language attestation, re-auth where required (`SEC-IAM-04`), then a satisfying spine state-change with the hash-chain tick (`SEC-AUD-01`). Frame-by-frame in §10.

---

## 2. Visual System

Calm, confident, modern — a tool that handles dangerous data without ever looking dangerous.

### 2.1 Signature look — "a piece of art you reach for daily"
The product should be *instantly recognisable* and quietly beautiful, the way Linear or Stripe is — not by decoration, but by a tight, ownable identity:
- **Signature accent.** Evolve the SRS header navy (`1F3864` family) into a brighter, modern **brand hue** for primary actions, paired with one **luminous highlight** reserved for *payoff moments* (a spine unlocking, a sign-off committing). The accent is rare on screen — which is exactly what makes pressing it feel significant.
- **Dark-first, light-equal.** **[new — not in SRS]** Security pros and on-call staff live in dark surfaces, often at night; the system is designed dark-first with light as a fully-tuned equal, not an afterthought. *Justification:* directly serves the daily-use / "addictive" intent (`NFR-USA` family) for this audience; both themes ship to AA contrast (`NFR-USA-03`).
- **Signature motifs.** Two recurring visual marks carry the brand: the **glowing spine-rail** (the engagement's heartbeat, §1.1) and the **hash-chain "tick"** — a small chain-link/check mark that appears wherever something becomes tamper-evident (`SEC-AUD-01`), turning "this is logged" into a felt moment of trust rather than fine print.

### 2.2 Simple front, powerful back
Every surface stays **calm and minimal** — one primary action, generous space, nothing to decode — while the *visible richness* (the live register's density, the capacity heatmap, the audit chain ticking) signals the powerful engine underneath. The depth is always one click away, never in your face: the stakeholder sees a single button; the platform behind it is enforcing a state machine, object-level auth, and a hash chain. Looking effortless **is** the design.

### 2.3 Foundations
- **Palette:** near-neutral canvas, the one signature brand accent for primary actions (+ its luminous payoff highlight), and a disciplined **status spectrum** used *only* for status, never decoration. Severity is a fixed 5-step scale (mirrors `FR-FND-01` CVSS bands); process state is waiting / on-you / done / blocked / locked. Every status colour ships an icon + label partner (`NFR-USA-03`). (Tokens in §12.)
- **Typography:** one strong sans for UI; mono *only* for credentials, hashes, CVSS vectors, references, evidence — signalling "machine-truth." Large, clear, tabular numbers.
- **Space & density:** generous whitespace on home/flow surfaces (calm); higher density only in working surfaces (register, capacity, dashboards) where pros want a lot on screen.
- **Motion as payoff:** fast, meaningful, never decorative. Optimistic UI; spine transitions animate *causality*. The few **signature moments** — the lock visibly breaking, the spine unlocking, a CVSS score resolving live, a finding landing in the register — are where motion and the luminous highlight are intentionally delightful (ceremony tokens in §12). Respect `prefers-reduced-motion` (crossfade fallbacks).
- **Command palette (⌘K):** jump to any engagement/finding/action by typing (§13).

---

## 3. Information Architecture per Role

≤6 tabs per role is a hard requirement (`NFR-USA-01`). Tabs are *nouns I own*, never feature menus. Spine + My-Turn mean most navigation is "follow the story," not "find the page."

| Role | Tabs (≤6) | Lands on |
|------|-----------|----------|
| **Acme CA Officer** | Home · New Request · Engagements · Approvals · Reports | My-Turn (approvals due, reports to read) |
| **Delivery Manager** | Home · Requests · Assignments · Capacity · Engagements · Approvals | My-Turn (unassigned requests, SoWs to review) |
| **Penetration Tester** | My Work · Assessment · SoW · Access · Findings · Reports | My-Turn (today's owned actions across engagements) |
| **Application Stakeholder** | My Assessment · Findings · Retests | My-Turn (forms to complete, findings to read) — the simplest surface |
| **System Administrator** | Users · Configuration · Audit · Integrations | Operational console (health, exceptions, audit search) |

The stakeholder is the least technical, most "kid-easy" surface: usually exactly one thing to do (fill a form / read a finding / ask for a retest).

---

## 4. The Universal Engagement View

One layout, every role, every stage:

```
┌──────────────────────────────────────────────────────────────────────────┐
│ ENG-2026-0412 · Claims Portal · [PROJECT] · Criticality: High   FR-REQ-04 │  Header
│ Owner now: ▸ A. Tester (Pentester)        ↪ prior: ENG-2025-0331 FR-REQ-05 │
├──────────────────────────────────────────────────────────────────────────┤
│  ●━━●━━●━━◍─ ─○─ ─○─ ─○─ ─○─ ─○      « The Spine »            NFR-USA-02   │  Spine
│ Intk Asgn Scp SoW Acc Test Find Rpt Rtst                                   │
├──────────────────────────────────────────────────────────────────────────┤
│ ┌── NEXT ACTION ───────────────────────────────────────────────────────┐  │
│ │ Sign the Statement of Work          ⏱ SLA 2d left   FR-ANL-01         │  │  Next-action
│ │ Owner: You (Acme CA Officer)              [  Review & Sign SoW  ▶ ]     │  │  card
│ └──────────────────────────────────────────────────────────────────────┘  │
├──────────────────────────────────────────────────────────────────────────┤
│  « STAGE BODY — role-filtered controls for the current stage »             │  Stage body
│                                                                            │
├──────────────────────────────────────────────────────────────────────────┤
│ [ 📄 Documents ]  [ 🐞 Findings ]  [ ✉ Comms log ]  [ 🔗 Audit ]           │  Drawer rail
└──────────────────────────────────────────────────────────────────────────┘
   FR-DOC        FR-FND          FR-EXE-05        FR-AUD
```

Field-level visibility (`SEC-AZN-04`) is enforced here: a stakeholder never sees the credential vault; a tester sees credentials only for their assignment. Every record access passes object-level auth (`SEC-AZN-02`), and **engagement/tenant isolation is enforced at the data layer, not the UI** (`SEC-AZN-03`) — a hidden control is never the only control.

**Field-level visibility matrix** (`SEC-AZN-04`) — the UI is generated from this; the same rules are re-checked server-side per record:

| Field / surface | Acme CA | Delivery Mgr | Tester (assigned) | Stakeholder | Sys Admin |
|-----------------|:------:|:-----------:|:----------------:|:-----------:|:--------:|
| Engagement header / spine | ✓ | ✓ | ✓ (own) | ✓ (own app) | ✓ |
| Credential vault (`SEC-CRD-01`) | — | — | ✓ reveal+re-auth | — | config only, never values |
| Evidence / report artifacts | ✓ download (re-auth) | ✓ | ✓ | ✓ own-app findings | — |
| Findings register | ✓ all | ✓ all | ✓ assigned | ✓ **own app only** | — |
| IR / business contacts | ✓ | ✓ | ✓ | ✓ own | ✓ |
| Audit timeline | ✓ own engagements | ✓ | ✓ assigned | — | ✓ global (`FR-AUD-03`) |
| Admin / integrations | — | — | — | — | ✓ |

`—` = not rendered *and* not served. Testers are scoped to assigned engagements throughout (`SEC-INS-01`); stakeholders to their own application (`FR-AUTH-06`).

---

## 5. Phase-by-Phase Screen Layouts

For each phase: **owner**, the **one primary action**, a labelled wireframe, the **gate**, and the **delight** detail. Maps to the SRS 15-step lifecycle (SRS §8) and FR areas. The Spine + Next-action card + Drawer rail from §4 are present on all of them and are abbreviated as `[spine]` / `[next ▶]` / `[drawers]` below.

### Phase 1 — Intake · `FR-REQ` · Owner: Acme CA Officer
**Primary action: "Raise request."** A short friendly wizard, not a long form.
```
┌── New Request ───────────────────────────────── step 2 of 4 ──┐
│  Type    ( ) BAU   (•) Project                    FR-REQ-01    │
│  Target  [ Claims Portal            ▾]  + add app             │
│  Unit    [ Retail Claims            ▾]                        │
│  Window  [ 14 Jul ]──[ 25 Jul ]    Criticality [High ▾] REQ-02 │
│  ┌ Tested 4 months ago (ENG-2025-0331). Reuse that scope? ┐   │
│  │  [ Reuse & prefill ]   [ Start fresh ]      FR-REQ-05   │   │
│  └─────────────────────────────────────────────────────────┘ │
│  Contacts  dev: [____]  business: [____]                      │
│                                   [ Back ]  [ Raise request ▶]│
└───────────────────────────────────────────────────────────────┘
```
On submit: unique reference minted (`FR-REQ-04`), routed to DM + notified (`FR-REQ-03`), spine appears with stage 1 done. **Delight:** "reuse that scope?" turns a 10-field form into one tap.

### Phase 2 — Assignment & Capacity · `FR-ASG` · Owner: Delivery Manager
**Primary action: "Assign tester."** The Capacity Board.
```
┌── Capacity Board ───────────────────────  week of 14 Jul ─────┐
│ Request: ENG-2026-0412 · needs: WEB, API · 6 days  FR-ASG-02  │
│                                                               │
│ Tester      Skills        Mon Tue Wed Thu Fri  Load  Fit      │
│ A. Khan ✦   WEB API CLOUD ▓▓░░ ░░░░ ░░░░ ▓▓▓▓ ▓▓  60%  ●Best   │
│ R. Patel    WEB MOBILE    ▓▓▓▓ ▓▓▓▓ ▓▓░░ ░░░░ ░░  85%  ○       │
│ S. Lee      API CLOUD     ░░░░ ░░░░ ░░░░ ░░░░ ░░  20%  ○       │
│            (drag request onto a tester, or…)   FR-ASG-01/03    │
│ ⚠ Assigning R. Patel would over-allocate Tue–Wed  FR-ASG-04   │
│                                  [ Assign A. Khan (Best fit) ▶]│
└───────────────────────────────────────────────────────────────┘
```
Recommended matches by skill (`FR-ASG-02`); over-allocation warns inline, never blocks (`FR-ASG-04`); board reflows live (`FR-ASG-03`); reassignment keeps full audit (`FR-ASG-05`). **Delight:** assign like moving a sticky note.

### Phase 3 — Scoping & Assessment · `FR-SCO` · Owners: Stakeholder (async) + Tester (call)
**Primary action — stakeholder: "Continue assessment" · tester: "Complete & confirm."** Two lenses of one form.
```
STAKEHOLDER LENS (kid-easy)              TESTER LENS (completing)
┌── My Assessment ───────────────┐      ┌── Assessment · ENG-0412 ─────────┐
│ ▓▓▓▓▓▓▓░░░  70% complete  SCO-03│      │ ▓▓▓▓▓▓▓▓▓░ 90%      FR-SCO-03    │
│ Section: Authentication        │      │ Conditional set: API   SCO-01    │
│ Q: Does the app use SSO?       │      │ ⚠ 2 items unanswered — fill in   │
│   (•) Yes  ( ) No   ⓘ why we ask│      │   call:                          │
│ Q: Any test accounts ready?    │      │   • Rate-limit thresholds [____] │
│   [ Not yet ▾]                 │      │   • Out-of-scope hosts    [____] │
│ Auto-saved ✓   prefilled from  │      │ Scoping call: 11 Jul, attendees… │
│ ENG-2025-0331    FR-SCO-04     │      │                       FR-SCO-05  │
│        [ Save & finish later ] │      │        [ Complete & confirm ▶ ]  │
└────────────────────────────────┘      └──────────────────────────────────┘
```
Conditional, chunked, auto-saving; completeness meter motivates (`FR-SCO-03`); retests pre-populate (`FR-SCO-04`). Stakeholder sees plain language + "why we ask," never scoring. **Delight:** "3 questions left" makes a questionnaire finishable.

### Phase 4 — Statement of Work & Sign-off · `FR-SOW` · **GATE**
**Owners:** Tester drafts → (Project) DM review → Acme sign-off; (BAU) Tester issues direct to Acme. **Primary action: "Generate SoW" → "Sign."**
```
┌── Statement of Work · ENG-0412 ──────────────  v3 (draft) ────┐
│ Path:  Draft ──▸ DM review ──▸ Acme sign-off   [PROJECT] SOW-03 │
│ ┌─────────────────────────────────────────────────────────┐   │
│ │ Auto-drafted from assessment.            FR-SOW-01       │   │
│ │ 1. Scope: Claims Portal web + API …                      │   │
│ │ 2. Methodology: OWASP … 3. Dates: 14–25 Jul …            │   │
│ │ 4. Prerequisites: 3 test accounts, VPN …                 │   │
│ └─────────────────────────────────────────────────────────┘   │
│ Versions: v1 v2 ▸v3   [ Edit draft ]          FR-SOW-02       │
│  🔒 Testing stays locked until signed         FR-SOW-06       │
│                                        [ Review & Sign SoW ▶ ] │
└───────────────────────────────────────────────────────────────┘
        → opens the Gate Ceremony (§10): re-auth, immutable record, spine unlock.
```
**Delight:** a polished SoW that wrote itself from form answers — "I didn't have to write this." The lock visibly breaking is the app's signature beat.

### Phase 5 — Access & Prerequisites · `FR-ACC` · Owners: Acme (chases) + Tester (verifies)
**Primary action: "Verify access."**
```
┌── Access & Prerequisites · ENG-0412 ──────────────────────────┐
│ Prerequisites (tester-defined)    [ + Define prereq ] FR-ACC-01│
│ Checklist                         live from ITSM   CON-05     │
│  ● Test account A    provisioned ✓        age 3d   FR-ACC-02  │
│  ◑ Test account B    ticket open ⏳ INC-8821 (amber, 5d)      │
│  ○ VPN profile       not requested ✗                         │
│ ── Test credentials (vault-backed) ───────  SEC-CRD-01 ──     │
│  user: svc_pentest_a        pass: ••••••••  [ Reveal 🔓 ]      │
│        masked  FR-ACC-03/SEC-CRD-02   reveal = re-auth+logged │
│  validity: time-boxed → auto-revoke 26 Jul  FR-ACC-05/SEC-CRD-03│
│                                      [ Verify access works ▶ ] │
└───────────────────────────────────────────────────────────────┘
```
The tester first **defines** the prerequisites and test-account/access requirements (`FR-ACC-01`); those become the checklist Acme then chases. Tester confirms access *before* start; outcome recorded (`FR-ACC-04`); spine won't light "Test" until verified. Credentials are **time-boxed with automatic revocation** when the engagement ends (`SEC-CRD-03`), shown as a live expiry. Downloading any sensitive artifact (evidence, report) re-prompts identity (`SEC-EVD-03`), reusing the gate skeleton (§10). **Delight:** the masked field whose reveal visibly logs itself — pros *trust* the tool.

### Phase 6 — Execution & Communications · `FR-EXE` · Owner: Tester
**Primary action: "Send IR notice & start test" → "End test & send notice."** The test cockpit.
```
┌── Test Cockpit · ENG-0412 ──────────────  Day 3 of 8 ─────────┐
│ Window 14–25 Jul · findings logged so far: 7      FR-EXE-02   │
│ ── Required comms (auto-drafted) ──                           │
│  ✓ IR advance notice   sent 14 Jul 09:01     FR-EXE-01        │
│  ○ End-of-test notice  drafts on final day   FR-EXE-03        │
│  ○ Findings summary    auto-built from register FR-EXE-04     │
│ Daily progress (optional) [ + add note ]      FR-EXE-02       │
│ All sends logged: content · recipient · time  FR-EXE-05       │
│                              [ + Add finding ]  [ End test ▶ ] │
└───────────────────────────────────────────────────────────────┘
```
Three mandatory comms auto-drafted, reviewed and sent in one tap; every send logged. **Delight:** the tester never writes a routine email again.

### Phase 7 — Findings & the Live Register · `FR-FND` · Owner: Tester → everyone consumes
**Primary action: "Add finding."** Enter once.
```
CAPTURE (drawer)                         THE LIVE REGISTER
┌── New finding ──────────────┐  ┌── Vulnerability Register ───────────────┐
│ Title  [SQLi in /claims ]   │  │ Sev   Title              Asset   Status │
│ Sev    [● High ▾]           │  │ ●Crit Auth bypass        API     open   │
│ CVSS   AV:N/AC:L/… → 8.1 ◀──│  │ ●High SQLi /claims       Web   retest-pd│
│        guided calc FR-FND-05│  │ ●High XSS stored         Web     open   │
│ Asset  [Claims API ▾]       │  │ ◐Med  Verbose errors     API   remediat │
│ Evidence ▢ drop here        │  │ ○Low  Cookie flags       Web    closed  │
│   → encrypted ✓ on upload   │  │ status: open·remediated·accepted·       │
│   FR-FND-02 / SEC-EVD-01/02 │  │         retest-pending·closed  FR-FND-04│
│ Remediation [__________]    │  │ 🔗 link recurring class        FR-FND-06│
│        FR-FND-01            │  │                                         │
│      [ Save → to register ▶]│  └─────────────────────────────────────────┘
└─────────────────────────────┘
   on save → flows live into register, no copy-paste   FR-FND-03
```
**Evidence upload** (`FR-FND-02`): drop a screenshot/request-response/log onto the tile → it is stored **private, server-side encrypted** (`SEC-EVD-01/02`) with an "encrypted ✓" badge; later viewing issues a **signed, short-lived URL** and is logged, with a re-auth gate on sensitive download (`SEC-EVD-03`). Reuses the **Evidence tile** primitive (§11). **Delight:** the CVSS calculator scoring live, and the finding appearing in the register the instant you save — visible cause and effect.

### Phase 8 — Reporting · `FR-REP` · **QA GATE** · Owner: Tester → peer → Acme
**Primary action: "Generate draft" → "Release."**
```
┌── Report · ENG-0412 ─────────────────────────  draft v2 ──────┐
│ Assembled from engagement + findings   FR-REP-01             │
│  Exec summary · 7 findings · register snapshot · appendix    │
│ 🔒 QA GATE: peer review required before release  FR-REP-02   │
│    reviewer: R. Patel — [ Request peer review ]              │
│ Post-test call: 28 Jul · attendees · actions     FR-REP-03   │
│ Export: [ Word ] [ PDF ]  approved template      FR-REP-05   │
│ Versions: draft ▸ final ▸ retest  access-controlled FR-REP-06│
│                                    [ Release final report ▶ ] │
└───────────────────────────────────────────────────────────────┘
```
Tester curates, system assembles; release held by peer-review ceremony. **Delight:** "your report is 90% written."

### Phase 9 — Retest · `FR-RET` · Owners: Stakeholder/Acme request → Tester verifies
**Primary action — stakeholder: "Request retest" · tester: "Mark pass/fail."**
```
REQUEST (stakeholder/Acme)                THEN — VERIFY (tester)
┌── Request retest · ENG-0412 ───┐
│ Pick remediated findings to     │      ┌── Retest of ENG-0412 → child ───┐
│ re-verify:           FR-RET-01  │      │ … before/after diff (below) …    │
│  ☑ SQLi /claims   (remediated)  │  →   └──────────────────────────────────┘
│  ☑ XSS stored     (remediated)  │
│  ☐ Verbose errors (accepted)    │      spawns child ENG-0488,
│ Reason [ fixes deployed v2.1 ]  │      inherits scope     FR-RET-02
│            [ Request retest ▶ ]  │
└─────────────────────────────────┘
```
The stakeholder/Acme **selects specific remediated findings** to re-verify and submits a reason (`FR-RET-01`); this spawns the linked child engagement that re-checks only those in-scope findings. The tester then works the before/after view:
```
┌── Retest of ENG-0412 → child ENG-0488 ──────  before/after ───┐
│ Inherited scope + findings           FR-RET-02               │
│ Finding              Before    After      FR-RET-03          │
│  SQLi /claims        ●High  →  ✓ PASS  (fixed)               │
│  XSS stored          ●High  →  ✗ FAIL  (still present)       │
│  Verbose errors      ◐Med   →  ✓ PASS                        │
│  ── diff view, green = fixed / red = not ──   FR-RET-04      │
│                                   [ Generate retest report ▶ ]│
└───────────────────────────────────────────────────────────────┘
```
Spawns a linked child engagement with its own spine, badged "Retest of…" (`FR-RET-02`). **Delight:** the before/after diff is instantly legible to a non-technical stakeholder — closure you can *see*.

---

## 6. Admin Console · `FR-ADM` · Owner: System Administrator

One operational console — the Admin's landing (per §3) — behind four tabs (Users · Configuration · Audit · Integrations), no more (`NFR-USA-01`). Same house style as §5: each surface has one primary action and a visible trust note. The Admin **never** sees secret *values* — only their lifecycle and config.

### 6.1 Users & roles — `FR-ADM-01`
```
┌── Users & Roles ─────────────────────────  synced from Entra ─┐
│ Source of truth: Entra groups · no local password store SEC-IAM-01│
│ Name            Role (= group)      Type      CA posture  FR-AUTH│
│  A. Khan        Pentester           native    ✓ compliant  -02   │
│  R. Patel       Pentester           native    ✓ compliant        │
│  J. Lee (ext)   Stakeholder    ✦B2B guest     ⚠ risky sign-in    │
│  M. Dev (ext)   Delivery Mgr   ✦B2B guest     ✓ compliant  -07   │
│ Role changes happen in Entra; PEMP reflects + audits   FR-ADM-01 │
│                          [ Open in Entra ]  [ Sync now ▶ ]       │
└──────────────────────────────────────────────────────────────────┘
```
Roles **are** Entra group membership (`FR-ADM-01`, `SEC-IAM-01`) — PEMP holds no password store. Native Acme staff vs **B2B guests** (testers, DM, stakeholders) are marked (`FR-AUTH-07`). **Conditional Access posture** — device-compliant / risky-sign-in — is surfaced as read-only cues (`SEC-IAM-03`, `FR-AUTH-03`); enforcement is Entra's, the badge is the operator's early-warning.

### 6.2 Configuration — `FR-ADM-02`, `FR-ADM-04`
```
┌── Configuration ─────────────────────────────────────────────┐
│ Templates    assessment · SoW · email      [ Edit ] FR-ADM-02│
│ SLA thresholds  assign 2d · sign 2d · QA 1d → feed clocks    │
│                                          FR-ADM-02 / FR-ANL-01│
│ Feature flags  ⌘K palette [on]  snooze [on]  …    FR-ADM-04  │
│ Retention   evidence 7y · reports 7y · creds purge-on-close  │
│             secure-delete on expiry    FR-DOC-04 / SEC-LIF-01/02│
│                                            [ Save changes ▶ ] │
└───────────────────────────────────────────────────────────────┘
```
Template manager for assessment/SoW/email (`FR-ADM-02`); **SLA thresholds** here feed the guard-table clocks (§8) and the exceptions feed (§7, `FR-ANL-01`). Feature/env flags toggle behaviour without code (`FR-ADM-04`) — the home for the **snooze** and **⌘K** switches, so those [new] features are reversible. **Retention & secure-deletion** policy per data class (`FR-DOC-04`, `SEC-LIF-01/02`).

### 6.3 Integrations — `FR-ADM-03`
```
┌── Integrations ──────────────────────────────  health ───────┐
│  Entra ID (SSO/SCIM)       ● connected      OIDC   FR-AUTH-01 │
│  Mail / Teams              ● connected             FR-NOT-02  │
│  ITSM ticketing            ◑ degraded · webhook   FR-ADM-03   │
│        live ticket status feeds Access checklist   CON-05     │
│  Azure Key Vault           ● connected (managed id)          │
│                                  [ Test connection ]  [ Edit ]│
└───────────────────────────────────────────────────────────────┘
```
Configure Entra, mail/Teams (`FR-NOT-02`), and **ITSM ticketing** (`FR-ADM-03`, `CON-05`) — the latter drives live ticket status on the Access screen (§5 Phase 5). Connection health is visible at a glance.

### 6.4 Audit search & export — `FR-AUD-03`
```
┌── Audit ─────────────────────────────────  hash-chain ✓ ─────┐
│ Filter  actor[__] action[__] engagement[__] date[──]  FR-AUD-03│
│  14:02  A.Khan   revealed credential   ENG-0412   🔗 verified │
│  13:51  Acme CA  signed SoW            ENG-0412   🔗 verified │
│  11:20  System   auto-revoked creds    ENG-0331   🔗 verified │
│ Chain integrity: ✓ continuous (no gaps)     SEC-AUD-01/02     │
│                          [ Verify chain ]  [ Export (CSV/JSON) ]│
└───────────────────────────────────────────────────────────────┘
```
Search the **append-only, hash-chained** log (`FR-AUD-01/02`, `SEC-AUD-01`) by actor / action / engagement / time, **verify chain integrity**, and export for compliance/investigations (`FR-AUD-03`). Reuses the **Audit timeline** primitive (§11) in a global/admin variant; the log is protected from modification by application *and* admin roles alike. **Delight:** the "chain ✓ continuous" line — provable, not promised.

---

## 7. Analytics, Exceptions & Security Monitoring · `FR-ANL` · `SEC-INS`

**Role-based dashboards** (`FR-ANL-05`): calm summary tiles up top (simple front), drilling into dense working views (powerful back). This is the *review* surface — alert *generation* (Azure Monitor / Sentinel) is platform infrastructure and **out of scope for this UX document** (§16). Three surfaces:

### 7.1 Exceptions feed — `FR-ANL-01/02`
```
┌── Exceptions ────────────────────────────  process SLAs ─────┐
│ ● OVERDUE  SoW unsigned 3d (SLA 2d)   ENG-0412  Acme  FR-ANL-02│
│ ◑ AT RISK  access unverified, start tomorrow  ENG-0419  Tester│
│ ◑ AT RISK  QA review pending 1d        ENG-0408  R.Patel      │
│ Sorted by breach proximity            feeds My-Turn  FR-ANL-01│
└───────────────────────────────────────────────────────────────┘
```
Stalled sign-offs, unprovisioned access, overdue actions — derived from the same SLA clocks (§8) that order **My-Turn** (§9). One feed, not scattered alerts.

### 7.2 Security-event monitor — `FR-ANL-03`, `SEC-INS-02/03`
```
┌── Security events ───────────────────────  triage ───────────┐
│ ⚠ Bulk export   18 evidence files in 2m   A.Khan  ENG-0412   │
│ ⚠ Off-hours     credential reveal 02:14    R.Patel ENG-0419  │
│ ⚠ Out-of-scope  access attempt on ENG not assigned  SEC-INS-01│
│ ⚠ Auth failures 5× in 1m   ext stakeholder        FR-ANL-03  │
│ each → drill to audit chain · mark reviewed   SEC-INS-02/03   │
└───────────────────────────────────────────────────────────────┘
```
Out-of-scope access, **bulk exports**, **off-hours / credential-access spikes**, auth-failure clusters (`FR-ANL-03`, `SEC-INS-02`) — surfaced as a reviewable triage list, each row drilling into the hash-chained audit trail. Underlying detection/alerting logs to Azure Monitor/Sentinel (`SEC-INS-03`, infra — §16); PEMP owns the **review experience**.

### 7.3 Portfolio analytics — `FR-ANL-04/05`
```
┌── Portfolio analytics ───────────────────  exportable ───────┐
│ Recurring classes   XSS ▓▓▓▓ · SQLi ▓▓ · authz ▓▓▓  FR-FND-06│
│ Mean-time-to-remediate   High 11d ▼  Med 24d ▲       FR-ANL-04│
│ Severity trend (6 mo)    ▁▂▃▅▃▂  per-app risk heat           │
│ Per-app risk    Claims●High  Payments◐Med  Portal○Low        │
│            [ Export management report (PDF) ]   FR-ANL-05     │
└───────────────────────────────────────────────────────────────┘
```
Recurring vulnerability classes (links to finding-dedup `FR-FND-06`), **mean-time-to-remediate**, severity trends, per-app risk (`FR-ANL-04`); exportable role-based management reports (`FR-ANL-05`). **Delight:** a non-technical exec sees risk dropping across the portfolio at a glance.

---

## 8. State-Machine Guard Table (the spine's source of truth)

Each transition unlocks **only** when its guard is satisfied. This table is the contract between the UI spine, the backend state machine, and the SRS. SLA clocks feed the exceptions feed (`FR-ANL-01/02`).

| # | Transition | Guard to unlock (FR) | Owner | SLA clock | UI while locked |
|---|-----------|----------------------|-------|-----------|-----------------|
| 1 | → Intake | Request fields complete + reference minted (`FR-REQ-01/04`) | Acme CA | — | wizard incomplete: submit disabled |
| 2 | Intake → Assignment | Request routed to DM queue (`FR-REQ-03`) | DM | time-to-assign | "Waiting on Delivery Manager" |
| 3 | Assignment → Scoping | ≥1 tester assigned (`FR-ASG-03`) | Tester | time-to-scope | 🔒 "Awaiting tester assignment" |
| 4 | Scoping → SoW | Assessment marked complete (`FR-SCO-03`) | Tester | time-in-scoping | 🔒 "Assessment N% — finish to draft SoW" |
| 5 | SoW → **Access** | **SoW signed** (`FR-SOW-05/06`); Project: DM-reviewed then Acme-signed (`FR-SOW-03`); BAU: issued to Acme (`FR-SOW-04`) | Acme CA (sign) | time-to-sign | 🔒 **"Locked — needs signed SoW"** |
| 6 | Access → Execution | Access verified before start date (`FR-ACC-04`) | Tester | time-to-access | 🔒 "Verify access to start" + ticket ages |
| 7 | Execution start | IR advance notice sent (`FR-EXE-01`) | Tester | — | "Send IR notice to begin" |
| 8 | Execution → Findings/summary | End-of-test notice (`FR-EXE-03`) + summary sent same day (`FR-EXE-04`) | Tester | test-window | counts up days in window |
| 9 | Findings → Report | Draft generated from findings (`FR-REP-01`) | Tester | time-to-draft | 🔒 "Add findings to draft report" |
| 10 | Report → Final | **Peer review passed** (`FR-REP-02`) | Peer tester | time-in-review | 🔒 "Awaiting QA peer review" |
| 11 | Final → Closed | Final report + register stored (`FR-REP-04`, `FR-DOC-02` immutable) | Tester | — | "Releasing…" (async, `NFR-PER-03`) |
| 12 | Closed → Retest (child) | Retest requested vs remediated findings (`FR-RET-01`) → child engagement (`FR-RET-02`) | Stakeholder/Acme | time-to-retest | "Request retest" available on closed |

Every transition writes the hash-chained audit entry (`SEC-AUD-01`, `FR-AUD-02`): actor, action, before/after state, timestamp, source. Reassignments (`FR-ASG-05`) and SoW re-versions (`FR-SOW-02`) are logged without leaving the stage.

---

## 9. "My Turn" Home — Full Spec

The single strongest daily-return mechanic. Built from notifications (`FR-NOT-01`) and the guard table (§8).

**Row anatomy (Your-turn pile):**
```
┌────────────────────────────────────────────────────────────────────┐
│ ●High  Sign SoW · Claims Portal           ENG-0412   ⏱ 2d   [ Sign ▶]│
│ ↑sev   ↑action verb + target              ↑ref      ↑SLA   ↑1 primary│
└────────────────────────────────────────────────────────────────────┘
```
- **Left:** priority dot (drives sort). **Centre:** action verb + target (always "do X to Y", never a noun). **Ref:** engagement id. **SLA badge** from `FR-ANL-01` (green→amber→red as it ages). **One primary button** = the next legal move from §8.

**Sorting / prioritisation (descending):**
1. **Overdue** (SLA breached, `FR-ANL-02`) — red, always top.
2. **Gate actions** (sign-off, access-verify, peer-review) — they unblock others.
3. **Severity** of the underlying engagement criticality / finding.
4. **SLA age** (closest to breach first).
5. Tie-break: oldest `updated_at`.

**"Your turn" vs "Waiting on others" logic:** an item is *yours* iff the current guard's owner (§8) resolves to you under RBAC + object-level auth (`SEC-AZN-01/02`) — testers only ever see their assigned engagements (`SEC-INS-01`). Everything where you're the *next-but-one* owner, or you raised it and it's moving, goes to **Waiting on others** with "who · what · age" and a polite **Nudge** (`FR-NOT-03`) — no action button.

**Empty state (the reward):** `"You're all clear. 3 engagements moving without you."` Calm, genuine, no confetti.

**Feeds:** every state transition emits an event (`FR-NOT-01`) → in-app + email (optional Teams, `FR-NOT-02`); reminders for pending owners (`FR-NOT-03`) re-surface a row. This is the *one* attention system — scattered alerts are explicitly avoided.

**[new — not in SRS]** Per-row "snooze until" (max to SLA cap) so testers can defer non-urgent rows without losing them. Justification: prevents inbox fatigue on long test windows; bounded by the SLA so it can't hide a breach.

---

## 10. Gate Ceremony — Frame by Frame (SoW sign-off)

The signature interaction. `FR-SOW-05/06`, `SEC-IAM-04`, `SEC-AUD-01`. Other gates (access-verify, peer-review, report-release) reuse this skeleton.

```
FRAME 1 — TRIGGER            FRAME 2 — ATTEST MODAL (full focus)
[ Review & Sign SoW ▶ ]      ┌─ Sign Statement of Work ───────────────┐
   click → dim background    │ You are agreeing to:                   │
                             │  • Scope: Claims Portal web + API      │
                             │  • Dates: 14–25 Jul 2026               │
                             │  • Prerequisites: 3 accounts, VPN      │
                             │ This creates an immutable, signed       │
                             │ record.            FR-SOW-05            │
                             │ 🔗 will be hash-chained  SEC-AUD-01     │
                             │            [ Cancel ]  [ Confirm sign ]│
                             └─────────────────────────────────────────┘
FRAME 3 — RE-AUTH            FRAME 4 — OPTIMISTIC COMMIT
┌─ Confirm it's you ───────┐ button → spinner inline; UI commits at once
│ Re-enter via Entra MFA   │ (optimistic, NFR-PER-01); background writes:
│  [ Verify with Entra ▶ ] │  1) signed record (identity+timestamp)
│   SEC-IAM-04             │  2) hash-chain append  SEC-AUD-01
└──────────────────────────┘  3) state transition #5 (§8)

FRAME 5 — SPINE UNLOCK (the payoff)        FRAME 6 — RESULT
  ●━━●━━●━━●  →  ●━━●━━●━━●━━◍              Toast: "SoW signed.
       SoW🔒          SoW✓ Access▶          Testing unlocked."
  the lock visibly breaks; Access lights   Next-action card flips to
  + soft pulse moves to the new owner       the new owner. My-Turn row
  (respects prefers-reduced-motion)         clears with a checkmark.
```

**Error / abort paths:**
- **Cancel / MFA fail:** nothing commits; modal closes; state unchanged; no audit "signed" entry (an *attempt* may be logged for security telemetry, `SEC-INS-02`).
- **Background write fails after optimistic commit:** UI rolls the spine back, shows a non-destructive banner "Couldn't finalise sign-off — retry," and the action returns to My-Turn. The audit chain is never left half-written (the transition is atomic with the chain append).
- **Concurrent signer:** if another authorised signer already signed, the modal resolves to "Already signed by X at HH:MM" rather than double-writing.

---

## 11. The Component Kit (12 core + 4 working-surface primitives)

The whole UI composes from these. Same vocabulary everywhere = the "butter" feel. States listed as default / hover / focus / disabled / loading / error.

**Core primitives** (the flow surfaces, §4–§5):

| # | Component | Purpose | Anatomy | Key states | Variants | SRS |
|---|-----------|---------|---------|-----------|----------|-----|
| 1 | **Spine-rail** | the engagement story | nodes + connectors + owner label | done / current(pulse) / future(ghost) / locked(🔒) / error | horizontal (default), compact (cards) | `NFR-USA-02` |
| 2 | **Engagement card** | list/grid unit | ref · app · type badge · spine-mini · owner · SLA | default / hover(lift) / focus-ring / loading(skeleton) | BAU / Project / Retest(child badge) | `FR-REQ-04`, `FR-RET-02` |
| 3 | **My-Turn row** | actionable inbox item | sev dot · verb+target · ref · SLA · 1 button | default / hover / focus / disabled(not-yours) / loading | your-turn (button) / waiting (nudge) | `FR-NOT-01`, `FR-ANL-01` |
| 4 | **Severity chip** | CVSS band | icon + label + colour (never colour alone) | static; focus(tooltip=vector) | Critical/High/Medium/Low/Info | `FR-FND-01` |
| 5 | **Status pill** | finding/process state | dot + text | static / hover(history) | open·remediated·accepted·retest-pending·closed; waiting·on-you·done·blocked·locked | `FR-FND-04` |
| 6 | **Owner tag** | who owns/acts | avatar + name + role | default / hover(contact card) | native / B2B-guest marker | `FR-AUTH-07` |
| 7 | **Gate-ceremony modal** | weighty confirm | attest summary · re-auth · confirm | open / re-auth / committing / success / error-abort | sign-off / access-verify / peer-review / release | `FR-SOW-05`, `SEC-IAM-04` |
| 8 | **Detail drawer** | progressive depth | slide-over panel + close | open / loading / error | findings · documents · comms · audit | `FR-DOC`, `FR-EXE-05` |
| 9 | **Completeness meter** | form progress | bar + "N left" | empty / partial / complete | assessment / report-readiness | `FR-SCO-03` |
| 10 | **Audit timeline** | tamper-evident story | entries: actor·action·before→after·time · chain tick | default / verifying / search-filtered | per-engagement / global(admin) | `FR-AUD-01/02/03` |
| 11 | **Masked-secret field** | vaulted credential | masked value + reveal(re-auth) + copy(logged) | masked / revealing(re-auth) / revealed(timeboxed) / revoked | credential / token | `SEC-CRD-01/02/03` |
| 12 | **Evidence tile** | secure artifact | thumb + type + secured badge | default / uploading / encrypted ✓ / download(signed-url) | screenshot · req/resp · log · file | `SEC-EVD-01/02`, `SEC-EVD-03` |

**Working-surface primitives** (the dense Admin/Analytics surfaces, §6–§7) — same vocabulary, higher density:

| # | Component | Purpose | Anatomy | Key states | Variants | SRS |
|---|-----------|---------|---------|-----------|----------|-----|
| 13 | **Dashboard stat-tile** | calm summary → drill | metric + sparkline + trend arrow | default / hover / loading / drill | SLA · MTTR · severity · per-app | `FR-ANL-04/05` |
| 14 | **Exception/alert row** | triage item | sev dot · what · who · ref · age | default / hover / reviewed / dismissed | process (§7.1) / security (§7.2) | `FR-ANL-02`, `SEC-INS-02/03` |
| 15 | **Integration-health card** | connection status | name · status dot · last-sync · test | connected / degraded / disconnected | Entra · mail/Teams · ITSM · Key Vault | `FR-ADM-03` |
| 16 | **Config/template editor row** | admin setting | label · value · edit · scope | view / editing / saved / flagged | template · SLA · feature-flag · retention | `FR-ADM-02/04`, `FR-DOC-04` |

Cross-cutting: every interactive component ships keyboard focus, visible focus ring, ARIA label, and a non-colour status cue (`NFR-USA-03`).

---

## 12. Design Tokens (names + intent; final values in Phase 2)

**Dark-first:** the Dark column is the *primary* tuning; Light is a fully-resolved equal (§2.1). Both ship to WCAG AA (`NFR-USA-03`).

### Colour
| Token | Intent | Light | Dark (primary) |
|-------|--------|-------|------|
| `canvas` | app background | warm off-white | deep slate |
| `surface` | cards/drawers | white | raised slate |
| `ink` / `ink-muted` | text primary / secondary | near-black / grey | near-white / grey |
| `brand` | the one accent — primary actions | confident blue (evolved from SRS header `1F3864` family) | brightened brand |
| `brand-glow` | luminous **payoff** highlight — ceremony commit, spine unlock, hash-chain tick | accent at high luminance, used sparingly | same, tuned to bloom on dark |
| `sev-critical … sev-info` | fixed 5-step severity scale | red→deep-orange→amber→blue→grey | same hues, tuned for contrast |
| `state-waiting / on-you / done / blocked / locked` | process spectrum | amber / brand / green / red / grey | tuned |
| paired `*-on` foregrounds | text-on-status (AA contrast) | — | — |

Rule: status colours appear **only** on status; never as decoration. `brand-glow` is the one expressive token, reserved for signature moments (§2.1). Every status colour ships an icon + label partner (`NFR-USA-03`).

### Type · Space · Form
| Token group | Tokens | Intent |
|-------------|--------|--------|
| `font` | `sans` (UI), `mono` (secrets/hashes/CVSS/refs) | mono signals machine-truth |
| `text` scale | `xs · sm · base · lg · xl · 2xl · display` | clear hierarchy; tabular numerals |
| `space` scale | `1·2·3·4·6·8·12·16` (4px base) | generous on flow, tight on working surfaces |
| `radius` | `sm · md · lg · pill` | soft-modern; pill for chips/buttons |
| `elevation` | `flat · raised · drawer · modal` | restrained shadow ladder |

### Motion
| Token | Value-intent | Use |
|-------|-------------|-----|
| `dur-fast` | ~120ms | hovers, focus, toggles |
| `dur-base` | ~200ms | drawers, cards |
| `dur-ceremony` | ~400ms | spine unlock, gate success — the signature moments (§2.1) |
| `ease-standard` / `ease-emphasis` | smooth / slight-overshoot | standard UI / payoff moments |
| `glow-pulse` | soft bloom of `brand-glow`, one cycle | lock-break, hash-chain tick, score-resolve — *only* on payoff |
| `reduced-motion` | crossfade-only fallbacks (glow → static highlight) | honor `prefers-reduced-motion` |

---

## 13. ⌘K Command Palette

The power-user spine; every action also has a keyboard path (`NFR-USA-03`). **[new — not in SRS]** — justification: directly serves the "fast & final" / simplified-navigation intent (`NFR-USA-01/02`) without adding tabs.

```
┌─ ⌘K ───────────────────────────────────────────────┐
│ > sign                                              │
│  ▸ Sign SoW · Claims Portal (ENG-0412)        ↵     │
│  ▸ Go to engagement…                                │
│  ▸ Add finding…                                     │
│  ▸ Open register                                    │
└─────────────────────────────────────────────────────┘
```

**Vocabulary:** Navigate (`go to engagement / register / my-turn / tab`), Act (`sign SoW`, `verify access`, `add finding`, `send IR notice`, `request retest` — each gated by the same guard table §8 and object-level auth, so the palette never offers an illegal move), Search (engagements, findings, refs).

**Keyboard model:** `⌘K` palette · `g` then letter = go-to (g+e engagements, g+m my-turn) · `j/k` move rows · `↵` primary action · `e` open drawer · `⌘↵` confirm in ceremony · `Esc` cancel/close. Results are RBAC-scoped (testers see only their engagements, `SEC-INS-01`).

---

## 14. What Makes It Addictive (legitimately, for this audience)

1. **Zero-inbox loop** — My-Turn → clear → reward (§9).
2. **No-think clarity** — one obvious action per screen.
3. **Instant + final** — optimistic UI, ⌘K, keyboard-everything.
4. **Visible momentum** — the spine filling stage by stage.
5. **Earned trust** — visible audit, masked secrets, gate ceremonies make relying on it *pleasant* for a security pro.
6. **Auto-drafting** — SoWs, emails, reports that write themselves remove the dreaded chores.

Deliberately **not** used: points, badges, streaks, confetti — they'd undermine credibility with regulated-insurer and security users. Addictiveness here is *frictionlessness*.

---

## 15. Accessibility, Responsiveness, Performance

- **WCAG 2.1 AA** (`NFR-USA-03`): status never by colour alone (chip + icon + label), full keyboard model, focus-visible, reduced-motion.
- **Desktop-primary, responsive** (`NFR-USA-04`): working surfaces (capacity, register) are desktop-rich; stakeholder surfaces degrade gracefully to tablet/phone.
- **Feel-fast** (`NFR-PER-01`, ~2s p95): optimistic UI hides latency; heavy work (report/doc generation, notifications) is async (`NFR-PER-03`) with clear in-progress states, never a frozen screen.

---

## 16. Resolved Decisions & Phase-2 Wireframe Checklist

**Resolved decisions (carry into Phase 2):**
- **Front end → SPA.** The optimistic-UI + ⌘K + drawer-heavy model resolves the one open stack item (`ASM-01`) in favour of a **SPA** (React or Blazor-WASM). Phase-2 design ratifies React-vs-Blazor-WASM only; the SPA posture itself is settled.
- **Snooze (§9) and ⌘K (§13) → in v1**, shipped behind **admin feature flags** (§6.2 Configuration) so scope stays reversible without code change (`FR-ADM-04`). Both remain tagged **[new — not in SRS]** with their justifications.
- **ITSM integration → webhook-preferred, polling-fallback** for live ticket status on the Access screen (`CON-05`, §6.3); exact system + auth named in Phase 2.
- **B2B guest first-run → kid-easy onboarding.** **[new — not in SRS]** Entra guest invite → one-tap consent → lands the stakeholder directly on their single assessment/finding (`FR-AUTH-07`), never on an empty console. *Justification:* protects the "simplest surface" promise (§3) for the least-technical, external user.
- **Daily-progress (`FR-EXE-02`) → optional, lightweight.** Kept opt-in (§5 Phase 6) so it never becomes busywork; validate exact fields with testers during wireframing.

**Out of scope for this UX document (Phase-2 architecture / threat-model deliverables):** infrastructure and SDLC requirements — UK-region hosting & residency (`NFR-CMP-01/02/03`), availability/DR/backup (`NFR-AVL-01/02`), modularity/observability/IaC (`NFR-MNT-01/03`), TLS/encryption-at-rest/Key-Vault (`SEC-DAT-01/02/03`), private networking & WAF (`SEC-NET-01/02/03`), and CI security gates / dogfooding / threat model (`SEC-SDL-01/02/03`). They are deliberately omitted here, not missed; the *review/operator surfaces* for security events (§7.2) and audit (§6.4) are in scope.

**Wireframe set (priority order):**
1. ☐ Spine component — all states (done/current/future/locked/error)
2. ☐ My-Turn home — Tester (busiest) + Stakeholder (simplest)
3. ☐ Universal Engagement view shell with role-filtered stage bodies + **field-visibility matrix** (§4)
4. ☐ Gate-ceremony modal — SoW sign-off, all 6 frames + error paths (§10)
5. ☐ Assessment — conditional + completeness meter, both lenses
6. ☐ Capacity board — assignment + over-allocation warning
7. ☐ Finding capture + Live Register (with CVSS calc) + **secure evidence upload** (§5 Phase 7)
8. ☐ Access & prerequisites — **prerequisite-definition** + masked-secret reveal + re-auth-download flow
9. ☐ Retest — **request initiator** + before/after diff
10. ☐ **Admin console** — Users · Configuration · Integrations · Audit search/export (§6, ×4)
11. ☐ **Analytics dashboards** — exceptions feed · security-event monitor · portfolio analytics (§7, ×3)
12. ☐ Component kit (the primitives) + colour/type/motion tokens (dark-first)

---

*v0.3 draft for review. Pair with the SRS; §16 decisions are resolved — carry them into wireframing. — Phase-1.5 design input.*
