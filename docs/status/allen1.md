<!-- JJQR - AI (21 Nov 2026) -->
<!-- JJQR - AI (21 Nov 2026) -->
## DOCTOR PARTIALS batch (21 Nov 2026, 1:1 against the web .tsx) - written, NOT compiled by me
**Files I own/changed:** `ClinicNode.App/Views/Doctor/Extra/DoctorPatientChartPage.cs` (tabs only; header untouched), `Extra/DoctorSchedulePage.cs`, `Extra2/DoctorTemplateEditorPage.cs` (namespace -> `ClinicNode.App.Views.Doctor.Extra2`, plus `using ...Doctor.Extra;` for DoctorExtraUi/DoctorRxFields). Backups of the two edited pages: /tmp/*.bak on the Mac.
**Web sources:** `Project02-fe/src/app/doctor/patients/[id]/page.tsx`, `Project02-fe/src/app/doctor/schedule/page.tsx`.
**Done:** Chart: Timeline = bookings only; Appointments = Date/Service/Status/Queue # table; Consultations = tap to expand + diagnosis chips + SOAP / "No SOAP notes recorded for this visit."; Vitals = List/Table toggle, sort select, "Add Vitals" when opened from a booking, default vitals only; Rx sort select; Labs/Documents title only; Vaccinations table; chart load-error card. Removed: global sort buttons, "View appointment" link, merged timeline events, upload dates.
Schedule: own "Daily patient limit" card, Save button below the cards, "–" separator, end>start validation message, "{date} — {reason}" rows, modal with no labels / unpicked date / Block disabled until the booking check succeeds, load-error text + retry.
**Left (reason):** Daily patient limit stays read-only (web edits it, but the API is Admin-only; backend change not allowed in this batch). Vitals list shows the vital name instead of the web icon (ChartVitalTemplate has no icon field). Prescription card keeps text buttons instead of icons.
**Half-done:** none. **Next:** fix compile errors allen0 reports.
**Registration lines for allen0:** none new (DoctorTemplateEditorPage is only constructed from DoctorTemplatesWorkspacePage, same namespace now; no DI/route entry).
<!-- JJQR - END -->

# WRAP-UP allen1 (Jessie's stop order, 21 Nov 2026) - DONE allen1
No file was left half-edited: I checked every Extra2 file, the chart page and the new sync file (braces/parentheses balance); nothing needed finishing or reverting. Nothing committed or pushed. Nothing here was compiled or tested by me (allen0 is compiling).

## Files I own
- Backend: `Project02-be/src/ClinicApp.Api/Controllers/{BookingsController,VaccinationsController,DoctorsController,StaffAccountsController,PatientsController,PaymentsController,SyncController}.cs`; `Project02-be/src/ClinicApp.Infrastructure/Sync/{SyncPushEngine.cs,SyncPushEngine.DayStatus.cs,SyncAggregates.cs,SyncContracts.cs,SyncPullService.cs,SyncBootstrap.cs,SyncDocuments.cs}`; `QueueSequencer.cs`; `Project02-be/deploy/*.sql` (not run on any hosted DB).
- Desktop Extra2: `ClinicNode.App/Doctor/DoctorExtra2Module.cs`; `ClinicNode.App/Views/Doctor/Extra2/{DoctorTemplatesWorkspacePage,DoctorTemplateEditorPage,DoctorPerformancePage,DoctorPatientHeaderCard}.cs`; `ClinicNode.Core/DoctorExtra2/{DoctorTemplateRules,DoctorTemplatesWorkspace,DoctorTemplateLinks,DoctorPatientCardRules}.cs`; `ClinicNode.Api/Services/Extra2/ApiDoctorTemplatesWorkspace.cs`. Card-only edit in `ClinicNode.App/Views/Doctor/Extra/DoctorPatientChartPage.cs` (header block in LoadAsync).
- Docs: `docs/API-SURFACE.md`, `docs/SYNC-API-CONTRACT.md`, `docs/status/allen1.md`, DAY0-PLAN.md item (c) only.

## What's done (written; not compiled by me)
- P9 `visit_type` / `discount_category` on `POST /api/bookings` and `/api/bookings/walk-in` (walk-in calls the same Create).
- P12 vaccine fields (`reaction_notes` max 2000, `administered_date`, `status`) on the by-consultation PUT and on `POST /api/patient-vaccinations`; that route documented: 200 not 201, client `id`, 409 `id_conflict`, `administered_date` defaults to today when Administered.
- `queue_number_clash`: printed number kept, both bookings flagged (audit `Queue number clash`), no renumber, no reprint; a booking pushed with no number still gets a cloud number (`merged` + `queue_number_reassigned`).
- G5 `doctor_day_status` pushable (Admin / Staff / that doctor, newest edit wins per field, deletes rejected).
- Extra2: Templates workspace + editor (search, tabs, List/Cards, deep links `doctor-templates?tab=&field=&id=`, highlight, scroll, two-column cards), Performance, `DoctorExtra2Module`. Wiring (allen0 only): `services.AddDoctorExtra2Module();` after `AddDoctorExtraModule()`, and `DoctorExtra2Module.RegisterDoctorExtra2Routes();` for routes `doctor-templates` and `doctor-performance`.
- Patient chart header 1:1 with the web; the printed `PatientCardDocument` left as-is.
- Docs pass on API-SURFACE.md, SYNC-API-CONTRACT.md, DAY0-PLAN.md item (c); desktop-only review codes (`online_only`, `reaction_notes_too_long`, `vaccination_rejected`, `local_table_missing`) listed in API-SURFACE as NOT API reason codes.
- CS0246 fix: `using ClinicNode.App.Views.Doctor.Extra;` in Extra2/DoctorTemplatesWorkspacePage.cs (line 6), DoctorPerformancePage.cs (line 10), DoctorPatientHeaderCard.cs (line 5).
- G3 / G4 (Jessie, verbatim: "Staff invites and revokes: online-only."): invites, revokes and password-changed are online-only, no replay, no backend change.

## What's half-done / open
- Extra2 has not been compiled by me; remaining compile errors are unknown (allen0 is compiling).
- Templates scroll (`DoctorTemplatesWorkspacePage.cs`, `ScrollToTargetAsync`, `_scrollTarget ??=`): targets the first match only and only on a deep-link load.
- API-SURFACE.md roles check was a script plus spot checks, not a full manual pass.
- Old SYNC-API-CONTRACT.md §5.2 renumber text (queue numbers, around line 248-249) is marked obsolete but is still in the file.
- Other G3/G4 edits (staff name/contact/status, own-profile) have no Jessie decision yet.
- Hosted OR unique-index migration (`deploy/migrate-or-number-unique.sql`) is NOT deployed; do not run it unless Jessie says so.
- G2 announcements: do not add a client id unless Jessie asks.
- Vaccination auth stays Doctor / Staff / Admin only (node token gets 403).

## What I'd do next
Fix any compile errors allen0 reports in the Extra2 files above; then wait. No new features.
<!-- JJQR - END -->

<!-- JJQR - AI (21 Nov 2026) -->
# allen1 status (backend / API)

## Item 1 - cloud-sync SQL: DONE
`Project02-be/deploy/migrate-cloud-sync.sql` now covers CloudSync + SyncSelfEditStamps (creates `sync_field_stamps`, PK aggregate+entity_id+field; guarded by `__EFMigrationsHistory`; preview lists the table and both migration states; preview-first `@apply = 0` header kept). Not run anywhere.

## Item 2 - API surface: DONE
`docs/API-SURFACE.md`. Every endpoint the web doctor/staff/admin pages call exists in the backend (0 gaps).

## Item 3 - PROPOSALS (nothing implemented; awaiting owner approval)

Already exist (no work needed): doctor schedule/blocked dates (`PUT /api/doctors/{id}/schedules`, blocked-dates are Admin,Doctor); sync-node admin (`POST/GET /api/sync/nodes`, `rotate-secret`, `revoke`, Admin).

### P1. Doctor edits own profile - `PUT /api/doctors/me`
- Roles: Doctor (resolves the doctor row from the JWT user, never from the body).
- Payload: `{ bio, consultationFee?, slotDurationMinutes?, slotCapacity? }` - whitelist: bio only by default; fee/slot/limits/license/PTR/S2 stay Admin-only (owner to confirm which extra fields).
- Today `PUT /api/doctors/{id}` is Admin-only. Reuses the same `Rules.Doctor` validation. Stamps sync_field_stamps like other self-edits.

### P2. Doctor edits own display name/avatar - `PUT /api/staff-accounts/me`
- Roles: any signed-in staff/doctor (self only).
- Payload: `{ fullName, phone?, avatarUrl? }`.
- Needed because `GET /api/staff-accounts/me` exists but the web only edits via `PUT /api/staff-accounts/{id}` (self-or-Admin rule in code); a `/me` form avoids needing the id offline. Optional if `{id}` is acceptable to the desktop.

### P3. Patient-card print data - `GET /api/patients/{id}/card`
- Roles: Admin, Staff, Doctor.
- Response: `{ patientId, patientCode, fullName, birthDate, sex, bloodType?, allergies:[string], emergencyContact:{name,phone}?, clinicName, clinicPhone, issuedAt }` - read-only, minimal fields, no clinical notes. Writes an audit-log access row (same as patient detail reads).
- Today the desktop would need to stitch `/api/patients/{id}` + `/allergies` + `/api/settings`.

### P4. Sync status for the desktop's own node - `GET /api/sync/status`
- Roles: sync-node token (SyncNodeAuth.Role), same auth as handshake/pull.
- Response: `{ nodeId, status, serverTime, lastPushAt, lastPullAt, latestSeq, pendingConflicts? }` so the desktop can show "last synced" without parsing handshake.
- Note: `handshake` may already return most of this; will check before coding and drop P4 if redundant.

### P5. Sync-node admin list with activity - extend `GET /api/sync/nodes` (Admin)
- Add fields `lastSeenAt`, `lastPushAt`, `operationCount24h`, `rejectedCount24h` (no secret hashes). Purely additive to the existing response; no new route.

### Not proposed
No new endpoints for queue, bookings, payments, consultations: the web contract already covers them.
<!-- JJQR - END -->

<!-- JJQR - AI (21 Nov 2026) -->
## Item 4 - BUILT vs SKIPPED (owner approved 21 Nov 2026, 5:03 PM Manila)
Code only: not built, not tested, not run, not committed.
- P1 BUILT `PUT /api/doctors/me` (Doctor; body `{bio}` only; Rules.Doctor reused on a copy). DoctorsController.cs.
- P2 BUILT `PUT /api/staff-accounts/me` (self only; body `{full_name, phone?, avatar_url?}`; Rules.Staff reused; role/status/email untouched). StaffAccountsController.cs.
- P3 BUILT `GET /api/patients/{id}/card` (Admin, Staff, Doctor; writes an access-log row "Patient card"). PatientsController.cs.
- P5 BUILT extension of `GET /api/sync/nodes` (Admin): + `last_push_at`, `operation_count_24h`, `rejected_count_24h`. SyncController.cs.
- P4 SKIPPED (`GET /api/sync/status`) per owner.
- P2 note: JSON keys follow the API's snake_case convention (`full_name`, `avatar_url`); `phone` maps to `contact_number`.
<!-- JJQR - END -->

<!-- JJQR - AI (21 Nov 2026) -->
# Read-only review (21 Nov 2026): sync pull coverage + OR numbers
Code read only. Nothing changed in the backend, no builds/tests/DB commands. Proposals below need Jessie's approval before any code.

## TASK A - Does `GET /api/sync/pull` cover what the desktop reads?

How the feed works (Infrastructure/Sync): `SyncChangeRecorder` writes one `sync_changes` row per changed aggregate inside every SaveChanges (rows are mapped by `SyncAggregates.Resolve`). Pull pages by `seq` (default 500, max 1000), holds back changes younger than a 2 s settle window, builds the full JSON document per aggregate (`SyncDocuments`), and sends `delete` when the row is gone. First pull (`since=0`) is seeded by `SyncBootstrap` for patient, booking, payment, consultation, vital_reading and every pull-only aggregate.

| Area | In pull feed? | Aggregate / how |
|---|---|---|
| Bookings / queue | YES | `booking` document = booking row (incl. queue_number, lane, status, pf fields) + `booking_services`. Queue board = bookings; **`queue_counters` is NOT synced** (see P6c) |
| Payments (incl. waived, confirm-waiver) | YES | `payment` row carries status, waived_by/reason/at, confirmed_by/at, or_number, refund fields. "Waived awaiting confirmation" is derived (booking Completed + payment Waived + confirmed_at null), so the desktop can compute it locally |
| Patients, allergies, emergency contacts | YES | `patient` document: patient row (emergency_contact_*, blood type, guardian, HMO...) + `allergies` replace-set |
| Doctors, schedules, blocked dates, day status, services | YES | `doctor`, `doctor_schedule`, `doctor_blocked_date`, `doctor_day_status`, `doctor_service`, `service` (cloud-owned; doctor/schedule/blocked-date also accept pushed self-edits) |
| Staff accounts (offline login) | YES | `staff_account`, plus `user` (staff users only) and `profile` (non-patient only) |
| Announcements | YES | `announcement` |
| Settings and fees | YES | `clinic_setting` (fee columns live on the row), `clinic_operating_hour`, `clinic_accepted_payment_method` |
| Templates, phrases, cert templates, favorites | YES | `soap_template`, `soap_phrase`, `medical_certificate_template`, `doctor_diagnosis_template`, `prescription_template` (+ items), `doctor_favorite_medicine`, `doctor_builtin_template_copy` |
| Medicines, labs, ICD-10, vitals fields | YES | `medicine`, `lab_test_catalog`, `icd10_code`, `vital_field_template` |
| Prescriptions, labs, diagnoses, follow-up, certificate, vaccinations | YES (inside consultation) | `consultation` document = consultation + diagnoses + prescription group + line items + lab_orders + vaccinations (those with a consultation) + follow_up + medical_certificate |
| Vitals | YES | `vital_reading` |
| Audit logs | PARTLY | `audit_log` aggregate is pulled, but `SyncBootstrap` does NOT seed existing audit rows, so the first pull only gets audit rows written after the change log started |
| Data requests (privacy) | NO | `DataRequest` / `PatientConsent` are not in `Resolve` or `PullOnly` |
| Sync nodes | NO (by design) | `sync_nodes` / `sync_operations` / `sync_changes` / `sync_field_stamps` are excluded; sync-node admin is online-only via `/api/sync/nodes` |
| Reviews | NO | `Review` not synced |
| Patient documents / lab-result uploads | NO | `PatientDocument`, `PatientLabResult` (metadata and files) not synced |
| Patient lab values | NO | `PatientLabValue` (used by `GET /api/patients/{id}/lab-values`) not synced |
| Standalone vaccinations | NO | `PatientVaccination` with no consultation_id resolves to null (dropped); only consultation-linked ones travel |

### P6. Fill the pull-feed gaps the desktop can hit
- **P6a - patient_lab_value, review, patient_document, patient_lab_result** (not synced): add to `SyncAggregates` (new pull-only aggregates, or inside the `patient` document as a replace-set like allergies for lab values) and to `SyncBootstrap` seeding. Document/lab-result files are large: sync metadata only and keep file download online via `/api/patient-documents/{id}/file` (fix: add a `has_file`/`file_url` field and let desktop show "online only"). Risk: low, additive. Effort: small for lab values and reviews, medium for files.
- **P6b - standalone `patient_vaccination`** (dropped when consultation_id is null): resolve it to the `patient` aggregate (add `vaccinations` to the patient document) instead of returning null. Risk: low.
- **P6c - `queue_counters`**: not synced, so each node numbers its own queue and the cloud's `queue_number` can collide with node numbers for the same day/lane. Fix: either pull it as a pull-only aggregate (read-only reference) AND make the node prefix queue numbers per node when offline, or keep it as is and accept per-node numbering. Needs a product decision from Jessie.
- **P6d - audit_log first pull**: add `audit_log` to the `SyncBootstrap` seed (or a `since` cutoff of N days) so a new node sees history; today it sees only new rows. Tradeoff: large first pull. Decision: seed last 90 days? (Jessie to choose.)
- **P6e - data requests / consents (privacy)**: if the desktop admin screen must show/resolve data requests offline, add `data_request` and `patient_consent` as pull-only aggregates; resolving stays online (Admin). If online-only is fine, no change (current behaviour).
- **Note (no change proposed):** `user` rows for staff are sent to nodes for offline login. Confirm that is intended and that only the hashed login material needed for offline sign-in is included (not reviewed field-by-field).

## TASK B - OR numbers

### Current behaviour (server)
- `PaymentsController.Confirm` stores `or_number` exactly as the client sends it (optional, max 100 chars). The server never assigns one, has no counter, and there is no unique index (`payments.or_number` is `nvarchar(max)` and nullable).
- Sync push (`SyncPushEngine.PaymentAsync`) copies `or_number` from the node's payment row like any other field. No duplicate check, no reason code for it. Two nodes pushing the same OR both succeed.
- Web: generates `OR-<first 8 hex of booking id>` per booking (unique in practice, not guaranteed).
- Desktop: `OR-<4 hex of local node_id>-<yyyymmdd>-<seq>`, next sequence from that computer's own payments. A unique filtered index exists only in the local SQLite (`ix_payments_or_number`). The online path (`ApiStaffPaymentsService`) also numbers from local rows only.

### Risks
1. Duplicates across computers: prefix is only 4 hex chars of the node id (collision about 1 in 65,536 per pair), and a cloned/imaged installation shares a node id, so duplicates are certain there.
2. Local counter only sees payments already synced to that computer, so an online computer can reuse a number another computer already used but has not pushed.
3. Cloud accepts any duplicate silently, so reports/receipts can show the same OR twice; nothing flags it.
4. Web `OR-<booking8>` and desktop formats mix in one column with no format rule.

### P7. Server-owned OR numbers (proposal)
- **P7a - unique index (migration, additive):** filtered unique index `ix_payments_or_number` on `payments(or_number) WHERE or_number IS NOT NULL`. Run a duplicate check first (preview query in the SQL script, same preview-first style); if duplicates exist, report them rather than auto-fix.
- **P7b - server allocator:** new table `or_counters(prefix, date, value)` (or one row per day) and a new endpoint `POST /api/payments/{id}/or-number` (Admin, Staff; idempotent: returns the existing OR if the payment has one). `Confirm` assigns one automatically when `or_number` is blank. Format stays `OR-<prefix>-<yyyymmdd>-<seq4>`, prefix `C` for cloud.
- **P7c - server-issued node prefix for offline use:** add `or_prefix` (unique, 2-4 chars) to `sync_nodes`, assigned at registration and returned by `POST /api/sync/token` / `GET /api/sync/handshake`; the desktop uses it instead of hashing its node id. That makes offline numbers unique by construction (node prefix + day + local seq). Desktop change needed (not in my scope).
- **P7d - push collision rule:** in `PaymentAsync`, if the incoming `or_number` already belongs to another payment, return `conflict` with new reason `or_number_duplicate` (and the existing payment id) instead of saving, so the node can re-issue/flag. (Alternative: server rewrites to a fresh cloud OR and returns it in the result; needs desktop to accept a changed value.)
- Recommended order: P7a + P7d first (cheap, stops silent duplicates), then P7c, then P7b.
- Decisions for Jessie: keep the old `OR-<booking8>` rows as is (assumed yes); preferred collision rule (conflict vs server rewrite).

Items this review could not settle: whether the desktop screens actually read reviews / data requests / patient documents (grep shows references in admin patient services only); I did not trace each ViewModel.
<!-- JJQR - END -->

<!-- JJQR - AI (21 Nov 2026) -->
# 21 Nov 2026 - owner decisions on P6/P7

## PART A - BUILT (code only, nothing compiled/run/committed; NO schema change)
- **A3 audit logs, 30 days:** `SyncPullService` drops `audit_log` changes whose `audit_logs.performed_at` is older than 30 days (`AuditPullDays = 30`); the cursor is taken before filtering so it still advances. `SyncBootstrap.BackfillAsync` now seeds the last 30 days of audit rows (older history is not seeded; the cloud keeps everything). Nodes still push their own audit entries (push path untouched).
- **A4 consents:** `patient_consent` added to `SyncAggregates.PullOnly` (seeded + pulled; row = id, patient_id, consent_version, decision, decided_at). Data requests NOT synced.
- **A5 lab values / lab results / documents:** `patient_lab_value`, `patient_lab_result`, `patient_document` added as pull-only aggregates (metadata rows only; files stay online). Reviews NOT synced.
- **A5 standalone vaccinations:** `SyncAggregates.Resolve` now maps a vaccination with no consultation to the `patient` aggregate, and `SyncDocuments` adds `vaccinations` (replace-set) to the patient document.
- Files: `Project02-be/src/ClinicApp.Infrastructure/Sync/SyncAggregates.cs`, `SyncDocuments.cs`, `SyncPullService.cs`, `SyncBootstrap.cs`; `docs/API-SURFACE.md` (pull content note).
- **Caveats (nothing stopped for schema):**
  1. These four new aggregates are pull-only: a consent captured on a node (registration offline) will be rejected `cloud_owned` if pushed. Making consents pushable needs a push handler (code only, no schema) - say if you want it (suggest yes; Data Privacy Act proof is created at registration).
  2. Nodes that already pulled patients will get `vaccinations`, consents, lab data only when those rows next change, or after a re-seed (the bootstrap seeds new aggregates once at API start, so a node pulling from cursor 0 gets everything).
  3. A node offline for more than 30 days will not receive audit rows older than 30 days.
  4. Pull-only rows use `row_version` = change seq (no ISyncTracked columns, because adding them would be a schema change).

## PART B - PLANS (NO CODE written; nothing here is built)

### P7 (final). OR number duplicates - "reject online, accept + flag from sync, never lose a payment"
**Today:** `PaymentsController.Confirm` saves the client's `or_number` as sent (<=100 chars); no uniqueness, no counter. `SyncPushEngine.PaymentAsync` copies `or_number` with the other fields. `payments.or_number` is `nvarchar(max)`.

**Rule per path**
1. **Online API (web/app) confirm/record** - `PaymentsController.Confirm` (and `Waive` n/a): before saving, normalise the OR (trim, upper) and check `db.Payments.AnyAsync(p => p.PaymentId != id && p.OrNumber == or)`. If taken: **HTTP 409**, nothing saved, body:
   `{ "error": "or_number_duplicate", "message": "OR number <x> is already used. Enter a different OR number.", "or_number": "<x>" }` (no patient/payment details leaked; matches the existing `Conflict(new { message })` style plus an `error` code). Also catch a `DbUpdateException` on the unique index (race) and return the same 409. Web `newOrNumber()` is `OR-<booking8>` so it will not clash in practice.
2. **Sync push from a desktop** - `SyncPushEngine.PaymentAsync`: ACCEPT and save as now (OR stays exactly as printed, never changed/dropped). After detecting that another payment already holds the same OR: (a) add an `AuditLog` row (`EntityType.Payment`, `EntityId` = the new payment, action `"OR number clash"`, details `"OR <x> also used by payment <id> (origin cloud|node <id>)"` - ids only), (b) return status `applied` (or `merged`) with `reason_code = "or_number_clash"` and a message so the node can show "flagged for admin review". Do the same check against the *other* payment so the first payment is also marked (second audit row on it).
3. **Index vs accepted duplicates:** a strict unique index on `or_number` would make step 2 fail (the payment would be lost). So the index (when approved) must exclude node-written rows: **filtered unique index on `payments(or_number) WHERE or_number IS NOT NULL AND origin_node_id IS NULL`** (cloud/online-written payments only). That needs no new column; node-vs-anything clashes are handled in code (step 2), online-vs-anything in code (step 1) plus the index for races. (Alternative: a boolean `or_clash` column + index `WHERE or_clash = 0`; cleaner but adds a column.)
4. **Clash flag storage:** use the existing `audit_logs` table (no schema change). A dedicated `or_clashes` table / `payments.or_clash` column would be a schema change - NOT proposed unless Jessie wants a resolvable workflow (mark reviewed).
5. **Admin review surface:** (a) no code: Admin audit-log page filtered by action `"OR number clash"` (`GET /api/audit-logs/search` already exists); (b) small read-only endpoint `GET /api/payments/or-clashes` (Admin) that returns payments whose `or_number` is shared by >1 payment (GROUP BY query, no schema) with `{or_number, payments:[{payment_id, booking_id, status, confirmed_at, origin}]}`. The admin fixes by correcting the OR on the online side (a future `PUT /api/payments/{id}/or-number`, Admin, writes an audit row) - not required for v1.
6. **Schema changes needed (migration NOT written, needs Jessie's OK):** (i) alter `payments.or_number` from `nvarchar(max)` to `nvarchar(100)` (an index key cannot be nvarchar(max); online already caps at 100, sync push must also cap at 100 or reject - check `or_numbers_too_long` in the script); (ii) the filtered unique index above. Before that: run `Project02-be/deploy/check-or-duplicates.sql` (read-only, written, NOT run) and clean any duplicates it lists by hand.
7. **Migration outline (unwritten):** `AlterColumn or_number nvarchar(100) null`; `CreateIndex ix_payments_or_number_cloud unique filter "[or_number] IS NOT NULL AND [origin_node_id] IS NULL"`; plus a hand-written preview-first SQL script in the style of `migrate-cloud-sync.sql` with a guard that aborts if duplicates exist.
8. **Files/methods to change (when approved):** `ClinicApp.Api/Controllers/PaymentsController.cs` (`Confirm`), `ClinicApp.Infrastructure/Sync/SyncPushEngine.cs` (`PaymentAsync`; new reason `or_number_clash` in `SyncContracts.cs` `SyncReasons`), `PaymentsController.cs` (new `GET or-clashes`), `ClinicAppDbContext.cs` + migration (only after OK), docs.
9. **Desktop (not my scope):** show `or_number_clash` as "kept as printed, flagged for admin" and stop treating it as an error; also adopt a server-issued node prefix later (P7c).

### P8. Queue numbers - PICK: reserved blocks (with the existing renumber-on-sync kept as the safety net)
**Why blocks:** a printed queue slip should not change after the patient holds it. Today the server already reconciles on push (`SyncPushEngine.ReconcileQueueNumberAsync`: keeps the node's number, raises the cloud counter, renumbers a cloud-created unserved booking, or reassigns the node's booking with `queue_number_reassigned` - the printed slip then changes). Blocks make that clash impossible in normal use; the renumber code stays for exhausted/unreserved blocks. The server side is small, so blocks are not too complex.
**Server plan (no schema change):**
- New endpoint `POST /api/sync/queue-blocks` (node token). Body `{ "date": "yyyy-MM-dd", "lane": "R"|"S", "size": 20 }` (size clamped 5-50). Response `{ "date", "lane", "from": 41, "to": 60, "format": "R-041" }`.
- Implementation reuses the single per-day counter: atomically raise `queue_counters.Value` for (date, lane) by `size` using the same optimistic loop as `QueueSequencer.NextAsync` (a new `QueueSequencer.ReserveBlockAsync`), returning `(old+1 .. old+size)`. Online check-ins keep using the same counter, so they can never hit a reserved range. Record the reservation as an `audit_logs` row (action `"Queue block reserved"`, details `node, date, lane, from-to`) for traceability (no new table). Unused numbers simply leave a gap (cosmetic; blocks are only valid for that day).
- `GET /api/sync/handshake` could also return a hint `queue_block_size: 20` (additive).
- Schema: NONE needed. (A `queue_blocks` table would only be needed to resume/expire reservations or to audit by query; not required.)
**Desktop changes needed (not my scope):** when online, top up blocks for today and tomorrow for both lanes (when fewer than ~5 numbers left); offline issue only from the local block (lane R / S); if a block is exhausted, fall back to the current local behaviour and rely on the server's reconcile; stop any per-computer numbering; keep `queue_counters` locally as the block cursor (it is not a synced table today).
**Fallback if blocks are not wanted:** keep today's behaviour (renumber on sync, already built); downside: the printed slip can change after sync.
**Needs Jessie's OK before I build:** (1) the block approach and size 20 per lane; (2) no schema change (audit-log trace only); (3) desktop team to implement the block client.

## Decisions / OKs needed from Jessie
1. P7: OK to build the code-only parts now (409 on online confirm, sync accept+flag via audit log, `GET /api/payments/or-clashes`)? Index + column-size migration only after you approve the plan and the duplicate script shows clean data.
2. P7: filtered unique index on cloud-written rows only (recommended) vs an `or_clash` column (needs a second schema change).
3. P8: approve reserved blocks (size 20).
4. Part A: should consents also be pushable from nodes (code only)?
<!-- JJQR - END -->

<!-- JJQR - AI (21 Nov 2026) -->
# 21 Nov 2026 (5:25 PM) - BUILT from owner approval (code only; not compiled/run; NO schema change, NO migration)
- **P7 online:** `PaymentsController.Confirm` trims the OR and returns 409 `{error:"or_number_duplicate", message, or_number}` for a case-insensitive duplicate, saving nothing. (No unique index exists yet: concurrent races are only narrowed, not eliminated, until the held migration is approved.)
- **P7 sync:** `SyncPushEngine.PaymentAsync` always accepts; new `FlagOrClashAsync` writes an "OR number clash" audit row on BOTH payments (ids only; no duplicates on re-push) and the result carries `reason_code = or_number_clash`. Never drops or renumbers. New reason in `SyncContracts.cs`.
- **P7 admin:** `GET /api/payments/or-clashes` (Admin, read-only).
- **P8:** `POST /api/sync/queue-blocks` (node token) + `QueueSequencer.ReserveBlockAsync` (same optimistic loop as `NextAsync`, raises the single per-day counter by size) + audit row "Queue block reserved". The renumber-on-push reconcile is untouched.
- **D consents:** `patient_consent` added to `Pushable`; new `PatientConsentAsync` (append-only, duplicate-safe). Still pulled via PullOnly. Data requests stay online-only.
- **HELD (not done, per Jessie):** unique-index migration and `or_number` column type change.
- Files: `Project02-be/src/ClinicApp.Api/Controllers/PaymentsController.cs`, `SyncController.cs`; `ClinicApp.Infrastructure/QueueSequencer.cs`; `ClinicApp.Infrastructure/Sync/SyncAggregates.cs`, `SyncContracts.cs`, `SyncPushEngine.cs`; `docs/API-SURFACE.md`.
- **Desktop changes still needed:** (1) treat `or_number_clash` as success + show "flagged for admin"; online confirm must handle 409 `or_number_duplicate` (ask for another OR; nothing printed yet); (2) call `POST /api/sync/queue-blocks` while online (today + tomorrow, lanes R and S, refill when < ~5 left) and issue offline queue numbers only from the reserved block; drop per-computer numbering; (3) push `patient_consent` operations (id = consent id) for consents captured offline; (4) optionally show the OR-clash list in the admin screen via `GET /api/payments/or-clashes`.
<!-- JJQR - END -->

<!-- JJQR - AI (21 Nov 2026) -->
# 21 Nov 2026 (5:30 PM) - Item B APPROVED and done (LOCAL dev DB only)
- **Local DB:** Docker SQL Server `clinic-mssql` on localhost:1433 (DB ClinicAppDb); credentials taken from the container env / `.env`, never printed or written. The `PHL8DPROG004\SQLEXPRESS` host in appsettings.json was NOT used.
- **Duplicate check** (`deploy/check-or-duplicates.sql`, SELECT-only, run locally): 160 payments, 54 with an OR; **0 exact duplicates, 0 case/space duplicates, 0 over 100 chars, 0 blank**. So **no data fixes were needed** and no payment/audit row was changed. (The script's set 2 is now dynamic so it also runs before CloudSync is applied.)
- **Migration:** `20261121092808_OrNumberUniqueCloud` (`Project02-be/src/ClinicApp.Infrastructure/Migrations/`): `or_number` -> nvarchar(100); filtered UNIQUE index `ix_payments_or_number_cloud` on `or_number` WHERE `or_number IS NOT NULL AND origin_node_id IS NULL` (online payments only). Case-insensitive via the column collation (local: SQL_Latin1_General_CP1_CI_AS); the app checks case-insensitively too. DbContext updated (`HasMaxLength(100)`, filtered unique index).
- **Applied to local dev DB:** yes, with `dotnet-ef database update`. It also applied the 4 migrations that were still pending locally (ReportsUnpaidIgnoresDeadBookings, BackfillOldWaiversConfirmed, CloudSync, SyncSelfEditStamps) then this one. Verified: index exists (unique, filtered), column length 100, last history row = this migration. The `dotnet ef` build succeeded, so the code written so far compiles (one extra `dotnet build` of the API project was also run by mistake and succeeded; no tests, server not started, :5055/:3000 untouched).
- **Race:** `Confirm` catches the unique-index violation and returns the same 409 `or_number_duplicate`.
- **Deploy script:** `Project02-be/deploy/migrate-or-number-unique.sql` (preview-first `@apply = 0`, idempotent, one transaction, refuses to apply while duplicates / over-100 values / missing `origin_node_id` exist; needs `migrate-cloud-sync.sql` first). Previewed against the local DB only (shows "already applied"); never run against a hosted DB.
- **Note for sync push:** the OR from a node is stored as sent; a node OR longer than 100 chars would now fail the column limit (DB error -> operation rejected by the engine's DbUpdateException path). Desktop OR format is far below 100. A cap check in `PaymentAsync` is a small follow-up if wanted.
<!-- JJQR - END -->

<!-- JJQR - AI (21 Nov 2026) -->
# 21 Nov 2026 (5:40 PM) - waive OR number + proposals P9-P12

## Item 1 - `POST /api/payments/{id}/waive` OR number: ADDED (code only, no schema change)
It did NOT accept `or_number` before (`WaivePaymentRequest(Reason)` only, so it was dropped). Now `WaivePaymentRequest(Reason, OrNumber = null)`: trimmed, saved on `Payment.OrNumber`, > 100 chars -> 400, duplicate (case-insensitive, other payment) -> 409 `{error:"or_number_duplicate", message, or_number}` with nothing saved, and the unique-index race maps to the same 409. File: `PaymentsController.cs`. Documented in `API-SURFACE.md` (consolidated section).

## Proposals (NOT implemented until confirmed)

### P9. Booking create: discount + visit type (desktop-reported gap a)
- **Route:** `POST /api/bookings` and `POST /api/bookings/walk-in` (Admin, Staff). `CreateBookingRequest` has no `visit_type` or `discount_category`, so those bookings are created locally by the desktop.
- **Missing:** the server always creates `VisitType.New` and no discount (the fee is computed by the existing fee code, see BookingsController ~line 550).
- **Fix:** add optional `visit_type` (New | FollowUp | ...; default New) and `discount_category` ("Senior" | "PWD" | null) to the request; validate the discount against the patient's senior/PWD id fields (same rule `QueueLane.For` / the fee calculator already use), then run the existing fee recompute so `total_fee`, `discount_amount` and the queue lane (S-) match. Optional fields, so the web is unaffected. No schema change (columns exist on `bookings`).

### P10. Staff invite password only works on that computer (gap b)
- **Route:** `POST /api/auth/invite` (Admin). It creates the user with a random password and `must_set_password`; the invitee sets a real one through `auth/set-password`. A password the desktop sets offline lives only in that computer's SQLite until the account syncs down (`user`, `staff_account` are pull-only), so it does not work on other computers.
- **Fix (recommended):** when the admin is online, the desktop calls invite with an optional `temporary_password` (Admin only, hashed server-side, `must_set_password` stays true, never logged, never returned), so the account exists in the cloud and syncs down to every computer with the same sign-in. Offline invites stay local-only and show "will work on other computers after the next sync"; they are not pushed (staff accounts remain cloud-owned, no push path). No schema change. Needs Jessie's OK because it touches sign-in data.
- Rejected alternative: making `user` pushable from nodes (an offline machine could then create admin logins).

### P11. Consultation certificate address can't be updated online (gap c)
- **Route:** `PUT /api/medical-certificates/by-consultation/{consultationId}` (Doctor, Admin). It already writes `patient_address_snapshot` on the certificate (per-issuance address), but it is only reachable by the owning doctor and the patient's address on `patients.address` is separate (`PUT /api/patients/{id}`).
- **Needs a decision which one is meant:** (1) the certificate's own snapshot after it is issued: it can already be edited by the owning doctor; if the desktop gets an error, send me the status/body and I will fix the exact check; (2) the patient's address: allow Staff to update `patients.address` / city / zip through `PUT /api/patients/{id}` (already open to staff-like roles, so only the desktop call is missing); (3) a new narrow `PATCH /api/medical-certificates/by-consultation/{id}/address` (Doctor, Admin, Staff) `{patient_address_snapshot}` that does not require re-sending the whole certificate. Recommendation: (3) if staff must fix addresses without the doctor, otherwise nothing to build.

### P12. Offline vaccine detail fields (gap d)
- **Server findings:** the sync push already carries every vaccination column inside the `consultation` document (`vaccinations` = full rows incl. manufacturer, dose_number, route, site, lot_number, expiry_date, next_dose_date, notes, reaction_notes, administered_date/by, status, source), so the cloud accepts them. The gap is the desktop outbox not sending those fields.
- **Online gap found:** `VaccinationInput` (`PUT /api/patient-vaccinations/by-consultation/{id}`) has only vaccine_name, manufacturer, dose_number, route, site, lot_number, expiry_date, next_dose_date, notes (+id). It lacks `reaction_notes`, `administered_date` and `status`.
- **Fix:** add optional `reaction_notes`, `administered_date`, `status` to `VaccinationInput` (additive, no schema change), and have the desktop outbox send the full vaccination row. Desktop-side change needed (not mine).
<!-- JJQR - END -->

<!-- JJQR - AI (21 Nov 2026) -->
# 21 Nov 2026 - READ-ONLY inventory: desktop-local work with no matching API (G1-G12)
Read-only (no code, no new endpoints, no builds/tests/DB). Sources: `ClinicNode.Api/Services/*` (what is converted), `ClinicNode.Core/**` (local services + outbox writes), `ClinicNode.Sync/Local/AggregateDocuments.cs` (what the outbox forwards), `ClinicApp` controllers + `SyncPushEngine`. Already covered by P1-P12 / built endpoints and NOT repeated: booking discount/visit type, invite password, vaccine fields, certificate/patient address, OR numbers, queue blocks, consents, lab values/documents, audit window.
Important: P9-P12 stay on hold; nothing below is implemented.

## A. Offline writes the desktop queues that the cloud REJECTS on push (the change never reaches the cloud)
The desktop forwards the raw outbox payload for any aggregate it does not snapshot itself (`AggregateDocuments.BuildSnapshotAsync`). The cloud only accepts `patient, booking, payment, consultation, vital_reading, audit_log, patient_consent` plus doctor self-edits (`doctor, doctor_schedule, doctor_blocked_date` with `actor_user_id`). Everything else is `rejected`.

| # | What the desktop does locally | Screen / service | What's missing on the API | Suggested fix |
|---|---|---|---|---|
| G1 | Admin edits clinic settings / fees offline: writes SQLite, outbox `clinic_settings` (payload `{"id":1}`) | Admin Settings, `AdminSettingsService` | Name mismatch (cloud aggregate is `clinic_setting`) and it is pull-only (`cloud_owned`). Online route exists: `PUT /api/settings` (Admin) | Either make the outbox replay these as REST calls with the admin session when back online (desktop), or add an Admin-actor push handler for `clinic_setting` (actor_user_id check like the doctor self-edit). Needs Jessie's OK (money: fees). |
| G2 | Admin creates/edits/deletes announcements offline: outbox `announcement` | Admin Announcements, `AdminAnnouncementService` | Pull-only (`cloud_owned`). Online: `POST/PUT/DELETE /api/announcements` (Admin) | Same pattern as G1: REST replay or Admin-actor push handler. |
| G3 | Admin edits staff accounts offline (name/contact/status/invite/revoke): outbox `staff_account` | Admin Staff, `AdminStaffService` (3 writes) | Pull-only. Online: `PUT /api/staff-accounts/{id}`, `POST /api/auth/invite`, `DELETE /api/auth/users/{id}` | REST replay when online; do NOT make staff/users pushable (an offline PC could create admin logins). |
| G4 | Staff edits own profile offline (outbox `staff_account`) and changes password (outbox `user`, op `password-changed`) | Staff Profile, `StaffProfileService` | `staff_account` and `user` are pull-only; `password-changed` is not a known operation type (`invalid_operation`); passwords must never travel over sync. Online: `PUT /api/staff-accounts/me` (built, P2) and `POST /api/auth/change-password` | Profile edit: desktop replays `PUT /api/staff-accounts/me` online. Password: online-only; offline attempt should be refused or kept as a local-only sign-in (as invite already is). |
| G5 | Front desk / doctor sets doctor day status (Available, Running late, Off) offline: outbox `doctor_day_status` (op `upsert`) | Staff Doctor Status, `DoctorStatusService` | Pull-only (`cloud_owned`), yet online `PUT /api/doctors/{id}/day-status` is open to Admin, Doctor, Staff | Add `doctor_day_status` to the pushable set (low risk, Staff/Doctor/Admin actor) with newest-write-wins per (doctor, date); or REST replay. |
| G6 | Staff reschedules a booking offline: creates a new booking + payment locally, outbox `booking` with op `reschedule` (payload `{bookingId,newBookingId,date,queueNumber}`) | Staff Booking detail, `StaffBookingService` | `reschedule` is not in the push `KnownTypes` (`invalid_operation`), so the old booking is not cancelled and the new one may not reach the cloud as a linked pair. Online: `PUT /api/bookings/{id}/reschedule` (accepts `new_booking_id`, `new_payment_id`) | Replay as `PUT /api/bookings/{id}/reschedule {appointment_date, new_booking_id, new_payment_id}` when online (the route already supports client ids), or add a `reschedule` handler to the booking push. NOT CONFIRMED: whether the new booking is also pushed as its own `booking` op; I did not trace the outbox for it. |

## B. Doctor lane screens still served only from the local copy (no Api implementation)
| # | What the desktop does locally | Screen / service | What's missing on the API | Suggested fix |
|---|---|---|---|---|
| G7 | Doctor edits own profile incl. consultation fee, license, PTR, S2, specialization, name; outbox `doctor` with only `{"doctorId":...}` (no `actor_user_id`, no changed fields) | Doctor Profile page, old `DoctorProfileService` (the newer `DoctorSelfEditService` is used only by the Doctor Schedule page) | Cloud accepts only specialization, bio, name, contact from a doctor (`DoctorSelfFields`); fee/license/PTR/S2 are Admin-only; this payload would be rejected `actor_required`. Online: `PUT /api/doctors/me` is bio-only (P1); no online route for specialization | Desktop should switch the profile page to `DoctorSelfEditService` (desktop work). API: if online specialization edit is wanted add it to `PUT /api/doctors/me` (needs OK; P1 was bio-only). Also warn that a doctor's local fee edit would change local fee maths but never reach the cloud. |
| G8 | Doctor sets daily patient limit offline: outbox `doctor_schedule` with `{"doctorId","dailyPatientLimit"}` | Old `DoctorScheduleService` | `daily_patient_limit` is an Admin-only doctor field in the cloud and `doctor_schedule` entities are keyed by schedule row id, not doctor id; rejected. No route lets a doctor set it online (`PUT /api/doctors/{id}` Admin only) | Decide policy: keep Admin-only (remove the desktop control) or add `daily_patient_limit` to the doctor self-edit fields (+ P1-style online route). Needs Jessie's OK. |
| G9 | Doctor "Call next" / "Start consult" run on local data | `ApiDoctorDashboardService.CallNextAsync/StartConsultAsync` throw "not converted" | Routes exist: `PUT /api/queue/{bookingId}/call` and `/complete` (Doctor allowed) | No API work: desktop conversion only. |
| G10 | Doctor dashboard custom date ranges and the doctor's own appointments list (scope: upcoming/past/filters) and patient chart are computed from the local copy | `ApiDoctorDashboardService` (ranges), `DoctorAppointmentsService`, `DoctorPatientsService` | Dashboard summary route covers today only (`GET /api/bookings/doctor/today-summary`); no summary route for an arbitrary date range. Lists/charts can be built from `GET /api/bookings`, `/doctor/patients`, `/api/consultations` | Optional: add `from`/`to` to `today-summary` (additive). Otherwise desktop conversion only. |
| G11 | Global search (top bar) and ICD-10/medicine/template pickers search the local copy; `IVisitStarter.StartWalkInConsult` and `PatientService` create/update walk-in patients + bookings locally | `GlobalSearchService`, `DoctorCatalogService`, `VisitStarter`, `PatientService` | Routes exist: `GET /api/search?q`, `GET /api/icd10-codes`, `GET /api/medicines`, `POST /api/patients`, `PUT /api/patients/{id}`. `StartWalkInConsult` with a discount hits P9 (no discount on `POST /api/bookings/walk-in`) | Desktop conversion; discount case follows P9. |

## C. Smaller notes
- G12 `ApiAdminBookingService`: `closed-dates` only answers up to 366 days (server clamps); beyond that stays local. Harmless; no fix.
- Admin Reports CSV is built on the desktop; the web has no CSV endpoint either: no gap.
- Admin Data Requests: online-only by decision (not in the pull feed), so the screen is empty offline: expected, not a gap.
- Patient address update from the consultation screen: route exists (`PUT /api/patients/{id}`, needs the whole patient row, same GET-merge-PUT the web does); the desktop marks it "not converted". This is the likely meaning of P11; still investigation-only.
- Amendment history reads `GET /api/audit-logs` (Admin, Doctor): fine for doctors, Staff cannot read it (matches the web).

## What I could NOT confirm
- Whether the desktop sync layer silently drops (rather than sends) outbox entries for cloud-owned aggregates; I only saw it forwards unknown aggregates as raw payloads, so I assumed they are sent and rejected. The local `sync_operations`/outbox result handling (`SyncReasonCodes` maps `cloud_owned` to a user message) suggests the user sees a note, not an error.
- G6: the exact outbox sequence for reschedule (new booking push).
- Which screens beyond the ones named are wrapped online-first at runtime: `MauiProgram`/`AppShell` were not read (off limits for edits; I did not inspect them), so wiring is inferred from the module files.
<!-- JJQR - END -->

<!-- JJQR - AI (21 Nov 2026) -->
## Extra2 (doctor Templates + Performance) - built (code only, no build)
- Files: `ClinicNode.App/Doctor/DoctorExtra2Module.cs` (`public static IServiceCollection AddDoctorExtra2Module(this IServiceCollection services)`, `public static void RegisterDoctorExtra2Routes()`; namespace `ClinicNode.App`), `ClinicNode.App/Views/Doctor/Extra2/{DoctorTemplatesWorkspacePage,DoctorTemplateEditorPage,DoctorPerformancePage}.cs`, `ClinicNode.Core/DoctorExtra2/{DoctorTemplateRules,DoctorTemplatesWorkspace}.cs`, `ClinicNode.Api/Services/Extra2/ApiDoctorTemplatesWorkspace.cs`.
- Wire: `services.AddDoctorExtra2Module();` after `AddDoctorExtraModule()`; routes `doctor-templates` / `doctor-performance`, or swap the Performance placeholder / Templates entry in AppShell for `DoctorPerformancePage` / `DoctorTemplatesWorkspacePage` (namespace `ClinicNode.App.Views.Doctor.Extra2`).
- Old read-only `Views/Doctor/Extra/DoctorTemplatesPage.cs` left untouched. Performance reuses IDoctorPerformanceService / ApiDoctorPerformanceService / DoctorPerformanceRules unchanged.
<!-- JJQR - END -->

<!-- JJQR - AI (21 Nov 2026) -->
## Queue number: never renumber (built)
- `SyncPushEngine.ReconcileQueueNumberAsync` now keeps a non-empty node number as-is (counter still raised to at least it). New `FlagQueueClashAsync` writes `Queue number clash` audit rows on both bookings; result `applied` + `queue_number_clash` (new `SyncReasons.QueueNumberClash`). Old renumber code removed; blank number still gets a cloud number.
- Desktop: treat `queue_number_clash` like `or_number_clash` (success, list on review page), and stop showing "reprint queue slip" for it. Admin surface: reads via audit log (action "Queue number clash"); no new route.
<!-- JJQR - END -->

<!-- JJQR - AI (21 Nov 2026) -->
## G5 built: doctor_day_status pushable
`SyncAggregates.DoctorDayStatus` (+ SelfEditable), dispatch in `SyncPushEngine.ApplyOneAsync`, new `SyncPushEngine.DayStatus.cs` (`DoctorDayStatusAsync`). Actors: Admin, Staff, or the doctor himself. Field merge by node time vs cloud `updated_at`. No schema change, still pulled.
<!-- JJQR - END -->

<!-- JJQR - AI (21 Nov 2026) -->
## G1 / G8 file locations (read-only)
- **G1** (offline Admin settings queued as `clinic_settings`): `ClinicNode.Core/Admin/AdminSettingsService.cs` line ~143 (`Aggregate = "clinic_settings"`, payload `{id:1}`). The online path is `ClinicNode.Api/Services/ApiAdminSettingsService.cs`; the offline-blocking wrapper is `ClinicNode.Core/Admin/OnlineOnlyAdminSettingsService.cs`. Local table list in `ClinicNode.Sync/Local/AggregateDocuments.cs` (lines ~67, 95: `clinic_setting` is pull-only). Fix: stop queueing (use the online-only wrapper) or replay as `PUT /api/settings`.
- **G8** (daily patient limit): online send = `ClinicNode.Api/Services/ApiAdminDoctorService.cs` line ~62 (`PUT /api/doctors/{id}`, Admin) and `ApiAdminStaffService.cs` line ~97 (staff create); the doctor-side field is shown read-only in `ApiDoctorExtraScheduleProfile.cs` (SaveAsync ignores `dailyPatientLimit`). Local write: `ClinicNode.Core/Admin/AdminDoctorService.cs` lines ~143/165. The old doctor-schedule outbox send (`doctor_schedule` with `dailyPatientLimit`) was in the superseded `DoctorScheduleService`; the new Extra screens do not queue it.

## G2 / G3 / G4 / G6 plan: desktop REST replay (PLAN ONLY, nothing implemented)
Principle: these aggregates stay cloud-owned (not pushable). The desktop keeps the local SQLite write for immediate feedback, but the outbox entry becomes a **replay request**, sent as the real REST call with the signed-in user's online token when connectivity returns (never with the sync-node token).
1. New desktop `RestReplayQueue` (SQLite table `rest_replay`: id, method, route, body json, created_at, actor_user_id, attempts, last_error, status pending/done/failed). Draining runs after each sync cycle, only when the online sign-in is valid for the same `actor_user_id`; otherwise it waits.
2. Per gap: **G2** announcement create/update/delete -> `POST /api/announcements`, `PUT/DELETE /api/announcements/{id}` (client id kept so the pull dedupes). **G3** staff edit -> `PUT /api/staff-accounts/{id}`; invite -> `POST /api/auth/invite`; revoke -> `DELETE /api/auth/users/{id}`; invite/revoke are NOT queued offline at all (show "needs internet"; an offline PC must not create logins). **G4** profile -> `PUT /api/staff-accounts/me`; password change -> online only (`POST /api/auth/change-password`), never queued and never in sync. **DECIDED (Jessie, see the decision note at the end):** staff invites and revokes are online-only (no replay, no backend change); password-changed is online-only. No separate decision exists yet for other G3/G4 edits (staff name/contact/status edits, own-profile edits): they are neither approved nor rejected for replay. **G6** reschedule -> replay ONE call, `PUT /api/bookings/{id}/reschedule` (exists; accepts the client `new_booking_id`, so replaying the same request returns the earlier result). The server clones the new booking itself; the desktop must then reconcile its locally created new booking to that id (queue number on the printed slip stays as issued, any clash is flagged by `queue_number_clash`). No second cancel call needed.
3. Failure handling: 4xx = mark failed, show on the sync review page with the server message (no endless retry); 5xx/timeout = retry with backoff; conflicts (e.g. booking already moved) shown for the desk. Success removes the local "pending" flag.
4. Backend needs: none new (all routes exist); optional later: accept the client id on `POST /api/announcements`. Desktop-only work. G3: invites and revokes are online-only (decided by Jessie); password-changed is online-only; other G3/G4 edits have no separate decision yet.
<!-- JJQR - END -->

<!-- JJQR - AI (21 Nov 2026) -->
NOTE: the "G3/G4 are REST replay" wording was reverted (earlier, before Jessie answered); superseded by the G3 / G4 decision at the end of this file: invites and revokes are online-only, password-changed is online-only. Nothing implemented.
<!-- JJQR - END -->

<!-- JJQR - AI (21 Nov 2026) -->
## Remaining backend gaps (list only)
- Hosted DB: OR-number unique-index migration + deploy script applied locally only; hosted still to run later.
- G1 admin clinic-settings and G2 announcements edited offline: no push path (cloud-owned); replay or online-only still undecided. `POST /api/announcements` takes no client id.
- G3/G4: invites/revokes and password-changed are online-only (decided, nothing to build on the backend); other G3/G4 edits (staff name/contact/status, own profile) have no separate decision yet.
- G8: no route for a doctor to set their own daily patient limit (Admin-only).
- Certificate address update online and invite temporary password: deliberately not built (Jessie).
- Standalone vaccination create returns 200 (not 201); desktop must accept 200 or 409.
<!-- JJQR - END -->

<!-- JJQR - AI (21 Nov 2026) -->
## Extra2 1:1 pass (Templates + Performance), read from the web .tsx
- **Templates workspace** (TemplatesWorkspace.tsx, shared.tsx, Soap/Phrases/Rx/Favorites/CertsTab.tsx): page header text, tab strip with counts, search row with "This tab" / "All tabs" and the "Search this tab…" / "Search all tabs…" placeholders (per-tab filter vs cross-tab results), exact button labels (New SOAP template / Add phrase / New prescription set / Add favorite medicine / New certificate template), "Copy" and "N meds" pills (no Built-in/Yours tags), previews, phrases grouped by all five SOAP fields with "Add to {field}", Move up/down, "Move to…", Duplicate, favorites without Duplicate and with the how-to empty state, empty/"no match" texts, load error "Couldn't load your … The other tabs still work." + Retry, "Restore built-in templates" as a bottom link (not on Favorites), delete with 5 s Undo.
- **Performance** (PerformanceDashboard / KpiCards / PerformanceChart / PerformanceLists / PeriodToggle): greeting title + headline, "Back to Today", range line, error state with retry, loading text, empty-period note, Collected hero + Billed + change chips, Patients seen, "Still to collect", "Last 14 days" chart (Collected | Patients, tap-a-bar readout), "Most common diagnoses", "Visits still to be paid", "Follow-ups due", "Also" strip; 60 s poll + hub events.
- Not ported: the web's list/grid ViewToggle on Templates (desktop shows the list view, the web default), and the ?tab=&field=&id= deep links.
<!-- JJQR - END -->

<!-- JJQR - AI (21 Nov 2026) -->
## G3 / G4 decision (Jessie, via allen0)
Jessie's words: "Staff invites and revokes: online-only."
- **Decided:** staff invites (`POST /api/auth/invite`) and revokes (`DELETE /api/auth/users/{id}`) are online-only. No replay queue, no backend change. `password-changed` stays online-only (never queued, never over sync).
- **Not decided (no invented rule):** any other G3/G4 edit, i.e. an admin editing a staff account's name/contact/status (`PUT /api/staff-accounts/{id}`) and a user editing their own profile (`PUT /api/staff-accounts/me`). No separate decision has been given for these; the earlier "REST replay" wording for them is only a plan, not an approval.
- G2 and G6 are unchanged.
- This supersedes the earlier note that said G3/G4 were ON HOLD pending Jessie.
<!-- JJQR - END -->
