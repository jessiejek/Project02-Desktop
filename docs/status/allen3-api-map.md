<!-- JJQR - AI (Nov 21, 2026) - allen3: staff + doctor-extra screens -> exact Project02-fe API calls. Read-only research, no code changed. -->
# allen3 API map — staff + doctor-extra screens

Sources: FE `Project02-fe/src/lib/data/*.ts` (every page goes through `lib/api/client.ts`), pages under `src/app/staff|doctor`, BE `Project02-be/src/ClinicApp.Api/Controllers/*`, entities in `ClinicApp.Domain/Entities/*`. Verified 21 Nov 2026.

## Conventions (all screens)
- Base URL `NEXT_PUBLIC_API_URL` (default `http://localhost:5000`), `Authorization: Bearer <access token>`. Data resources are **snake_case** (global `SnakeCaseLower` policy, `Program.cs:21`); `/api/auth/*` DTOs are **camelCase** (`ClinicApp.Auth/Dtos.cs`).
- Most GET endpoints return the **EF entity itself** (not a DTO), so the "BE class" below is the Domain entity; embeds are renamed with `[JsonPropertyName]` (`Booking`: `patients`, `doctors`, `booking_services[].services`, `payments`).
- Paged lists: `PagedResult<T>` = `{ items, totalCount, page, pageSize }` (TS `lib/data/paging.ts`, BE `Controllers/PagedResult.cs`). FE clamps `pageSize` to 1..200.
- Errors: 400/409 bodies are `{ message }` (FE `ApiError.body`). Roles from `[Authorize(Roles=…)]` are listed per call — **a role mismatch is a 403**.
- PUT on patients/doctors/staff-accounts **replaces the row**: FE does fetch → merge → PUT (`updatePatient/updateDoctor/updateStaffAccount`). BE only honours some fields (noted).
- Realtime: SignalR `ClinicHub` (`QueueUpdated`, `PatientCheckedIn`, payment status events) + 60 s poll fallback (`useClinicHubEvent`). Not an HTTP call.
- Common TS/BE shapes (referenced below):
  - **BookingRow** `lib/data/bookings.ts:51` ⇄ **Booking** `Entities/Booking.cs:7` (+ `Payment` `:71`, `BookingService` `:59`).
  - **QueueBoard / QueueEntry / QueueTicket** `lib/data/queue.ts:12-55` ⇄ anonymous objects in `QueueController.cs:140-216`.
  - **PatientRow** `lib/data/patients.ts:10` ⇄ **Patient** `Entities/Identity.cs:16`.
  - **StaffAccountRow** `lib/data/staff.ts:11` ⇄ **StaffAccount** `Identity.cs:76`. **DoctorRow** `lib/data/doctors.ts:20` ⇄ **Doctor** `Identity.cs:95`.

---

# Staff screens

## ST-DASH — `/staff/dashboard` (`StaffHomePage`)
| Trigger | Call | Req / query | Response (TS ⇄ BE) | Role |
|---|---|---|---|---|
| load | `GET /api/bookings/staff/today` | `pageSize=500&activeOnly=true&statuses=Confirmed,CheckedIn` | `{items: BookingRow[]}` ⇄ `PagedResult<Booking>` | Admin,Staff |
| load | `GET /api/bookings/staff/today` | `pageSize=1&activeOnly=true` (only `totalCount` used) | same | Admin,Staff |
| load / hub event | `GET /api/queue` | optional `date=YYYY-MM-DD` | `QueueBoard` (ready-for-payment = items with `status=Completed && amount_due>0`) | Admin,Staff,Doctor |
| load | `GET /api/announcements` | `activeOnly=true` | `AnnouncementRow[]` ⇄ `Announcement` (`Admin.cs:23`) | anonymous |
| Check in / Undo check-in | `PUT /api/bookings/{id}/status` | `{status:"CheckedIn"\|"Confirmed", reason:null}` ⇄ `UpdateBookingStatusRequest(BookingStatus Status, string? Reason)` | 204/200 | Admin,Staff,Doctor |
| Cancel (reason dialog) | `PUT /api/bookings/{id}/status` | `{status:"Cancelled", reason}` | | same |

## ST-QUEUE — `/staff/queue` (`StaffQueuePage`)
| Trigger | Call | Req | Response | Role |
|---|---|---|---|---|
| load / hub / 60 s | `GET /api/queue` | `?date=` optional | `QueueBoard {date, summary{booked,waiting,in_progress,completed,no_show,total}, items[QueueEntry{booking_id,queue_number,is_priority,patient_id,patient_name,patient_code,status,visit_type,amount_due,checked_in_at,is_walk_in}]}`; S- lane first then FCFS | Admin,Staff,Doctor |
| Check in / Call in / Hold / Complete / No-show | `PUT /api/queue/{bookingId}/{check-in\|call\|hold\|complete\|no-show}` | body `{}` | 200 | check-in & cancel: Admin,Staff; call/hold/complete/no-show: Admin,Staff,Doctor |
| Cancel (reason required) | `PUT /api/queue/{bookingId}/cancel` | `{reason}` ⇄ `CancelQueueEntryRequest(string? Reason)` | 200 | Admin,Staff |
| Print slip | none (uses QueueTicket data already loaded / `GET /api/settings` for clinic name) | | | |

## ST-WALK — `/staff/walk-in` (`WalkInWizard`)
| Trigger | Call | Req | Response | Role |
|---|---|---|---|---|
| open wizard | `GET /api/patients` | **all** patients (optional `?search=`); FE filters + pages client-side (`clientPage`, 10/page) | `PatientRow[]` ⇄ `List<Patient>` | Admin,Staff,Doctor |
| search | none (client filter). Server alternative exists: `GET /api/patients/search?q&page&pageSize&sort` | | | |
| Register patient | `POST /api/patients` | `{first_name,middle_name,last_name,date_of_birth,sex,contact_number,address,email:"",is_guest:true,user_id:null}` (Title-Cased by FE) ⇄ body is the `Patient` entity | created `PatientRow` incl. server-issued `patient_code` (`MF-000123`) | Admin,Staff,Doctor |
| Check in | `POST /api/queue` | `{patient_id, visit_type:"New"\|"FollowUp", med_cert_requested, discount_category:"Senior"\|"PWD"\|null, notes?}` ⇄ `CheckInWalkInRequest(Guid PatientId, VisitType?, bool? MedCertRequested, string? DiscountCategory, string? Notes, Guid? BookingId, Guid? PaymentId)` (the last two = client-supplied ids, already supported for offline sync) | `QueueTicket {booking_id,queue_number,lane,is_priority,sequence,patient_name,patient_code,doctor_name,clinic_name,clinic_address,visit_type,provisional_fee,issued_at}` | Admin,Staff |

## ST-PAY — `/staff/payments` (`StaffPaymentsPage`)
| Trigger | Call | Req | Response | Role |
|---|---|---|---|---|
| load | `GET /api/queue` | today's board (for "Completed & owes") | `QueueBoard` | Admin,Staff,Doctor |
| load | `GET /api/bookings/staff/for-payment` | `pageSize=500` (+`q`) | `{items:BookingRow[]}` (payment.status=Unpaid) | Admin,Staff |
| load | `GET /api/payments/waived-awaiting-confirmation` | – | `WaivedVisit[] {booking_id,payment_id,patient_name,patient_code,queue_number,appointment_date,waived_reason}` (anonymous object, `PaymentsController.cs:27`) | Admin,Staff |
| Record payment (step 1) | `GET /api/payments/booking/{bookingId}` | – | `Payment` (need `payment_id`) | any with booking access |
| Record payment (step 2) | `POST /api/payments/{paymentId}/confirm` | `{payment_method, amount_received, reference_number, or_number, confirm_notes}` ⇄ `ConfirmPaymentRequest(PaymentMethod, string? ReferenceNumber, string? OrNumber, decimal? AmountReceived, string? ConfirmNotes)`. **Amount must equal `payment.amount` exactly** (400 `{message}`); 409 if the doctor waived. Web sends `or_number = "OR-"+bookingId[0..8].upper` — the **web has no OR sequence**; server stores whatever is sent (max 100 chars) | `Payment` (`Booking.cs:71`) | Admin,Staff |
| Confirm waiver | `POST /api/payments/{paymentId}/confirm-waiver` | `{}` | `Payment` (idempotent) | Admin,Staff |

## ST-PATIENTS — `/staff/patients` (`StaffPatientsPage`)
| Trigger | Call | Req | Response | Role |
|---|---|---|---|---|
| load / search / page | `GET /api/patients/search` | `q, page, pageSize(25 default), sort` | `PagedResult<PatientRow>` ⇄ `PagedResult<Patient>` | Admin,Staff,Doctor |
No edit/create on this screen (register is only in the walk-in wizard).

## ST-PATIENT-DETAIL — `/staff/patients/[id]`
| Trigger | Call | Req | Response |
|---|---|---|---|
| load | `GET /api/patients/{id}` | – | `PatientRow` (+`patient_allergies[]`) |
| load | `GET /api/bookings` | `patientId={id}` | `BookingRow[]` ⇄ `List<Booking>` |
| load | `GET /api/consultations` | `patientId={id}` | `ConsultationRow[]` (`clinical.ts:12`) ⇄ `Consultation` (`Clinical.cs:8`) |
Read-only.

## ST-BOOK-DETAIL — `/staff/bookings/[id]` (`StaffBookingDetailPage`)
| Trigger | Call | Req | Response | Role |
|---|---|---|---|---|
| load | `GET /api/bookings/{id}` | – | `BookingRow` ⇄ `Booking` (404 if no access) | any with access |
| Check in / No-show (reason) / Cancel (reason) / Complete | `PUT /api/bookings/{id}/status` | `{status, reason}` | | Admin,Staff,Doctor |
| Record payment | `GET /api/payments/booking/{id}` then `POST /api/payments/{paymentId}/confirm` | as ST-PAY (web sends OR `OR-<id8>`) | `Payment` | Admin,Staff |
| Waive | `GET /api/payments/booking/{id}` then `POST /api/payments/{paymentId}/waive` `{reason}` ⇄ `WaivePaymentRequest(string Reason)` | Staff may only record a waiver the doctor already chose (`pf_decision=Waive`) | | Admin,Staff,Doctor |
| Reschedule | `PUT /api/bookings/{id}/reschedule` | `{appointment_date, reason}` ⇄ `RescheduleBookingRequest(DateOnly AppointmentDate, string? Reason, Guid? NewBookingId, Guid? NewPaymentId)` | `{old_booking_id,new_booking_id}` ⇄ `RescheduleBookingResponse` | Admin,Staff |
| Print receipt/slip | none (client-side from loaded data) | | | |

## ST-DOCTOR-STATUS — `/staff/doctor-status`
| Trigger | Call | Req | Response | Role |
|---|---|---|---|---|
| load | `GET /api/doctors` | – (anonymous) | `DoctorRow[]` ⇄ `List<Doctor>` (+`staff_accounts.full_name`) | anon |
| load | `GET /api/doctors/day-statuses` | `date=todayManila` | `DoctorDayStatusRow[] {id,doctor_id,status_date,status,running_late_minutes}` ⇄ `DoctorDayStatus` (`Scheduling.cs:33`) | anon |
| Set status | `PUT /api/doctors/{doctorId}/day-status` | `{doctor_id,status_date,status:"Available"\|"RunningLate"\|"UnavailableToday",running_late_minutes}` | | Admin,Doctor,**Staff** |

## ST-ANNOUNCE — `/staff/announcements`
| Trigger | Call | Req | Response |
|---|---|---|---|
| load | `GET /api/announcements` | `activeOnly=true` | `AnnouncementRow[] {id,title,body,is_active,posted_by_user_id,created_at,updated_at}` ⇄ `Announcement` |
| load | `GET /api/staff-accounts` | – (for "posted by" names; failure tolerated) | `StaffAccountRow[]` (Admin,Staff) |
Read-only for staff. Create/update/delete (`POST/PUT/DELETE /api/announcements[/{id}]`) are **Admin-only**.

## ST-PROFILE — `/staff/profile`
| Trigger | Call | Req | Response | Role |
|---|---|---|---|---|
| load | `GET /api/staff-accounts/{staffId}` (staffId from session) | – | `StaffAccountRow` | self/Admin |
| Save | `PUT /api/staff-accounts/{staffId}` | fetch-merge-put of the full row; **BE persists only `full_name`, `contact_number`** (+`status` only when Admin edits another) ⇄ body `StaffAccount` (validated by `Rules.Staff`) | `StaffAccount` | self or Admin |
| Change password | `POST /api/auth/change-password` | `{currentPassword,newPassword}` camelCase ⇄ `ChangePasswordRequest` (`Auth/Dtos.cs:41`); `PasswordPolicy.Check` → 400 `{message}` | 204/200 | any signed in |
**Editable on web:** name, contact number, password.

---

# Doctor-extra screens (all calls come from `session.staffId` = doctorId)

## D-APPT-LIST — `/doctor/appointments`
| Trigger | Call | Req | Response | Role |
|---|---|---|---|---|
| Today scope (load + 60 s poll) | `GET /api/bookings/doctor/today` | – | `BookingRow[]` ⇄ `List<Booking>` ordered by slot | Doctor |
| History/All scope | `GET /api/bookings` | `doctorId={id}` (server forces own id for doctors) | `BookingRow[]` | Admin,Staff,Doctor |
Filtering/search/paging are client-side. Row action = open consult (`/doctor/consultation?bookingId`). **No edits here.**

## D-APPT-DETAIL — `/doctor/appointments/[id]` (server component)
| Call | Req | Response |
|---|---|---|
| `GET /api/bookings/{id}` | – | `BookingRow` |
| `GET /api/consultations/by-booking/{bookingId}` | – | `ConsultationRow` ⇄ `Consultation` (404 → none) |
| `GET /api/consultations` | `patientId={b.patient_id}` | `ConsultationRow[]` |
| `GET /api/prescription-groups` | `patientId={b.patient_id}` | `RxGroupRow[]` (`clinical.ts:320`) ⇄ `PrescriptionGroup`+`PrescriptionLineItem` (`Prescriptions.cs:13/36`) |
| print availability: `GET /api/lab-orders/by-booking/{bookingId}` (+ rx group by booking) | – | `LabOrderRow[]` (`labs.ts`) ⇄ `LabOrder` (`LabsAndFiles.cs:21`) |
Read-only page (printing is client-side).

## D-PATIENTS — `/doctor/patients`
| Call | Req | Response |
|---|---|---|
| `GET /api/bookings` | `doctorId={id}` (full history; FE groups by patient client-side; the unused `GET /api/bookings/doctor/patients` returns the same list) | `BookingRow[]` |
Read-only; row opens the chart.

## D-CHART — `/doctor/patients/[id]` (Promise.all on load)
| Call | Req | Response (TS ⇄ BE) |
|---|---|---|
| `GET /api/patients/{id}` | | `PatientRow` ⇄ `Patient` |
| `GET /api/consultations` | `patientId, doctorId` | `ConsultationRow[]` ⇄ `Consultation` |
| `GET /api/prescription-groups` | `patientId, doctorId` | `RxGroupRow[]` |
| `GET /api/bookings` | `patientId, doctorId` | `BookingRow[]` |
| `GET /api/vital-field-templates` | anon | `VitalFieldTemplateRow[]` (`lookups.ts:16`) ⇄ `VitalFieldTemplate` (`Clinical.cs:94`) |
| `GET /api/vitals` | `patientId` | `VitalReadingRow[]` (`clinical.ts:165`) ⇄ `PatientVitalReading` (`Clinical.cs:107`) |
| `GET /api/patient-lab-results` | `patientId` | `PatientLabResultRow[]` ⇄ `PatientLabResult` (`LabsAndFiles.cs:97`) |
| `GET /api/patient-documents` | `patientId` | `PatientDocumentRow[]` ⇄ `PatientDocument` (`:79`); file bytes `GET /api/patient-documents/{id}/file`, `/patient-lab-results/{id}/file` |
| `GET /api/patient-vaccinations` | `patientId` | `VaccinationRow[]` ⇄ `PatientVaccination` (`:50`) |
| Rx print preview: `GET /api/settings`, `GET /api/doctors/{id}` | | `ClinicSettingsRow`, `DoctorRow` |
| **Edit:** Delete Rx group | `DELETE /api/prescription-groups/{id}` | Doctor,Admin |
Only edit on the chart is deleting a prescription group (confirm dialog). Doctor-side writes to clinical rows happen in the consultation screens (out of scope here): `PUT /api/consultations/by-booking/{id}`, `PUT /api/prescription-groups/by-booking/{id}`, `PUT /api/vitals/by-booking/{id}` (Staff,Doctor,Admin), `PUT /api/patient-vaccinations/by-consultation/{id}`, `POST /api/patient-documents|patient-lab-results` (multipart).

## D-SCHEDULE — `/doctor/schedule`
| Trigger | Call | Req | Response | Role |
|---|---|---|---|---|
| load | `GET /api/doctors/{id}` | – | `DoctorRow` (`daily_patient_limit`) | anon (license redacted without staff session) |
| load | `GET /api/doctors/{id}/schedules` | anon | `DoctorScheduleRow[] {id,doctor_id,day_of_week 0-6,is_active,start_time,end_time}` ⇄ `DoctorSchedule` (`Scheduling.cs:7`, `TimeOnly`) | anon |
| load | `GET /api/doctors/{id}/blocked-dates` | – | `BlockedDateRow[] {id,doctor_id,blocked_date,reason}` ⇄ `DoctorBlockedDate` (`:21`) | anon |
| pick a date to block | `GET /api/bookings` | `doctorId, date` → FE counts rows with `status != Cancelled` = affected bookings | `BookingRow[]` | Admin,Staff,Doctor |
| **Save** | `PUT /api/doctors/{id}` `{...doctor, daily_patient_limit}` | fetch-merge-put of `Doctor` | `Doctor` | **Admin only** ⚠ |
| **Save** (×7, sequential) | `PUT /api/doctors/{id}/schedules` | `{doctor_id,day_of_week,is_active,start_time,end_time}` ⇄ `DoctorSchedule` upsert by (doctor, day) | | Admin,Doctor |
| **Add blocked date** | `POST /api/doctors/{id}/blocked-dates` | `{doctor_id,blocked_date,reason}` ⇄ `DoctorBlockedDate` | `BlockedDateRow` | Admin,Doctor |
| **Remove blocked date** | `DELETE /api/doctors/blocked-dates/{blockedDateId}` | | | Admin,Doctor |
**Editable on web:** weekly hours/active days, blocked dates (add/remove), daily patient limit. ⚠ `DoctorsController.Update` (`DoctorsController.cs:119`) is `[Authorize(Roles="Admin")]`, so the daily-limit PUT (and the profile PUT below) returns **403 for a Doctor login** — the web page's `updateDoctor` call fails and its generic "could not be fully saved" error shows. Schedules and blocked dates are Doctor-writable and work.

## D-PROFILE — `/doctor/profile`
| Trigger | Call | Req | Response | Role |
|---|---|---|---|---|
| load | `GET /api/doctors/{id}` | – | `DoctorRow` | anon |
| **Save** (parallel) | `PUT /api/doctors/{id}` | `{specialization,bio,consultation_fee,license_number,ptr_number,s2_number}` merged into full row ⇄ `Doctor` (`Rules.Doctor` validates) | `Doctor` | **Admin only** ⚠ (403 for Doctor) |
| **Save** (parallel) | `PUT /api/staff-accounts/{id}` | `{full_name}` merged into the full row; BE keeps `full_name`,`contact_number` | `StaffAccount` | self or Admin ✅ |
| Change password | `POST /api/auth/change-password` | `{currentPassword,newPassword}` | | any |
**Editable on web:** full name, specialization, bio, consultation fee, license/PTR/S2, password — but only full name and password actually succeed for a Doctor role (see ⚠). Avatar upload is `POST /api/auth/avatar`.

## D-TEMPLATES — `/doctor/templates` (`TemplatesWorkspace`) — **fully editable on web** (all `[Authorize(Roles="Doctor,Admin")]`)
| Tab | Read | Create / Update / Delete | TS ⇄ BE |
|---|---|---|---|
| SOAP | `GET /api/soap-templates?doctorId` | `POST /api/soap-templates?doctorId` · `PUT /api/soap-templates/{id}` · `DELETE …/{id}` | `SoapTemplateRow` (`clinical.ts:416`) ⇄ `SoapTemplate` (`Clinical.cs:192`) / `SoapTemplateInput(Title,IsSystemTemplate,ChiefComplaint,Subjective,Objective,Assessment,Plan,…)` |
| Phrases | `GET /api/soap-phrases?doctorId` | `POST /api/soap-phrases?doctorId` · `PUT /api/soap-phrases/{id}` · `DELETE` · `PUT /api/soap-phrases/order?doctorId` `{ids}` | `SoapPhraseRow` (`:427`) ⇄ `SoapPhrase` / `SoapPhraseInput(SoapField Field,Label,Body,…)`, `OrderInput(List<Guid> Ids)` |
| Diagnoses | `GET /api/doctor-diagnosis-templates` | `POST` · `PUT /{id}` · `DELETE /{id}` on `/api/doctor-diagnosis-templates` | `DiagnosisTemplateRow` (`:536`) ⇄ `DoctorDiagnosisTemplate` / `DiagnosisTemplateInput(Label,Body,Icd10Code,…)` |
| Rx | `GET /api/prescription-templates?doctorId` (Admin,Staff,Doctor) | `POST` · `PUT /{id}` · `DELETE /{id}` on `/api/prescription-templates` | `RxTemplateRow` (`:386`) ⇄ `PrescriptionTemplate`+`Item` (`Prescriptions.cs:84/100`) / `UpsertRxTemplateRequest(DoctorId,Title,IsSystemTemplate,List<RxItemInput> Items,…)` |
| Favorites | `GET /api/doctor-favorite-medicines?doctorId` + `GET /api/medicines` (anon, picker) | `POST` · `PUT /{id}` · `DELETE /{id}` · `PUT /order?doctorId` `{ids}` | `FavoriteMedicineRow` (`:398`) ⇄ `DoctorFavoriteMedicine` / `FavoriteMedicineInput(MedicineId,GenericName,Dosage,Quantity,Instruction,…)` |
| Certificates | `GET /api/medical-certificate-templates?doctorId` | `POST` · `PUT /{id}` · `DELETE /{id}` | `MedicalCertificateTemplateRow` (`:496`) ⇄ `MedicalCertificateTemplate` / `MedicalCertificateTemplateInput` |
| "Restore built-ins" (any tab) | – | `POST /api/templates/restore-built-ins` (`{kind: soap\|rx\|cert\|diagnosis\|phrase}`; returns `{restored,kind}`) | `TemplatesController.cs:16` |
Note: the node's current read-only templates behaviour is a local choice, not a web rule — online-first can allow these edits.

---

# Findings for allen / allen1
1. **Edit permissions summary (web):** staff may edit own profile (name/contact) and password; staff cannot create/edit announcements (Admin). Doctors can edit templates (full CRUD), weekly schedule, blocked dates, own name, password; **doctor profile fields and daily patient limit are Admin-only on the API** (`PUT /api/doctors/{id}`), so the web doctor profile/limit save fails with 403 for a Doctor login. Needs an owner decision (new `PUT /api/doctors/me`-style endpoint, or accept Admin-only) before the MAUI doctor profile/limit edit goes online-first. Schedule + blocked dates already work for Doctor.
2. **OR number:** the web generates `OR-<bookingId first 8>` client-side and `POST /payments/{id}/confirm` simply stores `or_number`. Online-first MAUI should send its node-sequence `OR-<node>-<yyyymmdd>-<seq>` in `or_number` (≤100 chars); the cloud has no uniqueness/sequence, so legacy collisions (`OR-F2111111`) exist cloud-side — the node-only partial unique index (filtered to the new format) is deliberate.
3. **Client-supplied ids** already accepted by `POST /api/queue` (`BookingId`, `PaymentId`) and `PUT /bookings/{id}/reschedule` (`NewBookingId`, `NewPaymentId`); `POST /api/patients` takes the `Patient` entity (check whether `patient_id` in the body is honoured for offline-created patients).
4. **Walk-in search** loads every patient (`GET /api/patients`); the paged `GET /api/patients/search` is better for a native client.
5. **Payment is two calls** (`GET /payments/booking/{id}` → `POST /payments/{paymentId}/confirm`); `confirm` requires `amount_received == payment.amount` exactly.
6. Staff day-status write (`PUT /doctors/{id}/day-status`) is allowed for Staff; staff has no other doctor-table write.
