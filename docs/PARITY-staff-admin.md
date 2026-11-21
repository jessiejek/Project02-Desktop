<!-- JJQR - AI (Nov 21, 2026) - staff/admin feature parity vs Project02-fe, with the web API each feature calls. Audited 21 Nov 2026, Asia/Manila. -->

# Staff & admin feature parity (ClinicNode vs Project02-fe)

Audited **21 November 2026, ~4:00 PM Asia/Manila (UTC+8)** from current `Project02-fe` routes, pages, dialogs, filters and `src/lib/data/*` / `src/app/actions/*` API calls, compared with `ClinicNode` views, view-models and AppShell.

**How to read status.** `done` means the desktop UI and behaviour already exist in ClinicNode today (almost always against **local SQLite**, not yet the cloud API). This table is the list the **online-first** port should drive: each row’s **web API endpoint(s)** are the exact method + path the web app calls. `docs/ONLINE-FIRST.md` was not present in `Project02-desktop/docs` at audit time (not edited).

**Redirects (not missing screens).** `/staff/bookings` redirects to `/staff/queue` (the detail route `/staff/bookings/[id]` remains). `/admin/calendar` redirects to `/admin/bookings` (List / Week tabs). Patient portal routes are out of scope.

**Live updates.** The web staff/admin desk refetches on SignalR hub `/hubs/clinic` (`QueueUpdated`, `PaymentUpdated`, `PatientCheckedIn`). Desktop staff pages use in-process `IClinicEventBus` plus a 60s poll (`StaffLivePage`). That is called out once below, not repeated on every row.

**Owners.** allen3 = staff screens; allen4 = admin screens, TopBar, `Services/Files`, MauiProgram DI; allen2 = sync; allen5 = printing, NodeSettings / security / backup, `AdminRegisterNodePage`, other shared/settings; `unassigned` where none of those lanes clearly own it.

## Counts

| Area | done | partial | missing | total |
|------|------|---------|---------|-------|
| Staff | 22 | 4 | 0 | 26 |
| Admin | 32 | 5 | 0 | 37 |
| Shared / settings / printing | 8 | 4 | 1 | 13 |
| **Total** | **62** | **13** | **1** | **76** |

The one **missing** item is forgot-password (login button is an unwired stub). Several **partial** items are live SignalR, a save-dialog for exports, “create portal account” (same stub as the web), appointment-summary print (plain HTML, not a web form), and the SQLCipher bundle still being held off.

---

## Staff

| feature | web location | web API endpoint(s) | desktop status | owner bot |
|---------|--------------|---------------------|----------------|-----------|
| Dashboard — today’s counts, list, shortcuts (walk-in, payments, doctor status, patients) | `/staff/dashboard` `app/staff/dashboard/page.tsx` | `GET /api/bookings/staff/today` (active rows + total); `GET /api/queue` | done (`StaffHomePage`) | allen3 |
| Dashboard — check in / undo check-in | same | `PUT /api/bookings/{id}/status` (`CheckedIn` / `Confirmed`) | done | allen3 |
| Dashboard — cancel booking (reason required; not checked-in, not paid) | same, cancel dialog | `PUT /api/bookings/{id}/status` (`Cancelled`, `{ reason }`) | done | allen3 |
| Live refresh on desk screens | dashboard, payments, queue, walk-in | SignalR `/hubs/clinic`: `QueueUpdated`, `PaymentUpdated`, `PatientCheckedIn` | partial: in-process events + 60s poll, not the SignalR hub | allen3 (UI) / allen1 (API client) |
| Payments list (today first, earlier unpaid, waived-awaiting-confirmation) | `/staff/payments` `app/staff/payments/page.tsx` | `GET /api/queue`; `GET /api/bookings/staff/for-payment`; `GET /api/payments/waived-awaiting-confirmation` | done (`StaffPaymentsPage`) | allen3 |
| Record payment (method, optional reference/OR/notes; exact-amount rule) | same, payment dialog | `GET /api/payments/booking/{bookingId}` then `POST /api/payments/{paymentId}/confirm` | done (`RecordPaymentPage`) | allen3 |
| Confirm waiver | same | `POST /api/payments/{paymentId}/confirm-waiver` | done | allen3 |
| Print official receipt / OR after payment | same (and booking detail receipt modal) | *(client-side `printHtml`; no API)* | done (`IPrintDocumentFactory.PaymentReceiptAsync`) | allen5 (print) / allen3 (button) |
| Walk-in wizard — find patient (search, paging), register new patient | `/staff/walk-in` `components/walkin/WalkInWizard.tsx` | `GET /api/patients`; `POST /api/patients` | done (`WalkInWizardView`, `QuickRegisterPage`) | allen3 |
| Walk-in — visit type, Senior/PWD (auto 60+), med-cert flag, check-in, print queue slip | same, step 2 + done | `POST /api/queue` (`patient_id`, `visit_type`, `discount_category`, `med_cert_requested`, `notes`) | done | allen3 |
| Queue board — summary chips (waiting, with doctor, booked, completed, no-show, total) | `/staff/queue` `app/staff/queue/page.tsx` | `GET /api/queue` | done (`StaffQueuePage`) | allen3 |
| Queue actions: check in, call in, hold, complete, no-show | same | `PUT /api/queue/{bookingId}/{action}` where `{action}` is `check-in` \| `call` \| `hold` \| `complete` \| `no-show` | done | allen3 |
| Queue — cancel (reason required) | same, dialog | `PUT /api/queue/{bookingId}/cancel` `{ reason }` | done | allen3 |
| Queue — print slip | same | *(client-side `printHtml`; no API)* | done | allen5 / allen3 |
| Patients list — search, pagination | `/staff/patients` `app/staff/patients/page.tsx` | `GET /api/patients/search` (`q`, `page`, `pageSize`) | done (`StaffPatientsPage`) | allen3 |
| Patient detail — overview + bookings + consultations tabs | `/staff/patients/[id]` `app/staff/patients/[id]/page.tsx` | `GET /api/patients/{patientId}`; `GET /api/bookings` (patient filter); `GET /api/consultations` (patient filter) | done (`StaffPatientDetailPage`) | allen3 |
| Create portal account | same, dialog | *(web itself is unwired: “use the admin/staff invite flow when available”; no API called)* | partial: same unwired stub as the web | allen3 |
| Doctor status — per-doctor radios; bulk Available / Running late / Out today | `/staff/doctor-status` `app/staff/doctor-status/page.tsx` | `GET /api/doctors`; `GET /api/doctors/day-statuses`; `PUT /api/doctors/{doctorId}/day-status` | done (`StaffDoctorStatusPage`) | allen3 |
| Announcements (read-only, resolve staff names) | `/staff/announcements` `app/staff/announcements/page.tsx` | `GET /api/announcements`; `GET /api/staff-accounts` | done (`StaffAnnouncementsPage`) | allen3 |
| Profile — name, contact, save | `/staff/profile` `app/staff/profile/page.tsx` | session uses `GET /api/staff-accounts/me`; page loads `GET /api/staff-accounts/{staffId}`; save `GET`+`PUT /api/staff-accounts/{staffId}` | done (`StaffProfilePage`) | allen3 |
| Profile — change password | same | `POST /api/auth/change-password` | done | allen3 |
| Booking detail — load, timeline, check in, undo check-in, no-show, cancel | `/staff/bookings/[id]` `app/staff/bookings/[id]/page.tsx` (not in the sidebar; opened from the desk) | `GET /api/bookings/{id}`; `PUT /api/bookings/{id}/status` | done (`StaffBookingDetailPage`; reachable from dashboard/queue) | allen3 |
| Booking detail — reschedule modal | same, `components/bookings/RescheduleBookingModal.tsx` | `PUT /api/bookings/{id}/reschedule` | done | allen3 |
| Booking detail — record payment / waive professional fee | same | `GET /api/payments/booking/{bookingId}`; `POST /api/payments/{paymentId}/confirm`; `POST /api/payments/{paymentId}/waive` | done | allen3 |
| Booking detail — view / print receipt, print slip | same | *(client-side print; no API)* | done | allen5 / allen3 |
| Staff bookings **list** | `/staff/bookings` (redirect) | — (redirect to `/staff/queue`) | done: no separate list, same as the web | allen3 |

---

## Admin

| feature | web location | web API endpoint(s) | desktop status | owner bot |
|---------|--------------|---------------------|----------------|-----------|
| Dashboard — today / month stats, doctor share, latest bookings, refresh | `/admin/dashboard` `app/admin/dashboard/page.tsx` | `GET /api/reports/daily-booking-summary`; `GET /api/reports/unpaid-completed-visits`; `GET /api/reports/pending-follow-ups`; `GET /api/doctors`; `GET /api/bookings` | done (`AdminHomePage`) | allen4 |
| Bookings list — search (name/code/id), doctor / status / date filters, clear, walk-in link | `/admin/bookings` `app/admin/bookings/page.tsx` | `GET /api/doctors`; `GET /api/bookings/staff/{scope}`; `GET /api/bookings` | done (`AdminBookingsPage`) | allen4 |
| Bookings — week view, previous/next week (replaces calendar) | same, Week tab; `/admin/calendar` redirects here | same list endpoints | done (List / Week tabs) | allen4 |
| Booking detail — load, timeline, open patient | `/admin/bookings/[id]` `app/admin/bookings/[id]/page.tsx` | `GET /api/bookings/{id}` | done (`AdminBookingDetailPage`) | allen4 |
| Confirm booking (Pending → Confirmed) | same | `PUT /api/bookings/{id}/status` | done | allen4 |
| Reject booking / cancel (reason required) | same, reason modal | `PUT /api/bookings/{id}/status` (`Cancelled`, `{ reason }`) | done | allen4 |
| Confirm payment (ProofSubmitted → Confirmed + Paid + OR) | same | web calls `PUT /api/bookings/{id}/status` (`Confirmed`) only; it does **not** call `POST /api/payments/{id}/confirm` (OR is assigned in the page). Intended payment APIs: `GET /api/payments/booking/{bookingId}`; `POST /api/payments/{paymentId}/confirm` | done on desktop (also records payment / OR) | allen4 |
| Reject proof | same | `PUT /api/bookings/{id}/status` (reject/cancel path, reason) | done (reject) | allen4 |
| Mark complete / no-show | same | `PUT /api/bookings/{id}/status` | done | allen4 |
| Reschedule | same, `RescheduleBookingModal` | `PUT /api/bookings/{id}/reschedule` | done | allen4 |
| Waive payment | same, reason modal | `GET /api/payments/booking/{bookingId}`; `POST /api/payments/{paymentId}/waive` | done | allen4 |
| Refund payment (amount ≤ paid, reason) | same, refund modal | `GET /api/payments/booking/{bookingId}`; `POST /api/payments/{paymentId}/refund` | done | allen4 |
| Print receipt | same | *(client-side `printHtml`; no API)* | done (`PaymentReceiptAsync`) | allen5 / allen4 |
| Print appointment summary | same | *(client-side `printHtml`; no API)* | partial: desktop writes a simple HTML summary (also under Documents/ClinicNode); not the web form layout | allen4 (button) / allen5 (if a form factory method is wanted) |
| Walk-in (same wizard as staff) | `/admin/walk-in` `app/admin/walk-in/page.tsx` | `GET /api/patients`; `POST /api/patients`; `POST /api/queue` | done (`AdminWalkInPage` hosts allen3’s wizard) | allen4 (host) / allen3 (wizard) |
| Doctors list — deactivate / reactivate (confirm) | `/admin/doctors` `app/admin/doctors/page.tsx` | `GET /api/doctors`; `GET /api/doctors/{id}/schedules` (working days); deactivate via `GET`+`PUT /api/staff-accounts/{staffId}` | done (`AdminDoctorsPage`; desktop also has a search box the web list does not) | allen4 |
| Invite / create doctor | `/admin/doctors/new` `components/admin/DoctorForm.tsx` `app/actions/createDoctor.ts` | `POST /api/auth/invite` | done (`AdminDoctorNewPage`) | allen4 |
| Edit doctor — profile, schedule | `/admin/doctors/[id]/edit` `DoctorForm.tsx` | `GET /api/doctors/{id}`; `GET`+`PUT /api/doctors/{id}`; `GET`+`PUT /api/staff-accounts/{staffId}`; `GET`+`PUT /api/doctors/{id}/schedules` | done (`AdminDoctorEditPage`) | allen4 |
| Edit doctor — blocked dates, services | desktop form has these; current web `DoctorForm.tsx` does not call blocked-date or services APIs | web (if added): `GET/POST /api/doctors/{id}/blocked-dates`; `DELETE /api/doctors/blocked-dates/{id}` | done on desktop (extra vs current web) | allen4 |
| Patients list — search, add, pagination (`x–y of total`) | `/admin/patients` `app/admin/patients/page.tsx` | `GET /api/patients/search`; `POST /api/patients` | done (`AdminPatientsPage`) | allen4 |
| Patient detail — overview / edit (incl. middle, email, address) | `/admin/patients/[id]` `app/admin/patients/[id]/page.tsx` | `GET /api/patients/{id}`; `GET`+`PUT /api/patients/{id}` | done (`AdminPatientDetailPage`) | allen4 |
| Patient detail — bookings, consultations, access log tabs | same | `GET /api/bookings`; `GET /api/consultations`; `GET /api/prescription-groups`; `GET /api/audit-logs/patient/{patientId}/access` | done | allen4 |
| Patient / clinic data requests — list, schedule, mark done, reject | patient detail + settings `components/admin/DataRequestsPanel.tsx` | `GET /api/privacy/data-requests`; `PUT /api/privacy/data-requests/{id}` | done | allen4 |
| Staff list — search, invite, edit, deactivate / reactivate, revoke invite | `/admin/staff` `app/admin/staff/page.tsx` `app/actions/inviteStaffMember.ts` `revokeStaffInvite.ts` | `GET /api/staff-accounts` (desktop search may use `GET /api/staff-accounts/search`); invite `POST /api/auth/invite`; edit `GET`+`PUT /api/staff-accounts/{staffId}`; revoke `GET /api/staff-accounts/{staffId}` then `DELETE /api/auth/users/{userId}` | done (`AdminStaffPage`) | allen4 |
| Announcements — create, edit, activate/deactivate, delete (confirm) | `/admin/announcements` `app/admin/announcements/page.tsx` | `GET /api/announcements`; `POST /api/announcements`; `PUT /api/announcements/{id}`; `DELETE /api/announcements/{id}` | done (`AdminAnnouncementsPage`) | allen4 |
| Settings — General (name, address, default payment mode, etc.) | `/admin/settings` tab General `app/admin/settings/page.tsx` | `GET /api/settings`; `PUT /api/settings` | done (`AdminSettingsPage`) | allen4 |
| Settings — Hours | tab Hours | `GET /api/admin/operating-hours`; `PUT /api/admin/operating-hours` | done | allen4 |
| Settings — Fees (consultation, follow-up, senior/PWD, med cert, discount %) | tab Fees | `GET /api/settings`; `PUT /api/settings` | done | allen4 |
| Settings — Payments (accepted methods: Cash, GCash, Maya, BankTransfer) | tab Payments | `GET /api/admin/payment-methods`; `PUT /api/admin/payment-methods` | done | allen4 |
| Settings — Privacy & consent (policy text, bump consent version) | tab Privacy & consent | `GET /api/settings`; `PUT /api/settings` | done | allen4 |
| Settings — Branding (logo / favicon) | tab Branding | *(web is preview-only; no upload API — “Phase 9”)* | partial: desktop has a Branding tab and save; web has no persistence API yet | allen4 |
| Settings — clinic-wide data requests (on this page) | settings, `DataRequestsPanel` | `GET /api/privacy/data-requests`; `PUT /api/privacy/data-requests/{id}` | done | allen4 |
| Settings — Services tab | not on the current web settings tabs | — | done on desktop (extra vs web) | allen4 |
| Audit logs — search, entity filter, pagination, entity id on rows | `/admin/audit-logs` `app/admin/audit-logs/page.tsx` | `GET /api/audit-logs/search` (`q`, `entityType`, `page`, `pageSize`); names via `GET /api/staff-accounts` | done (`AdminAuditPage`) | allen4 |
| Reports — date range, doctor ratings, daily bookings, unpaid completed, pending follow-ups, doctor earnings | `/admin/reports` `app/admin/reports/page.tsx` | `GET /api/reports/doctor-ratings`; `GET /api/reports/daily-booking-summary`; `GET /api/reports/unpaid-completed-visits`; `GET /api/reports/pending-follow-ups`; `GET /api/reports/doctor-earnings` | done (`AdminReportsPage`) | allen4 |
| Reports — Export CSV | same, Export CSV button | *(client-side from already-fetched rows; no export API)* | partial: writes `clinic-reports_{from}_to_{to}.csv` under Documents/ClinicNode; no user-facing save dialog yet (IFileSaver is now registered — wire through it) | allen4 |
| Register this node | *(desktop only; no web screen)* | node registration / handshake via sync (not a public FE page) | done (`AdminRegisterNodePage`, admin System nav) | allen5 |
| This computer (node settings) | *(desktop only; no web screen)* | health probe `GET {baseUrl}/health` (`ISyncEndpoint.TestConnectionAsync`); sync URL is local config | done (`NodeSettingsPage`, admin-only flyout); live Online / Offline / Local-only pill + Recheck | allen5 |

---

## Shared / settings / printing

| feature | web location | web API endpoint(s) | desktop status | owner bot |
|---------|--------------|---------------------|----------------|-----------|
| Login (email + password; role from the account) | `/login` `app/login/page.tsx` `app/api/session/login/route.ts` | browser `POST /api/session/login` (BFF) → `POST /api/auth/login`; later session `POST /api/auth/refresh-token`; `GET /api/auth/me` / `GET /api/staff-accounts/me` | done (`LoginPage`, local hashes today) | unassigned |
| Remember my email on this computer | `/login` | *(local only; no API)* | done | unassigned |
| Forgot password | `/forgot-password` `app/forgot-password/page.tsx` `lib/auth/account.ts` | `POST /api/auth/forgot-password` (and reset `POST /api/auth/reset-password` / `POST /api/auth/set-password`) | **missing**: login “Forgot password?” only shows “not available on this computer yet” | unassigned |
| Sign out | shell | `POST /api/auth/logout` | done (local session clear) | unassigned |
| Top bar — global search, sync chip | web `AppShell`; desktop `Views/Shared/TopBar/TopBarView.cs` | web search `GET /api/search` | done (wired as Shell TitleView; TODOs in the file are stale) | allen4 |
| Sync now + pending/conflict banner | desktop shell (web has no node sync bar) | sync service / node APIs (allen2); not FE staff/admin routes | done | allen2 |
| Sync conflict review | desktop route `sync-conflicts` | sync APIs (allen2) | done (`SyncConflictsPage`) | allen2 |
| Prescription pad print (A5) | consult / appointment / chart `lib/print`, `AppointmentPrintActions.tsx` | *(client-side; no API — data already loaded)* | done (`ClinicFormsPrintDocumentFactory.PrescriptionAsync`) | allen5 |
| Lab request print (4.3×6.4 in) | same | *(client-side; no API)* | done (`LabRequestAsync`) | allen5 |
| Medical certificate print (A5) | same | *(client-side; no API)* | done (`MedicalCertificateAsync`) | allen5 |
| Queue slip print | walk-in, queue | *(client-side; no API)* | done (`QueueSlipAsync`) | allen5 |
| Payment receipt / OR print (pending-sync mark when unsynced) | payments, booking detail | *(client-side; no API)* | done (`PaymentReceiptAsync`) | allen5 |
| SQLCipher at-rest encryption + EncryptedDatabaseStartup | *(desktop only)* | — | partial: code and tests are ready; **csproj bundle swap and MauiProgram startup hook are held off** (allen decision) | allen5 |
| Local encrypted backup / restore (`.cnbackup`) | *(desktop only)* | — | done (NodeSettings Backup/Restore; held-off encryption does not block the format) | allen5 |

---

## Notes for the API-backed port

- Almost every “done” desktop feature still talks to **local EF / SQLite**. The endpoints in this table are what the web calls today and should be the first targets for allen1’s API client, with SQLite as the fallback when the API is unreachable.
- `confirmPayment` / `waivePayment` / `refundPayment` / `confirmWaiver` always resolve `payment_id` first with `GET /api/payments/booking/{bookingId}`.
- Admin “Confirm payment” on the web currently skips the payments confirm API (booking status only). Desktop already records the payment; the port should use the payment endpoints.
- Report and list “export” / “print” on the web are client-side. No export REST API exists.
- Logo/favicon upload has no API yet on the web.
- Queue action paths are literal suffixes: `check-in`, `call`, `hold`, `complete`, `no-show`, plus `cancel`.
- Health for the node settings pill is `GET {configuredBaseUrl}/health` (SyncOptions.HealthPath), not a staff/admin FE route.

<!-- JJQR - END -->
