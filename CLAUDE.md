# Clinic Node desktop (read this first)

Before doing anything, read `docs/handoff/00-START-HERE.md` (state, rules, Windows crash patterns, release steps).
Longer docs: `docs/handoff/1-SYSTEM-OVERVIEW.md`, `2-OPERATIONS-RUNBOOK.md`, `5-TODO-AND-RISKS.md`.

Non-negotiables: never tag/deploy without the owner saying "ship"; batch work into one release; the owner types all passwords; say where a UI control goes and build exactly that; wrap new code in `//JJQR - AI (date) - why` ... `//JJQR - END`; on Windows check every redraw for a reused control that still has an old parent.
