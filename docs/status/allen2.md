# allen2 — Sync client status

- **Percent:** 100% of assigned scope (real-API integration test still skipped: no /api/sync/* in Project02-be when last checked)
- **Last item:** Core items 5-8 done. ISyncBridge (Core, no-op default; Sync implements and replaces it): RequestSync() after saves in Patient/Queue/Payment/Consultation services; IsRevokedAsync() locks login (flag persisted in node DB). vital_reading ops queued at consultation completion (+ booking status and payment ops); audit_log swept into the outbox by Sync; PatientService.UpdateAsync queues update ops with changed_fields.
  - build Core / Sync / Tests: OK. `dotnet test --filter Sync`: 29 passed, 0 failed, 1 skipped. Full suite: 156 passed, 0 failed, 1 skipped.
- **Next item:** run SyncApiIntegration once allen1's endpoints exist (API on :5077). Staff/Admin hooks listed in the report (RequestSync after their saves).

_Updated: 2026-11-21 15:20 (Asia/Manila)_

## Update 15:20 (Manila) — runtime cloud URL, contract v1.1, real-API runs

- **Cloud URL**: `ISyncEndpoint`/`FileSyncEndpoint` (ClinicNode.Sync/SyncEndpoint.cs). Default `http://localhost:5055` (`SyncOptions.DefaultLocalBackendUrl`); precedence saved override (`<DataDirectory>/cloud-endpoint.json`) > `NodeOptions.CloudBaseUrl` if set > default. `""` = local-only. Changes apply immediately (token dropped, pull cursor reset, loop woken). App needs: inject `ISyncEndpoint`, text field bound to `BaseUrl`, call `await SetBaseUrlAsync(text)` on save (returns `SyncEndpointResult`), optional `TestConnectionAsync`, `ResetAsync`.
- **Contract v1/v1.1 alignment**: SyncDocuments.BuildAsync reused for push snapshots; REST names; child-key semantics; composite ':' ids; new reason codes; poison-batch isolation; `profile` pull aggregate added; wire op verbs outside the contract list are sent as `update`.
- **v1.1 allergies**: `IPatientService.UpdateAllergiesAsync` (Core/Patients/PatientService.cs) queues a patient `update` op with `changed_fields:["allergies"]`; snapshot carries full `allergies`; pull applies `allergies` (absent keeps, null/[] clears, NKDA never beside a real allergen); `merged/allergy_union` applied and listed.
- **v1.1 amendments**: `ConsultationService.AmendAsync` (another agent's) queues a consultation `update` op; snapshot has status Amended with the stored base_row_version. `conflict/stale_amendment` and `conflict/payment_settled` apply the cloud copy, keep the doctor's text in the review item, and ack. `duplicate`/`rejected invalid_payload` acked. A pulled cloud `Amended` copy updates a finished local consult (no pending ops).
- **Tests**: isolated Sync project (Tests/Sync/*.cs) 56 passed / 0 failed / 0 skipped, including SyncApiIntegration + SyncApiRoundTrip against :5077. The shared ClinicNode.Tests project currently does not compile because of ClinicNode.Tests/DiSmokeTests.cs (IPrinter / PrintDocument, not Sync).

## Update (Part B: queue numbers, catalogs, allergy ids, amend retry)

- **Catalogs**: `icd10_code`, `doctor_favorite_medicine`, `doctor_builtin_template_copy`, `announcement`, `doctor_day_status` added to `Aggregates.CloudOwned` + `ApplyOrder` (Sync/Local/AggregateDocuments.cs). Pull page size 1000. Runs of one cloud-owned aggregate (>=20 rows) are applied in bulk: `AggregateDocuments.ApplyBulkAsync` (one IN-query for existing rows, one SaveChanges per 500) + `SyncLocalStore.SetRowVersionsAsync` (multi-row write); a failing chunk falls back to per-change apply. A cloud row replaces a local row with the same unique key but another id (`RemoveUniqueClashesAsync`; found on :5077 with doctor_builtin_template_copy).
- **Reprint signal**: table `node_sync_reprint_flags`; `ISyncStatus.ReprintCount` (PropertyChanged), `event ReprintRequired`, `GetReprintFlagsAsync()`, `DismissReprintFlagAsync(id)`. Kind `patient_card` (patient code replaced). <!-- JJQR - AI (21 Nov 2026) --> No `queue_slip` flag is raised after a queue-number clash any more: the printed number stays valid (server `queue_number_clash`: kept, both bookings audited, no renumber); the clash shows only as the review pill "Queue number clash: flagged for admin". <!-- JJQR - END -->
- **Amend retry**: `conflict/stale_amendment` -> cloud copy applied, doctor's amendment re-applied on top, ONE automatic re-push in the same cycle with the new base_row_version (payload `retry_of` marks it); a second stale goes to the review list (text kept in LocalJson). `payment_settled` is never retried.
- **Allergies**: ids always come from the cloud's `current`/pulled list (replace-set by id); Core validation: trimmed, blank skipped, >200 chars throws, NKDA stored as 'NKDA'.
- Sync-only tests (private project /tmp/allen2-synctests): 66 passed, 0 failed, 0 skipped, incl. real-API integration + round trip against :5077. Shared ClinicNode.Tests build: still RED (errors in Staff/StaffOrSequenceTests.cs, not Sync).

## Real-DB doctor consultation + sync (Nov 21, 2026, ~16:00 Asia/Manila)
TESTED doctor+sync: 19 pass / 3 fixed / 0 open (+2 notes; step 19 was red before fixes)

- I worked on a copy at /tmp/allen2-realdb, taken from the app container at 15:43. There is a pristine copy in /tmp/allen2-realdb/pristine, and every run starts again from it. The original was never opened for writing. The harness refuses any path under Library/Containers.
- Harness: `ClinicNode.Tests/Sync/SyncRealDbDoctorFlowTests.cs`. It is skipped unless `CLINIC_REALDB_DIR` is set, and its sync part also needs the `CLINIC_SYNC_IT_*` environment variables. It uses the real Core, Data and Sync DI with InMemoryNodeCredentialStore, and it writes harness-report.md into the copy's folder.
- Flow: InitializeAsync on the copy (schema repair added no missing columns because they were already there), log in as the doctor, open the existing CheckedIn booking f2111111-…-201 by its bookingId with StartConsult, LoadByBooking, then draft, vitals, ICD-10 "j06", diagnosis, medicine search, Rx, lab names, lab order, med cert, complete, view (reload and history), and amend.
  - Every step passed.
- Sync: endpoint :5077 with the IT node credentials. Push 18 / ack 18, pull 12,694, second cycle a no-op.
- Fixes (all in ClinicNode.Sync/Local plus tests):
  1. `AggregateDocuments.ApplyPaymentAsync`: payments.booking_id is UNIQUE. A cloud payment with a different id for a shared demo booking used to give apply_failed with "UNIQUE constraint failed: payments.booking_id". Now an untouched local twin (Unpaid, nothing queued) gives way to the cloud's. Otherwise the local one is kept and flagged with `payment_booking_clash`.
  2. `AggregateDocuments.ApplyConsultationAsync`: consultations.booking_id is UNIQUE. A cloud consultation with another id for the same visit used to give apply_failed "error while saving". Now the local one is kept and flagged with `consultation_booking_clash`. Clinical data is never dropped.
  3. `SyncLocalStore.AddConflictAsync`: one open review item per aggregate/entity/kind/reason. Previously each op of a rejected consultation added its own copy, 8 identical rows.
  - `SyncReasonCodes.cs` has text for the two new codes.
  - Tests: two new tests in `SyncCorePathTests.cs`.
- Sync suite: 70/70 in the private project, including :5077 and the real-DB run. In the shared ClinicNode.Tests, Sync is 70 pass / 2 skipped and the shared build is green again.
- Notes:
  - Demo bookings use the same fixed ids on the node and in the cloud (DevDataSeeder f2111111-…), so two nodes, or a reset node, can consult the same visit. That shows up as consultation_booking_clash, finished_immutable and already_recorded (vitals) on the review list. This is an admin decision. The suggested owner is the Data seed owner: don't seed fixed booking ids on a real node.
  - The MAUI app was running (PID 18332, not started by me), and it wrote to the original DB at 15:46.

<!-- JJQR - AI (21 Nov 2026) -->
## WRAP-UP (21 Nov 2026, 18:15 Asia/Manila)

Code only today: nothing built, tested, committed or pushed after this wrap-up. No half-edited files; nothing reverted. Every edit is wrapped in JJQR (21 Nov 2026).

### Files I own / changed today
- Core: `Core/Queue/IQueueNumberSource.cs` (new, + `QueueNumberMessages.NoNumbersLeft`), `Core/Queue/QueueService.cs` (source wiring + compile fix `ClinicApp.Domain.Entities.Doctor?` ~line 111), `Core/Staff/StaffBookingService.cs`, `Core/Admin/AdminBookingService.cs` (create/reschedule use the reserved-number source).
- Sync: `Sync/Local/QueueBlocks.cs` (7-day blocks, lanes R/S, 20 per block, refill at <=5), `Sync/ServiceCollectionExtensions.cs`, `Sync/SyncService.cs` + `Sync/Local/SyncReasonCodes.cs` (online_only drop for clinic_settings/fee, announcement, staff_account, doctor_schedule; kind-specific text; queue_number_reassigned wording), `Sync/Local/AggregateDocuments.cs` (keep non-empty local QueueNumber, queue_number_clash; reprint flag removed), `Sync/Local/SyncLocalStore.cs`.
- Api: `Api/Services/ApiStaffPaymentsService.cs` (OR field flow, 401 copy, `ForgetClashingOrAsync`), `Api/Services/ApiConsultationService.cs` (vaccination PUT fields).
- App: `Shared/StandaloneVaccinationPush.cs` (new), `Shared/LiveSyncCoordinator.cs`, `Shared/SharedModule.cs`, `Views/SyncConflictsPage.cs` (KindInfo labels).
- App staff views (`ClinicNode.App/Views/Staff/`): `StaffUi.cs`, `RecordPaymentPage.cs`, `StaffLivePage.cs` (opt-in `LoadErrorState` / `ErrorStateView` / `OnLoadStateChanged` / `StaleLoadWarning`), `StaffHomePage.cs`, `StaffQueuePage.cs`, `StaffPaymentsPage.cs`, `StaffPatientsPage.cs`, `StaffBookingDetailPage.cs`, `StaffDoctorStatusPage.cs`, `StaffAnnouncementsPage.cs`, `StaffProfilePage.cs`.
- Docs: `docs/ONLINE-FIRST.md` §10, this file.

### Done
- Offline queue numbers from 7-day reserved blocks for walk-in, staff and admin bookings; offline with none left = fail with the reserved-numbers message, nothing saved.
- Sync review items: OR clash, queue number clash/reassigned, online_only drops, vaccination codes.
- Money path: typed-OR retry (online only), rejected OR cleared only on an Unpaid local row with the same OR.
- Standalone vaccine push: 200/201/409 id_conflict = done; 400/401/403/404/other 409 parked with a review item; 5xx retry; notes >2000 kept local.
- Staff 1:1 with web: Dashboard, Queue (error card + "Couldn't refresh just now. Showing the last loaded queue."), Payments, Patients (one-row pager, disabled bounds), Booking detail (404 copy, removed "Open patient"), Doctor status, Announcements, Profile (dirty-check Save changes). Kept on purpose: Pending-only check-in, reprint panel, ready-to-pay card.

### Half-done
- None. `StaffProfilePage.cs` edit was checked complete after an interrupted command.

### Next
- Compile pass (allen0 doing one build); fix any errors in the files above.
- Staff Patient detail page (`/staff/patients/[id]`) was not in the walk.
- Skeletons are still spinners/blank (allowed).
- `AdminOrNumbers.cs:91` Unpaid guard is with allen4.
<!-- JJQR - END -->

<!-- JJQR - AI (21 Nov 2026) -->
## RESUME batch (21 Nov 2026, 18:25 Asia/Manila)

Code only: not built, tested, committed or pushed. No half-edited files.

### Done
- **Staff patient detail 1:1** (`ClinicNode.App/Views/Staff/StaffPatientDetailPage.cs` vs `Project02-fe/src/app/staff/patients/[id]/page.tsx`): error card "This patient didn’t load" (first load only; failed refresh silent); missing patient = "404 / This page could not be found."; tabs + content in one card; Overview = contact, email, divider, "Portal account" row with pill + "Create portal account" (now the web's inline not-wired note with Close, not a popup alert); whole booking row opens the booking; web short/long date formats. No desktop-only controls removed.
- **SignalR bump** (`ClinicNode.Api/ClinicNode.Api.csproj`): Microsoft.AspNetCore.SignalR.Client 8.0.12 -> 10.0.11 to match Project02-be (net10.0, ASP.NET Core 10.0.11). Only csproj referencing SignalR. SignalR.Common is transitive (now 10.0.11); no direct reference added. No code changes. No API break expected in `ClinicNode.Api/Live/ClinicHubClient.cs` (all members used are unchanged in 10.x).
- **Patient card printout vs web:** the web has none (web print = med cert, prescription, lab request only). Desktop has `PatientCardDocument` in `ClinicNode.Core/Printing/DoctorPrintDocuments.cs` (doctor-owned); left untouched.

### Open / next
- allen0 compile of `StaffPatientDetailPage.cs` and the Api csproj.
- First restore of SignalR.Client 10.0.11 needs nuget.org (local cache only has 8.0.8 / 8.0.12).
- Decision (allen0/Jessie): keep or remove the desktop-only doctor patient-card printout under "if the web doesn't have it, it stays out".
- Small leftover: web hides empty sex / "Born" in the header; desktop always has both, so always shows.
<!-- JJQR - END -->
