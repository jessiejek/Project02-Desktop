# Prompt: "If it fetches, it shows a skeleton" (desktop app)

Paste everything below the line into a Claude Code session opened in `Project02-desktop`.

---

You are working in `Project02-desktop/ClinicNode` (.NET MAUI, Mac Catalyst). The live server is slow (about 0.2 s per round trip, 0.6 to 2 s on a new connection), so every screen that fetches data shows blank or empty boxes for 1 to 3 seconds. The owner is angry about it. Fix it for the WHOLE app with one rule:

**RULE: whenever data is being fetched and the area that will hold it is not filled yet, show a skeleton in that exact area. Never show a blank area, empty boxes, "No data", or only the text "Loading...".**

This covers every fetch, not only whole pages:
- first load of any page (Staff, Doctor, Admin, Settings, Sync pages)
- tabs inside a page (e.g. the patient chart tabs Visits, Lab Results, Documents, Vaccinations)
- lists, tables, cards, dashboards, queue boards, reports, charts
- forms that are filled from the server (Edit doctor, Edit patient, booking detail, consultation)
- modals and dialogs that load data (search results, notifications, pickers)
- paging, filtering, searching, switching a date range (the area shows skeleton while the new data loads, not the old data silently replaced and not blank)
- reloads triggered by tapping Try again or Refresh

## Already built (reuse it, do not rebuild)
- `ClinicNode.App/Controls/Skeleton.cs`: `SkeletonBar`, `Skeleton.Rows(n)`, `Skeleton.Card(lines)`, `Skeleton.Overlay(content, isLoadingPath)`.
- `ViewModels/Admin/AdminViewModelBase.cs`: `IsFirstLoad` (true until the first `RunAsync` finishes) and `IsBusy`.
- `Views/Staff/StaffLivePage.cs`: base class of about 20 pages. It calls `LoadAsync()` through `ReloadAsync()` on appear and on live events.
- Already done: DoctorAppointmentsPage, DoctorPatientsPage, TodayPage (totals), AdminDoctorEditPage.

## How to do it
1. **Central first.** In `StaffLivePage`, show a page-sized skeleton over the content until the FIRST `ReloadAsync()` finishes (success or failure), then remove it. This fixes all pages that inherit from it in one place. Do not flash the skeleton on later background reloads (live refresh keeps the old data on screen).
2. **Admin pages.** Wrap each page's content with `Skeleton.Overlay(content, "IsFirstLoad")` only where its view-model really loads through `RunAsync`. Check each one. Where the load does not use `RunAsync` (AdminAuditViewModel, AdminOrClashesViewModel, AdminSettings, AdminSyncComputers), add an explicit `IsLoading` flag set around the fetch.
3. **Tabs and sections.** Every tab or section that fetches on its own (patient chart tabs, consultation sections, lab results, documents, vaccinations, history lists) gets its own skeleton shaped like its content (rows for lists, cards for panels, bars for forms), not one generic block.
4. **Paging, filter, search, date range.** While a new page of data is loading, show skeleton rows in the list area. Keep the header and the filter controls visible and usable.
5. **Dialogs and pickers** that load (global search, notifications, patient pickers): skeleton rows inside the dialog.
6. **Per-call fetches in code-built pages** (`_pending`, `_loading`, `Loading...` labels): replace the text with the skeleton. Grep for `Loading`, `_pending`, `IsBusy`, `Loading…`.

## Hard requirements
- Skeleton shape should match the content that will replace it (same number of rows/cards roughly, same widths), so the layout does not jump.
- Pulse animation only (already in `SkeletonBar`). No spinners, no progress bars, no moving line in the top bar. The owner removed the top-bar loading line because it was annoying; do not bring it back.
- Never leave a skeleton stuck: if a fetch fails, replace the skeleton with the page's existing error state ("Try again"). Add a safety timeout of 15 s that turns a stuck skeleton into the error state.
- Do not show a skeleton for background pings, health checks, live refreshes or polling.
- Skeleton must not block the top bar, the sidebar or the Back button.
- Do not change data logic, services, view-models' public behavior, routes or colors. UI only.
- Keep the `//JJQR - AI (date) - reason` and `//JJQR - END` comment markers on every edit, as the rest of the code does.

## Also required in the same pass (owner requests)

### A. Pointer (hand) cursor on everything clickable
On the Mac, hovering any clickable thing must show the **pointing hand** cursor, like a web page. Today it shows the plain arrow.
- Covers: every Button, tapped Label/Border/Grid/Frame/Image (anything with a `TapGestureRecognizer` or a Command), sidebar items, table rows that open something, tabs, chips, pills (the connection pill, the sync badge), the Logout item, links, close icons, pagination, dropdown rows.
- MAUI has no cursor property on Mac Catalyst. Do it ONCE, centrally, not per page: add a Mac Catalyst handler customization (in `Platforms/MacCatalyst`, registered from `MauiProgram`) that adds a `UIPointerInteraction` with a hover/lift or hand style to the native view of every `Button`, and a small attached behavior/property (e.g. `Pointer.IsClickable="True"`) that you set automatically for any view that has a `TapGestureRecognizer`. Use `PointerGestureRecognizer` only if it can show the hand cursor; otherwise use the native interaction.
- Disabled controls must NOT show the hand.
- Verify by hovering: sidebar, a table row, a button, a tab, the top-bar pill.

### B. No double clicks: every action runs once
Right now you can click **Logout** many times, which starts the sign-out many times. The same risk exists on every button that starts work (Save, Sign in, Register, Sync now, Confirm payment, Complete consultation, Refund, Create, Delete, Print, Switch mode, Register this node).
- Add ONE shared guard (e.g. in `StaffUi.Btn`, `ClinicUi` button helpers and the Command helpers): the button disables itself (and ignores further taps) from the moment it is pressed until its async work finishes, success or failure, with a `try/finally`. Show the busy text where the screen already does ("Saving...", "Signing out...").
- Sidebar Logout and `AppShell.UserSignOutAsync`: make sign-out re-entrancy-safe (a single in-flight flag), so extra clicks do nothing. Also guard the Quit command and the mode switch.
- Also guard against double Enter key presses on forms.
- Add a unit test for the guard (two rapid invocations run the work once).

### C. Admin cannot change their password (BUG, must be fixed)
The signed-in **Admin** has no way to change their own password. The server already supports it: `POST /api/auth/change-password` (Authorize, current password + new password; 12+ characters, see `PasswordPolicy`). Doctor and Staff already have a "Change password" section on their Profile pages (`DoctorProfilePage`, `StaffProfilePage`); the Admin has no profile page at all, and the web app has no admin profile page either.
- Desktop: add a **My account** (or **Profile**) item for the Admin (sidebar, System group, or the name menu in the top bar) with: name and email (read-only), **Change password** (current password, new password, repeat new password, show/hide toggle, live rule hints: 12+ characters, not the email), **Save** (guarded against double clicks, shows "Saving...", then a clear success or error message from the server). Reuse the call and the layout that `StaffProfilePage` / `DoctorProfilePage` use.
- Web (`Project02-fe`): add `/admin/profile` with the same form, using the same endpoint, and link it from the admin nav (System group) and the top-right user menu.
- Also make sure the same works for **every role** from one place if possible (Admin, Staff, Doctor).
- After a successful change, keep the user signed in (the server issues new tokens) and never show or log the password.
- Test it: wrong current password gives a clear message; a weak new password is refused with the rule; success then allows signing in with the new password.

### D. Date picker pops up in the wrong place (BUG, must be fixed)
Seen on **Register a new patient** (Staff, "Who is checking in?" > Register new patient): clicking **Date of birth > Choose a date** opens the calendar **at the top-left corner of the window**, on top of the sidebar, far away from the field and outside the dialog. It should open **directly under the Date of birth field**, inside or next to the dialog, aligned to the field's left edge.
- Fix it in the shared date picker control (find it by grepping for the calendar popup / "Choose a date" / month-year header and `ClinicPopup`), so EVERY date field in the app is fixed at once: patient registration, walk-in, appointments and booking reschedule, reports date ranges, schedule blocked dates, certificates, vaccination dates, consultation follow-up dates, admin filters.
- Position rules: anchor under the field; if there is not enough room below, open above; never outside the window; never over the sidebar; keep it inside the visible window on small windows; close it when the user clicks outside or presses Escape; clicking a day sets the value and closes it.
- Date of birth specifically: the picker must be fast for old dates. Open on the year/month chooser (the "November 1996" header dropdown already exists) and also allow **typing the date** (e.g. `11/22/1996` or `1996-11-22`) in the field.
- Check the same positioning for other popups opened from a field: dropdowns, time pickers, the Sex toggle, search suggestion lists.
- Test it visually (screenshot) on Register a new patient, then on a field near the bottom of a page and one in a narrow window.

### E. The doctor can change their OWN PF (professional / consultation fee)
On **Your profile** (Doctor) the fee is read-only ("Name, specialization and fee are set by the clinic admin"). Change the rule: **the doctor may edit their own consultation fee (PF)**. Name and specialization stay admin-only. License, PTR and S2 numbers stay doctor-editable as today.
- Desktop (`DoctorProfilePage`, `DoctorProfileService`, `ApiDoctorExtraScheduleProfile`): make **Consultation fee** an editable money field (pesos, numbers only, 0 or more, 2 decimals, sensible upper limit such as 100,000), saved by the existing **Save profile** button. Update the grey note to: "Name and specialization are set by the clinic admin. Your fee, license, PTR and S2 numbers are yours to enter."
- Backend (`Project02-be`): allow a Doctor to update ONLY their own `consultation_fee` (not another doctor's, not name/specialization/status). Admin keeps full edit. Validate the value, write an audit row (old fee, new fee, who), and add tests: doctor can change own fee; doctor cannot change another doctor's fee (403); staff and patient cannot; invalid values refused. Do NOT merge the backend change to `main` without the owner saying "go" (main auto-deploys to production); work on a branch.
- Web (`Project02-fe`, `/doctor/profile`): same editable fee field.
- The fee change applies to NEW bookings only. Bookings and payments that already exist keep the amount they were made with. Say so in a small hint under the field.
- Sync: the fee travels with the doctor record like any other field; check that an Offline-mode edit syncs and shows on the web.

### F. Eye (show / hide) button on every password field
Every password box needs the **eye icon** to show or hide what was typed, like the sign-in page on the web already has. Missing today on **Your profile > Change password** (Current password, New password, Confirm new password), and check all others: desktop sign-in, Register this node / admin token field, Create staff/doctor forms, Set password, Reset password.
- One shared control (e.g. a `PasswordEntry` with the eye toggle) used everywhere, so no password box is plain. Hidden by default; the eye toggles it; it must hide again when the page is left.
- Pointer cursor on the eye (see section A) and a screen-reader label ("Show password" / "Hide password").
- Also on the **Change password** form: live hint of the rule (12+ characters), "passwords match" check on the confirm box, the Update Password button disabled until valid, guarded against double clicks (section B), and a clear success or error message.

### G. Small data typo
The doctor's specialization shows "Family Mediicne Adult & Pedia" (typo "Mediicne"). Do not change code for this; list it in your final report so the owner fixes it in Admin > Doctors > Edit.

## Finish
1. `dotnet build ClinicNode.App/ClinicNode.App.csproj -f net10.0-maccatalyst`
2. Run `dotnet test ClinicNode.Tests` and compare to `docs/status/test-baseline-2026-11-22.txt`: no NEW failing tests.
3. Make a list: every page/tab/list/dialog you checked, and whether it now shows a skeleton while fetching. Anything you could not cover must be listed with the reason.
4. Commit to a branch `ui/skeleton-everywhere`, then merge to `main` and push only if the build and tests are clean.
