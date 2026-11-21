<!-- JJQR - AI (Nov 21, 2026) - architecture for the native .NET MAUI Clinic Node. Records the data-layer decision and why. -->

# Clinic Node — MAUI architecture

Decision date: 21 Nov 2026 (Asia/Manila). Owner decision: native .NET MAUI, no embedded browser, no Node, no right-click / View Source. The previous Photino + Next.js spike (`Project02-desktop/ClinicDesktop`) is **kept but superseded**; do not delete it.

This document is the binding record of the data-layer choice asked for in the dispatch.

---

## 1. Data layer decision

**Chosen: reuse `ClinicApp.Domain`, `ClinicApp.Infrastructure` (EF Core on SQLite) and `ClinicApp.Auth` in-process inside the MAUI app. Do not use sqlite-net-pcl.**

**Why this protects "do not lose any feature" and offline correctness better than sqlite-net-pcl:**

1. The cloud API already runs the **entire** model on SQLite (`Database:Provider=Sqlite`, `EnsureCreated()` + `SqliteDemoViews`). All **419** backend tests run on that path. A second persistence stack would have to re-implement 53 DbSets, seeders (ICD-10 WHO 2019, medicine catalog, built-in templates, demo accounts), naming conventions, and every invariant (queue counters per day per lane, fee computation, waive-on-complete, exact-amount payments, booking/payment state machines, completion-window / immutable finished consults).
2. Those rules today live mostly in API controllers, but they already speak EF + the domain entities. `ClinicNode.Core` application services use the **same entities and `ClinicAppDbContext`**, ported from the controllers (consultation first). sqlite-net-pcl would force a parallel schema and a second copy of every rule — the fastest way to drift from the web product.
3. Sync (row versions, tombstones, outbox in the **same SQLite transaction** as the clinical write) is straightforward with `Database.BeginTransaction()` + outbox rows on the same connection. A second ORM would make "same transaction" fragile.
4. The SQL Server package comes along as a dependency of `ClinicApp.Infrastructure` but is unused at runtime on the node (SQLite only). If Mac Catalyst ever rejects `Microsoft.Data.SqlClient` native assets, the fallback is a thin shim or a conditional reference — **not** a rewrite onto sqlite-net-pcl.

**What we explicitly did not choose:**

- **sqlite-net-pcl** as the system of record (owner example only). Fine for a toy store; wrong for this clinic model.
- **Hosting Kestrel / the full `ClinicApp.Api` inside the MAUI process and calling HTTP localhost.** That would reuse controllers 1:1, but Mac Catalyst sandbox + a local HTTP port is a packaging risk, and it is how the Photino spike already worked. We keep this as a **fallback** if a screen is blocked on a controller we have not ported yet; the default path is in-process services.
- **Extracting a `ClinicApp.Application` project from the production API in this dispatch.** That is the right long-term share (cloud API and node call one implementation) but it is a wide refactor of an uncommitted production backend. We will not do that until the owner asks, and we will never push `main` (auto-deploy) without an explicit "push".

---

## 2. Solution layout

All new code lives under `Project02-desktop/ClinicNode/` (sibling of the Photino `ClinicDesktop/`). It **references** the existing backend projects; it does not copy them.

```
Project02-desktop/
  ClinicDesktop/          # superseded Photino spike (keep)
  docs/                   # this file, NATIVE-SCREEN-INVENTORY.md, API-SURFACE.md
  ClinicNode/
    ClinicNode.sln
    ClinicNode.Core/      # application services, session, fee math, consult steps
    ClinicNode.Data/      # local SQLite bootstrap, sync schema, transactional outbox
    ClinicNode.Sync/      # push/pull client, retry/backoff, status
    ClinicNode.App/       # MAUI UI (net10.0-maccatalyst, and net10.0-windows on Windows)
    ClinicNode.Tests/     # xUnit, net10.0 (no MAUI), data + sync + consult services
```

Target frameworks:

- Libraries and tests: `net10.0`.
- App: `net10.0-maccatalyst` always; `net10.0-windows10.0.19041.0` **only when the build runs on Windows** (a Mac cannot compile the Windows TFM). The owner builds the Mac target (DMG) themselves; a Windows PC will be used later for MSIX.

UI is **.NET MAUI native controls** (no `BlazorWebView`, no `WebView` for the product UI). A WebView used only as a print surface (HTML forms already exist) is an allowed print implementation detail, not a UI shell.

Every new file carries a `JJQR - AI (Nov 21, 2026)` header. Existing backend/frontend files are not edited in this dispatch unless a blocker forces a clearly marked, local-only change (none planned). The running web stack on `:3000` / `:5055` is not touched.

---

## 3. Local database

- Default file: `{AppData}/ClinicNode/clinic-node.db` (overridable for tests).
- Provider: `UseSqlite(...).UseSnakeCaseNamingConvention()` — same as the API's SQLite path.
- Schema: `EnsureCreated()` + `SqliteDemoViews.CreateViews` (SQL Server migrations are the wrong dialect).
- Seed (always): `Icd10Seeder`, `MedicineCatalogSeeder`, default vital-field templates (BP / HR) if missing, clinic fee schedule row if missing.
- Seed (demo only): `DevDataSeeder` when `ClinicNode:Demo=true` or env `CLINIC_NODE_DEMO=1`. This creates `admin|staff|doctor|patient@clinic.test`. **Never enable demo on a real clinic install.**
- Sync tables (`node_identity`, `node_sync_outbox`, `node_sync_cursor`) are created with raw SQL on the **same file**. They are not added to `ClinicAppDbContext` (that would edit the production model). Outbox writes use the same ADO.NET connection and transaction as the EF `SaveChanges`.

Encryption at rest (SQLCipher / DPAPI / FileVault / BitLocker) is **phase E**, not this dispatch. Until then the DB is plain SQLite and the machine must be treated as holding PHI.

---

## 4. Auth (offline)

- Staff/doctor/admin **accounts and password hashes** live in the local `users` / `staff_accounts` tables (synced down from the cloud when online; demo-seeded in development).
- Login uses `PasswordHasherService` (same as the API) and rejects `StaffStatus.Inactive`.
- The node issues **its own session** signed with a **node secret** created on first run (`node-secret.key` in the data folder, 32 random bytes). The cloud JWT secret never leaves the cloud. Access ~1 hour, refresh 7 days, rolling.
- **"Remember my email"** stores only the email in `Preferences` (or equivalent). The password is never stored.
- Maximum offline period, then lock until a successful sync: designed as 7 days (study doc 5.3); the constant lives in Core, enforcement comes with the real sync client.
- First-run setup for a real (non-demo) clinic is still outstanding (owner item D). Until then, only demo accounts or a future "sync accounts down once while online" path can log in.
- Forgot-password / email verification / social login are hidden or marked "needs internet".

---

## 5. Sync layer

Designed from `Project02-be/docs/DESKTOP-OFFLINE-STUDY.md`. This dispatch ships the **skeleton** (outbox, cursor, backoff, status); the cloud `push`/`pull` API does not exist yet.

**Write path:** every local mutation (create/update/delete) inserts an outbox row in the **same transaction**: `operation_id` (client GUID, idempotency key), aggregate name, entity id, operation type, payload JSON, `created_at`, attempt count, last error, `acked_at`.

**Client loop:** when internet is believed available, push unacked outbox rows in batches to `POST {cloud}/api/sync/push`; on success mark `acked_at`. Then `GET {cloud}/api/sync/pull?since={cursor}` and apply. Retry with exponential backoff and jitter (1s, 2s, 4s, … cap 5 min). Failures stay in the outbox.

**Status UI:** `Idle` / `Syncing` / `Pending (n)` / `Offline` / `Error` / `Last synced at …`. Shown in the shell.

**Conflict rules (per aggregate, not last-write-wins):**

| Data | Rule |
|------|------|
| Consultation (notes, vitals, Rx, labs, follow-up, certificate) | Node wins. A **finished** consultation is immutable; amendments are append-only. |
| Payments | Single-writer (front desk). Node's recorded payment wins; conflicting cloud edits are flagged, never merged. No refunds offline. |
| Audit logs, vitals readings, access logs | Append-only union. |
| Settings, templates, medicines, lab catalog, vital templates | **Cloud wins**; pulled down read-only (the node may have a local cache). |
| Patient demographics | Field-level merge; later `UpdatedAt` wins; remainder on a conflict list. |
| Booking status | State machine: keep the furthest-progressed valid state; illegal transitions go to a review list. |

Schema still needed on the **cloud** (later, owner-approved): `row_version`, `deleted_at` tombstones, node id, client-supplied ids on all creates, the two sync endpoints, version handshake. The node already generates GUIDs itself so it is ready.

One node per clinic is the intended topology (avoids two offline writers).

---

## 6. Numbering offline

| Number | Rule |
|--------|------|
| Queue `R-001` / `S-001` | The node is the **only issuer** while it is open. Uses local `QueueCounter` (per calendar day, per lane). Senior/PWD → S lane; S always sorts ahead of R. Cloud online bookings that arrived during the outage are numbered on sync **after** the node's numbers (clinic must confirm this rule). |
| Patient code `MF-000123` | Pre-reserve a block (e.g. 100 codes) per node while online. If the block runs out, issue a temporary `MF-T-0001` and replace it on sync (reprint the card). Block table lives next to node identity. |
| Receipt / OR | Already derived from the booking id on the web; keep that, and print a **pending sync** note when the outbox is unacked. Confirm legal OR rules before offline payments go live. |

---

## 7. Printing on MAUI

The web app already builds HTML for:

- Prescription and medical certificate: **A5 (148 mm × 210 mm)**
- Lab request: **4.3 in × 6.4 in**
- Queue slip: compact `.slip` layout (thermal later)

Letterhead is pre-printed stationery.

**Approach:** port the HTML builders (or call a small shared formatter in Core) and print via the platform print dialog with explicit page sizes (`UIPrintInfo` / `NSPrintInfo` on Mac Catalyst; `PrintManager` / `PrintDocument` on Windows). A hidden WebView used **only** to paginate that HTML is acceptable as a print renderer; it is not the product UI. Later, QuestPDF (or similar) can emit the same sizes as PDF for silent / thermal printers. Real printer testing (including thermal slips) is a Windows/Mac field task (owner item F), not this dispatch.

---

## 8. Navigation and UI structure

`AppShell` flyout / tabs by role after login:

- **Doctor:** Today, Appointments, Patient charts, Performance, Availability, Templates, Profile; Consultation is a pushed page from Today / Appointments / a walk-in start (consult must work even when the live queue is empty).
- **Staff:** Dashboard, Payments, Walk-In, Queue, Patients, Doctor Status, Announcements, Profile.
- **Admin:** Dashboard, Bookings, Walk-In, Doctors, Patients, Staff, Announcements, Settings, Audit Logs, Reports.

Patient routes and `/register` are absent. A persistent **sync status** + **idle lock** (later) live in the shell.

Look and typography should stay close to the web clinic (teal / "Grace Medical Clinic" tones) without cloning CSS. The consultation stepper, "Save & next", hidden Diagnosis flag, and fee-last rule are behavioural requirements, not optional styling.

---

## 9. Packaging (later)

| Platform | Package | Notes |
|----------|---------|-------|
| Windows | MSIX | Double-click install. Bundle the .NET runtime (self-contained). WebView2 is **not** required. Auto-start and auto-update come in hardening. |
| Mac | DMG | Owner builds the Mac Catalyst target themselves. Self-contained `net10.0-maccatalyst`. |

Version handshake with the cloud schema before sync (`GET /api/version`). A node older than the cloud must update first. Node auto-update is phase 4.

Do **not** ship Node, a Next standalone build, or an embedded browser.

---

## 10. Security (later phases)

Encrypt the local database; store the key with Windows DPAPI (and the Mac Keychain equivalent). Require full-disk encryption. Idle lock. Remote revocation (node refuses to sync after revoke). Encrypted backups. Data Privacy Act (RA 10173) — who may be stored locally (all patients vs recent only) is an owner decision still open in the study doc.

---

## 11. What this dispatch delivers vs later

**Now:** docs, MAUI scaffold that **builds for Mac Catalyst**, local SQLite bootstrap, auth + remember email, shell for the three roles, consultation flow (load / save draft / complete) against local SQLite, sync skeleton + unit tests.

**Next slices:** remaining P0 screens (queue, walk-in, payments confirm/waiver), numbering blocks, then P1 parity, then cloud sync API (owner-approved backend change), real accounts, encryption, Windows MSIX, real printers.
