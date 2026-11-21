<!-- JJQR - AI (21 Nov 2026) -->
# API surface used by the web doctor / staff / admin screens

Source: `Project02-fe/src/lib/**` + `app/doctor|staff|admin/**` (read only). Backend checked against `Project02-be/src/ClinicApp.Api/Controllers/*.cs`.
Roles = `[Authorize]` attribute on the action/controller; "Any signed-in" = no role restriction at attribute level (some are scoped or checked in code). SignalR hub: `/hubs/clinic` (used by lib/realtime/clinicHub.ts) - exists.

**Result: every endpoint the web calls exists in the backend (0 gaps).** Screens column is by module usage; verify per screen when wiring.

## Auth (lib/auth)

| Method | Route | Roles | Used by screen | Exists |
|---|---|---|---|---|
| POST | `/api/auth/login` | Anonymous | Sign-in | yes |
| POST | `/api/auth/register` | Anonymous | Sign-up (patient) | yes |
| POST | `/api/auth/refresh-token` | Anonymous | Session refresh | yes |
| POST | `/api/auth/logout` | Anonymous | Sign-out | yes |
| POST | `/api/auth/change-password` | Any signed-in | doctor/settings, staff/profile | yes |
| POST | `/api/auth/forgot-password` | Anonymous | Sign-in | yes |
| POST | `/api/auth/resend-verification` | Anonymous | Sign-in | yes |

## Settings / admin (lib/data/admin.ts)

| Method | Route | Roles | Used by screen | Exists |
|---|---|---|---|---|
| GET | `/api/settings` | Anonymous | admin/settings, admin/dashboard | yes |
| PUT | `/api/settings` | Admin | admin/settings | yes |
| GET | `/api/admin/operating-hours` | Anonymous | admin/settings | yes |
| PUT | `/api/admin/operating-hours` | Admin | admin/settings | yes |
| GET | `/api/admin/payment-methods` | Anonymous | admin/settings | yes |
| PUT | `/api/admin/payment-methods` | Admin | admin/settings | yes |
| GET | `/api/announcements` | Any signed-in | admin/announcements, staff/announcements, dashboards | yes |
| POST | `/api/announcements` | Admin | admin/announcements | yes |
| PUT | `/api/announcements/{id}` | Admin | admin/announcements | yes |
| DELETE | `/api/announcements/{id}` | Admin | admin/announcements | yes |
| GET | `/api/audit-logs` | Admin, Doctor | admin/audit-logs | yes |
| GET | `/api/audit-logs/search` | Admin | admin/audit-logs | yes |
| GET | `/api/audit-logs/patient/{patientId}/access` | Admin | admin/patients/[id] | yes |
| GET | `/api/reports/doctor-earnings` | Doctor, Admin | admin/reports, doctor/performance | yes |
| GET | `/api/reports/doctor-ratings` | Admin/Staff/Doctor reports roles + Patient | admin/reports, admin/dashboard | yes |
| GET | `/api/reports/daily-booking-summary` | Reports roles (StaffReports) | admin/reports, admin/dashboard | yes |
| GET | `/api/reports/unpaid-completed-visits` | Reports roles (StaffReports) | admin/reports | yes |
| GET | `/api/reports/pending-follow-ups` | Reports roles (StaffReports) | admin/reports | yes |

## Bookings (lib/data/bookings.ts)

| Method | Route | Roles | Used by screen | Exists |
|---|---|---|---|---|
| GET | `/api/bookings` | Any signed-in (scoped by role) | admin/bookings, doctor/appointments, doctor/patients/[id] | yes |
| GET | `/api/bookings/{id}` | Any signed-in | admin/bookings/[id], staff/bookings/[id], doctor/appointments/[id] | yes |
| GET | `/api/bookings/me` | Patient | (patient portal only) | yes |
| PUT | `/api/bookings/{id}/cancel` | Patient | (patient portal only) | yes |
| POST | `/api/bookings/book` | Patient | (patient portal only) | yes |
| GET | `/api/bookings/closed-dates` | Patient, Staff, Admin | staff walk-in/reschedule | yes |
| GET | `/api/bookings/staff/today` | Admin, Staff | staff/dashboard, staff/queue | yes |
| GET | `/api/bookings/staff/all` | Admin, Staff | staff/bookings, admin/bookings | yes |
| GET | `/api/bookings/staff/for-payment` | Admin, Staff | staff/payments | yes |
| GET | `/api/bookings/doctor/today` | Doctor | doctor/dashboard, doctor/appointments | yes |
| PUT | `/api/bookings/{id}/status` | Admin, Staff, Doctor | doctor/appointments/[id], staff/bookings/[id], consultation2 | yes |
| PUT | `/api/bookings/{id}/reschedule` | Admin, Staff | staff/bookings/[id], admin/bookings/[id] | yes |

## Queue (lib/data/queue.ts)

| Method | Route | Roles | Used by screen | Exists |
|---|---|---|---|---|
| GET | `/api/queue` | Admin, Staff, Doctor | staff/queue, staff/dashboard, doctor/dashboard | yes |
| POST | `/api/queue` | Admin, Staff | walk-in wizard (staff/walk-in, admin/walk-in) | yes |
| PUT | `/api/queue/{bookingId}/check-in` | Admin, Staff | staff/queue | yes |
| PUT | `/api/queue/{bookingId}/cancel` | Admin, Staff | staff/queue, staff/dashboard | yes |
| PUT | `/api/queue/{bookingId}/call` | Admin, Staff, Doctor | staff/queue, doctor/dashboard | yes |
| PUT | `/api/queue/{bookingId}/hold` | Admin, Staff, Doctor | staff/queue | yes |
| PUT | `/api/queue/{bookingId}/complete` | Admin, Staff, Doctor | consultation2 | yes |
| PUT | `/api/queue/{bookingId}/no-show` | Admin, Staff, Doctor | staff/queue | yes |

## Patients (lib/data/patients.ts, privacy.ts)

| Method | Route | Roles | Used by screen | Exists |
|---|---|---|---|---|
| GET | `/api/patients` | Admin, Staff, Doctor | staff/patients, admin/patients | yes |
| GET | `/api/patients/search` | Admin, Staff, Doctor | staff/patients, admin/patients, doctor/patients, walk-in | yes |
| GET | `/api/patients/{id}` | Any signed-in | all patient detail screens | yes |
| POST | `/api/patients` | Admin, Staff, Doctor | walk-in wizard | yes |
| PUT | `/api/patients/{id}` | Any signed-in (role-checked in code) | staff/patients/[id], admin/patients/[id] | yes |
| PUT | `/api/patients/{id}/consent` | Any signed-in | walk-in wizard | yes |
| GET | `/api/patients/me` | Patient | session.ts (patient only) | yes |
| GET | `/api/patients/{id}/allergies` | Admin, Staff, Doctor | consultation2, doctor/patients/[id] | yes |
| PUT | `/api/patients/{id}/allergies` | Admin, Staff, Doctor | consultation2, doctor/patients/[id] | yes |
| GET | `/api/patients/{id}/lab-values` | Admin, Staff, Doctor | consultation2, doctor/patients/[id] | yes |
| GET | `/api/patients/{id}/consents` | Any signed-in | admin/patients/[id] (privacy) | yes |
| POST | `/api/patients/{id}/consent-decision` | Any signed-in | privacy tab | yes |
| GET | `/api/patients/{id}/export` | Any signed-in | admin/patients/[id] (data export) | yes |
| POST | `/api/privacy/data-requests` | Any signed-in | privacy tab | yes |
| GET | `/api/privacy/data-requests/mine` | Any signed-in | privacy tab | yes |
| GET | `/api/privacy/data-requests` | Admin | admin/dashboard (DataRequestsPanel) | yes |
| PUT | `/api/privacy/data-requests/{id}` | Admin | admin/dashboard (DataRequestsPanel) | yes |

## Payments (lib/data/payments.ts)

| Method | Route | Roles | Used by screen | Exists |
|---|---|---|---|---|
| GET | `/api/payments/booking/{bookingId}` | Any signed-in | staff/bookings/[id], admin/bookings/[id], staff/payments | yes |
| POST | `/api/payments/{id}/confirm` | Admin, Staff | staff/payments, staff/bookings/[id] | yes |
| GET | `/api/payments/waived-awaiting-confirmation` | Admin, Staff | staff/payments | yes |
| POST | `/api/payments/{id}/confirm-waiver` | Admin, Staff | staff/payments | yes |
| POST | `/api/payments/{id}/waive` | Admin, Staff, Doctor | staff/payments, consultation2 | yes |
| POST | `/api/payments/{id}/refund` | Admin | admin/bookings/[id] | yes |

## Doctors / scheduling (lib/data/doctors.ts, scheduling.ts)

| Method | Route | Roles | Used by screen | Exists |
|---|---|---|---|---|
| GET | `/api/doctors` | Anonymous | admin/doctors, staff/doctor-status, walk-in | yes |
| GET | `/api/doctors/search` | Admin | admin/doctors | yes |
| GET | `/api/doctors/{id}` | Any signed-in | admin/doctors/[id]/edit, doctor/profile | yes |
| PUT | `/api/doctors/{id}` | Admin | admin/doctors/[id]/edit (doctor/profile reads only; see proposals) | yes |
| GET | `/api/doctors/{id}/schedules` | Anonymous | doctor/schedule, admin/doctors | yes |
| PUT | `/api/doctors/{id}/schedules` | Admin, Doctor | doctor/schedule, admin/doctors | yes |
| GET | `/api/doctors/{id}/blocked-dates` | Any signed-in | doctor/schedule | yes |
| POST | `/api/doctors/{id}/blocked-dates` | Admin, Doctor | doctor/schedule | yes |
| DELETE | `/api/doctors/blocked-dates/{blockedDateId}` | Admin, Doctor | doctor/schedule | yes |
| GET | `/api/doctors/{id}/day-status` | Any signed-in | staff/doctor-status, doctor/dashboard | yes |
| GET | `/api/doctors/day-statuses` | Any signed-in | staff/doctor-status | yes |
| PUT | `/api/doctors/{id}/day-status` | Admin, Doctor, Staff | staff/doctor-status, doctor/dashboard | yes |
| GET | `/api/staff-accounts` | Admin, Staff | admin/staff, admin/doctors | yes |
| GET | `/api/staff-accounts/search` | Admin, Staff | admin/staff | yes |
| GET | `/api/staff-accounts/{id}` | Any signed-in | admin/staff, staff/profile | yes |
| GET | `/api/staff-accounts/me` | Any signed-in | session.ts, staff/profile | yes |
| PUT | `/api/staff-accounts/{id}` | Any signed-in (self or Admin, checked in code) | admin/staff, staff/profile | yes |
| GET | `/api/search` | Any signed-in | Topbar global search | yes |

## Consultation (lib/data/clinical.ts, labs.ts, patientFiles.ts, lookups.ts)

| Method | Route | Roles | Used by screen | Exists |
|---|---|---|---|---|
| GET | `/api/consultations` | Any signed-in | doctor/patients/[id], admin/patients/[id] | yes |
| GET | `/api/consultations/{id}` | Any signed-in | doctor/patients/[id] | yes |
| GET | `/api/consultations/by-booking/{bookingId}` | Any signed-in | consultation2, appointments/[id] | yes |
| PUT | `/api/consultations/by-booking/{bookingId}` | Doctor, Admin | consultation2 | yes |
| GET | `/api/consultations/{id}/diagnoses` | Any signed-in | consultation2, patient history | yes |
| PUT | `/api/consultations/{id}/diagnoses` | Doctor, Admin | consultation2 | yes |
| GET | `/api/vitals` | Any signed-in | consultation2, patient history | yes |
| PUT | `/api/vitals/by-booking/{bookingId}` | Staff, Doctor, Admin | consultation/vitals, consultation2 | yes |
| GET | `/api/follow-ups` | Any signed-in | doctor/dashboard, patient history | yes |
| PUT | `/api/follow-ups/by-consultation/{id}` | Doctor, Admin | consultation2 | yes |
| DELETE | `/api/follow-ups/by-consultation/{id}` | Doctor, Admin | consultation2 | yes |
| GET | `/api/medical-certificates/by-booking/{bookingId}` | Any signed-in | consultation2 | yes |
| GET | `/api/medical-certificates/by-consultation/{id}` | Any signed-in | consultation2 | yes |
| PUT | `/api/medical-certificates/by-consultation/{id}` | Doctor, Admin | consultation2 | yes |
| DELETE | `/api/medical-certificates/by-consultation/{id}` | Doctor, Admin | consultation2 | yes |
| GET | `/api/prescription-groups` | Any signed-in | doctor/patients/[id], consultation2 | yes |
| GET | `/api/prescription-groups/{id}` | Any signed-in | doctor/patients/[id]/prescriptions/[id] | yes |
| PUT | `/api/prescription-groups/by-booking/{bookingId}` | Doctor, Admin | consultation2, prescriptions/create | yes |
| DELETE | `/api/prescription-groups/{id}` | Doctor, Admin | prescriptions | yes |
| GET | `/api/lab-orders/by-booking/{bookingId}` | Any signed-in | consultation2 | yes |
| GET | `/api/lab-orders/by-consultation/{id}` | Any signed-in | consultation2 | yes |
| PUT | `/api/lab-orders/by-consultation/{id}` | Doctor, Admin | consultation2 | yes |
| GET | `/api/patient-documents` | Any signed-in | doctor/patients/[id], consultation2 | yes |
| GET | `/api/patient-documents/{id}/file` | Any signed-in | patient history | yes |
| POST | `/api/patient-documents` | Any signed-in | doctor/patients/[id] | yes |
| GET | `/api/patient-lab-results` | Any signed-in | doctor/patients/[id] | yes |
| GET | `/api/patient-lab-results/{id}/file` | Any signed-in | patient history | yes |
| POST | `/api/patient-lab-results` | Any signed-in | doctor/patients/[id] | yes |
| GET | `/api/patient-vaccinations` | Any signed-in | doctor/patients/[id] | yes |
| GET | `/api/patient-vaccinations/by-consultation/{id}` | Any signed-in | consultation2 | yes |
| PUT | `/api/patient-vaccinations/by-consultation/{id}` | Doctor, Admin | consultation2 | yes |
| GET | `/api/reviews` | Any signed-in | doctor/dashboard | yes |
| GET | `/api/medicines` | Anonymous | consultation2, Rx form | yes |
| GET | `/api/vital-field-templates` | Anonymous | vitals editor | yes |
| GET | `/api/lab-test-catalog` | Anonymous | consultation2 | yes |
| GET | `/api/icd10-codes` | Any signed-in | diagnosis field | yes |
| GET | `/api/doctor/performance` | Doctor, Admin | doctor/performance | yes |

## Doctor templates (lib/data/clinical.ts)

| Method | Route | Roles | Used by screen | Exists |
|---|---|---|---|---|
| GET | `/api/prescription-templates` | Doctor/Admin (GET also Staff) | doctor/templates, consultation2 | yes |
| POST | `/api/prescription-templates` | Doctor/Admin (GET also Staff) | doctor/templates, consultation2 | yes |
| PUT | `/api/prescription-templates/{id}` | Doctor/Admin (GET also Staff) | doctor/templates, consultation2 | yes |
| DELETE | `/api/prescription-templates/{id}` | Doctor/Admin (GET also Staff) | doctor/templates, consultation2 | yes |
| GET | `/api/doctor-favorite-medicines` | Doctor, Admin | doctor/templates, consultation2 | yes |
| POST | `/api/doctor-favorite-medicines` | Doctor, Admin | doctor/templates, consultation2 | yes |
| PUT | `/api/doctor-favorite-medicines/{id}` | Doctor, Admin | doctor/templates, consultation2 | yes |
| DELETE | `/api/doctor-favorite-medicines/{id}` | Doctor, Admin | doctor/templates, consultation2 | yes |
| GET | `/api/soap-templates` | Doctor, Admin | doctor/templates, consultation2 | yes |
| POST | `/api/soap-templates` | Doctor, Admin | doctor/templates, consultation2 | yes |
| PUT | `/api/soap-templates/{id}` | Doctor, Admin | doctor/templates, consultation2 | yes |
| DELETE | `/api/soap-templates/{id}` | Doctor, Admin | doctor/templates, consultation2 | yes |
| GET | `/api/soap-phrases` | Doctor, Admin | doctor/templates, consultation2 | yes |
| POST | `/api/soap-phrases` | Doctor, Admin | doctor/templates, consultation2 | yes |
| PUT | `/api/soap-phrases/{id}` | Doctor, Admin | doctor/templates, consultation2 | yes |
| DELETE | `/api/soap-phrases/{id}` | Doctor, Admin | doctor/templates, consultation2 | yes |
| GET | `/api/medical-certificate-templates` | Doctor, Admin | doctor/templates, consultation2 | yes |
| POST | `/api/medical-certificate-templates` | Doctor, Admin | doctor/templates, consultation2 | yes |
| PUT | `/api/medical-certificate-templates/{id}` | Doctor, Admin | doctor/templates, consultation2 | yes |
| DELETE | `/api/medical-certificate-templates/{id}` | Doctor, Admin | doctor/templates, consultation2 | yes |
| GET | `/api/doctor-diagnosis-templates` | Doctor, Admin | doctor/templates, consultation2 | yes |
| POST | `/api/doctor-diagnosis-templates` | Doctor, Admin | doctor/templates, consultation2 | yes |
| PUT | `/api/doctor-diagnosis-templates/{id}` | Doctor, Admin | doctor/templates, consultation2 | yes |
| DELETE | `/api/doctor-diagnosis-templates/{id}` | Doctor, Admin | doctor/templates, consultation2 | yes |
| PUT | `/api/soap-phrases/order` | Doctor, Admin | doctor/templates | yes |
| PUT | `/api/doctor-favorite-medicines/order` | Doctor, Admin | doctor/templates | yes |
| POST | `/api/templates/restore-built-ins` | Doctor, Admin | doctor/templates | yes |

## Backend-only (not called by web doctor/staff/admin) - relevant to the desktop

| Method | Route | Roles | Note |
|---|---|---|---|
| POST | `/api/bookings` | Admin, Staff | create booking <!-- JJQR - AI (21 Nov 2026) -->(now also optional `visit_type`, `discount_category`)<!-- JJQR - END --> |
| POST | `/api/bookings/walk-in` | Admin, Staff | walk-in <!-- JJQR - AI (21 Nov 2026) -->(same Create request: optional `visit_type`, `discount_category`)<!-- JJQR - END --> |
| GET | `/api/bookings/doctor/today-summary`, `/doctor/patients` | Doctor | |
| GET | `/api/doctors/me`, `/api/doctors/admin` | Doctor / Admin | <!-- JJQR - AI (21 Nov 2026) -->(`PUT /api/doctors/me` {bio} is Doctor-only too)<!-- JJQR - END --> |
| GET/PUT | `/api/doctors/{id}/services` | Any / Admin | |
| GET/POST/PUT | `/api/services` | Any / Admin | |
| POST/GET | `/api/sync/nodes`, `/nodes/{id}/rotate-secret`, `/nodes/{id}/revoke` | Admin | sync-node admin ALREADY exists <!-- JJQR - AI (21 Nov 2026) -->(GET also returns `last_push_at`, `operation_count_24h`, `rejected_count_24h`)<!-- JJQR - END --> |
| POST/GET | `/api/sync/token`, `/handshake`, `/push`, `/pull` | node token | cloud sync |
| POST | `/api/auth/invite`, DELETE `/api/auth/users/{id}` | Admin | |
| GET | `/api/auth/me`, POST `/api/auth/avatar` | signed-in | |
| GET | `/api/version` | Anonymous | |

Total web-called endpoints listed: 150. Missing in backend: 0.
<!-- JJQR - END -->

<!-- JJQR - AI (21 Nov 2026) -->
## Added for the desktop (21 Nov 2026, owner-approved) - not used by the web

| Method | Route | Roles | Used by screen | Exists |
|---|---|---|---|---|
| PUT | `/api/doctors/me` | Doctor | desktop doctor profile (bio only; body `{bio}`) | yes (new) |
| PUT | `/api/staff-accounts/me` | Any signed-in (self only) | desktop profile (body `{full_name, phone?, avatar_url?}`) | yes (new) |
| GET | `/api/patients/{id}/card` | Admin, Staff, Doctor | desktop patient-card print | yes (new) |
| GET | `/api/sync/nodes` | Admin | desktop sync-node admin (adds `last_push_at`, `operation_count_24h`, `rejected_count_24h`; `last_seen_at` already present) | yes (changed) |

`GET /api/sync/status` was proposed (P4) and NOT built (skipped by owner).
<!-- JJQR - END -->

<!-- JJQR - AI (21 Nov 2026) -->
## `GET /api/sync/pull` content changes (route and envelope unchanged)
- New pull-only aggregates (full row as `data`, no schema change): `patient_consent`, `patient_lab_value`, `patient_document` (metadata only; `file_url` is informational, file bytes stay online), `patient_lab_result` (metadata only).
- `patient` document gained `vaccinations`: the patient's vaccinations with no consultation (replace-set, pull side; the push engine ignores it). Consultation-linked vaccinations still travel inside the `consultation` document.
- `audit_log`: a pull returns only audit rows whose `performed_at` is within the last 30 days (`SyncPullService.AuditPullDays`); the first-run seed covers the same window. The cursor still advances past skipped rows. Nodes still push their own audit rows.
- NOT synced (by decision): `data_request`, `review`.
- Note (corrected 21 Nov 2026): `patient_lab_value`, `patient_document` and `patient_lab_result` are pull-only (a push is rejected `cloud_owned`). `patient_consent` is ALSO pushable (append-only), see the consent push row.
<!-- JJQR - END -->

<!-- JJQR - AI (21 Nov 2026) -->
## OR numbers, queue blocks, consent push (21 Nov 2026, owner-approved)

| Method | Route | Roles | Shape | Exists |
|---|---|---|---|---|
| POST | `/api/payments/{id}/confirm` (changed) | Admin, Staff | OR is trimmed; a duplicate OR (case-insensitive) on another payment returns **409** `{error:"or_number_duplicate", message, or_number}` and saves nothing | yes (changed) |
| GET | `/api/payments/or-clashes` | Admin | `[{or_number, count, payments:[{payment_id, booking_id, status, amount, confirmed_at, origin_node_id, origin:"cloud"\|"node"}]}]`, read-only, max 500 clashes | yes (new) |
| POST | `/api/sync/queue-blocks` | sync-node token (Bearer from `/api/sync/token`, header `X-Sync-Protocol: 1`) | body `{date:"yyyy-MM-dd", lane:"R"\|"S", size?:20 (clamped 5-50)}` -> `{date, lane, from, to, size, first:"R-041", last:"R-060"}`; 400 invalid lane/date, 403 revoked node | yes (new) |
| POST | `/api/sync/push` (changed) | sync-node token | payment op: always accepted; if the OR is on another payment the result is `applied` with `reason_code:"or_number_clash"` (payment kept as printed, audit rows on both payments, visible in `/api/payments/or-clashes` and the audit log action "OR number clash"). New aggregate `patient_consent` is now pushable (append-only; `data` = `{id, patient_id, consent_version, decision, decided_at}`; same id again = `duplicate`; unknown patient = `retry missing_dependency`; delete = rejected `append_only`) | yes (changed) |

Data requests stay online-only (not synced). <!-- JJQR - AI (21 Nov 2026) -->(Superseded: queue numbers are no longer renumbered on push. A clash returns `queue_number_clash`: printed number kept, both bookings flagged, no reprint.)<!-- JJQR - END -->
<!-- JJQR - END -->

<!-- JJQR - AI (21 Nov 2026) -->
Update: `POST /api/payments/{id}/confirm` also maps a unique-index race (`ix_payments_or_number_cloud`) to the same 409 `or_number_duplicate`. `payments.or_number` is now max 100 characters; `/api/sync/push` payment ops with a longer OR are not truncated (see allen1.md).
<!-- JJQR - END -->

<!-- JJQR - AI (21 Nov 2026) -->
## Consolidated: approved batch of 21 Nov 2026 (all keys snake_case)

| Method | Route | Roles | Used by screen | Exists |
|---|---|---|---|---|
| POST | `/api/payments/{id}/confirm` | Admin, Staff | desktop/web payment confirm. Body `{payment_method, reference_number, or_number, amount_received, confirm_notes}`. `or_number` trimmed; a duplicate (case-insensitive, another payment) -> **409** `{"error":"or_number_duplicate","message":"OR number X is already used. Enter a different OR number.","or_number":"X"}`, nothing saved. A unique-index race gives the same 409. OR > 100 chars -> 400 | yes (changed) |
| POST | `/api/payments/{id}/waive` | Admin, Staff, Doctor | desktop online waive. Body `{reason, or_number?}`. NEW: `or_number` is trimmed and saved on `payments.or_number`; same 409 `or_number_duplicate` body as confirm (nothing saved); OR > 100 chars -> 400 `{message}` | yes (changed) |
| GET | `/api/payments/or-clashes` | Admin | admin review (read-only). `[{or_number, count, payments:[{payment_id, booking_id, status, amount, confirmed_at, origin_node_id, origin:"cloud"\|"node"}]}]` | yes (new) |
| POST | `/api/sync/queue-blocks` | sync-node token | desktop offline queue. Body `{date:"yyyy-MM-dd", lane:"R"\|"S", size?:20}` (5-50) -> `{date, lane, from, to, size, first, last}` e.g. `first:"R-041"`; 400 invalid lane/date; 403 revoked node | yes (new) |
| POST | `/api/sync/push` | sync-node token | payment op: accepted as printed; duplicate OR -> `status:"applied"`, `reason_code:"or_number_clash"` (audit rows on both payments). Aggregate `patient_consent` pushable: `entity_id` = consent id, `data` = `{id, patient_id, consent_version, decision, decided_at}`; `decision` in Accepted/Declined/Withdrawn; replay -> `duplicate`; unknown patient -> `retry` + `missing_dependency`; delete -> `rejected` + `append_only` | yes (changed) |
| GET | `/api/sync/pull` | sync-node token | desktop catch-up. New aggregates (pull-only, `data` = the full row, snake_case columns): `patient_consent` (`id, patient_id, consent_version, decision, decided_at`; also pushable, see above), `patient_lab_value` (`id, patient_id, test_code, value, unit, observed_at, created_at`), `patient_document` (METADATA only: `id, patient_id, booking_id, consultation_id, file_name, file_size, file_content_type, title, description, file_url, uploaded_by_user_id, uploaded_at`; file bytes stay online via `GET /api/patient-documents/{id}/file`), `patient_lab_result` (METADATA only: `id, patient_id, booking_id, consultation_id, lab_order_id, file_name, file_content_type, result_title, result_text, status, file_url, uploaded_at`; bytes via `GET /api/patient-lab-results/{id}/file`). NOT synced: `data_request`, `review`. | yes (changed) |
| GET | `/api/sync/pull` (patient document) | sync-node token | `patient` aggregate `data` gained `vaccinations`: `[ {vaccination row} ]` = the patient's vaccinations with `consultation_id = null` (replace-set; pull side only, ignored on push). Consultation-linked vaccinations stay inside the `consultation` document | yes (changed) |
| GET | `/api/sync/pull` (audit) | sync-node token | `audit_log` changes are returned only when the audit row's `performed_at` is within the last 30 days (`SyncPullService.AuditPullDays`); the first-run seed covers the same 30 days; the cursor still advances; the cloud keeps full history; a node's own audit rows still push | yes (changed) |
<!-- JJQR - END -->

<!-- JJQR - AI (21 Nov 2026) -->
## Visit type / discount on bookings, vaccine fields (21 Nov 2026, owner-approved; optional, backward-compatible, no schema change)

| Method | Route | Roles | Used by screen | Exists |
|---|---|---|---|---|
| POST | `/api/bookings` | Admin, Staff | desktop/staff booking create. Body gains optional `visit_type` ("New" \| "FollowUp") and `discount_category` ("Senior" \| "PWD"). Neither sent = unchanged (fee = doctor fee + services). If either is sent: consultation part from the clinic fee schedule (`ClinicFees.Compute`, same as walk-in check-in) + selected services; response booking carries `visit_type`, `discount_category`, `discount_amount`, `consultation_fee_snapshot`; the queue number uses the S- lane for a discount. 400 `{message}` for an invalid category, "Senior needs 60+ or a Senior ID on file", "PWD needs a PWD ID on file", unknown patient, missing clinic settings | yes (changed) |
| POST | `/api/bookings/walk-in` | Admin, Staff | same body and rules as `POST /api/bookings` (forces `is_walk_in = true`) | yes (changed) |
| PUT | `/api/patient-vaccinations/by-consultation/{consultationId}` | Doctor, Admin | body is a list of `{vaccine_name, manufacturer, dose_number, route, site, lot_number, expiry_date, next_dose_date, notes, id?}` plus NEW optional `reaction_notes` (max 2000), `administered_date` (yyyy-MM-dd; default today) and `status` ("Administered" \| "Scheduled" \| "Overdue"; default Administered). Omitted = as before | yes (changed) |

Not built (owner decision): temporary invite password (P10); no backend change for the certificate address (P11).
<!-- JJQR - END -->

<!-- JJQR - AI (21 Nov 2026) -->
| POST | `/api/sync/push` (changed) | sync-node token | booking op: a desktop-created booking's `queue_number` is NEVER reassigned (Jessie, 21 Nov 2026). If another booking of the same day (cloud or another node, any status incl. CheckedIn/InProgress/OnHold/Completed) holds the same number, the op is `applied` with `reason_code:"queue_number_clash"`; both bookings keep their number and each gets an audit row `Queue number clash` (details name the other booking id, no secrets). The cloud no longer moves a cloud booking to another number either. `queue_number_reassigned` now only happens when the node sent NO number (cloud assigns the next one, result `merged`). No schema change | yes (changed) |
<!-- JJQR - END -->

<!-- JJQR - AI (21 Nov 2026) -->
| POST | `/api/sync/push` (changed) | sync-node token | NEW pushable aggregate `doctor_day_status` (G5; was pull-only). Data `{id, doctor_id, status_date, status ("Available"\|"RunningLate"\|...as the enum), running_late_minutes?, created_at?}`, `actor_user_id` required: Admin, Staff or that doctor. Natural key (doctor_id, status_date); newest edit wins per field vs the cloud `updated_at` (`merged` + `cloud_field_newer` if the cloud is newer; `natural_key_remapped` if the cloud row has another id). Delete -> `rejected` `delete_not_allowed`. Bad actor -> `actor_required` / `actor_unknown` / `actor_not_allowed`. No schema change | yes (changed) |
<!-- JJQR - END -->

<!-- JJQR - AI (21 Nov 2026) -->
| POST | `/api/bookings/walk-in` | Admin, Staff | Same optional `visit_type` / `discount_category` as `POST /api/bookings` (the endpoint calls the same `Create` with `is_walk_in=true`: same validation, same fee schedule and queue-lane calculation, status CheckedIn). Neither sent = unchanged. Same 400 messages | yes (changed) |
<!-- JJQR - END -->

<!-- JJQR - AI (21 Nov 2026) -->
| POST | `/api/patient-vaccinations` | Doctor, Staff, Admin | standalone vaccination (not tied to a consultation). Body `{patient_id, consultation_id?, vaccine_name, manufacturer?, dose_number?, route?, site?, lot_number?, expiry_date?, administered_date?, administered_by?, next_dose_date?, status, source, notes?, reaction_notes?, id?}`. `id` = optional client id (offline sync); an id already used returns **409** `{error:"id_conflict", message}` and saves nothing. `reaction_notes` max 2000 (400 `{message}`), `administered_date` defaults to today (clinic date) when `status` is Administered and none is sent, `status` omitted = Administered. Success returns **200** with the saved row (not 201) | yes (changed) |
<!-- JJQR - END -->

<!-- JJQR - AI (21 Nov 2026) -->
| POST | `/api/sync/push` (note) | sync-node token | `reason_code:"queue_number_clash"` (status `applied`): the printed queue number is kept as-is, BOTH bookings are flagged (audit `Queue number clash`), nothing is renumbered, and the desk must not reprint the slip | yes |
<!-- JJQR - END -->

<!-- JJQR - AI (21 Nov 2026) -->
## Consistency pass (checked against the live code, 21 Nov 2026)
| Method | Route | Roles | Wording added / corrected | Exists |
|---|---|---|---|---|
| POST | `/api/payments/{id}/confirm` | Admin, Staff | `or_number` > 100 chars (also `reference_number`, `confirm_notes` > 1000) -> 400. Duplicate OR -> 409 `{error:"or_number_duplicate", message, or_number}`; the unique-index race (`ix_payments_or_number_cloud`) maps to the same 409 | yes |
| POST | `/api/payments/{id}/waive` | Admin, Staff, Doctor | body `{reason, or_number?}`; Staff may only waive when the doctor decided Waive (403 `{message}`); OR > 100 -> 400 `{message:"OR number is too long."}`; duplicate -> same 409 as confirm (nothing saved) | yes |
| POST | `/api/sync/queue-blocks` | sync-node token | errors are `{error:"invalid_request", detail}` (400), `{error:"node_revoked"}` (403), 426 `protocol_unsupported` for a bad `X-Sync-Protocol`. NO date window is checked: any `date` is accepted (past or far future); the desktop decides which days (today..+6). Each call atomically adds `size` to that day/lane counter and writes audit `Queue block reserved` | yes |
| POST | `/api/sync/push` | sync-node token | bookings: `queue_number_clash` = keep the printed number, flag both bookings, no renumber, no reprint. A booking pushed with NO queue number still gets a cloud number (`merged` + `queue_number_reassigned`) | yes |
| POST | `/api/sync/push` | sync-node token | `patient_consent` push: `data` = `{id, patient_id, consent_version, decision, decided_at}` (the `PatientConsent` row; `decision` Accepted / Declined / Withdrawn else `invalid_payload`); replay = `duplicate`; unknown patient = `retry` `missing_dependency`; delete = `rejected` `append_only` | yes |
| GET | `/api/patients/{id}/card` | Admin, Staff, Doctor | response `{patient_id, patient_code, full_name, birth_date, sex, blood_type, allergies:[string] ("NKDA" for no known allergies), emergency_contact:{name, phone, relationship}|null, clinic_name, clinic_phone, issued_at}`; no clinical notes; writes an access-log row "Patient card" | yes |
| PUT | `/api/doctors/me` | Doctor (only) | body `{bio}` only, same bio rule as Admin edits (`Rules.Doctor`); returns the doctor row | yes |
| PUT | `/api/staff-accounts/me` | any signed-in staff/doctor/admin | body `{full_name, phone?, avatar_url?}` (avatar_url max 2000 -> 400 `{error:"invalid_request", detail}`); returns the staff row | yes |
| GET | `/api/sync/nodes` | Admin | each node: `registered_at, last_seen_at, revoked_at, last_push_at, operation_count_24h, rejected_count_24h` (no secrets) | yes |
| POST | `/api/bookings`, `/api/bookings/walk-in` | Admin, Staff | optional `visit_type`, `discount_category`; walk-in = the same Create with `is_walk_in=true` | yes |
| PUT / POST | `/api/patient-vaccinations/by-consultation/{id}`, `/api/patient-vaccinations` | Doctor, Admin / Doctor, Staff, Admin | `reaction_notes` max 2000, `administered_date`, `status`; POST: client `id`, duplicate id 409 `{error:"id_conflict"}`, success 200 (not 201) | yes |
| POST | `/api/sync/push` | sync-node token | `doctor_day_status` pushable (Admin, Staff or that doctor; newest edit wins per field; delete rejected `delete_not_allowed`) | yes |
<!-- JJQR - END -->

<!-- JJQR - AI (21 Nov 2026) -->
## Desktop review codes (NOT API reason codes)
These strings appear only in the desktop's sync review list. The backend never sends them in `/api/sync/push` results or in any HTTP error body. (Checked against the desktop code, 21 Nov 2026.)
| Code | Where it comes from | Meaning |
|---|---|---|
| `online_only` | `ClinicNode.Sync/Local/SyncReasonCodes.cs` (`OnlineOnly`) | the change belongs to an online-only area (clinic settings / fees, queued under names like `clinic_settings`); it is not replayed and no push route exists, so the desk must redo it online |
| `reaction_notes_too_long` | `ClinicNode.App/Shared/StandaloneVaccinationPush.cs` | a local standalone vaccination has `reaction_notes` over 2000 characters (the `POST /api/patient-vaccinations` cap); it is never cut: the row stays local and is listed once per run |
| `vaccination_rejected` | `StandaloneVaccinationPush.RejectedReason` | `POST /api/patient-vaccinations` answered 400 (server message shown); the row stays local and is retried next run |
| `local_table_missing` | `ClinicNode.Sync/Local/AggregateDocuments.cs` | a pulled aggregate (e.g. lab values, documents) has no matching table in this computer's SQLite file, so that change was skipped instead of failing the page |
<!-- JJQR - END -->
