# PEMP — Experience & UI Design Plan (v0.2 draft)

> Companion to `PEMP_SRS_v1.0_Master.docx`. The SRS says *what* the platform must do; this document proposes *how it should feel and look*. It is input to Phase 2 (Design) wireframes — not a final spec. Everything here is traceable back to SRS requirement IDs. Anything proposed beyond the SRS is tagged **[new — not in SRS]** with a justification.
>
> **v0.2 adds:** per-phase ASCII wireframes (§5), the state-machine guard table (§6), the My-Turn home spec (§7), the gate-ceremony frame-by-frame (§8), the component kit (§9), design tokens (§10), and the ⌘K command palette (§11).

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
No role lands on charts. Every role lands on a **two-pile inbox**: **Your turn** (action required — the only pile with buttons) and **Waiting on others** (blocked, with who/what/age, no buttons). Clearing "Your turn" to zero is the daily win. Full spec in §7.

### 1.3 One Primary Action per screen
Each view has exactly **one** visually dominant button — the next legal move in the state machine, predicted for you. Kids-easy = only ever one bright thing to press.

### 1.4 Progressive disclosure
Keep all the detail, hide it until needed. Summary rows → side-drawers for depth; "show advanced" for power controls; conditional, chunked forms (`FR-SCO-01`) with a live completeness meter.

### 1.5 Gate Ceremonies
The product's soul is enforcement, so gate moments (sign-off, access-verified, test-start, report-release) are deliberate, weighty ceremonies — full-focus modal, plain-language attestation, re-auth where required (`SEC-IAM-04`), then a satisfying spine state-change with the hash-chain tick (`SEC-AUD-01`). Frame-by-frame in §8.

---

## 2. Visual System

Calm, confident, modern — a tool that handles dangerous data without ever looking dangerous.

- **Palette:** near-neutral canvas, one confident brand accent for primary actions, and a disciplined **status spectrum** used *only* for status, never decoration. Severity is a fixed 5-step scale (mirrors `FR-FND-01` CVSS bands); process state is waiting / on-you / done / blocked / locked. (Tokens in §10.)
- **Typography:** one strong sans for UI; mono *only* for credentials, hashes, CVSS vectors, references, evidence — signalling "machine-truth." Large, clear, tabular numbers.
- **Space & density:** generous whitespace on home/flow surfaces (calm); higher density only in working surfaces (register, capacity) where pros want a lot on screen.
- **Motion:** fast, meaningful, never decorative. Optimistic UI; spine transitions animate *causality*. Respect `prefers-reduced-motion`.
- **Command palette (⌘K):** jump to any engagement/finding/action by typing (§11).

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

Field-level visibility (`SEC-AZN-04`) is enforced here: a stakeholder never sees the credential vault; a tester sees credentials only for their assignment. Every record access passes object-level auth (`SEC-AZN-02`).

---

## 5. Phase-by-Phase Screen Layouts

For each phase: **owner**, the **one primary action**, a labelled wireframe, the **gate**, and the **delight** detail. Maps to SRS 15-step lifecycle (§8) and FR areas. The Spine + Next-action card + Drawer rail from §4 are present on all of them and are abbreviated as `[spine]` / `[next ▶]` / `[drawers]` below.

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
        → opens the Gate Ceremony (§8): re-auth, immutable record, spine unlock.
```
**Delight:** a polished SoW that wrote itself from form answers — "I didn't have to write this." The lock visibly breaking is the app's signature beat.

### Phase 5 — Access & Prerequisites · `FR-ACC` · Owners: Acme (chases) + Tester (verifies)
**Primary action: "Verify access."**
```
┌── Access & Prerequisites · ENG-0412 ──────────────────────────┐
│ Prerequisite checklist            live from ITSM   CON-05     │
│  ● Test account A    provisioned ✓        age 3d   FR-ACC-02  │
│  ◑ Test account B    ticket open ⏳ INC-8821 (amber, 5d)      │
│  ○ VPN profile       not requested ✗                         │
│ ── Test credentials (vault-backed) ───────  SEC-CRD-01 ──     │
│  user: svc_pentest_a        pass: ••••••••  [ Reveal 🔓 ]      │
│        masked  FR-ACC-03/SEC-CRD-02   reveal = re-auth+logged │
│  validity: time-boxed → auto-revoke 26 Jul   FR-ACC-05       │
│                                      [ Verify access works ▶ ] │
└───────────────────────────────────────────────────────────────┘
```
Tester confirms access *before* start; outcome recorded (`FR-ACC-04`); spine won't light "Test" until verified. **Delight:** the masked field whose reveal visibly logs itself — pros *trust* the tool.

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
│ Evidence ▢ drop here SEC-EVD│  │ ○Low  Cookie flags       Web    closed  │
│ Remediation [__________]    │  │ status: open·remediated·accepted·       │
│        FR-FND-01            │  │         retest-pending·closed  FR-FND-04│
│      [ Save → to register ▶]│  │ 🔗 link recurring class        FR-FND-06│
└─────────────────────────────┘  └─────────────────────────────────────────┘
   on save → flows live into register, no copy-paste   FR-FND-03
```
**Delight:** the CVSS calculator scoring live, and the finding appearing in the register the instant you save — visible cause and effect.

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

## 6. State-Machine Guard Table (the spine's source of truth)

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

## 7. "My Turn" Home — Full Spec

The single strongest daily-return mechanic. Built from notifications (`FR-NOT-01`) and the guard table (§6).

**Row anatomy (Your-turn pile):**
```
┌────────────────────────────────────────────────────────────────────┐
│ ●High  Sign SoW · Claims Portal           ENG-0412   ⏱ 2d   [ Sign ▶]│
│ ↑sev   ↑action verb + target              ↑ref      ↑SLA   ↑1 primary│
└────────────────────────────────────────────────────────────────────┘
```
- **Left:** priority dot (drives sort). **Centre:** action verb + target (always "do X to Y", never a noun). **Ref:** engagement id. **SLA badge** from `FR-ANL-01` (green→amber→red as it ages). **One primary button** = the next legal move from §6.

**Sorting / prioritisation (descending):**
1. **Overdue** (SLA breached, `FR-ANL-02`) — red, always top.
2. **Gate actions** (sign-off, access-verify, peer-review) — they unblock others.
3. **Severity** of the underlying engagement criticality / finding.
4. **SLA age** (closest to breach first).
5. Tie-break: oldest `updated_at`.

**"Your turn" vs "Waiting on others" logic:** an item is *yours* iff the current guard's owner (§6) resolves to you under RBAC + object-level auth (`SEC-AZN-01/02`) — testers only ever see their assigned engagements (`SEC-INS-01`). Everything where you're the *next-but-one* owner, or you raised it and it's moving, goes to **Waiting on others** with "who · what · age" and a polite **Nudge** (`FR-NOT-03`) — no action button.

**Empty state (the reward):** `"You're all clear. 3 engagements moving without you."` Calm, genuine, no confetti.

**Feeds:** every state transition emits an event (`FR-NOT-01`) → in-app + email (optional Teams, `FR-NOT-02`); reminders for pending owners (`FR-NOT-03`) re-surface a row. This is the *one* attention system — scattered alerts are explicitly avoided.

**[new — not in SRS]** Per-row "snooze until" (max to SLA cap) so testers can defer non-urgent rows without losing them. Justification: prevents inbox fatigue on long test windows; bounded by the SLA so it can't hide a breach.

---

## 8. Gate Ceremony — Frame by Frame (SoW sign-off)

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
└──────────────────────────┘  3) state transition #5 (§6)

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

## 9. The Component Kit (~12 primitives)

The whole UI composes from these. Same vocabulary everywhere = the "butter" feel. States listed as default / hover / focus / disabled / loading / error.

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
| 12 | **Evidence tile** | secure artifact | thumb + type + secured badge | default / uploading / encrypted ✓ / download(signed-url) | screenshot · req/resp · log · file | `SEC-EVD-01/02` |

Cross-cutting: every interactive component ships keyboard focus, visible focus ring, ARIA label, and a non-colour status cue (`NFR-USA-03`).

---

## 10. Design Tokens (names + intent; final values in Phase 2)

### Colour
| Token | Intent | Light | Dark |
|-------|--------|-------|------|
| `canvas` | app background | warm off-white | deep slate |
| `surface` | cards/drawers | white | raised slate |
| `ink` / `ink-muted` | text primary / secondary | near-black / grey | near-white / grey |
| `brand` | the one accent — primary actions | confident blue (e.g. SRS header `1F3864` family) | brightened brand |
| `sev-critical … sev-info` | fixed 5-step severity scale | red→deep-orange→amber→blue→grey | same hues, tuned for contrast |
| `state-waiting / on-you / done / blocked / locked` | process spectrum | amber / brand / green / red / grey | tuned |
| paired `*-on` foregrounds | text-on-status (AA contrast) | — | — |

Rule: status colours appear **only** on status; never as decoration. Every status colour ships an icon + label partner (`NFR-USA-03`).

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
| `dur-ceremony` | ~400ms | spine unlock, gate success |
| `ease-standard` / `ease-emphasis` | smooth / slight-overshoot | standard UI / payoff moments |
| `reduced-motion` | crossfade-only fallbacks | honor `prefers-reduced-motion` |

---

## 11. ⌘K Command Palette

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

**Vocabulary:** Navigate (`go to engagement / register / my-turn / tab`), Act (`sign SoW`, `verify access`, `add finding`, `send IR notice`, `request retest` — each gated by the same guard table §6 and object-level auth, so the palette never offers an illegal move), Search (engagements, findings, refs).

**Keyboard model:** `⌘K` palette · `g` then letter = go-to (g+e engagements, g+m my-turn) · `j/k` move rows · `↵` primary action · `e` open drawer · `⌘↵` confirm in ceremony · `Esc` cancel/close. Results are RBAC-scoped (testers see only their engagements, `SEC-INS-01`).

---

## 12. What Makes It Addictive (legitimately, for this audience)

1. **Zero-inbox loop** — My-Turn → clear → reward (§7).
2. **No-think clarity** — one obvious action per screen.
3. **Instant + final** — optimistic UI, ⌘K, keyboard-everything.
4. **Visible momentum** — the spine filling stage by stage.
5. **Earned trust** — visible audit, masked secrets, gate ceremonies make relying on it *pleasant* for a security pro.
6. **Auto-drafting** — SoWs, emails, reports that write themselves remove the dreaded chores.

Deliberately **not** used: points, badges, streaks, confetti — they'd undermine credibility with regulated-insurer and security users. Addictiveness here is *frictionlessness*.

---

## 13. Accessibility, Responsiveness, Performance

- **WCAG 2.1 AA** (`NFR-USA-03`): status never by colour alone (chip + icon + label), full keyboard model, focus-visible, reduced-motion.
- **Desktop-primary, responsive** (`NFR-USA-04`): working surfaces (capacity, register) are desktop-rich; stakeholder surfaces degrade gracefully to tablet/phone.
- **Feel-fast** (`NFR-PER-01`, ~2s p95): optimistic UI hides latency; heavy work (report/doc generation, notifications) is async (`NFR-PER-03`) with clear in-progress states, never a frozen screen.

---

## 14. Phase-2 Wireframe Checklist & Open Questions

**Wireframe set (priority order):**
1. ☐ Spine component — all states (done/current/future/locked/error)
2. ☐ My-Turn home — Tester (busiest) + Stakeholder (simplest)
3. ☐ Universal Engagement view shell with role-filtered stage bodies
4. ☐ Gate-ceremony modal — SoW sign-off, all 6 frames + error paths (§8)
5. ☐ Assessment — conditional + completeness meter, both lenses
6. ☐ Capacity board — assignment + over-allocation warning
7. ☐ Finding capture + Live Register (with CVSS calc)
8. ☐ Access & prerequisites — masked-secret reveal flow
9. ☐ Retest before/after diff
10. ☐ Exceptions/analytics feed + Audit timeline
11. ☐ Component kit (the 12 primitives) + colour/type/motion tokens

**Open questions for Phase 2:**
- **Front end:** Blazor vs SPA still open (`ASM-01`). The optimistic-UI + ⌘K + drawer-heavy model argues for a **SPA** (React or Blazor-WASM); confirm in design.
- **ITSM integration** depth for live ticket status on the Access screen (`CON-05`) — which system, what API, polling vs webhook.
- **B2B guest first-run** (`FR-AUTH-07`) — keep stakeholder onboarding kid-easy despite the Entra guest flow.
- **Daily-progress** detail testers actually want (`FR-EXE-02`) — validate it isn't busywork.
- **Snooze** (§7) and **command palette** (§11) are [new — not in SRS] — confirm they're in-scope for v1 or deferred.

---

*v0.2 draft for review. Pair with the SRS; resolve §14 before/with wireframing. — Phase-1.5 design input.*
