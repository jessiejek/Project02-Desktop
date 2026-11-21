<!-- JJQR - AI (Nov 21, 2026) - inventory of doctor/staff/admin screens to port into the native MAUI Clinic Node. Do not lose features. Patient portal is out of scope. -->

# Native screen inventory — Clinic Node (MAUI)

Source: `Project02-fe` routes and components as of 21 Nov 2026 (Asia/Manila). The canonical consultation UI is `/doctor/consultation2/[bookingId]` (~4,098 lines); `/doctor/consultation/[bookingId]` only redirects there. Patient-portal routes (`/patient/*`, `/register`) are **out of scope** and are listed only so they are not ported by accident.

**Priority:** P0 = must work fully offline on day one (login, patients, queue, consultation, payments confirmation). P1 = needed for 1:1 parity in the next slice. P2 = can follow (reports, email-dependent, rare admin).

**Size:** S = a day or two, M = several days, L = a week or more (or a cluster of dialogs).

**Counts (in-scope unique screens, excluding the consultation redirect and the settings→profile redirect):** 42 screens. **P0: 9 · P1: 21 · P2: 12.** Shared dialogs and print jobs are called out under the screen that owns them, not counted twice.

---

## Shared (all clinic roles)

| ID | Route / surface | Purpose | User actions | Dialogs / wizard steps | API endpoints | SignalR | Prints | Offline | Size |
|----|-----------------|---------|--------------|------------------------|---------------|---------|--------|---------|------|
| S-LOGIN | `/login` | Sign in as doctor, staff or admin | Email + password; **"Remember my email on this computer"** (email only, never the password); role is taken from the account | Validation errors; locked-account message | `POST /api/session/login` → `POST /api/auth/login`; `POST /api/auth/refresh-token`; `GET /api/auth/me` | — | — | **P0** (local hashes, node-issued session) | S |
| S-FORGOT | `/forgot-password` | Request a reset email | Submit email | Success/failure toast | `POST /api/auth/forgot-password`; `POST /api/auth/reset-password`; `POST /api/auth/set-password` | — | — | **P2** (no SMTP offline — hide or "needs internet") | S |

Out of scope (do not port): `/register`, `/privacy`, patient consent/export self-service, Google/Facebook login, public marketing home.

---

## Doctor portal

Nav (from `nav-config.ts`): **Patient care** — Today, Appointments, Patient charts. **Your practice** — Performance, Availability, Templates, Profile. (`/doctor/settings` only redirects to Profile.)

### D-TODAY — `/doctor/dashboard` — P0 / M

Today's list of this doctor's bookings / queue (checked-in, in progress, on hold, upcoming). Open a consultation, see doctor day-status, catch up after SignalR reconnect.

- **Actions:** open consult, refresh, filter by status, start a walk-in consult when there is no live queue (offline requirement: consult without seeing the live queue).
- **API:** `GET /api/bookings/doctor/today`, `GET /api/bookings/doctor/today-summary`, `GET /api/doctors/{id}/day-status`, `GET /api/queue`, `GET /api/announcements`.
- **SignalR:** `QueueUpdated`, `PatientCheckedIn` (refetch).
- **Prints:** none on this page.

### D-APPT-LIST — `/doctor/appointments` — P1 / M

List of the doctor's appointments (day/week, search).

- **Actions:** open detail, filter dates, search patient name.
- **API:** `GET /api/bookings` (doctor-scoped), `GET /api/bookings/doctor/patients`.
- **SignalR:** `QueueUpdated`.

### D-APPT-DETAIL — `/doctor/appointments/[id]` — P1 / M

One booking: patient, slot, status, payment, consult link, print actions (`AppointmentPrintActions`).

- **Actions:** open consult, print Rx/lab/cert if already issued, view history.
- **API:** `GET /api/bookings/{id}`, consult/Rx/lab/cert by booking, `GET /api/settings`, `GET /api/doctors/{id}`, `GET /api/patients/{id}`.
- **Prints:** prescription A5, lab 4.3×6.4 in, medical certificate A5 (if issued).

### D-CONSULT — `/doctor/consultation2/[bookingId]` (canonical) — **P0 / L**

The doctor's consultation workspace. This is the largest screen and the first port. `/doctor/consultation/[bookingId]` redirects here. `/doctor/consultation/[bookingId]/vitals` is a legacy vitals-only page (merge into this screen; do not keep a second vitals route).

**Visible sections (order).** Diagnosis is kept in the data model at index 2 but is **hidden from nav and from completion gates** (`SHOW_DIAGNOSIS_STEP = false`). Vaccinations sit behind **More**. Professional Fee is always the **last** visible step and is **required**. The yellow "{n} required sections left" banner is hidden (`SHOW_REQUIRED_LEFT_BANNER = false`); gating itself is unchanged.

| # | Section | In nav? | Required to complete? | Done when |
|---|---------|---------|----------------------|-----------|
| 0 | Clinical notes (chief complaint + SOAP: Subjective, Objective, Assessment, Plan; optional doctor notes; SOAP templates / phrases) | yes | **Yes** | Chief complaint is non-empty |
| 1 | Vital Signs | yes | **Yes** | **BP + HR** when those templates exist (`blood_pressure`, `pulse_rate`); otherwise every default template has a value |
| 2 | Diagnosis / Impressions (ICD-10 + custom, type Primary/Secondary/Differential/Comorbidity) | **hidden** | no (while the flag is false) | ≥1 diagnosis (only if the step is shown) |
| 3 | Prescription | yes | **Yes** | ≥1 line **or** doctor confirms "no medicines this visit" |
| 4 | Lab Orders | yes | no | (optional) |
| 5 | Vaccinations | behind **More** | no | (optional) |
| 6 | Follow-up | yes | **Yes** | Mode is **dated** (date required), **PRN** (saved with today's date, reason "PRN / return if needed"), or **No follow-up for this visit** |
| 7 | Medical Certificate | yes | no | (optional) issue/update & print |
| 8 | Professional Fee Decision | yes, **always last** | **Yes** | **Charge**: amount > ₱0 and ≤ ₱100,000 (defaults to the computed clinic fee; charge amount follows the computed total). **Waive**: a waive reason is required. Completing a waive **auto-waives the payment**; staff later see **Confirm waiver**. |

**Other actions and dialogs:**

- **Save draft** (upsert consultation and child aggregates).
- **Save & next** — save the draft, then open the next visible section; on the last section the button stays "Save draft"; a failed save stays on the current section. Prev/Next change section without saving.
- **Review & complete** — opens a **completion checklist** (per-section summary of what was entered). Disabled until every required section is done; names the missing sections. Sets `completeAttempted` so empty required steps show a warning indicator (empty circle until then).
- Patient banner, last-visit SOAP, patient history modal, growth chart (paeds), drug-safety / peds dosing helpers, template pickers (SOAP, Rx, certificate), Rx timing / SIG / as-needed / controlled-substance fields, lab confirm dialog, certificate confirm dialog, amendment history (finished consults are immutable; amendments are a later append-only path).
- **Cancel** is a staff-dashboard action, not this page.

**API (all of these must work offline against local SQLite):**

- `GET/PUT /api/consultations/by-booking/{bookingId}`
- `GET/PUT /api/consultations/{id}/diagnoses`
- `PUT /api/vitals/by-booking/{bookingId}`; `GET /api/vital-field-templates`
- `PUT /api/prescription-groups/by-booking/{bookingId}`; `GET /api/medicines`; `GET/POST/PUT/DELETE /api/prescription-templates`; `GET/POST/PUT/DELETE /api/doctor-favorite-medicines`
- `GET/PUT /api/lab-orders/by-consultation/{id}` (and by-booking); `GET /api/lab-test-catalog`
- `GET/PUT /api/patient-vaccinations/by-consultation/{id}`
- `GET/PUT/DELETE /api/follow-ups/by-consultation/{id}`
- `GET/PUT/DELETE /api/medical-certificates/by-consultation/{id}`; certificate templates
- `GET/POST/PUT/DELETE` SOAP phrases & templates; `GET /api/icd10-codes`; doctor diagnosis templates
- `GET /api/patients/{id}` (+ allergies, lab-values); `GET /api/bookings/{id}`; `GET /api/settings`; `GET /api/payments/booking/{bookingId}`
- `GET /api/queue` (optional — consult must work **even without** the live queue)

**SignalR:** none required on this page (queue updates are for Today).

**Prints:** prescription **A5 (148×210 mm)**; lab request **4.3×6.4 in**; medical certificate **A5**. Letterhead is pre-printed stationery (not taken from clinic settings).

### D-PATIENTS, D-CHART — `/doctor/patients`, `/doctor/patients/[id]` — P1 / M and P1 / L

Search/list the doctor's patients; open a chart (history, SOAP, Rx, labs, files, vaccinations, allergies, growth).

- **API:** `GET /api/bookings/doctor/patients`, `GET /api/patients/{id}`, consultations, Rx, labs, files, vaccinations, allergies, lab-values, `POST /api/audit-logs` (access).
- **Prints:** Rx / lab / cert from a past visit.
- Offline: **P1** for full chart history; **P0** is "look up a synced patient by name or create a new one" (also from Today / consult).

### D-RX-CREATE / D-RX-VIEW — `/doctor/patients/[id]/prescriptions/create` and `.../[prescriptionId]` — P1 / M and S

Standalone Rx editor (same fields as the consult Prescription step). Prefer one shared editor.

### D-PERF — `/doctor/performance` — P2 / M

KPI cards, charts, period toggle. `GET /api/doctor/performance`. Online-ish / after sync.

### D-SCHED — `/doctor/schedule` — P1 / M

Schedules, blocked dates, day status (the doctor can also set this; staff have a dedicated page).

- **API:** `GET/PUT /api/doctors/{id}/schedules`, `GET/POST /api/doctors/{id}/blocked-dates`, `DELETE /api/doctors/blocked-dates/{id}`, `GET/PUT /api/doctors/{id}/day-status`.

### D-TEMPL — `/doctor/templates` — P1 / L

Workspace tabs: SOAP, Diagnoses, Rx, Favorites, Phrases, Certificates. Undoable delete, reorder, restore built-ins (`POST /api/templates/restore-built-ins`). Large, but consult depends on reading these templates (**P0 to consume**, P1 to edit).

### D-PROFILE — `/doctor/profile` — P1 / S

Name, license, PTR, S2, avatar, change password. `GET /api/doctors/me`, `PUT /api/doctors/{id}`, `POST /api/auth/change-password`, `POST /api/auth/avatar`. Password change is local + queued for sync.

---

## Staff portal

Nav: Dashboard, Payments, Walk-In, Queue, Patients, Doctor Status, Announcements, Profile. Bookings list/detail are reachable from the dashboard (not in the sidebar).

### ST-DASH — `/staff/dashboard` — P0 / M

Today's clinic picture: arrivals, in-progress, unpaid, cancelled. **Cancel** a booking (with reason). Links to queue, walk-in, payments.

- **API:** `GET /api/bookings/staff/today`, `PUT /api/bookings/{id}/cancel` or `PUT /api/queue/{id}/cancel`, `GET /api/doctors/day-statuses`, announcements.
- **SignalR:** `QueueUpdated`, `PaymentUpdated`, `PatientCheckedIn`.

### ST-PAY — `/staff/payments` — P0 / L

Collect professional fees and confirm waivers.

- **Actions:** open payment dialog; method + optional reference / OR / notes; **exact-amount** rule (received == amount due; blank received = exact fee); cannot collect a waived fee. **Confirm waiver** for doctor-waived visits still awaiting front-desk acknowledgement (`GET /api/payments/waived-awaiting-confirmation`, `POST /api/payments/{id}/confirm-waiver`). Refunds are admin/online (do **not** allow refunds offline — study doc 5.5).
- **API:** `GET /api/bookings/staff/for-payment`, `GET /api/payments/booking/{bookingId}`, `POST /api/payments/{id}/confirm`, `POST /api/payments/{id}/waive` (only if the doctor already set `pf_decision = Waive`), `POST /api/payments/{id}/refund` (online only), `GET /api/admin/payment-methods`.
- **SignalR:** `PaymentUpdated`.
- **Prints:** receipt / OR (derived from booking id; mark **pending sync** when offline).

### ST-WALK — `/staff/walk-in` (same `WalkInWizard` as admin) — P0 / L

Register or look up a patient, pick doctor, Senior/PWD discount (priority lane), check in.

- **Wizard:** (1) find/create patient, (2) doctor + visit notes + discount, (3) confirm + **print queue slip**.
- **API:** `GET /api/patients/search`, `POST /api/patients` (client-supplied `PatientId`), `POST /api/bookings/walk-in` or `POST /api/queue`, `GET /api/doctors`, `GET /api/doctors/day-statuses`.
- **Prints:** **queue slip** (`R-nnn` / `S-nnn`), in-app `.slip` layout.
- **SignalR:** `PatientCheckedIn` / `QueueUpdated` after check-in.
- Numbering: the **node is the only issuer** of queue numbers while offline; patient codes from a **pre-reserved MF- block**.

### ST-QUEUE — `/staff/queue` — P0 / M

Live queue, two lanes: **S (senior/PWD) always ahead of R (regular)**; FCFS within a lane. Pre-lane `Q-` treated as regular.

- **Actions:** check in a booked patient, call, hold, complete, no-show, cancel, print slip.
- **API:** `GET /api/queue`, `POST /api/queue`, `PUT /api/queue/{bookingId}/check-in|call|hold|complete|no-show|cancel`.
- **SignalR:** `QueueUpdated`, `PatientCheckedIn`.
- **Prints:** queue slip.

### ST-PATIENTS / ST-CHART — `/staff/patients`, `/staff/patients/[id]` — P0 / M and P1 / M

Look up / register patients; view/edit demographics (full clinical chart is doctor-led). Quick register from the list.

- **API:** `GET /api/patients`, `GET /api/patients/search`, `POST /api/patients`, `PUT /api/patients/{id}`, `GET/PUT /api/patients/{id}/allergies`.

### ST-DOC-STATUS — `/staff/doctor-status` — P1 / S

Radios for each doctor's day status (available / away / etc.). `GET /api/doctors/day-statuses`, `PUT /api/doctors/{id}/day-status`.

### ST-ANN — `/staff/announcements` — P2 / S

Read clinic announcements. `GET /api/announcements`.

### ST-PROFILE — `/staff/profile` — P1 / S

`GET /api/staff-accounts/me`, `PUT /api/staff-accounts/{id}`, change password.

### ST-BOOK-LIST / ST-BOOK-DETAIL — `/staff/bookings`, `/staff/bookings/[id]` — P1 / M

All bookings, reschedule modal (`RescheduleBookingModal`), cancel, check-in, payment link.

- **API:** `GET /api/bookings/staff/all` (and today), `GET /api/bookings/{id}`, `PUT /api/bookings/{id}/reschedule`, `PUT /api/bookings/{id}/status`, `GET /api/bookings/closed-dates`, doctor schedules.

---

## Admin portal

Nav: **Operations** — Dashboard, Bookings, Walk-In. **Management** — Doctors, Patients, Staff, Announcements. **System** — Settings, Audit Logs, Reports. `/admin/calendar` exists but is not in the sidebar (P2).

| ID | Route | Purpose | Main API | Offline | Size |
|----|-------|---------|----------|---------|------|
| A-DASH | `/admin/dashboard` | Clinic-wide today / counts | staff today, day-statuses, announcements, reports summaries | P1 | M |
| A-BOOK-LIST | `/admin/bookings` | All bookings, filters | `GET /api/bookings`, cancel/reschedule/status | P1 | M |
| A-BOOK-DETAIL | `/admin/bookings/[id]` | One booking + print | booking + payment + consult + prints | P1 | M |
| A-WALK | `/admin/walk-in` | Same walk-in wizard as staff | same as ST-WALK | P1 (staff already covers the desk) | L |
| A-CAL | `/admin/calendar` | Calendar of bookings | bookings + closed-dates + schedules | P2 | M |
| A-DOC-LIST | `/admin/doctors` | List doctors | `GET /api/doctors/admin` | P1 | S |
| A-DOC-NEW | `/admin/doctors/new` | Invite/create doctor (`DoctorForm`) | `POST /api/auth/invite`, doctor + schedules + services | P1 | M |
| A-DOC-EDIT | `/admin/doctors/[id]/edit` | Edit doctor, services, schedule, blocked dates | `PUT /api/doctors/{id}`, schedules, services, blocked-dates, deactivate | P1 | M |
| A-PAT-LIST | `/admin/patients` | All patients | `GET /api/patients`, search, create | P1 | M |
| A-PAT-DETAIL | `/admin/patients/[id]` | Chart + privacy / data requests | patients, consents, `GET/PUT /api/privacy/data-requests`, export | P1 | L |
| A-STAFF | `/admin/staff` | Invite, edit, revoke staff | `GET /api/staff-accounts`, invite, `DELETE /api/auth/users/{id}`, `PUT /api/staff-accounts/{id}` | P1 | M |
| A-ANN | `/admin/announcements` | CRUD announcements | `GET/POST/PUT/DELETE /api/announcements` | P2 | S |
| A-SET | `/admin/settings` | Clinic name, fees, hours, payment methods, branding, privacy text | `GET/PUT /api/settings`, `GET/PUT /api/admin/operating-hours`, `GET/PUT /api/admin/payment-methods`, `GET/POST /api/services` | P2 (cloud wins on sync; local copy needed to compute fees **P0**) | M |
| A-AUDIT | `/admin/audit-logs` | Search access / change logs | `GET /api/audit-logs`, `/search`, `/patient/{id}/access` | P2 | M |
| A-REP | `/admin/reports` | Ratings, daily bookings, unpaid completed, pending follow-ups, doctor earnings | `GET /api/reports/*` | P2 | L |

Admin settings **fee schedule** (`FeeConsultation` ₱450, `FeeFollowUp` ₱350, `FeeSeniorPwd` ₱400, `FeeMedCert` ₱50, `DiscountPct` 20%) must be available offline because the consult fee step uses it (P0 data, P2 editor).

---

## Print jobs (shared)

| Job | Size | Where | Offline |
|-----|------|-------|---------|
| Prescription pad | A5, 148 mm × 210 mm | Consult, appointment, chart | P0 |
| Lab request | 4.3 in × 6.4 in | Consult, appointment, chart | P0 |
| Medical certificate | A5, 148 mm × 210 mm | Consult, appointment, chart | P0 |
| Queue slip | compact `.slip` (thermal-friendly later) | Walk-in, queue | P0 |
| Payment receipt / OR | derived from booking id; "pending sync" note when unsynced | Payments | P1 |

Letterhead is pre-printed stationery. MAUI printing approach is in `MAUI-ARCHITECTURE.md`.

---

## SignalR events (hub `/hubs/clinic`)

Server-push only. Groups: `staff` (Admin+Staff), `doctor:{doctorId}`.

| Event | Published from | Screens that refetch |
|-------|----------------|----------------------|
| `PatientCheckedIn` | `QueueController` check-in | Staff/doctor dashboards, queue |
| `QueueUpdated` | Queue status changes; booking cancel/reschedule | Queue, dashboards, appointments |
| `PaymentUpdated` | Payments confirm/waive/refund; consult complete (auto-waive) | Payments, dashboards |

On the node, live updates are in-process (same machine). A status bar should also show **pending sync** / last successful cloud sync. Catch-up after reconnect (already in the web client) should be mirrored.

---

## Flags and product rules the native UI must not lose

- `SHOW_DIAGNOSIS_STEP = false` — Diagnosis stays in the model (index 2) and is still saved, but is hidden from nav and completion gates. Flip of this flag must restore the step without a rewrite.
- Fee decision is the last required step; Charge amount follows the computed total; exact-amount collections; doctor waive auto-waives payment and leaves it for staff **Confirm waiver**.
- Queue lanes R-/S-; S always sorts ahead of R; Senior = 60+ (RA 9994) or explicit Senior/PWD discount.
- Finished consultations are immutable; a short post-complete window still accepts diagnoses (web: `CompletionWindow`) then changes go through Amend.
- Remember email only, never the password.
- No patient portal, no public `/register`, no cloud-only email verification / password reset while offline.

