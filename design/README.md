# PEMP — Design & Phase-2 Artifacts

Companion artifacts to the SRS (`../PEMP_SRS_v1.0_Master.docx`) and the Experience &
UI Design Plan (`../DESIGN_PLAN.md`). Everything traces back to SRS requirement IDs.

| File | What it is |
|------|-----------|
| `tokens.css` | Design tokens (DESIGN_PLAN §12) as CSS variables — **dark-first**, light equal. Theme source of truth for the SPA. |
| `prototype/index.html` | Self-contained **clickable prototype** with the **fractal spine**: a persistent master engagement rail up top, and every role tab (My Work · Assessment · SoW · Access · Findings · Reports) carries its **own sub-spine** for that stage's steps. The two levels lock together — signing the SoW (gate ceremony: attest → re-auth → optimistic commit) advances the master rail *and* unlocks the next tab's sub-flow. Open in any browser; toggle theme; click into tabs or master-spine stages; click *Review & Sign SoW* to play the ceremony. |
| `state-machine.md` | The enforced engagement **state machine** (Mermaid diagram + authoritative guard table + invariants). The product's core. |
| `state-machine.svg` | Rendered diagram (validated). |
| `architecture.md` | **Phase-2 architecture**: SPA decision, layered ASP.NET Core backend, RBAC + object-level auth + isolation, EF Core data model, credentials/evidence/audit chain, Azure platform, build sequencing. |
| `compliance/traceability.md` | SRS requirement → artifact **traceability matrix** (audit trail). |
| `compliance/dpia.md` | UK GDPR **DPIA starter** (`NFR-CMP-02`). |
| `compliance/threat-model.md` | STRIDE **threat model** skeleton (`SEC-SDL-03`). |

## How they fit together

```
SRS (what)  ─▶  DESIGN_PLAN (how it feels)  ─▶  prototype + tokens (see it)
                                            └─▶  architecture + state-machine (how it's built)
                                            └─▶  compliance/* (prove it)
```

Phase order (Musts first) is in `architecture.md` §6.
