<!-- JJQR - AI (Nov 21, 2026) - functional parity review of Clinic Node (MAUI) vs Project02-fe. Visual notes are appendix only. -->

# Clinic Node — functional parity review

**Date:** 21 Nov 2026, ~2:30 PM Asia/Manila  
**Compared:** web app at `Project02-fe` (running at http://localhost:3000) against MAUI at `Project02-desktop/ClinicNode`.  
**Inventory source:** `docs/NATIVE-SCREEN-INVENTORY.md` (42 in-scope screens). Patient portal is out of scope and is not scored.  
**Method:** each web feature / action / validation / dialog / state is marked **yes** (1), **partial** (0.5), or **missing** (0). Score = points ÷ items, as a percentage. Placeholder screens that only show “will be ported next” score **0%**.  
**Owners:** `allen-core` = shared AppShell / login / doctor screens; `allen3` = staff; `allen4` = admin; `allen5` = print layouts.  
**No app code was edited in this review.**

Web reference screenshots (Playwright, 1440×900 @2x, logged in with the e2e dev accounts): `docs/ui-review/web/`. MAUI window capture was **not possible** (the app was not running; `screencapture -l` / System Events lack permission). Built screens were compared from XAML / C#.

**Headline:** of the 42 inventoried screens, only about 8 have real UI. Overall functional parity across the inventory is roughly **12%**. Screens that already exist average about **50%**. The biggest product gaps are staff/admin Walk-In (wrong flow), the doctor Today dashboard (list only), and the consultation workspace (core loop present, most helpers missing). Visual polish is deferred; a short leftover visual list is in the [appendix](#appendix-visual-notes-deferred).

---

## Summary table

| Screen | ID | Owner | Status in MAUI | Score | Primary files |
|--------|----|-------|----------------|-------|---------------|
| App shell / nav / top bar | — | allen-core | Built (partial) | **44%** | `AppShell.xaml(.cs)`, `Controls/ClinicSidebar.cs` |
| Login | S-LOGIN | allen-core | Built | **90%** | `Views/LoginPage.xaml(.cs)` |
| Forgot password | S-FORGOT | allen-core | Link only (alert) | **10%** | `LoginPage.xaml.cs` `OnForgotPassword` |
| Doctor — Today | D-TODAY | allen-core | Built (thin) | **20%** | `Views/Doctor/TodayPage.xaml(.cs)` |
| Doctor — Consultation | D-CONSULT | allen-core | Built (core loop) | **48%** | `Views/Doctor/ConsultationPage.xaml(.cs)` |
| Doctor — New walk-in consult | (offline Today action) | allen-core | Built | **40%** | `Views/Doctor/NewWalkInPage.xaml(.cs)` |
| Doctor — Appointments list | D-APPT-LIST | allen-core | Placeholder | **0%** | `AppShell.xaml.cs` |
| Doctor — Appointment detail | D-APPT-DETAIL | allen-core | Missing | **0%** | — |
| Doctor — Patient charts list | D-PATIENTS | allen-core | Placeholder | **0%** | `AppShell.xaml.cs` |
| Doctor — Patient chart | D-CHART | allen-core | Placeholder | **0%** | — |
| Doctor — Standalone Rx create/view | D-RX-CREATE / D-RX-VIEW | allen-core | Missing | **0%** | — |
| Doctor — Performance | D-PERF | allen-core | Placeholder | **0%** | `AppShell.xaml.cs` |
| Doctor — Availability | D-SCHED | allen-core | Placeholder | **0%** | `AppShell.xaml.cs` |
| Doctor — Templates | D-TEMPL | allen-core | Placeholder | **0%** | `AppShell.xaml.cs` |
| Doctor — Profile | D-PROFILE | allen-core | Placeholder | **0%** | `AppShell.xaml.cs` |
| Staff — Front desk today | ST-DASH | allen3 | Built | **62%** | `Views/Staff/StaffHomePage.xaml(.cs)` |
| Staff — Collect payments | ST-PAY | allen3 | Built | **68%** | `StaffPaymentsPage.xaml(.cs)`, `RecordPaymentPage.cs` |
| Staff — Queue | ST-QUEUE | allen3 | Built | **62%** | `Views/Staff/StaffQueuePage.xaml(.cs)` |
| Staff — Walk-in check-in | ST-WALK | allen3 | Built, **wrong flow** | **25%** | Reuses `NewWalkInPage` via `staff-walk-in` |
| Staff — Patients list / chart | ST-PATIENTS / ST-CHART | allen3 | Placeholder | **0%** | `AppShell.xaml.cs` |
| Staff — Doctor status | ST-DOC-STATUS | allen3 | Placeholder | **0%** | `AppShell.xaml.cs` |
| Staff — Announcements | ST-ANN | allen3 | Placeholder | **0%** | `AppShell.xaml.cs` |
| Staff — Profile | ST-PROFILE | allen3 | Placeholder | **0%** | `AppShell.xaml.cs` |
| Staff — Bookings list / detail | ST-BOOK-LIST / ST-BOOK-DETAIL | allen3 | Missing | **0%** | — |
| Admin — Clinic overview | A-DASH | allen4 | Placeholder stub | **5%** | `Views/Admin/AdminHomePage.xaml(.cs)` |
| Admin — Walk-in check-in | A-WALK | allen4 | Built, **wrong flow** | **25%** | Reuses `NewWalkInPage` via `admin-walk-in` |
| Admin — Bookings list / detail | A-BOOK-LIST / A-BOOK-DETAIL | allen4 | Placeholder / missing | **0%** | `AppShell.xaml.cs` |
| Admin — Calendar | A-CAL | allen4 | Missing | **0%** | — |
| Admin — Doctors list / new / edit | A-DOC-LIST / NEW / EDIT | allen4 | Placeholder | **0%** | `AppShell.xaml.cs` |
| Admin — Patients list / detail | A-PAT-LIST / A-PAT-DETAIL | allen4 | Placeholder | **0%** | `AppShell.xaml.cs` |
| Admin — Staff | A-STAFF | allen4 | Placeholder | **0%** | `AppShell.xaml.cs` |
| Admin — Announcements CRUD | A-ANN | allen4 | Placeholder | **0%** | `AppShell.xaml.cs` |
| Admin — Settings | A-SET | allen4 | Placeholder (fees exist in SQLite) | **0%** | `AppShell.xaml.cs` |
| Admin — Audit logs | A-AUDIT | allen4 | Placeholder | **0%** | `AppShell.xaml.cs` |
| Admin — Reports | A-REP | allen4 | Placeholder | **0%** | `AppShell.xaml.cs` |
| Print jobs (Rx, lab, cert, slip, receipt) | — | allen5 | Factory + consult/queue hooks | **80%** | `ClinicNode.Core/Printing/*`, `MauiPrinter.cs` |

---

## Top 10 functional gaps

1. **Staff and admin Walk-In use the doctor’s “Start consultation” form** (`NewWalkInPage`), not the web `WalkInWizard`. There is no Patient → Confirm stepper, no “Check in and get queue number”, and no “Print slip” handoff. Front desk cannot run the real check-in flow.
2. **Doctor Today is a flat visit list.** The web “Your clinic today” dashboard is missing: day-status radios, Needs attention (Start / Resume / Finish chart / View chart), queue summary, Open appointments, Performance, report tiles, recent bookings.
3. **Consultation is a simplified editor.** The required-section loop (SOAP, vitals, Rx or “no medicines”, follow-up, fee last, Save draft, Save & next, Review & complete, prints) exists, but Next (no-save), the completion **checklist** dialog, View Patient History, last-visit SOAP, SOAP/Rx/cert templates, ICD-10 diagnoses, medicine catalogue / SIG / favorites, lab catalogue, and a real More menu are missing. The Diagnosis step is an empty stub.
4. **Most P1 screens are placeholders** (appointments, patient charts, templates, availability, profile, staff patients, doctor status, almost all admin). Nav items open an alert, not the feature.
5. **Staff Queue is missing Check in** for booked / pending arrivals and **Open consult** for in-progress visits. **Complete** is offered on Checked-in / On hold (web only allows it in In progress). Button label is “Call”, web is “Call in”.
6. **Payments exact-amount validation is looser than the web.** Web `checkAmountReceived` rejects a blank and anything other than the exact fee. MAUI allows a blank amount (server then stores the exact fee) and only checks “is a number” in the dialog. No “Waiting to pay / Total due / Waived — to confirm” totals. No payment receipt print.
7. **No global search top bar** (web `Topbar` searches patients, doctors, bookings), no notification bell, no doctor day-status / user badge in the chrome.
8. **Forgot password** is a “needs internet, not available yet” alert — the real email-reset screens are not ported (acceptable to hide offline, but the flow is still missing).
9. **Admin dashboard is a stub** (“inventoried for a 1:1 port”) — no Clinic overview stats, no doctor-share chart, no Latest bookings table, no Refresh.
10. **Staff dashboard is missing the glance stat cards, the “ready to pay” banner + Go to payments, and shortcuts to Doctor status and Patients** (those destination screens are unbuilt). Arrivals check-in / undo / cancel-with-reason do work.

---

## Per-owner fix lists (compact, forwardable)

### allen-core (shell, login, doctor)

- **Shell:** add the web top bar — global search, notifications, signed-in name + initials, doctor day-status badge; optional 260→72 collapse. Keep role sidebars and Logout. File: `AppShell.xaml.cs`, new top-bar control.
- **Login:** port or clearly hide Forgot password when offline (don’t leave a dead-end alert as the only path). Optional: Privacy Notice link if you want legal parity; skip Create-an-account (patient portal is out of scope). File: `LoginPage.xaml(.cs)`.
- **Today:** rebuild against `/doctor/dashboard` — title “Your clinic today”; Needs attention cards with **Start consultation / Resume consultation / Finish chart / View chart**; day-status **Available / Running Late / Unavailable Today**; queue summary + Reload queue; links to Appointments and Performance; report tiles (pending verifications, on hold, unpaid completed, follow-ups). Keep “New walk-in consult” as the offline extra. File: `TodayPage.xaml(.cs)`.
- **Consultation (priority order):** (1) add **Next** (navigate only, no save) beside Prev; (2) **Review & complete** must open a completion checklist (per-section summary), not only an alert; (3) patient banner + **View Patient History** + last-visit SOAP; (4) **More/Less** only for Vaccinations, fee step always last, Diagnosis stays hidden; (5) ICD-10 / custom diagnoses so the hidden step can still be saved; (6) SOAP / Rx / cert template pickers + Save as / Manage / Undo; (7) medicine search, favorites, SIG / timing / as-needed / controlled; (8) lab-test catalogue + confirm; (9) Amend / Cancel amendment after the post-complete window. File: `ConsultationPage.xaml(.cs)`.
- **New walk-in (doctor):** keep “look up or register, then start consult”, but add middle name, contact, address, DOB/phone validation, auto-Senior from age 60+, and a print-slip action after the node issues R-/S-. File: `NewWalkInPage.xaml(.cs)`.
- **Placeholders to actually build next:** Appointments list/detail, Patient charts, Availability, Templates (consult already needs to *consume* them), Profile.

### allen3 (staff)

- **Walk-In (highest):** stop reusing `NewWalkInPage`. Port `WalkInWizard` — steps Patient / Confirm; search by name, code, phone, email; register-new-patient (middle name, DOB, sex, contact, address); visit type New/Follow-up; discount None / Senior citizen / PWD (auto-Senior at 60+); **Check in and get queue number**; **Print slip**. Do not start a doctor consultation from this screen. New view, wire `staff-walk-in` to it.
- **Queue:** add **Check in** for Pending/Confirmed (booked, not arrived); add **Open consult** when In progress; rename Call → **Call in**; allow **Complete** only when In progress; keep Hold / No-show / Cancel-with-reason; show the six counts (Waiting, With doctor, Booked not arrived, Completed, No-show, Total today). File: `StaffQueuePage.xaml(.cs)`.
- **Payments:** client-validate amount received with the web rule (`checkAmountReceived`: must type the exact fee; reject blank / mismatch); show totals Waiting to pay, Total due, Waived — to confirm; one list with Visit date, Queue, Amount due, Record payment / Confirm waiver; toast/confirmation after success; hook receipt print (allen5). Tighten `RecordPaymentPage.cs`. Optional OR field is extra (web auto-builds `OR-{bookingId}`).
- **Front desk today:** add the three glance cards, the “N patient(s) ready to pay / Go to payments” banner, header action **New walk-in**, shortcuts **Doctor status** and **Patients** once those pages exist, and **Open queue board**. Keep Check in / Undo check-in / Cancel this booking? (reason required). File: `StaffHomePage.xaml(.cs)`.
- **Then build:** Patients list/register, Doctor status radios, Profile; later Announcements and Bookings list/detail.

### allen4 (admin)

- **Dashboard:** port “Clinic overview” — Refresh; Today stats (Appointments, Revenue, No-shows); Needs follow-up tiles; This month revenue/appointments; Doctor share of latest 20 bookings; Latest bookings table (Patient, Doctor, Time, Queue, Status) with All bookings link. File: replace stub `AdminHomePage`.
- **Walk-In:** same wizard as staff (share allen3’s component). Do not use `NewWalkInPage`.
- **Then build (P1):** Bookings list/detail, Doctors list/new/edit, Patients, Staff invite/edit/revoke. P2: Announcements CRUD, Settings editor (fee schedule already in SQLite), Audit logs, Reports, Calendar.

### allen5 (printing)

- Rx A5, lab 4.3×6.4 in, medical certificate A5, and queue slip factories already exist and are called from Consultation and Queue. **Add payment receipt / OR** (booking-derived number; “pending sync” when unsynced) and wire it from Record payment. Verify layouts against the web print HTML (polish can wait, but the receipt feature is missing).

---

## Screen-by-screen feature lists

Legend: **yes** / **partial** / **missing**. Owner and files are in the summary table.

### App shell / navigation — allen-core — 44% (4 yes, 1 partial, 4 missing)

| Feature | Status | Notes |
|---------|--------|-------|
| Role-specific sidebar, 260px, correct group labels and item names | yes | Doctor light; staff/admin inverse surface. Labels match `nav-config.ts`. |
| Navigate to built screens; locked flyout after login | yes | |
| Unbuilt items show a placeholder message | yes | `AddPlaceholder` in `AppShell.xaml.cs` |
| Logout | yes | |
| Pending-sync / last-sync affordance | partial | `ToolbarItem` bound to `SyncBanner` only; no full status bar or reconnect catch-up UI |
| Global search (patients, doctors, bookings) | missing | Web `Topbar` “Search…” |
| Notifications | missing | |
| Signed-in name + avatar initials; doctor day-status badge | missing | |
| Collapse sidebar 260→72 | missing | Style guide already reserved the width |

### S-LOGIN Login — allen-core — 90% (8 yes, 1 partial, 0 missing in-scope)

| Feature | Status | Notes |
|---------|--------|-------|
| Email + password, role taken from the account, enter doctor/staff/admin | yes | `LoginPage.xaml(.cs)` |
| “Remember my email on this computer” (never the password); prefill | yes | |
| Show / hide password | yes | `OnTogglePassword` + `ClinicIcons` eye |
| Loading state “Logging in…” / disable while submitting | yes | |
| Incorrect-credentials error | yes | |
| Locked-account and “offline too long” messages | yes | `AuthService` returns `Locked`; shown on `ErrorLabel` |
| Forgot password control | partial | Link present; `OnForgotPassword` only shows an alert — no email-reset screens (`S-FORGOT`) |
| Create account / Privacy Notice | n/a | Patient portal / marketing; intentionally out of scope |

### S-FORGOT — allen-core — 10%

| Feature | Status | Notes |
|---------|--------|-------|
| Submit email, success/failure, set-password after token | missing | Alert only from login. Fine to hide while offline; still not implemented. |

### D-TODAY Your clinic today — allen-core — 20% (3 yes, 0 partial, 12 missing)

Web: `/doctor/dashboard`. MAUI: `TodayPage`.

| Feature | Status | Notes |
|---------|--------|-------|
| List today’s bookings / visits; open a consult | yes | Always labelled “Open consult” |
| Refresh; live in-process updates | yes | `LiveRefresh` |
| Start a walk-in consult when there is no live queue | yes | “New walk-in consult” (desktop offline requirement) |
| State-specific actions: Start consultation / Resume consultation / Finish chart / View chart | missing | Web “Needs attention” |
| Filter by status | missing | |
| Set / show day status: Available, Running Late, Unavailable Today | missing | |
| Patient queue summary + Reload queue | missing | |
| Open appointments | missing | Placeholder route |
| Your performance / practice analytics (month/year/custom) | missing | Placeholder |
| Clinic report tiles (pending verifications, on hold, unpaid completed, follow-ups due) | missing | |
| Recent bookings | missing | |
| Announcements | missing | |
| Availability / clinic hours shortcut | missing | |

### D-CONSULT Consultation — allen-core — 48% (12 yes, 9 partial, 13 missing)

Web: `/doctor/consultation2/[bookingId]`. MAUI: `ConsultationPage`.

| Feature | Status | Notes |
|---------|--------|-------|
| Open by booking; work fully offline against local SQLite | yes | |
| Required-section gating (chief, BP+HR when templated, Rx or no-meds, follow-up, fee last); Diagnosis hidden from gates (`SHOW_DIAGNOSIS_STEP = false`) | yes | `ConsultSteps` + `MissingRequired` |
| Clinical notes: chief complaint, SOAP (S/O/A/P), doctor notes | yes | |
| Vitals from templates; BP + HR required when those templates exist | yes | |
| Follow-up: Dated (date), PRN, No follow-up for this visit | yes | |
| Professional Fee last: Charge / Waive, amount, waive reason, visit type, discount, med-cert fee | yes | Seeded to computed fee |
| Save draft | yes | |
| Save & next (advance only after a good save; last section stays “Save draft”) | yes | |
| Prev changes section without saving | yes | |
| Print prescription, lab orders, certificate | yes | Disabled until content exists |
| Print queue slip | yes | Extra vs web header; OK functionally |
| Completing a waive auto-waives payment (staff later Confirm waiver) | yes | Domain/service behaviour already used from this page |
| Patient banner (identity, allergies, queue, fee preview) | partial | Name, meta, fee preview only — no allergies / rich banner (`ConsultPatientBanner`) |
| New visit / Follow-up visit toggle | partial | Only on the fee step, not in the header |
| Section nav with done / warning / empty indicators after complete attempted | partial | `● / ! / ○` on buttons; Vaccinations concatenated into the bar (no More/Less); Diagnosis stub still reachable |
| “No medicines this visit” + add Rx lines | partial | Generic / dosage / quantity only — no medicine search, favorites, SIG, timing, as-needed, controlled, Rx templates |
| Lab orders | partial | Free-text test name only — no catalogue, no confirm dialog, no lab Dx |
| Vaccinations | partial | Name only; labelled “behind More” but there is no More control |
| Medical certificate | partial | Prepare checkbox + diagnosis / recommendations + dates — no templates, no confirm dialog |
| Review & complete disabled until required done; names missing sections | partial | Button disabled + “Still needed: …” text; confirm is a simple alert, not the checklist |
| Charge amount validation (> ₱0, ≤ ₱100,000, follows computed total until edited) | partial | Field exists and is seeded; little client-side messaging vs the web editor |
| Next (navigate only, no save) | missing | Web footer: Prev, Next, Save & next, Review & complete |
| Completion checklist dialog (per-section summary of what was entered) | missing | |
| View Patient History modal | missing | |
| Last-visit SOAP (and prior vitals) | missing | |
| Keyboard shortcuts help | missing | |
| SOAP phrases / templates: apply, Save as Template, Manage, Undo | missing | |
| Diagnoses (ICD-10 + custom, Primary/Secondary/Differential/Comorbidity) | missing | Case 2 is a message stub — cannot enter or save diagnoses |
| Amend path, Cancel amendment, amendment history; immutability after the post-complete window | missing | Complete works; no amend UI |
| Header print bar only when Rx/labs exist; help + history actions | missing | Prints are always in the footer |

### Doctor New walk-in consult — allen-core — 40% (4 yes, 0 partial, 6 missing)

| Feature | Status | Notes |
|---------|--------|-------|
| Search / pick an existing patient | yes | `SearchByNameAsync` (name / code display) |
| Register first name, last name, DOB, sex | yes | |
| Discount lane: none / Senior / PWD | yes | |
| Start consultation (issues a local visit + queue number) | yes | `StartWalkInConsultAsync` then `consult?bookingId=` |
| Search by phone or email | missing | |
| Middle name, contact number, address | missing | |
| DOB / phone validation (no future DOB, etc.) | missing | |
| Auto-tag Senior when age ≥ 60 (RA 9994) | missing | |
| Print queue slip from this screen | missing | |
| Visit type New vs Follow-up at intake | missing | Set later on the fee step |

### ST-DASH Front desk today — allen3 — 62% (8 yes, 1 partial, 5 missing)

Web: `/staff/dashboard`. MAUI: `StaffHomePage`.

| Feature | Status | Notes |
|---------|--------|-------|
| Arrivals list for today | yes | |
| Check in / Undo check-in | yes | Labels match |
| Cancel booking, reason required, “Cancel this booking?” | yes | `DisplayPromptAsync`; placeholder copy close to the web |
| New walk-in action | yes | Goes to `staff-walk-in` (but that screen is the wrong wizard — see ST-WALK) |
| Payments shortcut | yes | |
| Queue shortcut | yes | |
| Refresh + live updates | yes | `LiveRefresh` |
| Empty state “No one is booked or waiting right now.” | yes | |
| Today-at-a-glance counts | partial | One summary line (`N on today’s queue · waiting · in progress · ready to pay`) — not the three stat cards |
| “N patient(s) ready to pay” banner + Go to payments | missing | Visible on the live web screenshot |
| Shortcuts: Doctor status, Patients | missing | Destinations are placeholders |
| Open queue board link on the Arrivals card | missing | |
| Load-error / retry (“Today’s list didn’t load”) | missing | |
| Structured table columns Patient / Time / Queue / Status / Action | missing | Card rows + subtitle only |

### ST-PAY Collect payments — allen3 — 68% (8 yes, 3 partial, 3 missing)

Web: `/staff/payments`. MAUI: `StaffPaymentsPage` + `RecordPaymentPage`.

| Feature | Status | Notes |
|---------|--------|-------|
| List unpaid professional fees; Record payment | yes | |
| List waived-awaiting-confirmation; Confirm waiver | yes | Confirm dialog before `ConfirmWaiverAsync` |
| Payment methods Cash / GCash / Maya / Bank transfer | yes | Enum picker |
| Reference number, notes | yes | |
| Cannot collect a waived fee (separate confirm-waiver path) | yes | |
| Refunds not offered offline | yes | |
| Live updates | yes | `LiveRefresh` |
| Exact-amount rule (received == amount due) | partial | Server enforces if a number is typed; **blank is accepted and stored as the exact fee**. Web requires typing the exact amount (`checkAmountReceived`). Client only checks “must be a number.” |
| Totals: Waiting to pay, Total due, Waived — to confirm | missing | |
| Combined table (Patient, Visit date, Queue, Amount due) covering today + earlier unpaid days | partial | Two lists with those facts in subtitles; earlier-day coverage depends on `ListUnpaidAsync` |
| Amount received (₱) label + “Collect exactly ₱X — the amount the doctor set” in the dialog | partial | Copy exists; blank-allowed differs |
| OR number as a cashier field | n/a / extra | Web auto-generates `OR-{bookingId}`; MAUI shows an OR field |
| Payment receipt / OR print, “pending sync” when unsynced | missing | allen5 |
| Success toast (“waiver confirmed / paid”) and list-load error/retry | missing | |

### ST-QUEUE Today’s queue — allen3 — 62% (8 yes, 2 partial, 5 missing)

Web: `/staff/queue`. MAUI: `StaffQueuePage`.

| Feature | Status | Notes |
|---------|--------|-------|
| Live queue; S- (senior/PWD) ahead of R-; FCFS within a lane | yes | Board from `IQueueService` |
| Call (web: “Call in”) for Checked-in / On hold | partial | Action exists; label is “Call” not “Call in” |
| Hold | yes | Also offered in In progress, like the web |
| Complete | partial | MAUI also offers Complete on Checked-in / On hold; web only when **In progress** |
| No-show | yes | |
| Cancel with required reason | yes | Prompt copy close to the web |
| Print queue slip | yes | Web does this after walk-in check-in, not on the queue table; still a valid desk action |
| Refresh + live updates; New walk-in / Check in walk-in entry point | yes | Button says “New walk-in” (web: “Check in walk-in”) |
| Summary counts | partial | One line of Waiting / In progress / Completed / No-show — missing Booked not arrived + Total today as six tiles |
| Check in for Pending / booked-not-arrived | missing | Web `renderActions`: Pending → Check in + No-show |
| Open consult when In progress | missing | Web links to `/doctor/consultation/{id}` |
| Amount due as a first-class column | missing | Buried in the subtitle |
| Status pills | missing | Status is text in the subtitle |
| Empty state “No one in the queue yet today.” | missing | MAUI: “Today’s queue is empty.” (copy only; still an empty state — count as yes if being generous; marked missing only for the exact web empty path + error/retry) |
| Load-error / retry | missing | |

*(Empty-state exists, so treating it as yes would nudge this score to ~65%. Count used above keeps error/retry + exact empty copy under the missing load-error row.)*

### ST-WALK Walk-in check-in — allen3 — 25% (2 yes, 1 partial, 9 missing) — **wrong flow**

Web: `/staff/walk-in` → `WalkInWizard`. MAUI route `staff-walk-in` **reuses** `NewWalkInPage` (starts a doctor consultation).

| Feature | Status | Notes |
|---------|--------|-------|
| Look up an existing patient | partial | Name search only, no table of contact / sex / age / DOB / account |
| Register first + last + DOB + sex | yes | Missing middle, contact, address, portal-account checkbox |
| Discount Senior / PWD | yes | Labels “(none) / Senior / PWD”; web “None / Senior citizen / PWD” + auto-Senior at 60+ |
| Two-step wizard: Patient → Confirm (StepIndicator) | missing | |
| Search by patient code, phone, or email; pagination | missing | |
| Visit type New visit / Follow-up | missing | |
| “Check in and get queue number” (does not open a consult) | missing | **Starts a consultation instead** |
| Print slip after check-in | missing | |
| Select / confirm doctor if the wizard requires it | missing | |
| Med-cert requested at intake | missing | |
| Prepare portal account | missing | Cloud-ish; can wait, but it is on the web form |
| Load-error “Patients unavailable” / retry | missing | |

### A-WALK — allen4 — 25%

Same as ST-WALK: `admin-walk-in` also reuses `NewWalkInPage`. Fix is to share allen3’s wizard.

### A-DASH Clinic overview — allen4 — 5% (0 yes, 1 partial, 8 missing)

| Feature | Status | Notes |
|---------|--------|-------|
| Signed-in admin can open a home page and sign out | partial | Stub copy only: “Admin operations … are inventoried for a 1:1 port.” |
| Refresh; auto-refresh on queue/payment events | missing | |
| Today: Appointments, Revenue, No-shows | missing | |
| Needs follow-up tiles (proofs, unpaid completed, on hold, follow-ups in 7 days) | missing | |
| This month: Revenue, Appointments | missing | |
| Doctor share of the latest 20 bookings | missing | |
| Latest bookings table + All bookings link | missing | |
| Load-error “Dashboard unavailable” | missing | |

### Unbuilt screens — 0%

Each of the following is a placeholder alert or has no route. Main web capabilities still owed:

| Screen | Owner | Main missing capabilities |
|--------|-------|---------------------------|
| D-APPT-LIST / D-APPT-DETAIL | allen-core | Day/week list, search, booking detail, consult link, print Rx/lab/cert if issued |
| D-PATIENTS / D-CHART / D-RX-* | allen-core | Search doctor’s patients; full chart (history, SOAP, Rx, labs, files, vaccinations, allergies, growth); standalone Rx editor |
| D-PERF | allen-core | KPI cards, charts, period toggle |
| D-SCHED | allen-core | Weekly hours, blocked dates, day status |
| D-TEMPL | allen-core | SOAP, Diagnoses, Rx, Favorites, Phrases, Certificates — undoable delete, reorder, restore built-ins (consult must consume these even before the editor exists) |
| D-PROFILE | allen-core | Name, license, PTR, S2, avatar, change password |
| ST-PATIENTS / ST-CHART | allen3 | Search/register patients; demographics; allergies |
| ST-DOC-STATUS | allen3 | Radios per doctor: available / away / etc. |
| ST-ANN / ST-PROFILE | allen3 | Read announcements; staff profile + password |
| ST-BOOK-LIST / ST-BOOK-DETAIL | allen3 | All bookings, reschedule modal, cancel, check-in, payment link |
| A-BOOK-* / A-CAL | allen4 | All bookings, filters, reschedule/cancel, calendar |
| A-DOC-LIST / NEW / EDIT | allen4 | List, invite/create, edit services/schedule/blocked dates, deactivate |
| A-PAT-LIST / DETAIL | allen4 | All patients, chart, privacy / data requests |
| A-STAFF | allen4 | Invite, edit, revoke |
| A-ANN | allen4 | CRUD announcements |
| A-SET | allen4 | Clinic name, fee schedule, hours, payment methods, branding, privacy text (fees already in SQLite for consult) |
| A-AUDIT / A-REP | allen4 | Audit search; ratings, daily bookings, unpaid completed, pending follow-ups, doctor earnings |

### Print jobs — allen5 — 80% (4 yes, 0 partial, 1 missing)

| Feature | Status | Notes |
|---------|--------|-------|
| Prescription A5 (148×210 mm) | yes | `ClinicFormsPrintDocumentFactory.PrescriptionAsync`; hooked from Consultation |
| Lab request 4.3×6.4 in | yes | `LabRequestAsync` |
| Medical certificate A5 | yes | `MedicalCertificateAsync` |
| Queue slip (web or thermal) | yes | `QueueSlipAsync`; hooked from Queue (and consult) |
| Payment receipt / OR, “pending sync” when unsynced | missing | Not in the factory; payments screen does not print |

---

## Appendix — visual notes (deferred)

These are **not** part of the scores. Keep them for the polish pass after the functional gaps above are closed. Web references: `docs/ui-review/web/01-login.png`, `02-doctor-dashboard.png` (skeleton/chrome), `05-staff-dashboard.png`, `06-staff-payments.png`, `07-staff-queue.png`, `08-staff-walk-in.png`, `09-admin-dashboard.png`, `10-admin-walk-in.png`. MAUI could not be screenshotted this pass.

- **Page titles:** web `PageHeader` / `DoctorPageHeader` use `text-headline-lg` (**30px** / 24px mobile) plus a 14px muted description; no uppercase kicker. MAUI uses `HeadlineMd` (**20px**) plus a Primary `PageKicker` (“TODAY”, “DASHBOARD”, “QUEUE”, “PAYMENTS”). Staff/admin title copy also differs where screens exist (“Collect payments”, “Today’s queue”, “Walk-in check-in”, “Clinic overview”).
- **Chrome:** web has a top bar (Search…, bell, “Test Staff / TS”). Sidebars use **Font Awesome** icons (mapped from Material Symbol names in `src/components/ui/Icon.tsx`) and a collapse chevron. MAUI sidebars have no icon set in the nav rows (a `ClinicIcons` helper has started to appear on login). Bundle the same Font Awesome SVGs/font, not Lucide.
- **Lists vs tables:** web staff/admin use a rounded-xl (`24px`) table with `shadow-sm`, teal patient-name links, and **StatusPill** (rounded-full, uppercase, icon, tone colours). Queue numbers are large tabular text; **S- tickets get a small “PRIORITY” primary pill**, not a peach `TertiaryFixed` lane badge. MAUI uses `ListCard` rows and S-/R- lane badges from the style guide.
- **Type and colour tokens** in `Colors.xaml` already match `globals.css` `@theme`. Shared card padding is **16px** in MAUI vs web `p-lg` **24px**. Buttons (44px, `rounded-lg` 16px, Inter Medium 13) are close; hover / `active:scale-[0.97]` / disabled opacity 40% are weaker in MAUI. **Entry** styles have no explicit 16px corner or outline-variant border, so native fields will not match `inputStyles`.
- **Font:** the live web app uses the system stack (`ui-sans-serif` — San Francisco on this Mac). MAUI bundles Inter. That is a side-by-side mismatch until one side changes.
- **Login** copy has already been brought close to the web (“Log in to your account”, “Log in”, Forgot password, 7-day remember hint). Remaining visual gaps: remember-email sits after the password (web-desktop puts it between email and password); no VersionFooter; no “New patient? Create an account” (out of scope).
- **Stat cards** (icon in a 40×40 primary/10 tile, 30px tabular value, 13px label) are used on every web dashboard and are absent on the MAUI pages that exist.

<!-- JJQR - END -->
