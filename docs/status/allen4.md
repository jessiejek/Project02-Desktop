<!-- JJQR - AI (21 Nov 2026) -->
# ADDENDUM allen4: RESUME batch (Jessie t88u, 21 Nov 2026, evening)
Code only. Nothing compiled, tested, committed or pushed by me. MauiProgram / AppShell were not touched, and neither was allen3's `AdminWalkInPage.cs` layout. No new endpoints. No half-edited files.

## Screens in this batch (each compared with the web .tsx)
- **Patients list** (`admin/patients/page.tsx`): fixed to match.
  - A newer search or page now replaces a load that's still running, the way the web cancels the stale effect. Before, RunAsync's busy guard dropped the newer request.
  - The "Patients unavailable" ErrorState shows whenever the load fails, not only when no rows are left.
  - While loading: "Loading patients…", Previous/Next disabled, and a spinner instead of the empty message on the first load.
  - "Add a patient" modal: Add stays disabled until the form is valid and shows "Adding…" while saving. A failure shows inside the open modal and keeps the typed details. On success the new row goes on top and the total goes up by one, with no reload. Network or 500 errors show the web's fallback sentence.
- **Patient detail** (`admin/patients/[id]/page.tsx`): fixed to match.
  - "Edit patient details" keeps the modal open while saving ("Saving…") and shows the error in place. Before, it closed and reopened.
  - Removed the desktop-only max date on the DOB picker.
  - The access log now loads on its own: if it fails, only that tab says "The access log could not be loaded." Before, the whole page failed. This is the new `AccessLogFailed` on `AdminPatientDetail`, set in `ApiAdminPatientService.GetAsync`.
- **Settings** (`admin/settings/page.tsx` + `components/admin/DataRequestsPanel.tsx`): fixed to match.
  - Each Save now sends only its own tab group, like the web. saveSettings covers General/Payments/Privacy/Branding plus accepted methods and leaves fees and consent alone. Save fees sends only the fee lines. Publish consent sends only the new version. Before, every Save PUT the whole unsaved form, so unsaved edits on other tabs got written.
  - A failed hours or payment-methods read now shows "Settings unavailable", matching the web's Promise.all.
  - The services list (not on the web page) is no longer loaded there.
  - Data-request buttons keep their label while busy, and only the busy button is disabled.
- **Announcements** (`admin/announcements/page.tsx`): fixed to match.
  - New/Edit and Delete modals stay open while saving and show the error in place. Before, they closed and reopened.
  - Save is disabled until Title and Message are filled. The "Post announcement" / "Save as hidden" live label is kept.
  - ErrorState shows only when the list load itself failed. A failed re-read after a write keeps the cards.
  - Try again goes back to the loading state first.
- **Audit log** (`admin/audit-logs/page.tsx`): fixed to match.
  - Same load fix as patients: a newer filter or page replaces the running load. ErrorState shows whenever the load fails. While loading: "Loading entries…", pager disabled, and a spinner on the first load.
  - The record-type list already matched the web's 10 types in the same order.
- **Doctors: failed Deactivate**: fixed. The web keeps the modal open and shows the FormAlert in place, and the desktop now does the same through `AdminDialog.ShowSubmitAsync`.
- **Clean-up**: removed the unused `RescheduleOpen`, `RescheduleDate` (and its set and Raise), `RescheduleReason` and `SaveRescheduleCommand` from `AdminBookingDetailViewModel`. No XAML or code-behind bound to them. `OpenRescheduleCommand` and `CanReschedule` are still bound and were kept.

## Files changed in this batch (under `Project02-desktop/ClinicNode/`)
- `ClinicNode.App/Views/Admin/AdminPatientsPage.xaml.cs`
- `ClinicNode.App/Views/Admin/AdminPatientDetailPage.xaml.cs`
- `ClinicNode.App/ViewModels/Admin/AdminPatientsViewModel.cs`
- `ClinicNode.App/Views/Admin/AdminSettingsPage.xaml.cs`
- `ClinicNode.App/ViewModels/Admin/AdminSettingsViewModel.cs`
- `ClinicNode.App/Views/Admin/AdminAnnouncementsPage.xaml.cs`
- `ClinicNode.App/ViewModels/Admin/AdminAnnouncementsViewModel.cs`
- `ClinicNode.App/Views/Admin/AdminAuditPage.xaml.cs`
- `ClinicNode.App/ViewModels/Admin/AdminAuditViewModel.cs`
- `ClinicNode.App/Views/Admin/AdminDoctorsPage.xaml.cs`
- `ClinicNode.App/ViewModels/Admin/AdminBookingsViewModel.cs`
- `ClinicNode.Core/Admin/AdminPatientService.cs`: the `AdminPatientDetail` record gets an optional `AccessLogFailed = false`, so existing positional constructions still compile.
- `ClinicNode.Api/Services/ApiAdminPatientService.cs`

## Still different from the web / not done
- A patient that doesn't exist shows "Patient unavailable" with Try again. The web uses Next's notFound() (404 page), and the desktop has no 404 page.
- Patients' loading and empty states use an ActivityIndicator instead of the web's skeleton rows. The look differs, the behaviour matches.
- Settings Hours: a closed day still sends null open/close times. The web sends whatever times are typed. The server result is the same.
- Settings: the online-only banner and disabled Save while offline are kept (Jessie: settings/fees online-only). They aren't web controls, they're the offline guard.
- `ClinicNode.Core/Admin/AdminSettingsService.cs`: the unused `_outbox` field (L58/L71) is still there, left so existing tests still compile.
- `AdminPatientsViewModel` still has its old unused members (`SaveAddAsync`, `PageLabel`, `NewMiddleName` and others, nothing binds them). `AdminSettingsViewModel` still has its old services-tab members. Neither was removed because that's outside this batch.
- Not compiled. allen0's build will show any errors.

## What I'd do next
Fix any compile errors allen0 reports in the files above. Optional clean-up of the leftover unused VM members listed above. No new features.
<!-- JJQR - END -->

<!-- JJQR - AI (21 Nov 2026) -->
# WRAP-UP allen4 (Jessie's stop order, 21 Nov 2026) — DONE allen4
Stopped the admin re-check at a clean point after booking detail. No file was left half-edited (unused leftover members are listed below; the edits themselves are complete). Nothing committed or pushed. Nothing compiled or tested by me (allen0 is compiling). allen3's layout-only change in `Views/Admin/AdminWalkInPage.cs` was left as-is.

## Files I own
Under `Project02-desktop/ClinicNode/`:
- `ClinicNode.App/Admin/AdminModule.cs`
- `ClinicNode.App/Views/Admin/**` (host page `AdminWalkInPage.cs` is in this folder; the Continue-bar / done-header layout is allen3's and must stay)
- `ClinicNode.App/ViewModels/Admin/**`
- `ClinicNode.App/Services/Files/{IFileSaver.cs,FileSaver.cs}`
- `ClinicNode.Core/Admin/**`
- `ClinicNode.Api/Services/ApiAdmin*.cs`
- Docs: `docs/status/allen4.md`, `docs/status/allen4-admin-api-map.md`

I do not edit `MauiProgram.cs`, `AppShell`, `WalkInWizard`, or non-admin files. AppShell / MauiProgram wiring is allen0's at wave end.

## What's done (written; not compiled by me)
- **Announcements live label:** New-announcement confirm button switches between "Post announcement" and "Save as hidden" when "Show on dashboards now" is ticked. Edit still says "Save changes". Added admin-only `AdminDialog` because shared `ClinicDialog` can't relabel once open.
- **Settings, fees and service prices are online-only** (Jessie: keep service create/edit online-only). `OnlineOnlyAdminSettingsService` is the offline fallback: it reads locally, refuses every write with "needs an internet connection", writes nothing and queues no outbox row. Settings screen shows an offline banner and disables Save / Publish. `AdminSettingsService.UpdateAsync` no longer enqueues `clinic_settings` / `clinic_setting` (the `_outbox` field was kept so existing tests still construct the service).
- **Booking create fields:** `POST /api/bookings` and `/api/bookings/walk-in` send `visit_type` (only when FollowUp) and `discount_category` (when set); otherwise null. No other new fields. No admin screen currently calls create; Walk-In uses allen3's shared wizard, which does not go through `IAdminBookingService`.
- **Write-path audit:** online, every admin write already used its API. Offline guards in `OnlineOnlyAdminWrites.cs` block writes the server would reject: refunds, announcements, staff update/invite/revoke, doctor update/schedule/blocked dates/services, and resolving a patient data request. Left local offline because the server accepts them: booking create/walk-in/status/reschedule, payment confirm/waive, patient create/update. Invite password stays local-only. Queue-number allocation (`QueueSequencer` / `NextForPatientAsync` in `AdminBookingService.cs`) was not changed.
- **OR uniqueness:** 409 `or_number_duplicate` prompts for another number (field prefilled with the clashing number). `ReleaseAsync` only clears the local OR when payment is not null, `Status` is `PaymentStatus.Unpaid`, and `OrNumber` equals the clashing number (`StringComparison.Ordinal`).
- **1:1 screens this wave:** Clinic overview (dashboard), Reports, Bookings week view (web calendar is a redirect to Bookings), Bookings list, Sync computers and Receipt number clashes (no web pages; states tightened only), Booking detail (modals, load error, one-click confirm payment). Daily patient limit stays off the doctor form; the existing value is still saved unchanged. Removed desktop-only controls stay off (Jessie).
- **CS0103 fix:** `AdminBookingDetailViewModel` (same file as the list VM) calls `AdminBookingsViewModel.WebShowsPayment` at line 397.

## What's half-done (file and line)
- Strict re-check stopped after staff. Re-walked and matching: booking detail, doctors list, doctor new/edit (small gap: a failed Deactivate closes and reopens the modal with the error instead of staying open). Staff fixed: load failure now shows "Staff list unavailable" / "The staff list could not be loaded. Try again." with Try again (`ClinicNode.App/Views/Admin/AdminStaffPage.xaml.cs`, JJQR). **Not re-walked:** patients (list/detail), settings, announcements, audit.
- `ClinicNode.App/ViewModels/Admin/AdminBookingsViewModel.cs` (`AdminBookingDetailViewModel`): leftover unused reschedule members after the modal rewrite — `SaveRescheduleCommand` (L264, L331), `RescheduleOpen` (L289), `RescheduleDate` (L322, still set at L413/L418), `RescheduleReason` (L323). The page no longer binds them.
- `ClinicNode.Core/Admin/AdminSettingsService.cs`: unused `_outbox` field (L58) and constructor parameter (L71) after the enqueue was removed. Left in so existing tests still compile; may warn as unused.
- Not compiled. Booking detail + dashboard + bookings list were large rewrites; compile errors are unknown until allen0's build.
- AppShell flyouts / System nav for Sync computers (`admin-sync-computers`) and Receipt number clashes (`admin-or-clashes`) are still pending allen0's wave-end wiring. No new AdminModule lines outstanding; `AddOnlineFirst` for the guarded/online-only services is already in `AdminModule.cs`.
- Already-queued `clinic_settings` / announcement / `staff_account` / `doctor_schedule` outbox rows from before today's guards are still in the outbox (allen2).
- If the online session expires mid-write, `OnlineFirstProxy` (allen2) still falls back to the offline side (blocked writes now show the internet message instead of saving).
- Offline doctor weekly schedule stays blocked: allowing it would mean reusing DoctorExtra's self-edit payload with `actor_user_id` (non-admin code).

## What I'd do next
Fix any compile errors allen0 reports in the admin files above. Then continue the strict re-check from patients (list/detail), then settings, announcements, audit. Optionally remove the unused reschedule members and the unused `_outbox` field if tests allow. No new features. Don't put desktop-only controls or the daily patient limit back.
<!-- JJQR - END -->


<!-- JJQR - AI (Nov 21, 2026) -->
Percent: 100%. Suite green; TopBar compact indicator; admin OR fix; API map written.
Last: Full ClinicNode.Tests 371 / 366 passed / 5 env-gated skipped / 0 failed in 4.2643 min. Mapping at docs/status/allen4-admin-api-map.md. Stopped before Api-backed admin services: docs/ONLINE-FIRST.md is not present (ClinicNode.Api OnlineFirst client is).
Next: When ONLINE-FIRST.md lands, wrap admin services with OnlineFirstProxy (Api + existing SQLite fallback) per that doc.
<!-- JJQR - END -->

<!-- JJQR - AI (Nov 21, 2026) - behaviour-parity gap list vs Project02-fe + Project02-be (actions only, not looks). -->
A-DASH — no action gap. Stats, doctor share, latest bookings, refresh already match the web Clinic overview.

A-WALK — no action gap. Hosts allen3 WalkInWizard (check-in + queue + slip, no consult).

A-BOOK-LIST / A-CAL — no action gap. Search, doctor/status/date filters, clear, list + week tab, walk-in link already present. Web list is not paginated; no bulk actions on the web.

A-BOOK-DETAIL — fixed. Confirm booking (Pending), confirm payment (ProofSubmitted → Confirmed + Paid + OR), reject (Cancelled + required reason), cancel (≥5 chars), complete, no-show, reschedule, waive (Unpaid + reason), refund (Paid, amount >0 and ≤ paid, reason), print receipt + appointment summary when Completed (printer when available; also writes HTML under Documents/ClinicNode).

A-DOC-LIST — fixed. Deactivate / reactivate on the list with the same confirm dialog as the edit page. (Web has no list search; desktop search is kept.)

A-DOC-NEW / A-DOC-EDIT — no new action gap. Profile, schedule, blocked dates, services, deactivate/reactivate already on the form.

A-PAT-LIST — fixed. Server search, newest-first sort, pagination PAGE_SIZE 25, “x–y of total”, prev/next.

A-PAT-DETAIL — no action gap. Edit (incl. middle/email/address), bookings, consults, access log, data-request resolve already present. Web admin patient page has no CSV export.

A-STAFF / A-ANN — no action gap vs web (invite/update/deactivate staff; create/update/expire announcements, validation messages).

A-SET — no action gap found. General, hours, fees, payments, privacy, branding, services, medicines search, clinic-wide data requests already present.

A-AUDIT — fixed. Search + entity filter already present; added entity id on rows and “x–y of total” range label.

A-REP — fixed. Date range defaults to today-30..today, rejects from > to, filters unpaid / daily / pending follow-ups in range, Export CSV (`clinic-reports_{from}_to_{to}.csv`, BOM + CRLF) written to Documents/ClinicNode.

Remaining:
- Export / print files go to Documents/ClinicNode because the app has no FileSaver / save-dialog service. TODO(allen): optional platform save-dialog; then route AdminLocalExport through it.
- `dotnet test ClinicNode.Tests` currently fails to compile Tests/Sync/SyncApiRoundTripTests.cs (ISyncNodeRegistration / FollowUpMode — other bot, out of scope). Admin tests were run in isolation: 24 passed.
<!-- JJQR - END -->
