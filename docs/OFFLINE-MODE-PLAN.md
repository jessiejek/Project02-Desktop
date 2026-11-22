# Offline mode and sync: project plan

Status: DRAFT for owner approval, 22 Nov 2026 (Asia/Manila). Nothing in this plan has been built yet.
Owner: Jessie. Builder: Claude (coding agent). This file is the single source of truth. If the plan and a conversation disagree, the plan wins until the owner changes it here.

## 1. What the owner asked for (verbatim scope)

1. A live checker for the connection between the desktop and the local API, which also checks the database.
2. A full local copy of the database in SQLite on the desktop.
3. If the connection is slow or down, suggest Offline mode, but never switch automatically.
4. In Offline mode, everything goes under the doctor's account. Registering patients, booking, consultations and payments are all done by the doctor, so there is only one writer.
5. In Offline mode, the doctor registers new patients or finds them in the local database, then creates the booking.
6. Consultations are saved locally.
7. When the API is back, sync manually or let auto sync run.
8. Sync issues are uploaded to the local server as review items, and the admin resolves or merges them in the web admin portal.
9. The admin merges duplicate patients, manually.
10. Register the desktop with the local backend before testing offline, or nothing will sync.
11. Auto sync at the first login of the day.
12. IDs are created by the desktop, so there are no duplicates.
13. No web bookings for now.
14. Only the doctor marks offline payments as received, using the booklet receipt number, and clashes are flagged for the admin.
15. Printing works offline.
16. Offline login works for up to 7 days.
17. A sync results screen and a status badge showing Online, Offline with the number waiting, or Sync problem.
18. A warning before closing the app or switching back to Online while items are still unsynced.
19. Test it all on the owner's Mac by stopping the local API or the Docker database to simulate going offline.

Anything not on this list is OUT OF SCOPE (section 9) unless the owner adds it here.

## 2. Working rules (no deviation)

1. **One branch per step**, named `plan/step-N.N-short-name`, branched from the latest `main` of the repo it touches. A step that touches two repos uses the same branch name in both.
2. **One pull request per step.** Title `Step N.N: <name>`. The body lists: what changed, which acceptance checks (section of this plan) it meets, and the exact test commands with results.
3. **Never push or merge to `Project02-be` `main`.** The workflow `.github/workflows/deploy-backend.yml` deploys on every push to `main`. Only the owner merges backend PRs, after the release gate (Phase 5). Desktop and web `main`: the owner merges.
4. **Tests are run after every step before the PR:**
   - Backend: `dotnet test tests/ClinicApp.Api.Tests`
   - Desktop: `dotnet test ClinicNode/ClinicNode.Tests`, plus `dotnet build ClinicNode/ClinicNode.App/ClinicNode.App.csproj -f net10.0-maccatalyst`
   - Web (only Phase 4): `npx tsc --noEmit`
   - A step is not done while a test that passed at the baseline (step 0.1) now fails. Failures already in the baseline are not hidden or fixed silently; they stay listed.
5. **Facts, not guesses.** Every item marked **VERIFY FIRST** must be checked in the code or by running it, and the result written into section 11 (Verification log) before any code depends on it. If a check disproves an assumption, work stops and the owner is told before the plan changes.
6. **Stop and ask the owner** (do not continue) if: a decision in section 3 marked OPEN is needed; a step needs a database migration; a step would take more than twice its estimate; something would touch production; a verification result contradicts the plan.
7. **Code markers:** `//JJQR - AI (<date>) - <why> ... //JJQR - END` around every change, as in the existing code.
8. **Wire changes** are recorded in `docs/SYNC-API-CONTRACT.md` section 11 (Changelog) in the same PR.
9. **Test data:** only records prefixed `QA-Test` on the owner's Mac. No real patient data. No production URL in any test.
10. **No scope creep:** nothing from section 9 gets built "while we are there".

## 3. Decisions

### Confirmed by the owner
| # | Decision |
|---|---|
| D1 | Two modes only: **Online** and **Offline**. No automatic mode (the old AUTO fallback is removed). Default for a new install: Online. |
| D2 | The system never switches mode by itself. It may only suggest. |
| D3 | In Offline mode the doctor is the only writer (registration, booking, consultation, payment). |
| D4 | Duplicate patients are flagged at sync and merged **manually by an Admin only**, in the web admin portal. Never automatically. |
| D5 | Auto sync runs at the first login of the day. |
| D6 | Only the doctor marks offline payments received, typing the booklet receipt number. Clashes are flagged for the admin. |
| D7 | The desktop must be registered with the local backend before offline mode can be used. |
| D8 | No web bookings for now. |
| D9 | Offline login works up to 7 days since the last successful sync. |

### Derived from the confirmed rules (owner to confirm; I will not build until confirmed)
| # | Derived rule | Why |
|---|---|---|
| R1 | In **Online** mode, if a save cannot reach the API, the app shows an error and offers "Switch to Offline mode". It does **not** quietly save locally. | D2 forbids switching by itself; today's silent fallback is exactly what hid unsynced edits. |
| R2 | In **Offline** mode the app never calls the API for writes. | D1, D3. |

### OPEN (the owner must answer; each blocks the phase named)
| # | Question | Blocks | My recommended default (NOT decided) |
|---|---|---|---|
| Q1 | Offline queue numbers come only from reserved blocks, and with no block left there is no number. Keep that hard stop, or book with a "Paper queue" note and let the server assign a number at sync? | Phase 2 step 2.3 | Book with "Paper queue" and no number, so the doctor is never blocked. **ANSWERED 22 Nov (owner): Paper queue. Numbers are handed out on paper after the last number in the system; when the patient enters the doctor's room the doctor searches or registers the patient and adds them to the queue then.** |
| Q2 | Staff and admin users while the computer is in Offline mode: read-only, or fully blocked from the app? | Phase 2 step 2.1 | Read-only. **ANSWERED 22 Nov (owner): NOT read-only. Staff and Admin do not work at all in Offline mode; every transaction is under the doctor's account (staff are on the same network, so if one is offline all are).** |
| Q3 | May the doctor also mark payments received while **Online** (needs a backend change: `POST /api/payments/{id}/confirm` is currently Admin and Staff only), or only in Offline mode? | Phase 2 step 2.4 | Offline only, for now. **ANSWERED 22 Nov (owner): yes, if possible: the doctor may also mark payments received while Online (backend change on a branch; never pushed to main without the owner's go).** |
| Q4 | Which problems become review items for the admin in the web portal: only duplicate patients and receipt-number clashes, or every sync problem type? | Phase 4 step 4.0 | Duplicate patients, receipt clashes, and rejected or stuck items. |
| Q5 | In Offline mode, when the API comes back, does background auto sync run by itself, or only the first-login sync and the manual Sync button? (Decision 7 says "sync manually or let auto sync run".) | Phase 1 step 1.2 | Only first-login sync and the button; the background loop runs in Online mode only. **ANSWERED 22 Nov (owner): accepted.** |
| Q7 | Offline login counts 7 days from the last successful **sync** (not the last login), and a registered computer that has never completed a sync is never locked (V6). Keep that, or also lock a never-synced computer after 7 days from registration? | Phase 2 step 2.6 | Lock it 7 days after registration if it still has not synced. **ANSWERED 22 Nov (owner): no lock, keep storing.** |
| Q6 | Are the monitor thresholds acceptable: Good under 300 ms; Slow 300-1500 ms or 1 failure in the last 10 checks; Unstable 2-4 failures; Down at 3 failures in a row, 5 or more failures in the last 10, or database not ready? | Phase 1 gate | Use them; adjust at the Phase 1 gate by feel. **ANSWERED 22 Nov (owner): accepted.** |

## 4. What exists today (verified by reading the code on 22 Nov 2026, not run)

Use this as the starting point. Anything here marked "(read, not run)" is still VERIFY FIRST.

| Area | Finding | Where |
|---|---|---|
| Connection ping | Pings `GET {base}/health` (liveness only, no database). Raises `Changed`. No latency, no history. | `ClinicNode.Api/Http/ApiConnectivity.cs`; backend `Program.cs` line ~327 |
| Database-aware check | `GET /api/version` is anonymous and calls `Database.CanConnectAsync`. | `Project02-be/.../Controllers/VersionController.cs` |
| Local copy | Pull carries patients, bookings, payments, consultations, vitals, catalogs, templates, users (with password hash for offline login). Patient files are metadata only. Whether a fresh node receives full history is not verified. | `ClinicNode.Sync/Local/AggregateDocuments.cs`; `docs/SYNC-API-CONTRACT.md` sections 4-6, 10 |
| Save routing | `OnlineFirstProxy<T>`: API first, falls back to local on a network failure. No mode switch exists. A static `OnlineRouteStatus.LastWriteWasLocalOnly` flag (added 22 Nov) drives the "Saved on this computer only" notice. | `ClinicNode.Api/OnlineFirst/OnlineFirstProxy.cs` |
| Visit protection | `VisitVersions` and the backend `VisitVersionMiddleware` refuse stale saves (added 22 Nov). Interaction with local saves and sync is not yet defined. | `ClinicNode.Api/Http/VisitVersions.cs`; backend `Middleware/VisitVersionMiddleware.cs` |
| Sync | Outbox, `SyncService`, `SyncLoop` (background, 30 s style loop), "Sync now" in the top-bar menu and node settings, queue-number blocks, `SyncConflictsPage` (local list). `RequestSync()` is fire-and-forget: there is no awaited run and no result object. | `ClinicNode.Sync/*`; `AppShell.xaml.cs` (`SyncNowCommand`); `Views/SyncConflictsPage.cs`; `NodeSettingsViewModel.cs` |
| Status badge | Shows Online / Offline with the waiting count / conflicts. `Phase.Error` is shown as "Offline", not "Sync problem". | `Views/Shared/TopBar/TopBarView.cs` (~lines 192-210) |
| Registration | Admin screens exist (`AdminSyncComputersPage`, `AdminRegisterNodePage`). Nothing blocks local writes on an unregistered computer; today they are accepted and never sync. | `Views/Admin/*` |
| Doctor offline walk-in | "New walk-in consult" on the doctor's Today page reuses `WalkInWizardView` (look up a synced patient or register a new one, then start the consult). | `Views/Doctor/NewWalkInPage.xaml.cs`, `TodayPage.cs` |
| Client-made IDs | **Missing.** `WalkInRequest` has no booking or payment ID. The online path lets the server mint IDs; the local fallback mints its own (`Guid.NewGuid()` at `QueueService.cs` lines ~135-136, `PatientService.cs` line ~110). A lost reply creates two bookings. The server already accepts `booking_id`, `payment_id` and `patient_id` (`QueueController.CheckInWalkInRequest`, `PatientsController.Create`). | `ClinicNode.Core/Queue/QueueModels.cs`; `ClinicNode.Core/Queue/QueueService.cs` |
| Payments | Local `StaffPaymentsService` allocates receipt numbers (`AllocateOrNumberAsync`). No doctor payment screen. `POST /api/payments/{id}/confirm` is Admin and Staff only; `waive` allows Doctor. The server flags `or_number_clash`. Whether the local flow accepts a typed booklet number is not verified. | `ClinicNode.Core/Staff/StaffPaymentsService.cs`; backend `Controllers/PaymentsController.cs`; `Sync/SyncContracts.cs` |
| Duplicates | Server reason codes: `patient_code_taken`, `queue_number_reassigned`, `or_number_clash`. No same-name-same-birthdate detection. No merge endpoint. | backend `Infrastructure/Sync/SyncContracts.cs`, `SyncPushEngine.cs` |
| Review items | Conflicts live only in a local desktop list. No server table, no endpoint, no web admin page (`Project02-fe/src/app/admin/*` has none). | - |
| Offline login | `NodeOptions.MaxOfflinePeriod = 7 days`; checked against `LastSuccessfulSyncAt`. | `ClinicNode.Data/NodeOptions.cs` line 29; `ClinicNode.Core/Auth/AuthService.cs` line ~73 |
| Printing | Local rendering (HTML to PDF; browser auto-print on Mac). | `ClinicNode.App/Services/MauiPrinter.cs` |
| Closing the app | `Window.Destroying` only stops the sync loop. No warning. | `ClinicNode.App/App.xaml.cs` line ~72 |
| Test baseline | Backend: 4 failing (SyncTests queue-clash). Desktop: 73 failing (Sync, Staff, Admin, Printing areas). None in consultation or prescription code. | measured 22 Nov |

## 5. Overview of phases

| Phase | Goal | Steps | Owner gate |
|---|---|---|---|
| 0 | Foundation: no duplicates, no unsafe offline saves, honest health check | 0.1-0.6 | Owner's lost-reply and unregistered-computer tests pass |
| 1 | Offline mode, live monitor, suggestions, status badge | 1.1-1.6 | Owner runs Offline on/off and sees monitor states |
| 2 | The doctor's offline day | 2.1-2.7 | Owner runs a 10-patient offline day |
| 3 | Sync experience | 3.1-3.5 | Owner syncs after the offline day; web matches |
| 4 | Review items and admin web portal | 4.0-4.6 | Owner resolves and merges test items in the web |
| 5 | Hardening and release | 5.1-5.4 | Owner says "go" for production |

Build in this order. A phase starts only after the previous phase's gate is passed and recorded in section 12.

## 6. Phase 0: Foundation

Goal: close the holes that would make an offline day unsafe, before adding any mode.

### Step 0.1 Baseline and verification
- Record the exact failing tests (names) for backend and desktop into `docs/status/test-baseline-2026-11-22.txt`.
- Run the VERIFY FIRST items V1-V10 (section 11) and fill in the results.
- Confirm the dev setup on the owner's Mac: Docker database `clinic-mssql`, backend on `:5055`, desktop launched with `CLINIC_NODE_CLOUD_URL=http://localhost:5055`.
- **Done when:** baseline file committed, section 11 filled in, owner informed of any result that contradicts section 4.

### Step 0.2 Health check that includes the database
- First decide, from V2, whether `GET /api/version` is suitable (anonymous, answers when the database is down, not rate-limited). If yes, use it and add nothing to the backend. If no, add `GET /health/ready` (anonymous) returning `{status, db_ok, db_ms, server_time, version}` and HTTP 503 when the database fails (backend branch only; no migration).
- Desktop: `ApiConnectivity` calls the chosen endpoint, so "online" means API **and** database.
- **Acceptance:** with the API up and Docker `clinic-mssql` stopped, the **connection check** (`ApiConnectivity` and the sync loop's probe) reports "not ready" within 10 seconds; restarting Docker returns it to ready. The **top-bar label** is not part of this step (step 0.5 and Phase 1 change it): for a computer that is not registered for sync it prints a fixed "Online · not registered for sync" and does not read the probe.
- **Tests:** backend test for the endpoint (database up / down); desktop fake-handler test for each answer.

### Step 0.3 Client-made IDs
Scope is only the flows the offline plan uses: walk-in check-in, patient quick-register, payment record.
- Add `BookingId` and `PaymentId` to `WalkInRequest`; add `PatientId` to the quick-register form.
- The screen creates the IDs once, before the call. `QueueService.CheckInWalkInAsync` and `PatientService` (local) use the given IDs instead of `Guid.NewGuid()`. The Api services send `booking_id`, `payment_id`, `patient_id` (the server already accepts them).
- A repeat of the same ID must return the existing record, not create another (confirm on the server in V3).
- **Acceptance (lost reply):** the API saves the record but the reply is dropped; the desktop retries locally; after sync the web shows exactly one booking and one payment for that patient. For the owner's test a **debug-build-only** switch exists: start the desktop with `CLINIC_NODE_TEST_DROP_RESPONSES=1` and every successful POST's reply is thrown away. It is compiled out of release builds.
- **Tests:** a desktop test with a fake handler that saves and then times out; a backend test that a repeated `booking_id` / `patient_id` creates one row.

### Step 0.4 Registration gate
- Define "registered" as: a node secret exists and `SyncPhase` is not `NotRegistered`, `Revoked` or `NeedsUpdate`.
- On an unregistered computer, any local write throws `LocalWriteBlockedException` with the message "Register this computer for sync first (Admin > Sync computers)." Reads still work. Online API writes still work.
- Replace the interim "Saved on this computer only" notice (22 Nov) with this gate for the unregistered case; keep the notice text only where a local save is actually allowed.
- **Acceptance:** unregistered + API stopped + doctor saves a consultation: blocked with that message. After registering: the save works locally.
- **Tests:** unit test of the gate in `OnlineFirstProxy`; UI check by the owner.

### Step 0.5 "Sync problem" state
- Define the badge states exactly:
  - **Online**: reachable, nothing waiting.
  - **Offline (N waiting)**: mode is Offline (Phase 1) or the API is unreachable; N = outbox items not yet acknowledged.
  - **Sync problem**: `SyncPhase` is `Error`, `Revoked` or `NeedsUpdate`, **or** one or more outbox items have been retried at least 10 times (V4 decides how attempts are counted).
- Fix `TopBarView` so `Phase.Error` reads "Sync problem", not "Offline".
- **Acceptance:** each state can be produced on the owner's Mac (stop API; revoke the node; force a failing item) and the badge shows the right word.

### Step 0.6 Phase 0 gate
Owner tests, in order. Run the backend from the local branch `plan/phase-0-integration` (it contains steps 0.2 and 0.3) and the desktop from branch `plan/step-0.5-sync-problem`.

**T1 health check**
1. `curl -s -w "\n%{http_code}\n" http://localhost:5055/health/ready` shows `"db_ok":true` and 200.
2. `docker stop clinic-mssql`, wait 3 s, run the same curl: `"db_ok":false` and **503 in about 2 seconds**.
3. `docker start clinic-mssql`, wait about 15 s: 200 again. (The first answer right after the restart can still say not ready once.)

**T2 registration gate**
1. On this unregistered computer, stop the backend. In the desktop, as Doctor, open a visit, change a note, press Save & next. Expected: a box "Can't save here yet: Register this computer for sync first (Admin > Sync computers)…", and your typed text is still on the screen.
2. Start the backend. Register this computer: Admin > Register this node (needs an admin access token; I can put one on your clipboard for you to paste, as the field cannot be typed into by me).
3. Stop the backend again, repeat the save. Expected: it saves, with the notice "Saved on this computer only…", and the top bar reads "Offline, N changes waiting".

**T3 lost reply**
1. Quit the desktop. Start it with `CLINIC_NODE_TEST_DROP_RESPONSES=1`: `cd Project02-desktop/ClinicNode; open -n --env CLINIC_NODE_CLOUD_URL=http://localhost:5055 --env CLINIC_NODE_DEMO=0 --env CLINIC_NODE_TEST_DROP_RESPONSES=1 "ClinicNode.App/bin/Debug/net10.0-maccatalyst/maccatalyst-arm64/Clinic Node.app"`.
2. As Doctor: New walk-in consult for a QA-Test patient and check in. Then press Sync now (top bar menu).
3. In the web staff queue for today: **exactly one** booking for that patient (not two).
4. Quit and restart the desktop normally afterwards (without the switch).

**T4 "Sync problem" word**
1. Quit the desktop. `sqlite3 ~/Library/Containers/ph.clinicnode.app/Data/Library/ClinicNode/clinic-node.db "UPDATE node_sync_outbox SET attempts=10 WHERE acked_at IS NULL AND rowid=(SELECT MIN(rowid) FROM node_sync_outbox WHERE acked_at IS NULL);"` (needs at least one waiting change; make one with T2 step 3 first).
2. Start the desktop with the backend running. The top bar reads **Sync problem**. To undo: set `attempts=0` the same way.

All tests green except the baseline. Record "Phase 0 OK" in section 12.

## 7. Phase 1: Offline mode, monitor, suggestions

Goal: the owner chooses Offline for the day; the app shows the real connection quality and only suggests.

### Step 1.1 Work mode state
- Add `WorkMode { Online, Offline }` and `IWorkModeService` (Core), implemented in `ClinicNode.Sync`.
- Persist in the existing local key/value store (`SyncLocalStore.SetStateAsync`, key `work_mode`) with who and when it changed. Load it before the login page so a restart keeps the mode.
- Who can switch: Doctor and Admin, either direction. Staff: not at all (Q2 decides what they see). Every change writes a local audit row.
- Cannot switch to Offline on an unregistered computer (reuses 0.4).
- **Acceptance:** switch, quit the app, reopen: same mode; Staff cannot switch.

### Step 1.2 Save and sync behaviour per mode
- `OnlineFirstProxy` receives `IWorkModeService`.
  - Offline: reads and writes go to local only; the API is never called for writes; routes marked API-only throw "Not available in Offline mode".
  - Online: API first; a network failure on a write raises an error with the option "Switch to Offline mode" (rule R1, confirmed by the owner 22 Nov). No silent local save.
- `SyncLoop` and the live hub: run in Online. In Offline they are paused, except the first-login sync (3.3) and the manual Sync button. If Q5 is answered differently, follow that answer.
- The visit-version sessions (`VisitVersions`) are cleared when the mode changes and after any sync that pulls consultation data, so a stale version can never block or allow a wrong save.
- **Acceptance:** with a fake API that counts calls, Offline makes zero write calls; Online + API down shows the error and does not save locally.
- **Tests:** proxy tests for every mode and for the visit-version reset.

### Step 1.3 Connection monitor
- A `ConnectionMonitor` calls the health endpoint from step 0.2 every 5 seconds while its panel is open and every 15 seconds otherwise, in both modes.
- It keeps the last 60 samples: time, milliseconds, HTTP status, database ok, error kind.
- Status rules (thresholds per Q6) over the last 10 checks: Good, Slow, Unstable, Down, Unknown.
- **Acceptance:** scripted fake latencies and failures produce exactly the expected status; with Docker stopped the status is Down with reason "database not ready".

### Step 1.4 Suggestions (never an automatic switch)
- Online and Down for 60 seconds, or Slow for 3 minutes: show "Connection is poor. Switch to Offline mode?" with a [Switch] button. Do not repeat for 15 minutes.
- Offline and Good for 5 minutes: show "Connection looks good. Sync, then go Online?"
- **Acceptance:** the mode value is identical before and after a suggestion appears, unless the user presses the button.

### Step 1.5 Top bar and connection panel
- Top bar: mode pill, a status dot with latency, and the badge from step 0.5.
- Connection panel: status word and reason, a chart of the last 60 samples, last / median / worst milliseconds, last successful contact, database health, outbox waiting count, last successful sync time (Asia/Manila), the mode switch and a [Sync now] button.
- **Acceptance:** panel numbers match the monitor's samples; every state is reachable on the owner's Mac.

### Step 1.6 Phase 1 gate
Owner: switch modes, restart, stop and start the API, stop and start Docker, watch the monitor and suggestions; confirm nothing switches by itself. Record "Phase 1 OK" in section 12, including the owner's verdict on the thresholds (Q6).

**Owner test script (22 Nov, on your Mac, signed in as the doctor; the app must be the build from `plan/step-1.5-top-bar-panel`):**
1. **G1 mode and restart.** Tap the pill at the top (dot + "Online"), press "Switch to Offline mode". The pill must read "Offline mode". Quit the app, open it again: still "Offline mode". Switch back to Online.
2. **G2 nothing happens by itself.** Stop the API (Ctrl+C on the backend window). In Online mode wait 2 minutes. A box "Connection is poor. Switch to Offline mode?" may appear: press **Not now**. The pill must still say "Online". Start the API again.
3. **G3 refused save.** With the API stopped and Online mode, press a save (for example Running Late on Today). You must see "Not saved ... Nothing was saved". Press Cancel and check nothing changed.
4. **G4 database down.** Stop Docker (`docker stop clinic-mssql`). Within about 20 seconds the dot turns red and the panel says "Down" with "Database not ready on the clinic server". Start Docker again; note how long it takes to go back to Good (see the Q6 note in the log below).
5. **G5 Offline stays quiet.** In Offline mode with the API running, keep working for a few minutes: nothing is sent by itself; "Sync now" in the panel sends it.
Tell me the verdict on the thresholds (Q6): too jumpy, too slow, or fine.

## 8. Phases 2 to 5

### Phase 2: The doctor's offline day

Goal: with the network unplugged, the doctor can run the clinic day alone.

- **Step 2.1 One writer (Q2).** In Offline mode, only a Doctor session can write. Other roles follow the answer to Q2. Screens that would write are disabled with the message "Offline mode: only the doctor can make changes on this computer."
- **Step 2.2 Find or register, then book.** Use the existing "New walk-in consult" flow. Verify end to end offline: search the local patient list; register a new patient (client ID, step 0.3); check in as today's visit; start the consultation. Any gap found is fixed here, not worked around.
- **Step 2.3 Queue numbers offline (Q1).** Implement the answer to Q1. Verify with V10 what the walk-in screen does today when no number block is left.
- **Step 2.4 Doctor payments (D6, Q3).** A doctor payment screen records: amount, method, and the **booklet receipt number typed by the doctor**. V5 decides whether the existing local flow (which allocates numbers automatically) must change to accept a typed number. Receipt-number clashes are detected at sync using the existing `or_number_clash` reason and become review items (Phase 4). Online payments by the doctor only if Q3 says yes (needs a backend branch change).
- **Step 2.5 Printing offline.** With the API stopped, print: queue slip, prescription, lab order, medical certificate, and receipt. Each must print from local data.
- **Step 2.6 Offline login (D9).** With the API stopped: doctor login works; day 8 without a successful sync locks sign-in with a clear message and the date to sync by. V6 decides the exact rule and the first-ever-login case.
- **Step 2.7 Phase 2 gate.** Owner runs one simulated clinic day in Offline mode with at least 10 patients (register, book, consult, pay with booklet numbers, print), force-quits the app once mid-consultation, and reopens. Nothing lost. Record "Phase 2 OK".

### Phase 3: Sync experience

Goal: syncing is clear, safe, and happens at the right times.

- **Step 3.1 A real sync run.** Replace the fire-and-forget request with an awaited manual run that: checks registration, health (including database), the app version, then pushes the outbox oldest first until empty, then pulls until finished, then tops up queue blocks. It returns a result object: sent, accepted, merged, conflicts, rejected, still waiting, received. It can be cancelled safely (an interrupted run is simply continued next time; V1 and the contract's idempotency rules back this up).
- **Step 3.2 Results screen.** After every manual or daily sync: the counts, and a list of every item that needs review with a link; the sync time in Asia/Manila; the last 30 runs kept locally.
- **Step 3.3 First login of the day (D5).** After a successful login and before the dashboard: if this computer has not finished a sync for today's clinic date, run the step 3.1 sync with a progress screen and a Skip button. "Today" comes from the server date when reachable, else the computer clock; a stored date later than the server date counts as not done. The "done today" flag is set only when the push left nothing waiting. It runs in both modes. It never blocks login: on failure or Skip, show a dismissible notice "Couldn't sync this morning" with [Sync now]. It does not run on an unregistered computer (show the registration notice).
- **Step 3.4 Closing the app (item 18).** First a spike: V7 decides whether Mac Catalyst lets the app intercept closing. If yes, warn "N changes are not synced. Quit anyway?". If no, the fallback (agreed with the owner before coding) is: warn on logout and on mode switch, and keep the badge always visible.
- **Step 3.5 Switching back to Online.** If items are waiting: show the count with [Sync now] [Switch anyway, items stay queued] [Cancel].
- **Step 3.6 Phase 3 gate.** Owner syncs after the Phase 2 day; every record (patients, bookings, consultations, payments, receipt numbers) matches on the web; the morning sync works on three different days; Skip and a failed morning sync behave as written.

### Phase 4: Review items and the admin web portal

Goal: sync problems are visible to the admin on the web, and duplicates are merged by hand.

**Where review items live (clarification, 22 Nov 2026).** The admin web portal reads the **server database** (the local backend and SQL Server in Docker), never the desktop's SQLite file. So every review item must exist on the server:
- **Case A, the server finds it during push** (possible duplicate patient, receipt-number clash): the data is pushed and applied on the server, and the **server writes the review item itself**. Nothing is uploaded from the desktop for these.
- **Case B, only the desktop knows** (an item the server rejected, or one stuck after repeated retries): the desktop keeps it on its local list (badge shows Sync problem) and **uploads it to the server as a review item when the API is reachable**. Until then the admin cannot see it.
- **Merging happens only on the server database**, by an Admin in the web portal. The desktop receives the result on its next pull (the kept patient, plus a marker that the other patient was merged away). The desktop never merges patients.

- **Step 4.0 Design note (owner approves before any code).** Written in this plan: the review-item kinds (per Q4), their fields, statuses (open, resolved), who may resolve, the exact endpoints, and the migration. The migration is a backend schema change; the owner schedules the window.
#### Step 4.0 design note (PROPOSED 22 Nov 2026, waiting for the owner's approval; no code until approved)

**Facts found in the backend (Project02-be, `main`):** `patients` is soft-delete + sync-tracked (`deleted_at`, `row_version`, `origin_node_id`). Tables that hold a `patient_id` and must move on a merge: bookings, consultations, medical certificates, vitals readings, follow-ups, lab orders, lab values, lab results, documents, vaccinations, prescription groups, patient consents, data requests. Receipt-number clashes are already readable by an Admin (`GET /api/payments/or-clashes`, read-only) and queue-number clashes are only audit rows. There is no review-item table and no merge today.

**1. What is stored (new table `sync_review_items`, on the server):** `id` (guid; for desktop uploads it is the desktop's own item id, so uploading twice is harmless), `kind`, `status` (`open` | `resolved`), `title`, `details_json`, `patient_id` and `other_patient_id` (for duplicates), `aggregate` + `entity_id` + `other_entity_id` (for clashes and rejected changes), `origin_node_id`, `created_at`, `resolved_at`, `resolved_by_user_id`, `resolution` (`merged` | `dismissed` | `fixed`), `resolution_note`. A unique key on (`kind`, `entity_id`, `other_entity_id`) stops the same item being created twice.

**2. Kinds (your Q4 answer):**
| Kind | Made by | When |
|---|---|---|
| `possible_duplicate_patient` | the server, at push | a pushed new patient has the same first name, last name and birthdate (spaces, capitals, accents and punctuation ignored) as another live patient with a different id |
| `or_number_clash` | the server, at push | two payments carry the same receipt number (names both payments) |
| `change_rejected` | the desktop, uploaded | the server refused a change (includes "consultation already completed in the cloud") |
| `change_stuck` | the desktop, uploaded | a change failed 10 times |
Not admin items: queue-number clashes and "merged / cloud kept some values" notes stay on the desktop's own list and in the audit log (not in your Q4 list).

**3. Who may do what:** only an **Admin** lists, opens, resolves or merges (Staff, Doctor, Patient get 403). Desktops upload with their node credential (same as push). Every create, resolve and merge writes an audit row.

**4. Endpoints (all new, nothing existing changes):**
- `GET /api/sync/review-items?status=&kind=&page=` and `GET /api/sync/review-items/{id}`, Admin.
- `POST /api/sync/review-items/{id}/resolve` `{resolution, note}`, Admin (note required, 5+ characters).
- `POST /api/sync/review-items` (node credential): the desktop uploads its items; idempotent by id.
- `POST /api/patients/merge` `{keep_patient_id, merge_patient_id, note}`, Admin, one transaction.

**5. The merge (step 4.4), exactly:** the patient you choose to **keep** stays as it is; any field that is **empty** on the kept record is filled from the merged one (nothing is overwritten); every row in the tables listed above is moved to the kept patient; the merged patient is marked deleted and gets `merged_into_patient_id` = the kept patient; the merged patient's code and a one-line summary go into the audit row; the open duplicate item is resolved as `merged`. All moved rows get a new `row_version`, so every desktop receives them on its next pull; the desktop applies "this patient was merged into that one" and never merges anything itself.

**6. The database change (the migration):** one new table `sync_review_items` and one new nullable column `patients.merged_into_patient_id` (with an index). Nothing is deleted or rewritten, so rolling back means dropping that table and column. It ships together with the payment change (step 2.4 branch) in one planned window that the owner chooses.

**7. Desktop side (Case B upload):** a new local column marks each review item as uploaded; at the end of a successful sync the desktop uploads the items not yet uploaded (rejected changes, and changes stuck at 10 tries) and marks them. Items it cannot upload yet (server unreachable) stay on the desktop list and the badge keeps saying "Sync problem".

**8. Web page (step 4.5):** one new admin page "Sync review" (sidebar item under the existing admin pages): list with filters (status, kind), an item opens with its details and a Resolve button with a note; for a duplicate it opens the merge screen: two columns, the two records side by side with counts of what each holds (visits, consultations, payments, ...), a choice of which one to keep, and a confirm step that says what will move. Nothing else on the web changes.

**To decide before code (owner):** (a) merge rule in point 5 (kept record wins, empty fields filled from the other): OK? (b) queue-number clashes stay off the admin list: OK? (c) the sidebar name "Sync review": OK?

- **Step 4.1 Server.** A table for review items; the desktop uploads its review items during sync; list and resolve endpoints for Admin only; each change audited.
- **Step 4.2 Duplicate detection.** When a pushed patient create matches an existing patient by normalized first name, last name and birthdate: apply it anyway and create a "possible duplicate patient" review item. Never merge automatically (D4).
- **Step 4.3 Receipt clashes.** A pushed `or_number_clash` creates a review item naming both payments.
- **Step 4.4 Merge endpoint.** Admin only: choose the patient to keep and the one to merge; move bookings, consultations, payments, vaccinations, consents and allergies in one transaction; mark the merged patient deleted with a pointer to the kept one; write an audit row. Staff and doctors get 403.
- **Step 4.5 Web admin page.** A review-items list with filters, a resolve action with a note, and a merge screen showing both records side by side. Web changes are limited to this page.
- **Step 4.6 Phase 4 gate.** Owner creates a duplicate patient on purpose (web and offline desktop), syncs, sees the item, merges it, and checks the kept chart holds the full history; a receipt-number clash is created and resolved. Backend stays on its branch until the release gate.

### Phase 5: Hardening and release

- **Step 5.1 Failure tests** on the owner's Mac, each recorded pass or fail: force-quit mid-save; stop the API mid-sync; stop Docker mid-sync; slow network (macOS Network Link Conditioner); computer clock one day ahead and one day behind; sleep and wake mid-day.
- **Step 5.2 Regression:** web walk-in, booking, consultation and payment; desktop Online flows; the visit-version protection (stale save still refused).
- **Step 5.3 Docs:** update `docs/ONLINE-FIRST.md` and the `docs/SYNC-API-CONTRACT.md` changelog; write a one-page "offline day" runbook for the clinic.
- **Step 5.4 Release gate (backend goes to production on merge):** the owner says "go"; production database backed up and restore verified; migrations applied first in a quiet window; backend PRs merged; check production health and version; first sync run; smoke test (one booking, one payment, one sync); rollback plan written before starting.

## 9. Out of scope (not built unless the owner adds it above)
- An automatic mode. Web bookings offline. Staff or patient offline writes (beyond Q2's read-only). Windows build and installer. Local database encryption and automatic backups (recommended before real patient data; not part of this plan). Backend concurrency-token and pull-cursor rewrites. Double-booking detection. Patient files and documents offline. Offline refunds.

## 10. Risks (watch list)
1. `OnlineFirstProxy` is shared by many services; a mistake breaks many screens. Mitigation: proxy tests first (step 1.2), small steps.
2. Backend schema change (Phase 4) needs a planned migration window; backend `main` auto-deploys.
3. Existing failing tests could hide new failures. Mitigation: the baseline file (step 0.1).
4. Features assumed to work (section 4 marked "read, not run") may not. Mitigation: V1-V10 before coding.
5. The unsynced data of an offline day exists only on one computer until sync. Mitigation: the badge, the warnings (3.4, 3.5), and a recommendation to add backups later.

## 11. Verification log (fill in; nothing below is assumed true until filled)

| # | Check | How | Result | Date |
|---|---|---|---|---|
| V1 | Does a freshly registered node pull the full history (all patients, bookings, consultations), and is a pull page applied in one transaction with its cursor? | Read `SyncService`/`SyncPullService`; run a pull on a clean local database | Backend runs `SyncBootstrap.BackfillAsync` at startup (`Program.cs` line 294), so existing rows are logged for pull; no "backfill skipped" warning in the dev log. Pull starts from an empty cursor (= 0) and applies each page with its cursor in one local transaction (`SyncService.cs` ~lines 335-347, 494-502). **Not yet proven by counting rows or by a real first pull** (sa login to count rows failed); prove in the Phase 3 gate. | 22 Nov |
| V2 | `GET /api/version`: anonymous? answers when the database is down (field value vs error)? rate-limited? | Call it with Docker stopped | **`/api/version` is NOT suitable.** Anonymous and returns HTTP 200 with `db_connected:false` when the database is down, but took **14.5 s** to answer, and has no timing field. `/health` stays 200 (liveness only). Decision: add `GET /health/ready` with a 2 s database timeout (step 0.2). | 22 Nov |
| V3 | Does the server return the existing record for a repeated `booking_id` / `patient_id` / `payment_id` (idempotent)? | Read controllers; call twice | Queue check-in with the same `booking_id` twice: **200, same booking (idempotent)**. `POST /api/patients` with the same `patient_id` twice: first 201, second **500** (`DbUpdateException`), **not idempotent**. Step 0.3 must include a backend fix so a repeated `patient_id` returns the existing patient (or a clean 409) instead of a 500. | 22 Nov |
| V4 | Outbox columns: attempts / last error / created order. How is "retried N times" counted? | Read `node_sync_outbox` mapping | **RESOLVED in step 0.5: `Attempts` goes up only when the server answered the push but did not accept that operation.** `SyncOutboxEntry` has `Attempts`, `LastError`, `LastAttemptAt`, `AckedAt`, `CreatedAt`. Retry timing uses `Attempts` and `Backoff.DelayAfter` (`SyncService.cs` ~line 264). "Retried at least 10 times" = `Attempts >= 10 AND AckedAt IS NULL`. Whether `Attempts` is incremented on every failed push (not only some failure kinds) is **still to confirm in step 0.5**. | 22 Nov |
| V5 | Local payment flow: automatic receipt numbering vs typed number; where `or_number_clash` is raised. | Read `StaffPaymentsService`, `SyncPushEngine` | `StaffPaymentsService.RecordAsync` **auto-allocates** the receipt number (`OR-<nodePrefix>-<yyyymmdd>-<seq>`); `RecordPaymentInput` has no field for a typed booklet number. Requirement D6 therefore needs a **change** (typed number in, auto-allocation off for the doctor path) in step 2.4. Server flags clashes with reason `or_number_clash` (`SyncContracts.cs`); how it detects a clash with typed numbers is to be read in step 2.4. | 22 Nov |
| V6 | Offline login rule: behaviour when `LastSuccessfulSyncAt` is empty; exact 7-day arithmetic; message shown. | Read `AuthService`; test with API stopped | `IsLockedForOfflinePeriodAsync`: locked at once if the node is revoked; otherwise locked when `now - LastSuccessfulSyncAt > 7 days`; **if `LastSuccessfulSyncAt` is empty it never locks**. So the 7 days run from the last successful **sync**, not the last login, and a registered node that has never completed a sync is never locked. Decision needed in step 2.6 (see Q7 in section 3 if the owner wants that closed). | 22 Nov |
| V7 | Can Mac Catalyst intercept app close (to warn about unsynced items)? | Spike | **Not verified.** `Window.Destroying` runs after the decision to close and cannot cancel it. Intercepting quit on Mac Catalyst probably needs AppKit hooks and may not be possible. Spike in step 3.4 with the fallback already written there. | 22 Nov |
| V8 | Full list of services behind `OnlineFirstProxy` and which writes the doctor path uses. | Read `AddOnlineFirst` registrations | 35 `AddOnlineFirst<...>` registrations in 5 module files: `Doctor/DoctorModule.cs`, `Doctor/DoctorExtraModule.cs`, `Doctor/DoctorExtra2Module.cs`, `Admin/AdminModule.cs`, `Staff/StaffModule.cs` (plus the extension in `ClinicNode.Api/OnlineFirst/ApiServiceCollectionExtensions.cs`). | 22 Nov |
| V9 | Does the push engine accept a Doctor-actor push for patient / booking / payment (no role rejection)? | Read `SyncPushEngine` actor rules; test | `SyncPushEngine` dispatches patient / booking / payment / consultation with **no role or actor check** (only the doctor self-edit aggregates use `actor_user_id`). A push from a registered node is accepted regardless of which user made the change. Verified by reading, not by a test push. | 22 Nov |
| V10 | Walk-in screen behaviour when no queue-number block is left. | Read `QueueBlocks`, `WalkInWizardView`; test | With no queue-number block left, `QueueService.CheckInWalkInAsync` returns `QueueActionResult.Fail(QueueNumberMessages.NoNumbersLeft)`: **a hard stop, the walk-in is refused**. Q1's default ("Paper queue") would change this. | 22 Nov |

## 12. Progress tracker (update in the PR of each step)

| Step | Branch | PR | Tests | Owner OK |
|---|---|---|---|---|
| 0.1 | plan/step-0.1-baseline | (none yet: gh not logged in) | baseline file written; backend 4 failing, desktop 73 failing | |
| 0.2 | be: plan/step-0.2-health-ready (d567a59); desktop: plan/step-0.2-health-ready | (none yet: gh not logged in) | backend 4 baseline failures only (+2 new passing); desktop Api tests 53 pass; desktop main suite same 73 baseline failures | |
| 0.3 | be: plan/step-0.3-client-ids (24fb300); desktop: plan/step-0.3-client-ids | (none yet: gh not logged in) | backend 4 baseline failures only (+1 new passing); desktop Api tests 56 pass (3 new); desktop main suite: see below | |
| 0.4 | plan/step-0.4-registration-gate | (none yet: gh not logged in) | desktop Api tests 60 pass (4 new); main suite same 73 baseline failures; seen live: unregistered + API stopped, a save is refused with the message and the entries stay | owner to check the registered half at the gate |
| 0.5 | plan/step-0.5-sync-problem | (none yet: gh not logged in) | desktop main suite 324 pass (+23 new), same 73 baseline failures; Api tests 60 pass | owner to check each word at the gate |
| 0.6 gate | plan/step-0.5-sync-problem (20d0091) | (none yet) | T1 pass, T3 pass, T4 pass, T2 not re-run (see log 22 Nov gate) | Owner OK 22 Nov |
| 1.1 | plan/step-1.1-work-mode | (none yet) | 11 new WorkMode tests pass; main suite: baseline failures + 1 environment-dependent (see log); Mac build 0 errors | |
| 1.2 | plan/step-1.2-save-sync-modes | (none yet) | Api tests 66 pass (6 new; 7 old tests changed to the new rules); main suite = baseline + the 1 environment-dependent test; Mac build 0 errors | |
| 1.3 | plan/step-1.3-connection-monitor | (none yet) | Api tests 84 pass (18 new); live: Docker stopped -> Down, "Database not ready" on the first failed check; Mac build 0 errors | |
| 1.4 | plan/step-1.4-suggestions | (none yet) | Api tests 92 pass (8 new); Mac build 0 errors | |
| 1.5 | plan/step-1.5-top-bar-panel | (none yet) | Api tests 97 pass (5 new panel-number tests); seen live on the owner's Mac (see log); Mac build 0 errors | |
| 1.6 gate | | | G1-G5 run by me on the owner's Mac (see log); owner's verdict on thresholds still needed | |
| 2.6 | plan/step-2.6-offline-login | (none yet) | Api tests 101 pass (4 new); WorkMode/Auth/DI tests pass; app starts live | |
| 2.1 | plan/step-2.1-one-writer | (none yet) | Api tests 105 pass (4 new); WorkMode tests 11 pass; Mac build 0 errors; staff sign-in refusal not tried live (needs a typed password) | |
| 2.3 | plan/step-2.3-paper-queue | (none yet) | Api tests 109 pass (4 new); desktop queue/print failures = baseline only; Mac build 0 errors; server side not proven by a test or live (see log) | |
| 2.4 | desktop: plan/step-2.4-doctor-payments; backend: plan/step-2.4-doctor-confirm-payment (a9485d2, NOT pushed) | (none yet) | Api tests 115 pass (6 new); backend 476 pass + 4 baseline failures; seen live: button and dialog, empty number refused | owner OK + backend go needed |
| 2.2, 2.5 | plan/step-2.2-2.5-offline-day-checks | (none yet) | new offline-day test passes on the app's real wiring; Api tests 115 pass; main suite: no new failures vs baseline; real printers NOT tried | owner at the Phase 2 gate |
| 2.7 gate | | | | |
| 3.1 | plan/step-3.1-sync-run | (none yet) | 5 new sync-run tests pass; all Sync tests = baseline (2 known failures); Api tests 115 pass; Mac build 0 errors | |
| 3.2 | plan/step-3.2-results-screen | (none yet) | 7 sync-run tests pass (2 new for the history); Mac build 0 errors; screen seen live (Sync now > results screen > Past syncs) | |
| 3.3 | plan/step-3.3-daily-sync | (none yet) | 12 new daily-sync tests pass; Sync tests = 2 known baseline failures; Api tests 115 pass; Mac build 0 errors; the sign-in flow itself not yet seen live (needs the owner to sign out and in) | |
| 3.4 | plan/step-3.4-close-warning | (none yet) | Mac build 0 errors; spike and warning seen live (menu Quit); red button and Dock Quit tried/not tried (see log) | |
| 3.5 | plan/step-3.5-leave-guards | (none yet) | Mac build 0 errors; not yet seen live (needs a sign-in by the owner) | |
| 3.6 gate | | | | |
| 4.0 design approved | | | | |
| 4.1-4.5 | | | | |
| 4.6 gate | | | | |
| 5.1-5.3 | | | | |
| 5.4 release | | | | |

## 13. Change control
Any change to scope, order, a decision, or a threshold is made here, with the date and the owner's approval, before any code changes. Otherwise the work stops and asks.

Change log:
- 22 Nov 2026: first draft.
- 22 Nov 2026: step 0.1 done; verification results V1-V10 recorded in section 11. Consequences: step 0.2 adds `/health/ready` (V2); step 0.3 also fixes patient-create idempotency on the backend (V3); step 2.4 must add a typed booklet receipt number (V5); Q1 default would change the V10 hard stop. No scope change.
- 22 Nov 2026: step 0.2 done. Acceptance wording clarified (measured at the connection check, not the top-bar label). Found: the sync loop has its own probe (`SyncOptions.HealthPath`), changed too. Known behaviour: the first `/health/ready` right after the database restarts can report not ready once (the backend's pooled connection is dead), then recovers.
- 22 Nov 2026: step 0.4 done. Gate applies only when a cloud address is configured (a stand-alone demo computer has nothing to register with). The refusal raises one dialog ("Can't save here yet") from the shell, whichever screen saved. The registered half of the acceptance (register, then the save works locally) is for the owner's gate: registering needs an admin token typed by the owner.
- 22 Nov 2026: step 0.5 done. V4 resolved: `Attempts` is increased only when the server answered a push but did not accept an operation (retry outcome); a push that fails because the server is unreachable does not count. Badge rule lives in one place (`ClinicNode.Sync/SyncBadge.cs`) and is unit-tested for every state. Seen live: with the API stopped an unregistered computer now reads "Offline · not registered for sync" (it used to print a fixed "Online"). The "Sync problem" word can only be produced on a REGISTERED computer (an unregistered one never retries), so the owner checks that word at the Phase 0 gate; unit tests cover it.
- 22 Nov 2026: Phase 4 clarified (where review items live; case A server-created, case B desktop-uploaded; merge only on the server). No scope change.
- 22 Nov 2026: Q7 answered by the owner: never lock a registered computer that has not synced; keep storing locally. Step 2.6 follows V6 as is (7 days from the last successful sync; never-synced never locks).
- 22 Nov 2026: Phase 0 gate run (step 0.6), on this Mac with the local backend. T1 (Docker DB stopped): `/health/ready` 503 `db_ok:false` after 2.0 s while `/health` stayed 200; back to 200 about 10 s after the database restarted. T3 (`CLINIC_NODE_TEST_DROP_RESPONSES=1`, one walk-in): the server received the POST, the reply was dropped, the desktop saved locally and printed R-053; after sync the server holds exactly ONE booking for the patient (R-073) and a `queue_number_clash` review item says "kept the printed number R-053; the clinic server has R-073". T4 (a stuck outbox row, attempts=10, on the registered computer): top bar read "Sync problem"; after the row was removed it left that state on the next sync cycle (not instantly). T2 (unregistered vs registered): the unregistered half was seen at step 0.4; the registered half is shown by T3 (local save and sync work once registered). Registering needed the admin token pasted by the owner. Findings: (a) the running app had been built before step 0.5, so it must be rebuilt after each step before a gate; (b) when registration completed, 3 old pending consultation/payment items were answered by the server as conflicts (`finished_immutable`, `not_owner`); these are real review items, not a defect; (c) the top bar can keep showing "Syncing... (N pending)" for a while after the outbox is already acked; it refreshed after an app restart and a sync cycle. Not fixed yet; to be looked at in step 2.x (sync run/results).
- 22 Nov 2026: owner approved Phase 0 and answered Q5 (button + first-login sync only in Offline mode) and Q6 (thresholds accepted, tune at the Phase 1 gate). Q1-Q4 remain open (block Phase 2 steps 2.1, 2.3, 2.4 and Phase 4 step 4.0). Phase 1 starts.
- 22 Nov 2026: step 1.1 done (work mode state, no screen yet: the switch screen is step 1.5). `IWorkModeService` (Core) + `WorkModeService` (Sync): saved in key `work_mode`, loaded at start-up before login, Doctor and Admin may switch, Staff and signed-out may not, Offline refused on a computer that cannot sync (reuses 0.4), one audit row per change (`WorkModeChanged`). Test note: `DoctorDashboardLegacyDbTests` copies the LIVE app database; after today's registration and first sync that database holds cloud profiles and the demo seeder collides (`UNIQUE profiles.id`). It is a test-environment effect, not caused by this step; not fixed here. Otherwise the failing set equals the baseline (parameter text differs only).
- 22 Nov 2026: R1 CONFIRMED by the owner (Online + save cannot reach the server = error with "Switch to Offline mode", nothing saved silently).
- 22 Nov 2026: step 1.2 done. Router (`OnlineFirstProxy`) now takes the work mode. Offline: every call (read and write) uses this computer only, the API is never called, an API-only feature says "Not available in Offline mode.", no automatic sync request. Online: a write that cannot reach the server throws `OnlineWriteFailedException` and raises `OnlineRouteStatus.OnlineWriteFailed`; the shell shows "Not saved" with a [Switch to Offline mode] button for Doctor/Admin (Staff are told to ask the doctor); reads still fall back to the local copy. `SyncLoop` does nothing but recount in Offline; `LiveSyncCoordinator` closes the live connection, skips pulls and the vaccination upload in Offline and restarts them on Online; remembered visit versions are cleared on every mode change. Seven old tests that relied on the quiet fallback now switch to Offline first (the lost-reply tests now prove: Online refuses and keeps nothing, then Offline with the same ids keeps one booking). Not changed: the manual Sync button and the first-login sync still work in Offline (step 3.3 adds the first-login one).
- 22 Nov 2026 PROPOSAL (needs owner OK, not built): step 1.2 also says to clear remembered visit versions "after any sync that pulls consultation data". The sync loop runs every minute in Online mode; clearing then would switch off the stale-form protection on an open visit every minute. Proposed instead: clear only on a mode change (done) and after a MANUAL or first-login sync. Say yes and I add it in step 3.x (sync run).
- 22 Nov 2026: step 1.3 done. `ConnectionMonitor` (ClinicNode.Api/Http): keeps the last 60 health checks (time, ms, HTTP status, database ok, error kind: unreachable / timeout / database_not_ready / http_NNN), judges the last 10 with `ConnectionRules` (Q6 thresholds): Good (typical time under 300 ms, no failures), Slow (typical 300 ms or more, or exactly 1 failed), Unstable (2-4 failed), Down (3 failed in a row, or 5+ failed, or the newest answer says database not ready), Unknown (no check yet). "Typical" = median of the answered checks, so one slow reply does not turn Good into Slow. Checks every 15 s, every 5 s while the panel is open (`PanelOpen`), in both modes. It only observes; it never changes the work mode. Live check: with the database stopped, Down + "Database not ready on the clinic server." from the first failed check. Observation for the Phase 1 gate (Q6 tuning): after an outage the word stays Down/Unstable until old failures leave the window of 10 (about 2 minutes at the 15 s pace even though the server is healthy again). If that feels wrong at the gate, shorten the window or let recent good checks count more.
- 22 Nov 2026: step 1.4 done. `ConnectionAdvisor` reads the monitor and the work mode (read only: it has no way to set the mode) and raises a suggestion: Online + Down for 60 s, or Online + Slow for 3 min -> "Connection is poor. Switch to Offline mode?"; Offline + Good for 5 min -> "Connection looks good. Sync, then go Online?". The same suggestion is not repeated for 15 min; any change of state or mode restarts the clock; Unstable and Unknown never suggest (the plan names only Down, Slow and Good). Note: the 60 s counts from the moment the status turned Down (the 3rd failed check, about 30 s after the first failure), so the first suggestion comes about 90 s after the server stops answering. Screen: a dialog with [Switch to Offline mode] / [Not now] (or [Sync and go Online] / [Not now]) shown only to Doctor and Admin; Staff are not asked. "Sync and go Online" runs one sync and switches only if it succeeded; the full warning about unsynced items before going Online is step 3.5.
- 22 Nov 2026: step 1.5 done and seen live on this Mac (signed in as the doctor). Top bar: new pill [status dot  Online | Offline mode  NN ms] before the sync badge; the dot follows the monitor (green Good, amber Slow/Unstable, red Down, grey checking); the badge from step 0.5 is unchanged. Tapping the pill opens the Connection panel: status word and reason, chart of the last 60 checks (newest right, red = no answer), last / typical / worst ms, checks failed, last contact with the server, database on the server (Ready / Not ready / Unknown), changes waiting, last successful sync, all times in Manila time, the mode switch (Doctor/Admin; others see a note) and Sync now. The numbers come from `ConnectionPanelModel` (tested against the samples). Seen live: Online -> Switch to Offline mode (pill and panel changed, mode and audit row saved, 40 s later the server had seen only the monitor's health checks, no sync, no live connection); restart kept Offline mode; Online with the API stopped -> dot red "Down", badge "Offline", pressing a save showed "Not saved ... Nothing was saved ... switch to Offline mode" and nothing was kept (status unchanged, no outbox row). Not tried live: the Slow / Unstable looks and the 5 s checking while the panel is open (unit-tested); Staff session (cannot switch). Decision for Phase 3 (3.5): the panel's "Switch to Online" does not yet warn about unsynced changes; that warning is step 3.5.
- 22 Nov 2026: owner answered Q1 (Paper queue, doctor adds the patient to the queue when they enter the room), Q2 (Staff and Admin fully blocked in Offline mode) and Q3 (doctor may also mark payments Online, needs a backend branch). Steps 2.1, 2.3, 2.4 are unblocked. Still open: Q4 (Phase 4) and the visit-version proposal (Phase 3).
- 22 Nov 2026 (owner approved): (1) Q1 clarified: the paper queue is literal paper kept by staff outside the app; offline, the doctor adds the patient to the queue when they enter the room and gets the next reserved number if one is left, otherwise no number (the server assigns one at sync). (2) Q3 clarified: the doctor may mark any payment received, Online too (backend branch only). (3) Q4 answered: review items = duplicate patients, receipt-number clashes, and changes the server rejected or got stuck on. (4) Step 3.5 gains: when the doctor signs out, closes the app, or switches Offline to Online, the desktop first runs a sync by itself; if it fails the warning says how many changes are still waiting and lets her cancel or leave anyway. The manual Sync now button stays. Offline mode still has no background sync (Q5). (5) Operating rule for the clinic (for the Phase 5 handover sheet): the doctor uses ONE of web or desktop for the whole day; to move from one to the other she finishes the consultation, syncs the desktop until "changes waiting" is 0 (now automatic at sign-out), then signs out and uses the other. The backend "one active session per doctor" idea is NOT adopted (an offline desktop cannot be signed out remotely); it can be added later if the rule is broken in real use. (6) Still open: the visit-version proposal (Phase 3) was explained but not yet answered.
- 22 Nov 2026: step 2.6 done. The rule (V6, Q7): offline sign-in is allowed for 7 days after the last successful SYNC (not the last login); day 8 is locked; a computer that has never synced is never locked (owner: keep storing). New: the lock message names the dates ("last synced on Nov 12 ... had to sync by Nov 19 ... connect to the clinic server and sign in once so it can sync"), in Manila time; in Offline mode sign-in uses this computer only (no call to the server) while the 7 days allow it; when they are over, sign-in goes through the server once, which is the way back; the 7-day rule applies in Offline mode even if the server answers. Wiring note: `WorkModeService` now looks up the signed-in user lazily (sign-in reads the work mode, so a direct link was circular).
- 22 Nov 2026: step 2.1 done, following the owner's Q2 answer (Staff and Admin do not work at all in Offline mode). In Offline mode: Staff and Admin cannot sign in ("This computer is in Offline mode. Only the doctor can use it until it is switched back to Online."), also not through the server (the way back after the 7-day lock); a Staff/Admin session that is open when the mode is Offline is ended at the next session check, which now also runs the moment the mode changes. CONSEQUENCE of Q2, owner please confirm: only the DOCTOR can switch the work mode (step 1.1 said Doctor and Admin; an Admin who switched to Offline would lock themselves out). Staff/Admin see the switch disabled with a note. The doctor switches back to Online. The screens-disabled wording planned for Q2 "read-only" is no longer needed.
- 22 Nov 2026: step 2.3 done (owner Q1: Paper queue). V10 behaviour before: with no reserved number the walk-in was refused ("no reserved queue numbers left"). Now: the doctor's check-in goes through with NO queue number (the booking is CheckedIn, so the doctor can start the visit); the patient is on the staff's paper list and the doctor adds them when they enter the room; the ticket card says "Paper queue / No queue number yet (the clinic server gives one when this computer syncs)"; the printed slip says "PAPER QUEUE" instead of a blank; a reserved number is still used whenever one is left; pressing the button again does not duplicate (same booking id). Not changed: Staff/Admin booking screens still refuse with the old message (they are blocked in Offline mode anyway, Q2). NOT YET PROVEN: that the clinic server really gives a number to such a booking at sync. Reading `SyncPushEngine.ReconcileQueueNumberAsync` says an empty number gets the next free number of its lane (result `merged`, `queue_number_reassigned`); no backend test covers it. To prove at the Phase 3 gate (sync run) with a real offline walk-in and no reserved block, and to add a backend test on a branch.
- 22 Nov 2026: step 2.4 done (D6, Q3). V5 resolved: the existing local flow auto-numbers receipts (`OR-<node>-<date>-<seq>`); the doctor path now types the number from the booklet and it is used as typed. Desktop: `RecordPaymentInput` gains an optional `OrNumber`; with it, no automatic numbering; local check that the number is not already on another payment on this computer (case-insensitive, nothing saved if it is); Online the number goes to the server as typed and a duplicate answer saves nothing. Screen: on a completed unpaid visit (Appointments > the visit, or Today > Needs attention) the doctor sees "Mark payment received"; the dialog has Method, exact amount (prefilled), optional reference, notes and a required "Receipt number (from your booklet)"; seen live, an empty number is refused. Unchanged: the desk's own flow (Staff/Admin) still auto-numbers. Clashes between computers are detected at sync (server rule: duplicate OR numbers are refused/flagged, reason `or_number_clash`); the review item for the admin is Phase 4. BACKEND (branch only, a9485d2, from main): `POST /api/payments/{id}/confirm` now allows the Doctor role, and only for the doctor's OWN visits (another doctor and patients get 403); the old role test was changed to say so. This is the Q3 "Online too" part: it does NOT work against the live server until that branch is merged and deployed, which needs the owner's go (backend `main` deploys automatically). Offline it needs no backend change.
- 22 Nov 2026: steps 2.2 and 2.5 verified by a new test (`DoctorOfflineDayTests`, the app's real service graph, no clinic server): the computer is refused Offline mode until registered (D7), then the doctor registers a patient with a desktop-made id, finds them in the local list, checks them in as a paper-queue patient, prints the slip, marks the payment with a typed booklet number, prints the receipt showing that number, all of it lands in the outbox (patient, booking, payment), and Staff and Admin are refused. GAP FOUND AND FIXED: the real queue-slip printer refused to print a booking with no queue number ("This booking has no queue number yet"), so a paper-queue patient had no slip; both slip builders now print "PAPER QUEUE". NOT PROVEN: (a) the prescription, lab request and medical certificate printers were read, not run, in an offline test (they read only the local database); (b) printing on a real printer / the thermal printer (needs a real printer, owner at the gate); (c) the consultation itself in this test, because the demo test database gives the signed-in doctor a different id than its doctor row (a test-data quirk that predates this work; consultation behaviour is covered by `ConsultationServiceTests`). Also seen: the old `ClinicDayTests` offline-day test was already failing before this work (it expects a temporary `MF-T-` patient code that the code no longer produces); left as it was.
- 22 Nov 2026: owner APPROVED the visit-version proposal: remembered visit versions are cleared on a mode change (done in step 1.2) and after a MANUAL or first-login sync, NOT after every sync. To build in Phase 3 (steps 3.2/3.3).
- 22 Nov 2026: Phase 1 gate G1-G5 run by me on the owner's Mac (doctor session, local backend). G1 mode and restart: pass (Offline survived a restart; audit row saved). G2 nothing switches by itself: pass; with the API stopped in Online mode the box "Connection is poor. Switch to Offline mode?" appeared about 105 s after the stop, I pressed Not now and the saved mode and its timestamp were unchanged. G3 refused save: pass (see step 1.5 log). G4 database down: pass at the monitor (Down, "Database not ready", first failed check; see 1.3); recovery after an outage takes about 75 s to leave Down, 120 s to leave Unstable and 150 s to read Good at the 15 s pace (my calculation from the rule, not timed live). G5 Offline stays quiet: pass; in Offline mode the server saw zero requests from the app in 45 s except the health checks, and the work waited as "2 changes pending" until Sync now. OWNER VERDICT STILL NEEDED on the thresholds; my recommendation: keep the rule but let recent checks count more so recovery shows Good sooner (for example: if the last 3 checks all succeeded the word cannot be worse than Slow). Not changed without the owner's OK (Q6).
- 22 Nov 2026: LIVE PROOF of the paper queue end to end (closes the "not yet proven" note of step 2.3): Offline mode, this computer's reserved numbers used up, the doctor's New walk-in consult check-in saved the booking with NO number ("Paper queue", status CheckedIn); on Sync now the clinic server accepted it and gave it R-094, the local copy was updated to R-094 (review item `queue_number_reassigned`: the printed slip may need reprinting), nothing was left waiting. A numbered offline booking (R-074, from a block the app reserved by itself while still Online) also synced. Note: the app reserves new number blocks by itself while Online whenever the old ones run low, so a paper-queue patient only happens after a long Offline stretch or when many patients arrive Offline.
- 22 Nov 2026 (owner): the backend payment change (doctor confirms payments Online) waits for Phase 4 and ships with it. Until then the doctor's "Mark payment received" button, when the computer is Online, says "Not available yet ... switch to Offline mode first, or staff mark it". Owner also decided: merge everything to main (the setup is local, so no production risk); the backend payment branch stays out of main until Phase 4.
- 22 Nov 2026: step 3.1 done. New `ISyncService.RunManualAsync` (SyncService): awaited; checks first, in this order: cloud address set, computer registered ("not registered, an admin registers it under Admin > Sync computers"), server answers with its database ready (/health/ready, else "cannot be reached, or its database is not ready. Nothing was sent; your changes are safe on this computer"); the app version is checked by the server on the first push/pull (426 = "Needs update", existing behaviour). Then it sends the outbox oldest first until nothing is left (up to 1000 batches, instead of the background cycle's 20), receives until finished, tops up queue numbers, and returns a result with: sent, accepted, merged, conflicts, rejected, new review items, still waiting, received, queue blocks reserved, start and end time. A run cancelled half way keeps what was already accepted; the next run sends the rest (tested: every change reaches the server exactly once by operation id). The background loop is unchanged (`RunOnceAsync`). The screens still call the old method; step 3.2 switches them to this one and shows the result.
- 22 Nov 2026: step 3.2 done in code. New screen `SyncResultPage` (modal): it runs the sync (step 3.1) and shows "Syncing..." then either "Sync finished" with the counts (sent, accepted, accepted with some server values kept, clashed, refused, received, still waiting) and the time in Manila time, or "Sync did not finish" with the reason and how many changes are still waiting; below it the items that need review (the newest 6 and a button to the existing review list) and a "Past syncs" list of the last 30 runs. Every run (also a failed or stopped one) is kept in a new local table `node_sync_runs` (30 kept) with its trigger (manual, daily, switch, signout). All the Sync now buttons now use it: the top-bar sync menu, the Connection panel, the shell's Sync now, and the "Sync and go Online" suggestion (runs without the screen, keeps the history); the This computer page uses the new run too. NOT yet seen on screen: the app came back at the sign-in screen after a restart and a password has to be typed by the owner.
- 22 Nov 2026: step 3.2 seen live on the owner's Mac (signed in by the owner as the doctor): Sync now in the Connection panel opened the results screen: "Sync finished", Manila time, all counts 0 (nothing was waiting), "Needs review: 7 items" (the older review items from the registration sync and the offline day, with Review items / Close buttons), and "Past syncs" listing the run. The 7 old items are real and still unresolved: they are what Phase 4 will let an admin sort out.
- 22 Nov 2026: step 3.3 done in code (D5). Rule: after a successful sign-in and before the dashboard, if this computer has not finished a sync for today's clinic date, it runs the step 3.1 sync on the results screen with a Skip button. "Today" = the clinic server's date from the handshake when it answers within 3 s, else this computer's clock (Manila date). "Done today" is recorded (local key `daily_sync_done`) at the end of ANY successful run (the morning one, a manual one, or the background loop) but only when that run left nothing waiting; a stored date that is not today (yesterday, or LATER than today) counts as not done. It runs in both modes (Q5), for every role. It never blocks the sign-in: on a failure, a skip, or changes still waiting, a notice "Couldn't sync this morning" with [Sync now] [Dismiss] appears (and says why). A computer that is not registered gets a once-a-day notice "This computer is not registered" and no sync; one with no server address does nothing. Also done here (owner approved the proposal): remembered visit versions are forgotten after a manual, daily or switch run (not after the background loop). Test help: the fake server's clock can now be set (`ServerNow`).
- 22 Nov 2026: step 3.4 done; SPIKE V7 ANSWERED on the owner's Mac. Can the app intercept closing? PARTLY. (1) Menu > Quit Clinic Node (and Cmd-Q, which is the same menu command): YES. The app's own Quit command replaces the system one; seen live: with 1 change waiting and the server stopped, choosing Quit ran a sync (the results screen said "Sync did not finish: the clinic server cannot be reached ... Nothing was sent; your changes are safe on this computer"), then asked "1 change is not on the clinic server yet. They are safe on this computer and will go next time you sync. Quit anyway?" with [Stay] [Quit anyway]; Stay kept the app open. With nothing waiting, Quit simply quits. If the sync works the app quits without asking. (2) The red window button: it does NOT quit the app: the app keeps running with no window (reopen from the Dock), so nothing is lost; nothing is intercepted and no sync runs. (3) Dock > Quit, Log Out and Shut Down of the Mac: NOT intercepted by this method and not tried (the system ends the app); a change waiting then stays safely in the local database and goes on the next sync. FALLBACK (the one the plan asked to agree before coding) is therefore only needed for those: warn on sign-out and on switching to Online (step 3.5) and keep the status badge always visible (it is). Test tool note: Cmd-Q itself could not be pressed by my tools (system key); the same command was chosen from the menu.
- 22 Nov 2026: step 3.5 done in code (owner approved the automatic sync). Before the doctor signs out (the Logout button) or switches Offline to Online (Connection panel), the computer first syncs by itself on the results screen (with Skip). If everything went up nothing is asked. If something is still waiting it asks: "N changes are not on the clinic server yet. They are safe on this computer." with [Sync now] [Sign out anyway | Switch anyway, items stay queued] [Cancel]. Forced sign-outs (access revoked, session expired) never ask. The "Sync and go Online" suggestion (step 1.4) already ran a sync first and stays Offline if it fails. Not yet seen on screen: needs the owner to sign in (password).
