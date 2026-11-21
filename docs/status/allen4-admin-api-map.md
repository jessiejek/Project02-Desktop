<!-- JJQR - AI (Nov 21, 2026) -->
# Admin screens → Project02-be API map

Source of truth: `Project02-fe/src/lib/data/*` + `src/app/admin/*` and `Project02-be/src/ClinicApp.Api/Controllers/*`.
JSON is snake_case on the wire (the FE `api` client). Native admin services should call these same methods/paths when online, and fall back to the existing SQLite implementations when the API is unreachable.

`docs/ONLINE-FIRST.md` and allen1's API client were **not present** when this map was written, so no Api-backed admin services were implemented yet.

## Dashboard (`AdminHomePage` / IAdminDashboardService)

The web dashboard is assembled client-side (no dedicated `/api/admin/dashboard`).

| Action | Method + path | DTO / notes |
|---|---|---|
| Daily stats | `GET /api/reports/daily-booking-summary` | Rows from `v_daily_booking_summary` (`appointment_date`, totals, completed, paid, unpaid, no_shows, revenue). FE: `queryReport(..., "v_daily_booking_summary")`. |
| Unpaid completed | `GET /api/reports/unpaid-completed-visits` | Unpaid-completed visit rows. FE counts `rows.length`. |
| Follow-ups due in 7 days | `GET /api/reports/pending-follow-ups` | `{ follow_up_date, ... }`; FE filters today..today+7. |
| Doctor load | `GET /api/doctors` | `DoctorRow` / `DoctorStaffEmbed` (`lib/data/doctors.ts`). Counts bookings per doctor from the bookings list. |
| Latest bookings | `GET /api/bookings` | `BookingRow` (+ patient/doctor/payment embeds). Query: unscoped list, FE slices recent + today/month. |

BE: `ReportsController` `[Route("api/reports")]`, `DoctorsController`, `BookingsController`.

## Bookings list + calendar (`AdminBookingsViewModel`)

| Action | Method + path | DTO / notes |
|---|---|---|
| List / search / filter | `GET /api/bookings` | `BookingRow`. Query typically includes `q`, `doctorId`, `status`, `date` / `from` / `to`, `page`, `pageSize`. |
| Week (calendar) | `GET /api/bookings` | Same, with `from`/`to` for the visible week. |
| Closed dates | `GET /api/bookings/closed-dates?days={days}` | `ClosedDate { date, reason }`. |
| Open detail | `GET /api/bookings/{bookingId}` | `BookingRow` with embeds. |

## Walk-in (`AdminWalkInPage` — same `WalkInWizard` as staff)

| Action | Method + path | DTO / notes |
|---|---|---|
| Board | `GET /api/queue?date=` | `QueueBoard` (`items: QueueEntry[]`, counts). |
| Check in walk-in | `POST /api/queue` | Body `CheckInWalkIn { patient_id, visit_type?, med_cert_requested?, discount_category?, notes? }` → `QueueTicket`. |
| Call / hold / complete / no-show / check-in existing | `PUT /api/queue/{bookingId}/{action}` | `action` = `check-in` \| `call` \| `hold` \| `complete` \| `no-show`. Empty body. |
| Cancel | `PUT /api/queue/{bookingId}/cancel` | `{ reason }`. |
| Patient search / create (wizard) | see Patients | `GET /api/patients/search`, `POST /api/patients`. |

Admin `CreateWalkInAsync` on the node today writes a booking locally; the web wizard uses the queue API, not `POST /api/bookings`.

## Booking detail (`AdminBookingDetailViewModel`)

| Action | Method + path | DTO / notes |
|---|---|---|
| Load | `GET /api/bookings/{bookingId}` | `BookingRow`. |
| Status (confirm booking, reject, cancel, complete, no-show) | `PUT /api/bookings/{bookingId}/status` | `{ status, reason? }`. |
| Reschedule | `PUT /api/bookings/{bookingId}/reschedule` | Date + reason (see `bookings.ts`). |
| Resolve payment id | `GET /api/payments/booking/{bookingId}` | `{ payment_id }`. |
| Confirm payment | `POST /api/payments/{paymentId}/confirm` | `ConfirmPaymentInput` (optional amount / method). Issues sequential OR on the server. |
| Waive | `POST /api/payments/{paymentId}/waive` | `{ reason }`. Web does **not** issue an OR. |
| Refund | `POST /api/payments/{paymentId}/refund` | `{ amount, reason }`. |

BE: `BookingsController`, `PaymentsController` `[Route("api/payments")]`.

## Doctors list / new / edit

| Action | Method + path | DTO / notes |
|---|---|---|
| List all | `GET /api/doctors` | `DoctorRow[]`. |
| Search | `GET /api/doctors/search` | `PagedResult<DoctorRow>` (`q`, `page`, `pageSize`, `sort`). |
| Get | `GET /api/doctors/{doctorId}` | `DoctorRow`. |
| Update (edit + deactivate) | `PUT /api/doctors/{doctorId}` | Full row replace: GET then PUT `{ ...current, ...DoctorPatch }`. |
| Invite / create | `POST /api/auth/invite` | `{ role: "Doctor", email, full_name, ... }` → `{ user_id, staff_id }` (`app/actions/createDoctor.ts`). |
| Schedules | `GET/PUT /api/doctors/{doctorId}/schedules` | `DoctorScheduleRow`. PUT body includes `doctor_id`. |
| Blocked dates | `GET/POST /api/doctors/{doctorId}/blocked-dates` | `BlockedDateRow`. |
| Remove blocked date | `DELETE /api/doctors/blocked-dates/{id}` | |
| Day status (not the main admin form) | `GET/PUT /api/doctors/{doctorId}/day-status`, `GET /api/doctors/day-statuses` | `DoctorDayStatusRow`. |
| Doctor services | `GET /api/doctor-services` | BE `DoctorServicesController`. |

## Patients list / detail

| Action | Method + path | DTO / notes |
|---|---|---|
| Search | `GET /api/patients/search` | `PagedResult<PatientRow>` (`q`, `page`, `pageSize`, `sort`). |
| List | `GET /api/patients` | `PatientRow[]`. |
| Get | `GET /api/patients/{patientId}` | `PatientRow`. |
| Create | `POST /api/patients` | `PatientRow` / create body. |
| Update | `PUT /api/patients/{patientId}` | GET then PUT `{ ...current, ...PatientPatch }`. |
| Allergies | `GET/PUT /api/patients/{patientId}/allergies` | `PatientAllergyRow` / `PatientAllergyInput`. |
| Access log | `GET /api/audit-logs/patient/{patientId}/access` | `PatientAccessEntry[]`. |
| Data requests | `GET /api/privacy/data-requests?status=`, `PUT /api/privacy/data-requests/{id}` | `DataRequestRow`; PUT `{ status, note }`. |

## Staff

| Action | Method + path | DTO / notes |
|---|---|---|
| List | `GET /api/staff-accounts` | `StaffAccountRow[]`. |
| Search | `GET /api/staff-accounts/search` | `PagedResult<StaffAccountRow>`. |
| Get | `GET /api/staff-accounts/{staffId}` | `StaffAccountRow`. |
| Update (name, contact, status) | `PUT /api/staff-accounts/{staffId}` | GET then PUT `{ ...current, ...StaffPatch }`. |
| Invite | `POST /api/auth/invite` | `{ role: "Staff" \| "Admin", email, full_name, ... }` → `{ user_id, staff_id }`. |
| Revoke invite | `GET /api/staff-accounts/{staffId}` then `DELETE /api/auth/users/{user_id}` | |

## Announcements

| Action | Method + path | DTO / notes |
|---|---|---|
| List | `GET /api/announcements` | `AnnouncementRow[]` (active filter via query). |
| Create | `POST /api/announcements` | Announcement input. |
| Update | `PUT /api/announcements/{id}` | GET current + patch. |
| Delete | `DELETE /api/announcements/{id}` | |

## Settings (clinic) + catalog

| Action | Method + path | DTO / notes |
|---|---|---|
| Get / update clinic settings | `GET/PUT /api/settings` | `ClinicSettingsRow`. PUT is a full row (GET + patch). |
| Operating hours | `GET/PUT /api/admin/operating-hours` | `OperatingHourRow[]`. BE also has `PUT /api/admin/operating-hours/{dayOfWeek}`. |
| Payment methods | `GET/PUT /api/admin/payment-methods` | FE GET returns `{ payment_method: string }[]`; PUT sends the selected methods. |
| Services catalog | `GET/POST /api/services`, `PUT /api/services/{id}` | BE `ServicesController`. |
| Medicine search | `GET /api/medicines` | `MedicineRow[]` (`lib/data/lookups.ts`). |

## Audit

| Action | Method + path | DTO / notes |
|---|---|---|
| Search (paged) | `GET /api/audit-logs/search` | `PagedResult<AuditLogRow>` (`q`, `entityType`, `page`, `pageSize`). |
| Unpaged list | `GET /api/audit-logs` | `AuditLogRow[]`. |

## Reports + CSV

| Action | Method + path | DTO / notes |
|---|---|---|
| Daily booking summary | `GET /api/reports/daily-booking-summary` | View row. |
| Unpaid completed visits | `GET /api/reports/unpaid-completed-visits` | View row. |
| Pending follow-ups | `GET /api/reports/pending-follow-ups` | View row. |
| Doctor earnings | `GET /api/reports/doctor-earnings?doctorId=` | `DoctorEarningsRow` (`doctor_id`, `period`, `completed_visits`, `gross_billed`, `collected`, `waived`). |
| Doctor ratings | `GET /api/reports/doctor-ratings` | `DoctorRatingRow`. |

CSV export on the node is built locally (`AdminReportService.BuildCsv`) from these same datasets; the web has no separate CSV endpoint.

## Register this node / This computer

Out of the clinic-admin CRUD set (node/sync settings). Register uses the sync/node registration APIs, not the list above. "This computer" is `NodeSettingsPage` (sync URL, backup, printers).

## Shared types (FE)

`PagedResult<T>` from `lib/data/paging.ts`. Snake_case wire fields throughout (`patient_id`, `booking_id`, `full_name`, `appointment_date`, …).
<!-- JJQR - END -->
