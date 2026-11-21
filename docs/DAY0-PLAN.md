<!-- JJQR - AI (21 Nov 2026) - Day 0 plan by allen0 (tech lead). Numbers below are copied from command output run on the owner's Mac on 21 Nov 2026, 16:28-16:42 (UTC+8). -->
# Clinic Node — Day 0 plan (allen0)

Root: `/Users/jessiejayrubi/Documents/Allyn`. Desktop solution: `Project02-desktop/ClinicNode/ClinicNode.slnx`. Logs of this run: `/tmp/day0/*.log` on the Mac.

## (1) Verification results (real, 21 Nov 2026 ~16:30 UTC+8)

| # | Command (cwd) | Exit | Result |
|---|---|---|---|
| 1a | `dotnet build ClinicNode.App/ClinicNode.App.csproj -f net10.0-maccatalyst` (Project02-desktop/ClinicNode), incremental | 0 | **0 errors, 49 warnings**, 3.93 s |
| 1b | same with `--no-incremental` | 0 | **0 errors, 23 warnings** (MSBuild summary), 15.87 s. Warning lines by code (with duplicates): NU1903 ×45, NU1902 ×18, CS0618 ×10, EF1002 ×8, CS8625 ×6, CA1422 ×4, CS7095 ×2, CA1416 ×2. Vulnerable packages flagged: Microsoft.AspNetCore.WebUtilities 2.3.0, System.Security.Cryptography.Pkcs 8.0.1 and 9.0.11, System.Security.Cryptography.Xml 8.0.4 |
| 2 | `dotnet test ClinicNode.Tests/ClinicNode.Tests.csproj` | 0 | **Passed 374, Skipped 5, Failed 0, Total 379** (2 m 47 s) |
| 3 | `dotnet test ClinicNode.Api.Tests/ClinicNode.Api.Tests.csproj` | 0 | **Passed 49, Skipped 0, Failed 0, Total 49** (23 s) |
| 4 | `dotnet test tests/ClinicApp.Api.Tests` (Project02-be) | 0 | **Passed 480, Skipped 0, Failed 0, Total 480** (14 s). Uses `WebApplicationFactory` (in-memory TestServer): no port bound; :5055 untouched |
| — | `scripts/smoke-maccatalyst.sh` | not run | Day 0 did not launch the app (it launches a GUI app; an app instance may be open). First run = I-2 |

**Skipped tests (5, all env-gated):**
- `Sync.SyncApiRoundTripTests.SyncApiRoundTrip_offline_visit_then_sync_lands_in_the_cloud_and_a_second_sync_is_a_no_op` and `Sync.SyncApiIntegrationTests.SyncApiIntegration_push_and_pull_round_trip_against_the_real_api` — need `CLINIC_SYNC_IT_URL` + `CLINIC_SYNC_IT_ADMIN_TOKEN` (or NODE_ID/NODE_SECRET); refuse :5055/:3000.
- `Sync.SyncRealDbDoctorFlowTests.RealDb_doctor_consultation_and_sync` — needs `CLINIC_REALDB_DIR` (a COPY of clinic-node.db).
- `Staff.StaffRealDbHarness.Staff_pages_against_copy_of_sandbox_db`, `Staff.StaffRealDbHarness.Pristine_copy_without_init_doctor_today_reads` — real-DB copy harness (env-gated).

**Other facts gathered**
- Listening ports: :5055 ClinicApp (PID 89117), :3000 node (PID 97726), **:5077 dotnet (PID 19142) = allen1's scratch sync-IT API** (`/tmp/allen1-sync-it`). New scratch backends must use :5078+.
- TODO/NotImplemented markers (`grep -rnE "TODO|NotImplemented"` in *.cs/*.xaml, excl. bin/obj/tests): **43** (42 in ClinicNode.App, 1 in ClinicNode.Core/Admin/AdminServiceCollectionExtensions.cs). All 43 are `TODO(allen)` wiring notes (routes/transients for admin pages, IFileSaver registration, TopBarView into AppShell, AdminWalkInPage route, staff flyout items). No `NotImplementedException` found. Many look stale (admin DI is already called at MauiProgram.cs:91) — must be checked one by one (I-1).
- `ClinicNode.App.csproj` TargetFrameworks: `net10.0;net10.0-maccatalyst`; on a Windows host it ALSO adds `net10.0-windows10.0.19041.0` (line 5) with `WindowsPackageType=None` → the Windows TFM has never been compiled, no MSIX packaging set.
- **Online-first state (code read):** `ClinicNode.Api` has `AddClinicNodeApi`, `AddOnlineFirst<TService,TApi,TLocal>` + `OnlineFirstProxy`, `ApiAuthClient` (login/refresh/logout/me on `/api/auth/*`), `InMemoryApiTokenStore`, `ApiConnectivity`. Converted services: **2** (`IDoctorPerformanceService`, `IDoctorDashboardService`). MauiProgram.cs:100 calls `AddClinicNodeOnlineFirst(...)` with base URL from `ISyncEndpoint` (default http://localhost:5055).
  **But login is still local-only:** `LoginPage` → `IAuthService.LoginAsync` (Core `AuthService`) checks the password hash in local SQLite; nothing calls `IApiAuthClient.LoginAsync`. The proxy routes to the API only when the token store has an access token, so **at runtime every "online-first" service currently falls back to local**. Online-first is effectively 0 % in the running app.
- `docs/ONLINE-FIRST.md`: **does not exist** (allen4 is waiting on it).
- Doctor Today (`Views/Doctor/TodayPage.cs`) already consumes `IDoctorDashboardService` → becomes the reference screen as soon as online login lands.
- Doctor Performance: Core + API services exist; **no page** in `Views/Doctor/Extra/`.
- Forgot password: `LoginPage.OnForgotPassword` shows a "not available" alert (stub).
- Conflict review: `Views/SyncConflictsPage.cs` exists (list + resolve) — finish/verify, not build from scratch.
- `Project02-be/deploy/migrate-cloud-sync.sql`: `sync_field_stamps` mentioned **0** times; contains `20261121064610_CloudSync`; migration `20261121073852_SyncSelfEditStamps` exists in `src/ClinicApp.Infrastructure/Migrations/` but is **NOT in the script**.
- Git (read-only): Project02-desktop **NO REPO**; Project02-be repo on `main`, **69** uncommitted entries (38 modified, 31 untracked), last commit f538900 20 Nov 2026 19:45 (UTC+8); Project02-fe repo on `main`, **43** entries (33 modified, 10 untracked), last commit 57e08a4 20 Nov 2026 20:47 (UTC+8).
- `PARITY-staff-admin.md` summary table: **62 done / 13 partial / 1 missing = 76** (Staff 22/4/0, Admin 32/5/0, Shared 8/4/1). A mechanical count of the status column gives 71 / 6 / 1 = 78 rows → the summary and the rows do not reconcile (task B-2).
- Docs present: API-SURFACE.md, MAUI-ARCHITECTURE.md, NATIVE-SCREEN-INVENTORY.md, PARITY-staff-admin.md, PROGRESS.md, STYLE-GUIDE.md, SYNC-API-CONTRACT.md, handoff/, status/, ui-review/. status/: allen1..5.md, allen3-api-map.md, allen4-admin-api-map.md, sync-it-credentials.local.md.
- Safety snapshots (copy only, excl. bin/ obj/ node_modules/ .next/; `unzip -t` OK on all): `/Users/jessiejayrubi/Documents/Allyn-snapshots/20261121/` — Project02-desktop-20261121.zip **112 MB** (1,261 files), Project02-be-20261121.zip **80 MB** (1,450 files), Project02-fe-20261121.zip **305 MB** (2,648 files). Note: the desktop zip contains `sync-it-credentials.local.md` and local .db files — keep the snapshot folder private.
- Integration baseline stamp: `/Users/jessiejayrubi/Documents/Allyn-snapshots/20261121/.baseline-stamp` (used by the checklist to find files changed since Day 0, because there is no git).

## (2) Risks / assumptions
- **R1 Online-first is not live** (login local-only). Everything in Wave 2 depends on F-1..F-3. Highest schedule risk.
- **R2 Offline-login policy**: if the API answers 401 we must NOT fall back to the local hash (otherwise a revoked/changed password still works). Assumption: fall back ONLY on `ApiOfflineException` (unreachable/timeout).
- **R3 Unencrypted local patient data** (SQLCipher built, OFF). No real patient data until G-1.
- **R4 No version control** on desktop; weeks of uncommitted be/fe work. Zips exist; git init needs owner approval (Q1).
- **R5 Deploy script incomplete** (no `sync_field_stamps`): a hosted deploy today would break sync self-edit. B-1.
- **R6 Vulnerable NuGet packages** (NU1902/NU1903) in the app build. I-3.
- **R7 PII in logs**: `AuthService` logs "Signed in {Email}". Low but fix while touching login (F-1).
- **R8 Parity ledger drift**: staff/admin audit summary ≠ rows; no doctor audit; web changed recently (4e). B-2/D-1.
- **R9 Single-writer bottleneck** on MauiProgram/AppShell: ~40 TODO(allen) wiring notes already queued for allen0. I-1 before Wave 2.
- **R10 Shared scratch backend :5077** is one process owned by allen1; concurrent tests from several workers may collide on its DB. Rule: allen2 gets :5078, others :5079+, each with its own SQLite copy.
- **R11 Windows TFM never compiled** (Platforms/Windows/Printing untested) — expect a fix-up day once a PC exists (GATED).
- **R12 Money**: exact-amount payments, waiver confirmation, per-computer unique OR numbers must be covered by tests in both offline and online modes.
- Assumption: test counts above are the baseline; any drop in passed count = regression.
- Assumption: allen0 does not run the GUI smoke on the owner's open app; smoke runs in I-2 when the owner confirms the app may be launched.

## (3) TASK BOARD
Size: S ≤ 2 h, M ≤ half a day. Standard gate for every task = build 0 errors + ClinicNode.Tests 0 failed + Api.Tests 0 failed (+ backend tests if Project02-be touched), with counts not below the Day 0 baseline (374/5/0, 49/0/0, 480/0/0).

| id | wave | lane | title | files owned | depends-on | acceptance test | size |
|---|---|---|---|---|---|---|---|
| I-0 | 0 | allen0 | Day 0 verification, snapshots, PROGRESS.md, this plan | docs/PROGRESS.md, docs/DAY0-PLAN.md | — | numbers in §1 | done |
| I-1 | 1 | allen0 | Resolve the 43 TODO(allen) wiring notes: wire missing routes/DI (IFileSaver, TopBarView, admin-walk-in → AdminWalkInPage, staff flyout items), delete stale notes | MauiProgram.cs, AppShell.xaml(.cs), the noted files (comment lines only) | — | TODO count 43 → ≤ 3 (each remaining one justified); every admin/staff flyout item opens in smoke | M |
| I-2 | 1 | allen0 | First GUI smoke: `scripts/smoke-maccatalyst.sh` | — | owner OK to launch | script exit 0, no new crash report | S |
| I-3 | 1 | allen0 | Pin patched versions for NU1902/NU1903 packages | *.csproj / Directory.Packages.props (desktop) | — | `--no-incremental` build shows 0 NU190x | S |
| I-4 | 1-4 | allen0 | Wire each worker's XModule.cs once (1-line calls) | MauiProgram.cs, AppShell | worker report | build + tests green after wiring | S each |
| I-5 | 0 | allen0 | GATED: git init + .gitignore + local commit/branch (no push) in all 3 folders | .gitignore files | owner Q1 | `git status` clean after local commit; `git check-ignore docs/status/sync-it-credentials.local.md` matches | S |
| F-1 | 1 | allen2 | **Online login**: API first (`IApiAuthClient.LoginAsync`), local fallback ONLY when unreachable; 401 = error, no fallback; role from ApiUser; remember email only; no email in logs | ClinicNode.Api/Auth/* (new OnlineFirstAuthService decorator of IAuthService), ClinicNode.Api.Tests | — | new Api.Tests: online OK → token set + session role; 401 → error, local not called; offline → local login; `grep -rn "Password" ` shows no persisted password. Manual: signed in with :5055 up → `/api/auth/me` call seen in the :5055 log or app log "route=api" | M |
| F-2 | 1 | allen2 | **docs/ONLINE-FIRST.md** — the contract for every lane | docs/ONLINE-FIRST.md | F-1 design | doc contains: how to write `ApiXService`, `AddOnlineFirst` one-liner in the lane module, OnlineRoute attributes (ApiOnly/LocalOnly), offline rules, write-while-offline → local + ISyncBridge.RequestSync, error mapping, test recipe with ApiRig | S |
| F-3 | 1 | allen2 | **Reference screen: doctor Today end-to-end** via IDoctorDashboardService | ClinicNode.Api/Services/ApiDoctorDashboardService.cs, Api.Tests | F-1 | Api.Tests: online → API data; API down → local data, no exception; manual: Today after online login shows the same counts as web `/doctor/dashboard` for the same doctor/day | S |
| F-4 | 1 | allen2 | Session = 1 h access + 7-day rolling refresh; refresh token in SecureStorage (never a file); 401 → one refresh → retry | ClinicNode.Api/Http/*TokenStore*, ApiClient | F-1 | tests: expired access → refresh once → success; refresh refused → session dropped → local mode; sign-out clears store | M |
| F-5 | 1 | allen2 | Connectivity indicator (Online / Offline-local / Syncing) exposed as a service for the TopBar | ClinicNode.Api/Http/ApiConnectivity.cs | F-1 | unit test state transitions; allen0 binds it in TopBar | S |
| F-6 | 3 | allen2 | SignalR client for `/hubs/clinic` (QueueUpdated, PaymentUpdated, PatientCheckedIn) → IClinicEventBus; reconnect; falls back to 60 s poll | ClinicNode.Api/Realtime/* | F-1 | test with a fake hub or the :5078 backend: event raised → bus event within 2 s; hub down → poll continues | M |
| F-7 | 3 | allen2 | Forgot password (S-FORGOT) online-only: `POST /api/auth/forgot-password`; offline → clear message | ClinicNode.Api/Auth/*, Views/Shared/ForgotPassword* (own file) | F-1 | Api.Tests for 200/400/offline; parity row "Forgot password" → done | S |
| F-8 | 3 | allen2 | Conflict review page parity: field diff, keep-local / take-cloud, resolution recorded | Views/SyncConflictsPage.cs, ClinicNode.Sync/* | — | tests on resolve paths; page lists conflicts produced by the existing stale_amendment test fixture | M |
| F-9 | 2 | allen2 | Sync IT runbook: own backend on :5078 + script to run the 3 skipped sync tests | ClinicNode/scripts/run-sync-it.sh | — | script runs: 3 previously skipped tests pass (env set inside script, secrets read from the local creds file, never echoed) | S |
| B-1 | 1 | allen1 | Complete `deploy/migrate-cloud-sync.sql` for CloudSync + SyncSelfEditStamps (idempotent `dotnet ef migrations script ... --idempotent`) | Project02-be/deploy/migrate-cloud-sync.sql | — | `grep -c sync_field_stamps` > 0; both migration ids in the `__EFMigrationsHistory` guards; script regenerated = diff-identical; backend tests 480+/0 | S |
| B-2 | 1 | allen1 | Single **parity ledger** `docs/PARITY-LEDGER.md`: every 4e web behaviour + recent Project02-fe changes (git diff of the 33 modified/10 new files) vs native, owner lane, status; reconcile PARITY-staff-admin.md summary (62/13/1) with its rows | docs/PARITY-LEDGER.md, PARITY-staff-admin.md | — | ledger rows ≥ 4e list (R-/S- queue, doctor Charge/Waive last, auto-waive + Confirm waiver, exact payments, charge follows total, med-cert auto-tick, footer Prev/Next/Save & next/Review & complete, checklist summary, "-" for cancelled/no-show, staff cancel with reason, 1 h + 7 d session, built-in catalogs); summary counts = mechanical row counts | M |
| B-3 | 1 | allen1 | API-SURFACE.md / SYNC-API-CONTRACT.md truth pass vs controllers | docs/API-SURFACE.md, docs/SYNC-API-CONTRACT.md | — | every `[Http*]` route in Project02-be controllers appears once in API-SURFACE.md (script count match) | M |
| B-4 | 2 | allen1 | Endpoint gap proposals for the desktop (e.g. doctor profile/limit PUT is Admin-only; patient-card data) — PROPOSE, owner approves before code | docs/status/allen1.md | B-3 | proposal list with route, auth, payload; no code until approved | S |
| B-5 | 1 | allen1 | Backend tests proving R-/S- lane counters per day, exact-amount payment rule, waived-awaiting-confirmation + confirm-waiver | tests/ClinicApp.Api.Tests/* | — | new tests pass; total ≥ 480 | M |
| S-1 | 1 | allen3 | Queue numbers R-001 / S-001 per lane per day, S lane first (local) | Core/Staff/*, Tests/Staff/* | — | tests: 2 regular + 2 senior/PWD same day → R-001,R-002,S-001,S-002, sorted S first; new day restarts | S |
| S-2 | 1 | allen3 | Payments parity (local): exact amount only, "Waived by doctor, PHP 0" row + Confirm waiver, cancelled/no-show show "-" | Core/Staff/*, Views/Staff/StaffPaymentsPage.cs, RecordPaymentPage.cs, Tests/Staff | — | tests: over/under amount rejected; doctor waive → appears in awaiting list → confirm → OR unique per computer; UI shows "-" | M |
| S-3 | 1 | allen3 | Verify staff cancel (not checked-in, reason) + `StaffModule.cs` with AddStaffModule/RegisterStaffRoutes | ClinicNode.App/Staff/StaffModule.cs | — | module compiles; allen0 wires it; all staff flyouts open | S |
| S-4 | 2 | allen3 | API: dashboard + queue (ST-DASH, ST-QUEUE) | ClinicNode.Api/Services/ApiStaff*.cs, Staff module | F-2 | Api.Tests online/offline per service; manual: check-in in desktop appears on web `/staff/queue` | M |
| S-5 | 2 | allen3 | API: walk-in + patients + patient detail (ST-WALK, ST-PATIENTS, ST-CHART) | same | F-2 | same pattern; walk-in created online gets server queue number | M |
| S-6 | 2 | allen3 | API: payments + confirm-waiver (ST-PAY) | same | F-2, S-2 | Api.Tests: `GET /api/payments/waived-awaiting-confirmation`, `POST /api/payments/{id}/confirm-waiver`, exact amount 400 handling | M |
| S-7 | 2 | allen3 | API: booking detail, doctor status, announcements, profile | same | F-2 | Api.Tests per service | M |
| S-8 | 3 | allen3 | Live refresh on desk screens via F-6; close the 4 staff partials | Views/Staff/* | F-6 | parity staff 26/0/0 or justified | M |
| S-9 | 2 | allen3 | OVERFLOW for allen5: doctor-extra READ paths to API (D-APPT, D-PATIENTS, D-CHART) per allen3-api-map.md | ClinicNode.Api/Services/ApiDoctorExtra*.cs | F-2 | Api.Tests online/offline | M |
| A-1 | 1 | allen4 | IFileSaver with OS save dialog (Mac Catalyst now, Windows stub) + Reports CSV through it | ClinicNode.App/Services/Files/*, Admin/AdminModule.cs | — | manual: Export CSV opens save dialog, file has BOM+CRLF; unit test of CSV content | M |
| A-2 | 1 | allen4 | Verify admin OR numbers use per-computer format (OR-XXXX-YYYYMMDD-NNNN) on confirm/waive | Core/Admin/*, Tests/Admin | — | test: two admin confirmations → sequential unique per-computer OR | S |
| A-3 | 1 | allen4 | `AdminModule.cs` (AddAdminModule/RegisterAdminRoutes) replacing the TODO(allen) notes | ClinicNode.App/Admin/AdminModule.cs | — | allen0 wires it; every admin flyout opens | S |
| A-4 | 3 | allen4 | Admin sync-node management (list, register, revoke nodes) | Views/Admin/AdminRegisterNodePage.cs + new page | B-3 | manual against :5079 scratch backend; test with fake API | M |
| A-5 | 2 | allen4 | API: dashboard + bookings list/detail/calendar | ClinicNode.Api/Services/ApiAdmin*.cs | F-2 | Api.Tests online/offline | M |
| A-6 | 2 | allen4 | API: doctors (list/new/edit) + staff | same | F-2 | same | M |
| A-7 | 2 | allen4 | API: patients + audit + data requests | same | F-2 | same | M |
| A-8 | 2 | allen4 | API: settings (fees/hours/payment methods) + announcements + reports | same | F-2 | same; fee schedule still available offline | M |
| A-9 | 3 | allen4 | Close the 5 admin partials | Views/Admin/* | A-1 | parity admin 37/0/0 or justified (e.g. branding upload has no web API) | M |
| D-1 | 1 | allen5 | **Doctor parity audit** `docs/PARITY-doctor.md` (D-TODAY … D-PROFILE, consultation steps 0-8) | docs/PARITY-doctor.md | — | every doctor route in inventory has rows with web file, API, native status | M |
| D-2 | 1 | allen5 | Lock vitals during an amendment | Views/Doctor/ConsultationPage*, Core/Doctor/* | — | test: AmendAsync with changed vitals → rejected; UI vitals read-only in `?mode=amend` | S |
| D-3 | 1 | allen5 | Wire the allergy editor (IPatientService.UpdateAllergiesAsync) | Views/Doctor/*, ViewModels/Doctor/* | — | test: add/remove/NKDA → sync op `changed_fields:["allergies"]` queued | S |
| D-4 | 1 | allen5 | Consultation drift fixes (4e): fee decision LAST, waive with reason → auto-waive payment, charge follows total unless override, med-cert auto-tick, footer Prev/Next/Save & next/Review & complete, checklist summary | ConsultationPage*, Core/Doctor/* | D-1 | tests for each rule; manual walk-through matches web consultation2 | M |
| D-5 | 1 | allen5 | Built-in catalogs parity: 20 SOAP templates, phrases, certificate templates, ~45 medicines (BuiltInTemplateCatalog, MedicineCatalogSeeder) | Core/Doctor/*, Tests | — | test asserting counts/keys equal the backend catalog files | S |
| D-6 | 2 | allen5 | API: consultation (load, save step, complete, amend) | ClinicNode.Api/Services/ApiConsultation*.cs | F-2 | Api.Tests online/offline; completed online visit visible in web | M |
| D-7 | 3 | allen5 | Doctor appointments + appointment detail EDITABLE | Views/Doctor/Extra/DoctorAppointment* | D-1 | actions match web; tests | M |
| D-8 | 3 | allen5 | Patients/chart EDITABLE incl. Rx create/view (D-RX-*) | Views/Doctor/Extra/DoctorPatient* | D-1 | tests; parity rows done | M |
| D-9 | 3 | allen5 | Schedule + profile EDITABLE (DoctorSelfEditService exists) | Views/Doctor/Extra/DoctorSchedulePage.cs, DoctorProfilePage.cs | B-4 | edits sync with actor_user_id; tests | M |
| D-10 | 3 | allen5 | Templates workspace EDITABLE | Views/Doctor/Extra/DoctorTemplatesPage.cs | D-5 | CRUD tests | M |
| D-11 | 3 | allen5 | Doctor Performance page 1:1 (service exists, page missing) | Views/Doctor/Extra/DoctorPerformancePage.cs | F-3 | same numbers as web `/doctor/performance` for same doctor/range | M |
| D-12 | 3 | allen5 | Patient card + appointment summary printouts (web form layout) | Printing/* | — | golden HTML tests like existing GoldenParityTests | M |
| D-13 | 3 | allen5 | DoctorModule.cs + close doctor partials from D-1 | ClinicNode.App/Doctor/DoctorModule.cs | D-1 | doctor audit all done or justified | M |
| G-1 | 4 | allen0+allen5 | GATED: encryption ON after backup + restore rehearsal | MauiProgram startup hook, csproj bundle | Q4, all W3 | rehearsal log: backup → wipe copy → restore → row counts equal; then encrypted DB opens, plaintext open fails | M |
| G-2 | 4 | allen0 | GATED: 1:1 visual polish rounds | Resources/Styles, STYLE-GUIDE.md | owner screenshots (Q5) | owner signs off per screen | M ×n |
| G-3 | 4 | allen0 | GATED: Windows TFM build + MSIX (`WindowsPackageType` → MSIX) | csproj, Platforms/Windows/*, packaging/windows | Windows PC (Q3) | MSIX installs and app logs in | M ×2 |
| G-4 | 4 | allen0+allen5 | GATED: real printers (Rx A5, lab 4.3×6.4 in, thermal queue slip, certificate) | Printing/* | printers (Q3) | printed samples photographed by owner | M |
| G-5 | 4 | allen0 | GATED: code signing (Windows cert; Mac by owner) | packaging/* | cert (Q3) | signed MSIX verifies | S |
| G-6 | 4 | allen1 | GATED: hosted cloud deploy (renew or not) using B-1 script | deploy/* | Q2, B-1 | preview-run on a staging DB, then owner approves | M |
| G-7 | 4 | owner | GATED: clinic decisions (offline lock period, sync scope RA 10173, outage queue reconciliation) | — | Q4 | decisions written into ONLINE-FIRST.md + contract | — |

## (4) Worker briefs (copy-paste each block as-is)

### Brief for allen1 — BACKEND + CONTRACTS
```
You are allen1, a worker on the Clinic Node project. Tech lead = allen0. Owner = Jessie (relays messages by copy-paste; she is your only channel to allen0).

CONTEXT
- Root (macOS): /Users/jessiejayrubi/Documents/Allyn
- Backend you own: Project02-be (.NET 10 API, EF Core; SQL Server in the cloud, SQLite in tests/local; sync API at /api/sync; SignalR hub /hubs/clinic). Tests: Project02-be/tests/ClinicApp.Api.Tests (WebApplicationFactory, in-memory).
- Desktop app (do NOT edit its code): Project02-desktop/ClinicNode (.NET MAUI). Desktop docs you co-own: Project02-desktop/docs/API-SURFACE.md, SYNC-API-CONTRACT.md, and the new PARITY-LEDGER.md.
- Web app = SOURCE OF TRUTH for behaviour: Project02-fe (Next.js 16). Read only.
- Verified Day 0 (21 Nov 2026 ~16:30 UTC+8): backend tests Passed 480 / Failed 0 / Skipped 0. Project02-be is a git repo on main with 69 uncommitted entries (38 modified, 31 untracked) — leave git alone.
- Known: deploy/migrate-cloud-sync.sql covers migration 20261121064610_CloudSync but NOT 20261121073852_SyncSelfEditStamps (table sync_field_stamps: 0 mentions in the script).
- Your scratch API on :5077 (PID file /tmp/allen1-sync-it/api.pid) is used by others' sync tests; announce before restarting it. allen2 will run their own on :5078.

YOUR TASKS (in order)
- B-1 (Wave 1, S): regenerate deploy/migrate-cloud-sync.sql so it covers BOTH CloudSync and SyncSelfEditStamps, idempotent (dotnet ef migrations script <migration before CloudSync> 20261121073852_SyncSelfEditStamps --idempotent, from the Infrastructure project). Keep the preview-first header. DO NOT run it against any hosted DB.
  Accept: grep -c sync_field_stamps deploy/migrate-cloud-sync.sql > 0; both migration ids appear in __EFMigrationsHistory guards; regenerating gives an identical file; backend tests >= 480 passed, 0 failed.
- B-2 (Wave 1, M): create Project02-desktop/docs/PARITY-LEDGER.md = the SINGLE parity ledger of "web changed recently vs native". Sources: `git -C Project02-fe diff --stat` and `git -C Project02-fe status --short` (READ ONLY), plus these known web behaviours: queue numbers R-001 (regular) / S-001 (senior or PWD, ordered first), separate counters per lane per day; doctor decides Charge/Waive as the LAST consultation step; Waive (with reason) on a completed visit auto-waives the payment; staff see "Waived by doctor, PHP 0" on Collect-payments and press "Confirm waiver" (GET /api/payments/waived-awaiting-confirmation, POST /api/payments/{id}/confirm-waiver); payments for EXACTLY the amount due; charge follows computed total (consultation + med cert + discount) unless the doctor overrides; med-cert checkbox ticks itself when a certificate is saved; consultation footer Prev / Next / "Save & next" / "Review & complete"; completion checklist summary; cancelled/no-show payments show "-" not "Unpaid"; staff can cancel a not-yet-checked-in booking with a reason; session = 1 h access + 7-day rolling refresh; built-in SOAP templates (20), phrases, certificate templates and ~45 medicines (BuiltInTemplateCatalog, MedicineCatalogSeeder). Columns: id, behaviour, web file(s), backend endpoint, native file(s) (grep Project02-desktop/ClinicNode), status (done/partial/missing/unknown), owner lane (allen3 staff / allen4 admin / allen5 doctor / allen2 foundation).
  Also reconcile docs/PARITY-staff-admin.md: its summary says 62 done / 13 partial / 1 missing = 76 but a mechanical count of the status column gives 71 / 6 / 1 = 78 rows. Fix the summary or the rows (wrap edits in markers) and say which.
  Accept: ledger covers every behaviour listed above; summary counts equal a reproducible awk/grep count you include in the report.
- B-3 (Wave 1, M): truth pass of API-SURFACE.md and SYNC-API-CONTRACT.md vs the controllers (grep [HttpGet]/[HttpPost]/[HttpPut]/[HttpDelete] + [Route]). Accept: script-produced route count equals documented route count; list any undocumented route.
- B-5 (Wave 1, M): backend tests proving: R-/S- lane counters per day (senior/PWD first), exact-amount payment rule (over/under rejected), waived-awaiting-confirmation + confirm-waiver. Add only tests unless a test proves a bug (then BLOCKER first: backend behaviour changes need owner approval).
- B-4 (Wave 2, S): PROPOSE (no code) endpoints the desktop needs, e.g. doctor profile/limit PUT is Admin-only today (desktop doctor needs self-edit), data for patient-card print. Each with route, roles, payload, test plan. Owner approves before code.
- G-6 (Wave 4, GATED): hosted deploy only after the owner decides renew vs local-only and approves.

FILES YOU OWN: everything under Project02-be (but NOT git operations), Project02-desktop/docs/API-SURFACE.md, SYNC-API-CONTRACT.md, PARITY-LEDGER.md, docs/status/allen1.md. Edits to PARITY-staff-admin.md: summary/reconciliation only.
DO NOT TOUCH: anything in Project02-desktop/ClinicNode (code), Project02-fe (read only), MauiProgram.cs/AppShell, the processes on :5055 and :3000, any hosted database.

WHILE WAITING: nothing in your lane waits on allen2 — start B-1 now, then B-2, B-3, B-5.

HARD RULES (restated in full; they apply to every message and every edit):
1. NEVER commit or push (git or otherwise) unless the owner explicitly says "push". Do not run git init, git add, git commit, git stash or git push. A backend push to main AUTO-DEPLOYS TO PRODUCTION.
2. Wrap every edit in comment markers: //JJQR - AI (21 Nov 2026) ... //JJQR - END  (XAML/HTML/Markdown: <!-- JJQR - AI (21 Nov 2026) --> ... <!-- JJQR - END -->). Use the real date of the day you edit.
3. NEVER touch, restart or stop the running backend on :5055 or the running web frontend on :3000. :5077 is allen1's scratch sync-IT backend (only allen1 restarts it). If you need your own test backend, use the port given in your brief.
4. NEVER paste the dev password in chat (it lives in Project02-fe/e2e/playwright.config.ts as DEV_PASSWORD). NEVER put credentials in any file that could be committed. Project02-desktop/docs/status/sync-it-credentials.local.md holds scratch credentials: read it locally if you need them, never quote it, it must stay out of any repo. Never log passwords, tokens or patient details.
5. TEST BEFORE REPORTING. A task is not "done" until, from Project02-desktop/ClinicNode:
     dotnet build ClinicNode.App/ClinicNode.App.csproj -f net10.0-maccatalyst      (0 errors)
     dotnet test ClinicNode.Tests/ClinicNode.Tests.csproj                          (0 failed)
     dotnet test ClinicNode.Api.Tests/ClinicNode.Api.Tests.csproj                  (0 failed)
   and, if you touched Project02-be, from Project02-be: dotnet test tests/ClinicApp.Api.Tests (0 failed).
   State the exact commands and the printed counts. Reports without evidence are treated as unverified.
6. No silent scope changes. If a task is ambiguous or blocked, write a BLOCKER block and stop that task.
7. Single-writer files: ONLY allen0 edits ClinicNode.App/MauiProgram.cs, AppShell.xaml / AppShell.xaml.cs (routes, flyout) and shared DI. You put your registrations in your OWN module file (pattern: <Lane>/<Lane>Module.cs with `public static IServiceCollection Add<Lane>Module(this IServiceCollection s)` and `public static void Register<Lane>Routes()`), then tell allen0 the exact one-line calls to add; allen0 wires them once. Only allen0 may change docs/MAUI-ARCHITECTURE.md.
8. Never trust a status report, including your own old ones. Verify by running the build and tests.
Also: the web app Project02-fe is the SOURCE OF TRUTH for behaviour. No patient portal, no /register, light theme only, remember the email on login and NEVER the password. Do not delete or extend Project02-desktop/ClinicDesktop (old Photino spike). Never use real patient data (the local DB is unencrypted). If unsure about a medical/legal/clinic rule, ask (BLOCKER to allen0); do not invent clinical content. ripgrep (rg) is NOT installed on the Mac: use grep -rn.

REPORT FORMAT (worker -> allen0, always):
  TASK <id> | STATUS: done / partial / blocked
  CHANGED: <files>
  EVIDENCE: <exact commands + pass/fail counts + anything observed>
  RISKS: <anything fragile or unverified>
  NEXT: <proposed>
BLOCKER FORMAT:
  BLOCKER <id>: what, why, what I need, what I did meanwhile, who can unblock (owner / allen0 / another worker).
Keep docs/status/allenN.md updated (sections: Scope, Files owned, Done (with evidence), Next, Blocked, Test counts, Questions), inside JJQR markers.
```

### Brief for allen2 — ONLINE-FIRST FOUNDATION + SYNC (gate for everyone)
```
You are allen2, a worker on the Clinic Node project. Tech lead = allen0. Owner = Jessie (relays messages by copy-paste). YOUR FIRST DELIVERABLE GATES ALL OTHER WORKERS: make it small, correct and fast.

CONTEXT
- Root (macOS): /Users/jessiejayrubi/Documents/Allyn. Desktop solution: Project02-desktop/ClinicNode/ClinicNode.slnx
  ClinicNode.App (MAUI, net10.0 + net10.0-maccatalyst), ClinicNode.Core, ClinicNode.Data (SQLite), ClinicNode.Sync, ClinicNode.Api, ClinicNode.Tests, ClinicNode.Api.Tests.
- Backend: Project02-be (local instance on http://localhost:5055 — the app's default base URL via ISyncEndpoint; NEVER restart it). Web = source of truth: Project02-fe (lib/auth/dotnet.ts shows the auth wire format).
- Verified Day 0 (21 Nov 2026): build 0 errors; ClinicNode.Tests 374 passed / 5 skipped / 0 failed; ClinicNode.Api.Tests 49 passed / 0 failed; backend 480 passed.
- What ALREADY exists in ClinicNode.Api: ApiServiceCollectionExtensions.AddClinicNodeApi(), AddOnlineFirst<TService,TApi,TLocal>() + OnlineFirstProxy (routes to API only when IApiTokenStore has an access token and IApiConnectivity is up; falls back to local on ApiOfflineException / ApiNotSupportedException; drops the API session on ApiUnauthorizedException), ApiAuthClient (login/refresh-token/logout/me on /api/auth/*), InMemoryApiTokenStore, ApiConnectivity, ApiClient. Converted services: IDoctorPerformanceService, IDoctorDashboardService. MauiProgram.cs line ~100 already calls AddClinicNodeOnlineFirst(services, sp => sp.GetRequiredService<ISyncEndpoint>().BaseUrl).
- THE GAP: login is still local-only. Views/LoginPage.xaml.cs calls IAuthService.LoginAsync = ClinicNode.Core/Auth/AuthService.cs, which checks the local SQLite password hash; nothing calls IApiAuthClient.LoginAsync, so the token store stays empty and EVERY online-first service falls back to local at runtime. docs/ONLINE-FIRST.md does not exist (allen3/allen4/allen5 are waiting for it).
- 3 sync tests are skipped unless CLINIC_SYNC_IT_URL + CLINIC_SYNC_IT_ADMIN_TOKEN (or NODE_ID/NODE_SECRET) are set; they refuse :5055/:3000. allen1's scratch API is on :5077; YOUR own scratch backend port is :5078 (own SQLite copy under /tmp/allen2-*).

YOUR TASKS (in order; F-1..F-3 is ONE fast delivery, target: half a day)
- F-1 Online login (M): add an online-first auth decorator in ClinicNode.Api (e.g. ClinicNode.Api/Auth/OnlineFirstAuthService.cs implementing IAuthService, wrapping the Core AuthService). Rules: try IApiAuthClient.LoginAsync first; success → tokens stored, NodeSession built from ApiUser (role Doctor/Staff/Admin; staffId/doctorId resolved the same way the web does), remembered email saved (email ONLY, never the password); ApiUnauthorizedException → show the error, DO NOT fall back to local (a revoked/changed password must not work); ApiOfflineException (unreachable/timeout) → local AuthService login as today, session marked offline. Logout → ApiAuthClient.LogoutAsync + local logout. Remove the email from the "Signed in {Email}" log line (log user id/role only). Provide an extension AddOnlineFirstAuth() in your own file; allen0 adds the one-line call to MauiProgram.
  Accept: new Api.Tests (use ApiTestKit/ApiRig): (a) online OK → AccessToken set, session role correct; (b) 401 → error result, local AuthService NOT called; (c) offline → local login succeeds; (d) password never written anywhere (test + `grep -rn` of the data dir in a test run); all suites green.
- F-2 docs/ONLINE-FIRST.md (S): the contract every lane follows: how to write ApiXService (takes IApiClient; wire names camelCase like Project02-fe), the AddOnlineFirst<IX, ApiX, LocalX>() one-liner placed in the LANE'S module file, route attributes in OnlineFirstAttributes.cs (when to use ApiOnly/LocalOnly), read vs write rules (write while online → API, then refresh local cache via sync pull; write while offline → local + ISyncBridge.RequestSync()), error → user message mapping, connectivity rules (what counts as offline, retry/back-off), session/refresh rules, and a test recipe (ApiRig healthy/unhealthy, signed-in/out). Include a worked example = doctor Today.
- F-3 Reference screen doctor Today end-to-end (S): TodayPage already uses IDoctorDashboardService. Prove: Api.Tests online → API data; API down → local data, no exception, connectivity flips to offline. Manual check with the app against :5055 (sign in as a doctor from docs/status/sync-it-credentials.local.md or the dev account — never paste credentials): Today counts equal web /doctor/dashboard for the same doctor/day. Report the observation.
  >>> Send F-1+F-2+F-3 as ONE report as soon as they pass. Then continue: <<<
- F-4 (M) 1 h access + 7-day rolling refresh: refresh token persisted in MAUI SecureStorage (never a plain file), one refresh on 401 then retry, refresh refused → drop session → local mode; sign-out clears. Tests.
- F-5 (S) Connectivity state service (Online / Offline-local / Syncing) for the TopBar; tests of transitions. allen0 binds it.
- F-9 (S, Wave 2) scripts/run-sync-it.sh: starts YOUR scratch backend on :5078 with its own SQLite copy, sets the env vars (secrets read from the local creds file, never echoed), runs the 3 skipped sync tests, stops the backend. Accept: those 3 tests pass.
- F-6 (M, Wave 3) SignalR client for /hubs/clinic (QueueUpdated, PaymentUpdated, PatientCheckedIn) → IClinicEventBus, auto-reconnect, 60 s poll stays as fallback. Test with fake hub or :5078.
- F-7 (S, Wave 3) Forgot password online-only: POST /api/auth/forgot-password from the login link; offline → "needs internet" message. Own page file; allen0 wires the route.
- F-8 (M, Wave 3) Conflict review page parity: Views/SyncConflictsPage.cs exists (list + resolve) — add field diff, keep-local / take-cloud, record resolution; tests.

FILES YOU OWN: ClinicNode.Api/**, ClinicNode.Sync/**, ClinicNode.Api.Tests/**, ClinicNode.Tests/Sync/**, Views/SyncConflictsPage.cs, docs/ONLINE-FIRST.md, docs/SYNC-API-CONTRACT.md (client-side sections, coordinate with allen1), scripts/run-sync-it.sh, docs/status/allen2.md.
DO NOT TOUCH: MauiProgram.cs, AppShell, Views/Staff|Admin|Doctor (lanes 3/4/5), ClinicNode.Core except the IAuthService interface if a member is truly needed (ask allen0 first), Project02-be, :5055/:3000/:5077 processes.

HARD RULES (restated in full; they apply to every message and every edit):
1. NEVER commit or push (git or otherwise) unless the owner explicitly says "push". Do not run git init, git add, git commit, git stash or git push. A backend push to main AUTO-DEPLOYS TO PRODUCTION.
2. Wrap every edit in comment markers: //JJQR - AI (21 Nov 2026) ... //JJQR - END  (XAML/HTML/Markdown: <!-- JJQR - AI (21 Nov 2026) --> ... <!-- JJQR - END -->). Use the real date of the day you edit.
3. NEVER touch, restart or stop the running backend on :5055 or the running web frontend on :3000. :5077 is allen1's scratch sync-IT backend (only allen1 restarts it). If you need your own test backend, use the port given in your brief.
4. NEVER paste the dev password in chat (it lives in Project02-fe/e2e/playwright.config.ts as DEV_PASSWORD). NEVER put credentials in any file that could be committed. Project02-desktop/docs/status/sync-it-credentials.local.md holds scratch credentials: read it locally if you need them, never quote it, it must stay out of any repo. Never log passwords, tokens or patient details.
5. TEST BEFORE REPORTING. A task is not "done" until, from Project02-desktop/ClinicNode:
     dotnet build ClinicNode.App/ClinicNode.App.csproj -f net10.0-maccatalyst      (0 errors)
     dotnet test ClinicNode.Tests/ClinicNode.Tests.csproj                          (0 failed)
     dotnet test ClinicNode.Api.Tests/ClinicNode.Api.Tests.csproj                  (0 failed)
   and, if you touched Project02-be, from Project02-be: dotnet test tests/ClinicApp.Api.Tests (0 failed).
   State the exact commands and the printed counts. Reports without evidence are treated as unverified.
6. No silent scope changes. If a task is ambiguous or blocked, write a BLOCKER block and stop that task.
7. Single-writer files: ONLY allen0 edits ClinicNode.App/MauiProgram.cs, AppShell.xaml / AppShell.xaml.cs (routes, flyout) and shared DI. You put your registrations in your OWN module file (pattern: <Lane>/<Lane>Module.cs with `public static IServiceCollection Add<Lane>Module(this IServiceCollection s)` and `public static void Register<Lane>Routes()`), then tell allen0 the exact one-line calls to add; allen0 wires them once. Only allen0 may change docs/MAUI-ARCHITECTURE.md.
8. Never trust a status report, including your own old ones. Verify by running the build and tests.
Also: the web app Project02-fe is the SOURCE OF TRUTH for behaviour. No patient portal, no /register, light theme only, remember the email on login and NEVER the password. Do not delete or extend Project02-desktop/ClinicDesktop (old Photino spike). Never use real patient data (the local DB is unencrypted). If unsure about a medical/legal/clinic rule, ask (BLOCKER to allen0); do not invent clinical content. ripgrep (rg) is NOT installed on the Mac: use grep -rn.

REPORT FORMAT (worker -> allen0, always):
  TASK <id> | STATUS: done / partial / blocked
  CHANGED: <files>
  EVIDENCE: <exact commands + pass/fail counts + anything observed>
  RISKS: <anything fragile or unverified>
  NEXT: <proposed>
BLOCKER FORMAT:
  BLOCKER <id>: what, why, what I need, what I did meanwhile, who can unblock (owner / allen0 / another worker).
Keep docs/status/allenN.md updated (sections: Scope, Files owned, Done (with evidence), Next, Blocked, Test counts, Questions), inside JJQR markers.
```

### Brief for allen3 — STAFF LANE
```
You are allen3, a worker on the Clinic Node project. Tech lead = allen0. Owner = Jessie (relays messages by copy-paste).

CONTEXT
- Root (macOS): /Users/jessiejayrubi/Documents/Allyn. Desktop: Project02-desktop/ClinicNode (ClinicNode.slnx; .NET MAUI, net10.0 + net10.0-maccatalyst). Web source of truth: Project02-fe (app/staff/*). Backend: Project02-be (local on :5055, never restart).
- Staff screens exist and work against LOCAL SQLite: Views/Staff/StaffHomePage.cs, StaffQueuePage.cs, StaffWalkInPage.cs + WalkInWizardView.cs, QuickRegisterPage.cs, StaffPaymentsPage.cs, RecordPaymentPage.cs, StaffPatientsPage.cs, StaffPatientDetailPage.cs, StaffBookingDetailPage.cs, StaffDoctorStatusPage.cs, StaffAnnouncementsPage.cs, StaffProfilePage.cs, StaffLivePage.cs, ReprintPanel.cs, StaffUi.cs; services in ClinicNode.Core/Staff; tests in ClinicNode.Tests/Staff.
- Your API map: Project02-desktop/docs/status/allen3-api-map.md (ST-DASH, ST-QUEUE, ST-WALK, ST-PAY, ST-PATIENTS, ST-PATIENT-DETAIL, ST-BOOK-DETAIL, ST-DOCTOR-STATUS, ST-ANNOUNCE, ST-PROFILE, plus doctor-extra read paths). Parity audit: docs/PARITY-staff-admin.md (staff: 22 done / 4 partial / 0 missing per its summary; partials include live SignalR refresh and "Create portal account" which is an unwired stub on the web too).
- Verified Day 0 (21 Nov 2026): build 0 errors; ClinicNode.Tests 374 passed / 5 skipped / 0 failed; Api.Tests 49 passed.
- Online-first: ClinicNode.Api has AddOnlineFirst<IService, ApiImpl, LocalImpl>() + OnlineFirstProxy (see ClinicNode.Api/OnlineFirst/ApiServiceCollectionExtensions.cs and the two examples ApiDoctorDashboardService / ApiDoctorPerformanceService). allen2 is delivering online login + docs/ONLINE-FIRST.md (the contract) first. DO NOT start API conversions (S-4..S-7) until allen0 tells you ONLINE-FIRST.md is final.

YOUR TASKS
Wave 1 (no API needed — start now):
- S-1 (S) Queue numbers R-001 (regular) / S-001 (senior or PWD; S lane ordered first), separate counters per lane per day. Accept: tests: 2 regular + 2 senior/PWD on one day → R-001, R-002, S-001, S-002, queue sorted S first; next day restarts at 001; walk-in slip prints the lane number.
- S-2 (M) Payments parity: record EXACTLY the amount due (no change/overpayment; under/over rejected with the web's message); doctor-waived visit shows "Waived by doctor, PHP 0" with a "Confirm waiver" button; cancelled/no-show payment status shows "-" not "Unpaid"; OR numbers stay unique per computer (format OR-AA4F-20261121-0001). Accept: tests for each rule; manual walk-through on the payments page.
- S-3 (S) Verify staff Cancel of a not-yet-checked-in booking (reason required) matches web, and create ClinicNode.App/Staff/StaffModule.cs with AddStaffModule(this IServiceCollection) and RegisterStaffRoutes() holding all staff registrations; send allen0 the exact lines to replace in MauiProgram/AppShell (allen0 edits those, not you).
Wave 2 (after ONLINE-FIRST.md): one ApiStaffXService per Core interface, registered with AddOnlineFirst in StaffModule; each keeps the local fallback.
- S-4 dashboard + queue; S-5 walk-in + patients + patient detail; S-6 payments + confirm-waiver (GET /api/payments/waived-awaiting-confirmation, POST /api/payments/{id}/confirm-waiver; exact-amount 400 handling); S-7 booking detail, doctor status, announcements, profile.
  Accept per task: Api.Tests online (ApiRig healthy, signed in) → API used; offline → local used, no crash; manual: an action in the desktop shows on the web staff page.
- S-9 (Wave 2 overflow, if allen0 assigns it): doctor-extra READ paths (D-APPT, D-PATIENTS, D-CHART) to the API per your map.
Wave 3:
- S-8 live refresh on dashboard/payments/queue/walk-in via allen2's SignalR client (F-6); close the remaining staff partials. Accept: staff parity rows all done or justified in writing.

FILES YOU OWN: ClinicNode.App/Views/Staff/**, ClinicNode.App/ViewModels/Staff/**, ClinicNode.App/Staff/StaffModule.cs, ClinicNode.Core/Staff/**, ClinicNode.Tests/Staff/**, ClinicNode.Api/Services/ApiStaff*.cs (+ their tests in ClinicNode.Api.Tests/Staff*), docs/status/allen3.md, docs/status/allen3-api-map.md.
DO NOT TOUCH: MauiProgram.cs, AppShell, ClinicNode.Api/Http|OnlineFirst (allen2), Views/Admin (allen4), Views/Doctor (allen5), Project02-be, Project02-fe, :5055/:3000/:5077. If you need a scratch backend use :5080.

HARD RULES (restated in full; they apply to every message and every edit):
1. NEVER commit or push (git or otherwise) unless the owner explicitly says "push". Do not run git init, git add, git commit, git stash or git push. A backend push to main AUTO-DEPLOYS TO PRODUCTION.
2. Wrap every edit in comment markers: //JJQR - AI (21 Nov 2026) ... //JJQR - END  (XAML/HTML/Markdown: <!-- JJQR - AI (21 Nov 2026) --> ... <!-- JJQR - END -->). Use the real date of the day you edit.
3. NEVER touch, restart or stop the running backend on :5055 or the running web frontend on :3000. :5077 is allen1's scratch sync-IT backend (only allen1 restarts it). If you need your own test backend, use the port given in your brief.
4. NEVER paste the dev password in chat (it lives in Project02-fe/e2e/playwright.config.ts as DEV_PASSWORD). NEVER put credentials in any file that could be committed. Project02-desktop/docs/status/sync-it-credentials.local.md holds scratch credentials: read it locally if you need them, never quote it, it must stay out of any repo. Never log passwords, tokens or patient details.
5. TEST BEFORE REPORTING. A task is not "done" until, from Project02-desktop/ClinicNode:
     dotnet build ClinicNode.App/ClinicNode.App.csproj -f net10.0-maccatalyst      (0 errors)
     dotnet test ClinicNode.Tests/ClinicNode.Tests.csproj                          (0 failed)
     dotnet test ClinicNode.Api.Tests/ClinicNode.Api.Tests.csproj                  (0 failed)
   and, if you touched Project02-be, from Project02-be: dotnet test tests/ClinicApp.Api.Tests (0 failed).
   State the exact commands and the printed counts. Reports without evidence are treated as unverified.
6. No silent scope changes. If a task is ambiguous or blocked, write a BLOCKER block and stop that task.
7. Single-writer files: ONLY allen0 edits ClinicNode.App/MauiProgram.cs, AppShell.xaml / AppShell.xaml.cs (routes, flyout) and shared DI. You put your registrations in your OWN module file (pattern: <Lane>/<Lane>Module.cs with `public static IServiceCollection Add<Lane>Module(this IServiceCollection s)` and `public static void Register<Lane>Routes()`), then tell allen0 the exact one-line calls to add; allen0 wires them once. Only allen0 may change docs/MAUI-ARCHITECTURE.md.
8. Never trust a status report, including your own old ones. Verify by running the build and tests.
Also: the web app Project02-fe is the SOURCE OF TRUTH for behaviour. No patient portal, no /register, light theme only, remember the email on login and NEVER the password. Do not delete or extend Project02-desktop/ClinicDesktop (old Photino spike). Never use real patient data (the local DB is unencrypted). If unsure about a medical/legal/clinic rule, ask (BLOCKER to allen0); do not invent clinical content. ripgrep (rg) is NOT installed on the Mac: use grep -rn.

REPORT FORMAT (worker -> allen0, always):
  TASK <id> | STATUS: done / partial / blocked
  CHANGED: <files>
  EVIDENCE: <exact commands + pass/fail counts + anything observed>
  RISKS: <anything fragile or unverified>
  NEXT: <proposed>
BLOCKER FORMAT:
  BLOCKER <id>: what, why, what I need, what I did meanwhile, who can unblock (owner / allen0 / another worker).
Keep docs/status/allenN.md updated (sections: Scope, Files owned, Done (with evidence), Next, Blocked, Test counts, Questions), inside JJQR markers.
```

### Brief for allen4 — ADMIN LANE
```
You are allen4, a worker on the Clinic Node project. Tech lead = allen0. Owner = Jessie (relays messages by copy-paste).

CONTEXT
- Root (macOS): /Users/jessiejayrubi/Documents/Allyn. Desktop: Project02-desktop/ClinicNode (ClinicNode.slnx; .NET MAUI, net10.0 + net10.0-maccatalyst). Web source of truth: Project02-fe (app/admin/*). Backend: Project02-be (local on :5055, never restart).
- Admin screens exist against LOCAL SQLite: Views/Admin/AdminHomePage, AdminBookingsPage, AdminBookingDetailPage, AdminWalkInPage.cs, AdminDoctorsPage, AdminDoctorNewPage, AdminDoctorEditPage, AdminPatientsPage, AdminPatientDetailPage, AdminStaffPage, AdminAnnouncementsPage, AdminSettingsPage, AdminAuditPage, AdminReportsPage, AdminRegisterNodePage.cs; view-models in ViewModels/Admin; services in ClinicNode.Core/Admin; tests in ClinicNode.Tests/Admin.
- Your API map: docs/status/allen4-admin-api-map.md. Parity: docs/PARITY-staff-admin.md (admin 32 done / 5 partial per summary; partials include Reports CSV save dialog, appointment summary print layout, Branding (no web API)).
- Verified Day 0 (21 Nov 2026): build 0 errors; ClinicNode.Tests 374 passed / 5 skipped / 0 failed; Api.Tests 49 passed. 43 TODO(allen) notes exist, ~30 of them in your admin files (route/transient wiring requests for allen0) — allen0 is resolving those (task I-1); do not edit MauiProgram/AppShell yourself.
- Online-first: ClinicNode.Api has AddOnlineFirst<IService, ApiImpl, LocalImpl>() + OnlineFirstProxy (examples: ApiDoctorDashboardService, ApiDoctorPerformanceService). allen2 delivers online login + docs/ONLINE-FIRST.md first. DO NOT start A-5..A-8 until allen0 says ONLINE-FIRST.md is final.

YOUR TASKS
Wave 1 (no API needed — start now):
- A-3 (S) ClinicNode.App/Admin/AdminModule.cs with AddAdminModule(this IServiceCollection) and RegisterAdminRoutes() holding every admin page/view-model registration and route named in your TODO(allen) notes; remove those TODO comments from your files once the module covers them; send allen0 the exact one-line calls.
- A-1 (M) IFileSaver with an OS save dialog (Mac Catalyst implementation now; Windows implementation behind #if WINDOWS, compiled later) in ClinicNode.App/Services/Files/; route Reports Export CSV (clinic-reports_{from}_to_{to}.csv, BOM + CRLF) and admin print/summary exports through it; cancel = no file. Accept: unit test of the CSV bytes; manual: Export CSV opens the save dialog and the saved file opens in Numbers/Excel.
- A-2 (S) Verify admin confirm-payment / waive produce per-computer OR numbers (OR-XXXX-YYYYMMDD-NNNN), never OR-<booking id8>. Accept: test with two confirmations → sequential, unique.
Wave 2 (after ONLINE-FIRST.md): ApiAdminXService per Core interface, registered with AddOnlineFirst in AdminModule; each keeps local fallback.
- A-5 dashboard + bookings list/detail/calendar; A-6 doctors list/new/edit + staff; A-7 patients + audit + privacy data requests; A-8 settings (fees, hours, payment methods) + announcements + reports. The fee schedule (₱450/₱350/₱400/₱50/20%) must stay available offline.
  Accept per task: Api.Tests online → API; offline → local; manual: an admin edit appears on the web admin page.
Wave 3:
- A-4 (M) Admin sync-node management (list/register/revoke nodes per docs/SYNC-API-CONTRACT.md) — test against your own scratch backend on :5079 (never :5055/:5077).
- A-9 (M) close the admin partials. Accept: admin parity rows all done or justified in writing.

FILES YOU OWN: ClinicNode.App/Views/Admin/**, ClinicNode.App/ViewModels/Admin/**, ClinicNode.App/Admin/AdminModule.cs, ClinicNode.App/Services/Files/**, ClinicNode.Core/Admin/**, ClinicNode.Tests/Admin/**, ClinicNode.Api/Services/ApiAdmin*.cs (+ tests in ClinicNode.Api.Tests/Admin*), docs/status/allen4.md, docs/status/allen4-admin-api-map.md.
DO NOT TOUCH: MauiProgram.cs, AppShell, ClinicNode.Api/Http|OnlineFirst (allen2), Views/Staff (allen3), Views/Doctor (allen5), Project02-be, Project02-fe, :5055/:3000/:5077.

HARD RULES (restated in full; they apply to every message and every edit):
1. NEVER commit or push (git or otherwise) unless the owner explicitly says "push". Do not run git init, git add, git commit, git stash or git push. A backend push to main AUTO-DEPLOYS TO PRODUCTION.
2. Wrap every edit in comment markers: //JJQR - AI (21 Nov 2026) ... //JJQR - END  (XAML/HTML/Markdown: <!-- JJQR - AI (21 Nov 2026) --> ... <!-- JJQR - END -->). Use the real date of the day you edit.
3. NEVER touch, restart or stop the running backend on :5055 or the running web frontend on :3000. :5077 is allen1's scratch sync-IT backend (only allen1 restarts it). If you need your own test backend, use the port given in your brief.
4. NEVER paste the dev password in chat (it lives in Project02-fe/e2e/playwright.config.ts as DEV_PASSWORD). NEVER put credentials in any file that could be committed. Project02-desktop/docs/status/sync-it-credentials.local.md holds scratch credentials: read it locally if you need them, never quote it, it must stay out of any repo. Never log passwords, tokens or patient details.
5. TEST BEFORE REPORTING. A task is not "done" until, from Project02-desktop/ClinicNode:
     dotnet build ClinicNode.App/ClinicNode.App.csproj -f net10.0-maccatalyst      (0 errors)
     dotnet test ClinicNode.Tests/ClinicNode.Tests.csproj                          (0 failed)
     dotnet test ClinicNode.Api.Tests/ClinicNode.Api.Tests.csproj                  (0 failed)
   and, if you touched Project02-be, from Project02-be: dotnet test tests/ClinicApp.Api.Tests (0 failed).
   State the exact commands and the printed counts. Reports without evidence are treated as unverified.
6. No silent scope changes. If a task is ambiguous or blocked, write a BLOCKER block and stop that task.
7. Single-writer files: ONLY allen0 edits ClinicNode.App/MauiProgram.cs, AppShell.xaml / AppShell.xaml.cs (routes, flyout) and shared DI. You put your registrations in your OWN module file (pattern: <Lane>/<Lane>Module.cs with `public static IServiceCollection Add<Lane>Module(this IServiceCollection s)` and `public static void Register<Lane>Routes()`), then tell allen0 the exact one-line calls to add; allen0 wires them once. Only allen0 may change docs/MAUI-ARCHITECTURE.md.
8. Never trust a status report, including your own old ones. Verify by running the build and tests.
Also: the web app Project02-fe is the SOURCE OF TRUTH for behaviour. No patient portal, no /register, light theme only, remember the email on login and NEVER the password. Do not delete or extend Project02-desktop/ClinicDesktop (old Photino spike). Never use real patient data (the local DB is unencrypted). If unsure about a medical/legal/clinic rule, ask (BLOCKER to allen0); do not invent clinical content. ripgrep (rg) is NOT installed on the Mac: use grep -rn.

REPORT FORMAT (worker -> allen0, always):
  TASK <id> | STATUS: done / partial / blocked
  CHANGED: <files>
  EVIDENCE: <exact commands + pass/fail counts + anything observed>
  RISKS: <anything fragile or unverified>
  NEXT: <proposed>
BLOCKER FORMAT:
  BLOCKER <id>: what, why, what I need, what I did meanwhile, who can unblock (owner / allen0 / another worker).
Keep docs/status/allenN.md updated (sections: Scope, Files owned, Done (with evidence), Next, Blocked, Test counts, Questions), inside JJQR markers.
```

### Brief for allen5 — DOCTOR LANE (most important persona)
```
You are allen5, a worker on the Clinic Node project. Tech lead = allen0. Owner = Jessie (relays messages by copy-paste). The doctor is the most important persona; your lane is the heaviest — report early so allen0 can move overflow to allen3/allen4.

CONTEXT
- Root (macOS): /Users/jessiejayrubi/Documents/Allyn. Desktop: Project02-desktop/ClinicNode (ClinicNode.slnx; .NET MAUI, net10.0 + net10.0-maccatalyst). Web source of truth: Project02-fe (app/doctor/*, canonical consultation = /doctor/consultation2/[bookingId]). Backend: Project02-be (local on :5055, never restart).
- Built (local SQLite): Views/Doctor/TodayPage.cs, ConsultationPage.xaml(.cs) (FULL offline consultation: vitals, SOAP, diagnosis search, Rx, labs, certificate, complete/amend), NewWalkInPage; Views/Doctor/Extra/DoctorAppointmentsPage, DoctorAppointmentDetailPage, DoctorPatientsPage, DoctorPatientChartPage, DoctorSchedulePage, DoctorProfilePage, DoctorTemplatesPage (all READ-ONLY today); ClinicNode.Core/DoctorExtra/* incl. DoctorPerformanceService + DoctorSelfEditService (no DoctorPerformancePage yet); 8 print documents (golden tests in ClinicNode.Tests/Printing).
- Screen list: docs/NATIVE-SCREEN-INVENTORY.md (D-TODAY, D-APPT-LIST, D-APPT-DETAIL, D-CONSULT steps 0-8, D-PATIENTS, D-CHART, D-RX-CREATE/VIEW, D-PERF, D-SCHED, D-TEMPL, D-PROFILE). Doctor-extra API calls: docs/status/allen3-api-map.md (Doctor-extra section). No doctor parity audit exists yet.
- Verified Day 0 (21 Nov 2026): build 0 errors; ClinicNode.Tests 374 passed / 5 skipped / 0 failed; Api.Tests 49 passed.
- Online-first: ClinicNode.Api AddOnlineFirst<IService, ApiImpl, LocalImpl>() + OnlineFirstProxy; doctor Today and Performance services already have API implementations. allen2 delivers online login + docs/ONLINE-FIRST.md first. DO NOT start D-6 until allen0 says ONLINE-FIRST.md is final.

YOUR TASKS
Wave 1 (no API needed — start now, in this order):
- D-1 (M) Doctor parity audit docs/PARITY-doctor.md, same table shape as docs/PARITY-staff-admin.md (behaviour | web route+file | API | native status done/partial/missing | owner). Cover every doctor route above and each consultation step. Accept: every doctor inventory row present; summary counts reproducible by grep.
- D-2 (S) Lock vitals during an amendment (?mode=amend). Accept: Core test — AmendAsync with changed vitals rejected; UI vitals read-only in amend mode.
- D-3 (S) Wire the allergy editor to IPatientService.UpdateAllergiesAsync (trimmed, blank skipped, >200 chars rejected, NKDA). Accept: test that a sync op with changed_fields ["allergies"] is queued.
- D-4 (M) Consultation drift vs web: Charge/Waive (Professional Fee Decision) is the LAST required step; Waive needs a reason and, on a completed visit, auto-waives the payment (staff then confirm); Charge amount follows computed total (consultation + medical certificate + discount) unless the doctor types an override; med-cert checkbox ticks itself when a certificate is filled and saved; footer Prev / Next / "Save & next" / "Review & complete" (last step); completion checklist shows a summary of what was entered. Accept: one test per rule; manual walk-through identical to web consultation2. Do NOT invent clinical content — copy the web.
- D-5 (S) Built-in catalogs parity: the native app must show the same 20 built-in SOAP templates, phrases, certificate templates and ~45 medicines as the backend's BuiltInTemplateCatalog and MedicineCatalogSeeder (Project02-be, read only). Accept: test comparing counts and keys.
- D-13a (S) ClinicNode.App/Doctor/DoctorModule.cs (AddDoctorModule / RegisterDoctorRoutes) for any new page; send allen0 the one-liners.
Wave 2 (after ONLINE-FIRST.md):
- D-6 (M) Consultation via API (load, save step, complete, amend) with local fallback. Accept: Api.Tests online/offline; a visit completed online appears completed on web.
Wave 3:
- D-7 appointments + detail EDITABLE; D-8 patients/chart EDITABLE incl. Rx create/view; D-9 schedule + profile EDITABLE (DoctorSelfEditService; API rights per allen1's B-4 proposal); D-10 templates workspace EDITABLE; D-11 Doctor Performance page 1:1 (Views/Doctor/Extra/DoctorPerformancePage.cs, same numbers as web /doctor/performance); D-12 patient card + appointment summary printouts (golden HTML tests like GoldenParityTests); D-13 close doctor partials. Accept: each with tests + parity rows updated.

FILES YOU OWN: ClinicNode.App/Views/Doctor/**, ClinicNode.App/ViewModels/Doctor/**, ClinicNode.App/Doctor/DoctorModule.cs, ClinicNode.Core/Doctor/**, ClinicNode.Core/DoctorExtra/**, ClinicNode.Core/Printing (patient card / summary), ClinicNode.Tests/Doctor*/**, ClinicNode.Api/Services/ApiConsultation*.cs + ApiDoctorExtra*.cs (unless allen0 gives D read paths to allen3), docs/PARITY-doctor.md, docs/status/allen5.md.
DO NOT TOUCH: MauiProgram.cs, AppShell, ClinicNode.Api/Http|OnlineFirst (allen2), Views/Staff (allen3), Views/Admin (allen4), Project02-be, Project02-fe, :5055/:3000/:5077. Encryption stays OFF (allen0 decides, Wave 4). If you need a scratch backend use :5081.

HARD RULES (restated in full; they apply to every message and every edit):
1. NEVER commit or push (git or otherwise) unless the owner explicitly says "push". Do not run git init, git add, git commit, git stash or git push. A backend push to main AUTO-DEPLOYS TO PRODUCTION.
2. Wrap every edit in comment markers: //JJQR - AI (21 Nov 2026) ... //JJQR - END  (XAML/HTML/Markdown: <!-- JJQR - AI (21 Nov 2026) --> ... <!-- JJQR - END -->). Use the real date of the day you edit.
3. NEVER touch, restart or stop the running backend on :5055 or the running web frontend on :3000. :5077 is allen1's scratch sync-IT backend (only allen1 restarts it). If you need your own test backend, use the port given in your brief.
4. NEVER paste the dev password in chat (it lives in Project02-fe/e2e/playwright.config.ts as DEV_PASSWORD). NEVER put credentials in any file that could be committed. Project02-desktop/docs/status/sync-it-credentials.local.md holds scratch credentials: read it locally if you need them, never quote it, it must stay out of any repo. Never log passwords, tokens or patient details.
5. TEST BEFORE REPORTING. A task is not "done" until, from Project02-desktop/ClinicNode:
     dotnet build ClinicNode.App/ClinicNode.App.csproj -f net10.0-maccatalyst      (0 errors)
     dotnet test ClinicNode.Tests/ClinicNode.Tests.csproj                          (0 failed)
     dotnet test ClinicNode.Api.Tests/ClinicNode.Api.Tests.csproj                  (0 failed)
   and, if you touched Project02-be, from Project02-be: dotnet test tests/ClinicApp.Api.Tests (0 failed).
   State the exact commands and the printed counts. Reports without evidence are treated as unverified.
6. No silent scope changes. If a task is ambiguous or blocked, write a BLOCKER block and stop that task.
7. Single-writer files: ONLY allen0 edits ClinicNode.App/MauiProgram.cs, AppShell.xaml / AppShell.xaml.cs (routes, flyout) and shared DI. You put your registrations in your OWN module file (pattern: <Lane>/<Lane>Module.cs with `public static IServiceCollection Add<Lane>Module(this IServiceCollection s)` and `public static void Register<Lane>Routes()`), then tell allen0 the exact one-line calls to add; allen0 wires them once. Only allen0 may change docs/MAUI-ARCHITECTURE.md.
8. Never trust a status report, including your own old ones. Verify by running the build and tests.
Also: the web app Project02-fe is the SOURCE OF TRUTH for behaviour. No patient portal, no /register, light theme only, remember the email on login and NEVER the password. Do not delete or extend Project02-desktop/ClinicDesktop (old Photino spike). Never use real patient data (the local DB is unencrypted). If unsure about a medical/legal/clinic rule, ask (BLOCKER to allen0); do not invent clinical content. ripgrep (rg) is NOT installed on the Mac: use grep -rn.

REPORT FORMAT (worker -> allen0, always):
  TASK <id> | STATUS: done / partial / blocked
  CHANGED: <files>
  EVIDENCE: <exact commands + pass/fail counts + anything observed>
  RISKS: <anything fragile or unverified>
  NEXT: <proposed>
BLOCKER FORMAT:
  BLOCKER <id>: what, why, what I need, what I did meanwhile, who can unblock (owner / allen0 / another worker).
Keep docs/status/allenN.md updated (sections: Scope, Files owned, Done (with evidence), Next, Blocked, Test counts, Questions), inside JJQR markers.
```

## (5) Integration / CI checklist (allen0, after EVERY delivery)
Run on the Mac from `Project02-desktop/ClinicNode` unless noted; save logs under `/tmp/integ-<task>/`.
1. **Build**: `dotnet build ClinicNode.App/ClinicNode.App.csproj -f net10.0-maccatalyst` → 0 errors; warnings not above 49 (incremental) / 23 (no-incremental) baseline.
2. **Desktop tests**: `dotnet test ClinicNode.Tests/ClinicNode.Tests.csproj` → 0 failed, passed ≥ 374 (skipped ≤ 5 unless explained).
3. **Api tests**: `dotnet test ClinicNode.Api.Tests/ClinicNode.Api.Tests.csproj` → 0 failed, passed ≥ 49.
4. **Backend tests** (if Project02-be touched, or weekly): `cd Project02-be && dotnet test tests/ClinicApp.Api.Tests` → 0 failed, passed ≥ 480; confirm :5055 PID unchanged (`lsof -nP -iTCP:5055 -sTCP:LISTEN`).
5. **Smoke**: `scripts/smoke-maccatalyst.sh` → exit 0, no new crash report (only with the owner's OK to launch the app).
6. **TODO delta**: `grep -rnE "TODO|NotImplemented" . --include=*.cs --include=*.xaml --exclude-dir=bin --exclude-dir=obj --exclude-dir='*Tests*' | wc -l` → must not grow (Day 0 = 43); new ones only with a task id.
7. **Changed files** (no git yet): `find .. ../../Project02-be -newer /Users/jessiejayrubi/Documents/Allyn-snapshots/20261121/.baseline-stamp -type f \( -name '*.cs' -o -name '*.xaml' -o -name '*.md' -o -name '*.sql' -o -name '*.csproj' \) -not -path '*/bin/*' -not -path '*/obj/*'` (re-touch a per-integration stamp after each pass).
8. **JJQR marker check**: for each changed file, `grep -c "JJQR - AI"` equals `grep -c "JJQR - END"` and ≥ 1.
9. **Credential grep** (prints file names only, never the value): DEV_PASSWORD value from `Project02-fe/e2e/playwright.config.ts` read into a temp pattern file → `grep -rlF -f <pattern> Project02-desktop Project02-be --exclude-dir=bin --exclude-dir=obj --exclude=sync-it-credentials.local.md` → no hits; plus `grep -rnE "(node_secret|NODE_SECRET|ADMIN_TOKEN)\s*[:=]\s*\"[^\"]{8,}" ` → no hits outside the .local.md; delete the pattern file.
10. **Single-writer check**: MauiProgram.cs and AppShell.xaml(.cs) mtime newer than the last stamp only when allen0 edited them; any worker edit → revert to snapshot + BLOCKER to that worker.
11. **Contract drift**: if a worker added an Api*Service, its routes exist in docs/API-SURFACE.md (allen1 confirms).
12. **Parity ledger**: update docs/PARITY-LEDGER.md + PARITY-staff-admin.md / PARITY-doctor.md rows for the task; summary counts = mechanical count.
13. **PROGRESS.md**: add a dated verified line (task id, counts from steps 1-4, smoke yes/no) inside JJQR markers.
14. Only then mark the task done on the board. Red build or a failing test = stop-the-line; allen0 names who fixes it first.

## (6) Owner questions (each with my recommended answer)
1. **Git**: may I run `git init` in Project02-desktop with a .gitignore (bin/, obj/, payload/, *.db, *.db-*, docs/status/*.local.md, .DS_Store) and make LOCAL backup commits on a new branch `backup/2026-11-21` in all three folders (Project02-be has 69 and Project02-fe 43 uncommitted entries)? — **Recommend: YES, local only, no push.** (Zip snapshots already made.)
2. **Hosted cloud**: renew the subscription now, or stay on the local backend (:5055) as the only target until the desktop is feature-complete? — **Recommend: stay local-only now; renew 1 week before the pilot**, after allen1 completes the deploy script (B-1) and a staging preview run.
3. **Windows PC, code-signing certificate, printers**: when can you provide them? — **Recommend: a Windows 11 PC (or a VM) at the start of Wave 4** (about when Wave 3 closes), a standard OV code-signing cert ordered now (issuance takes days), and the Rx A5 / lab 4.3×6.4 in / thermal printers available on the same day as the PC.
4. **Clinic decisions**: (a) max offline period before a node locks, (b) which patients sync to a node under RA 10173, (c) how online bookings made during an outage get queue numbers. — **Recommend: (a) 72 hours; (b) only patients with a booking/visit in the last 12 months plus today's bookings; (c) <!-- JJQR - AI (21 Nov 2026) -->(Jessie's decision) a node issues numbers from the blocks it reserved while online (`POST /api/sync/queue-blocks`); the printed number is kept as-is on sync, even if it clashes with a live or another node's booking: both bookings are flagged (`queue_number_clash`, audit `Queue number clash`) for the desk, nothing is renumbered and no slip is reprinted.<!-- JJQR - END -->**
5. **Visual polish**: will you send screenshots (desktop vs web, same screen, same data) per screen for the 1:1 rounds? — **Recommend: yes, in Wave 4, in batches of ~8 screens per round, starting with the P0 screens (login, doctor Today, consultation, staff dashboard, payments, walk-in, queue).**
<!-- JJQR - END -->
