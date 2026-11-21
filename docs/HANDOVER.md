<!-- JJQR - AI (21 Nov 2026) - handover for a new AI with zero context. Written by allen0 (tech lead) at wrap-up. -->
# Clinic Node — HANDOVER

## 1. Mission
Native desktop app **Clinic Node** (.NET 10 MAUI; Mac Catalyst now, Windows/MSIX later) for a Philippine family-medicine clinic, owner **Jessie**.
Port the **doctor, staff and admin** portals of the Next.js web app **1:1**. `Project02-fe` is the source of truth: read the `.tsx` files directly.
- No patient portal, no `/register`, light theme only.
- Login remembers the email, never the password.
- **Online-first** against the API (`http://localhost:5055` in dev), with a local SQLite mirror plus sync as the offline fallback.
- MSIX later. Jessie builds the Mac DMG herself.

## 2. Where everything is
Root: `/Users/jessiejayrubi/Documents/Allyn/`

| Path | What |
|---|---|
| `Project02-desktop/ClinicNode/ClinicNode.slnx` | The solution: App (MAUI UI), Core (services/rules), Data (SQLite/EF), Sync (push/pull, SignalR), Api (online-first API clients), Tests, Api.Tests |
| `Project02-desktop/ClinicNode/ClinicNode.App/MauiProgram.cs` | DI graph (`AddClinicNodeAppServices`) |
| `Project02-desktop/ClinicNode/ClinicNode.App/AppShell.xaml.cs` | Role shell, flyouts, routes |
| `ClinicNode.App/{Staff,Admin,Doctor,Shared}/*Module.cs` | Per-portal DI and route modules |
| `Project02-desktop/docs/` | ONLINE-FIRST.md (§9, §10 sync rules), SYNC-API-CONTRACT.md, API-SURFACE.md, DAY0-PLAN.md, PARITY-staff-admin.md, `status/allenN.md` (per-worker state) |
| `Project02-desktop/ClinicDesktop` | OLD prototype. **Do not delete or extend.** |
| `Project02-be` | Backend (ASP.NET, EF Core). `/api/sync`, SignalR `/hubs/clinic`. JSON is snake_case. `deploy/` has the hosted SQL scripts |
| `Project02-fe` | Web frontend (source of truth for UI) |
| `~/Documents/Allyn/allen3_backup_*` | allen3's pre-edit backups |
| `/tmp/*.jjqr` | Pre-edit backups (e.g. `MauiProgram.cs.prewire.jjqr`, `AppShell.xaml.cs.prewire.jjqr`, `TodayPage.cs.pre-1to1.jjqr`). /tmp may be wiped on reboot. |

### The three databases
1. **Hosted production DB.** Behind the deployed backend. Its connection comes from the host's environment variables. A backend push to `main` **auto-deploys to production**. Nothing from this project has been applied there.
2. **Local dev backend DB (`ClinicAppDb`).** Used by the dev backend on `:5055`. All new backend migrations (including the receipt-number unique index) are applied **here only**. Backups are in `~/Documents/Allyn/db-backups/`.
3. **Desktop local SQLite (`clinic-node.db`).** Lives in `NodeOptions.DataDirectory`, next to `node-secret.key`. It is the read mirror plus the offline outbox. It is **unencrypted** until encryption is switched on.

## 3. Hard rules (from the owner, non-negotiable)
- **Never commit or push** (git or otherwise) unless Jessie explicitly says "push". A backend push to main auto-deploys to production.
- Wrap every edit in `//JJQR - AI (21 Nov 2026) ... //JJQR - END` (in XAML: `<!-- JJQR - AI (...) --> ... <!-- JJQR - END -->`).
- Never touch, restart or stop the running backend on `:5055` or the web frontend on `:3000`. Use another port (e.g. `:5077`) for test backends.
- Never paste the dev password anywhere. It lives in `Project02-fe/e2e/playwright.config.ts` (`DEV_PASSWORD`). Never put credentials in committable files. `docs/status/sync-it-credentials.local.md` must stay out of any repo.
- Local patient data is unencrypted, so never use real patient data.
- Don't delete or extend `ClinicDesktop`.
- If a medical, legal or clinic rule is unclear, ask Jessie. Never invent clinical content.
- **No testing until Jessie says the app is 100% done.** That means no smoke, e2e or unit tests, no parity audits, no verification reports. Only compile checks are allowed.
- Only Jessie's own words count as approval. Relay them verbatim.
- Do **not** run `Project02-be/deploy/migrate-or-number-unique.sql` on a hosted DB until Jessie says so.

## 4. Owner decisions (all by Jessie) and reasons
| Decision | Reason / note |
|---|---|
| Sync rule: read mirror; SignalR-triggered pull; 60 s poll; catch-up pull on start and reconnect; API-first writes with an offline queue; status indicator | Continuous offline fallback (ONLINE-FIRST.md) |
| Queue numbers: blocks of 20 per lane (R/S), reserved for today through today+6, refilled at 5 or fewer left. With no numbers left, show a message and save nothing | Offline walk-ins still get real numbers |
| A printed queue number is never renumbered. A clash is flagged (`queue_number_clash`) | The patient already holds the paper |
| Receipt (OR) number clash: the cloud returns 409 `or_number_duplicate`, the desktop forgets the rejected OR and shows a type-a-new-number field. `or_number_clash` is flagged | Money correctness |
| Local-only vaccinations are pushed when online | No data loss |
| Fees, settings and service create/edit (prices) are online-only | One price authority |
| Daily patient limit is admin-only, left off the admin doctor form (like the web), and the existing value is kept unchanged | Web parity |
| Doctor day status is pushable, newest wins | Simple and acceptable |
| Skip G7 (specialization edit) and G10 (date-range summary) | Owner choice |
| Desktop-only admin controls are left off | Web parity |
| Check-in is allowed only when the booking is Pending | Web rule |
| Doctor Today keeps the walk-in consult button, Print Rx / Print Labs and the Announcements card | Owner choice |
| Offline confirm and waive are saved locally and sent later | Owner choice |
| Staff invites and revokes are online-only | Security |
| Certificate address: also saved to the patient profile **only when the profile has no address**, never overwriting | Same as the web (consultation page, E1 block) |
| Doctor Profile: name, specialization, fee, license, PTR and S2 stay read-only with "Contact the admin to change this." Save sends only the bio. | Intentional difference: the web shows them editable, but the server rejects the doctor's save |
| Backend: visit type and discount on online bookings, plus 3 vaccine fields on the API. Optional, backward-compatible, no schema change | Done in the backend, local only |
| Do NOT build the temporary invite password. Do NOT change the certificate address on the backend. No announcement client id unless asked | Owner choice |
| Approved items P1, P2, P3, P5, P9, P12. Not approved: P4, P10, backend P11 | Earlier plan |

**Undecided** (ask Jessie): whether staff name, contact and status edits and own-profile edits are online-only offline; whether to keep the printed patient card (the web has none); the pregnancy trimester (there's no field, so the strict rules always apply).

## 5. Screen state vs the web
DONE means coded to match the web, as of the wrap-up reports in `docs/status/allenN.md`. Nothing has been run or tested.

**Doctor** (`ClinicNode.App/Views/Doctor/`, `Extra/`, `Extra2/`)
- DONE:
  - Today (`TodayPage.cs`)
  - Consultation UI pass (`ConsultationPage.xaml`, `.xaml.cs`, `.Gaps.cs`, `.Ui.cs`). Banner chips (payment, Preg/LMP, G/P, Guardian) and the OB strip are in, with data loaded online and offline via `IConsultationLibraryService`. Vaccine PUT no longer sends `NextDoseDate=null`. Certificate address saves to the profile only when the profile has no address (never overwrites).
  - Rx form (`Extra/DoctorPrescriptionFormPage.cs`)
  - Chart header (`Extra2/DoctorPatientHeaderCard.cs`)
  - Templates workspace (`Extra2/DoctorTemplatesWorkspacePage.cs`)
  - Performance (`Extra2/DoctorPerformancePage.cs`)
  - Appointments list (`Extra/DoctorAppointmentsPage.cs`) and Appointment detail (`Extra/DoctorAppointmentDetailPage.cs`). The desktop-only Print summary button was removed (the web has none).
  - Patients list (`Extra/DoctorPatientsPage.cs`)
  - Patient chart body (`Extra/DoctorPatientChartPage.cs`); header was already done. Vitals list uses names instead of icons; prescription actions are text buttons.
  - Schedule (`Extra/DoctorSchedulePage.cs`). Daily patient limit is left read-only (Jessie's G8 rule and the admin-only API); the web lets the doctor edit it — decision still open.
  - Profile (`Extra/DoctorProfilePage.cs`). See Differences from the web below: name, specialization, fee, license, PTR and S2 are greyed out with "Contact the admin to change this."; Save sends only Bio.
  - New walk-in consult (`NewWalkInPage.xaml.cs`)
- PARTIAL: none in this portal, pending Jessie's call on the doctor daily-limit field (coded read-only). This batch compiled clean at 18:32.
- NOT STARTED: none known.

**Staff** (`ClinicNode.App/Views/Staff/`)
- DONE: Dashboard, walk-in wizard, Queue, Payments, Patients list (pager), Booking detail, Staff patient detail (`StaffPatientDetailPage.cs`), Doctor status, Announcements, Profile. Each list/detail page now shows the web's error card.
- PARTIAL: none in this portal. Skeletons are still spinners (intentional).

**Admin** (`ClinicNode.App/Views/Admin/`, `ViewModels/Admin/`)
- DONE: Dashboard, Reports, Bookings list and week view, Booking detail (modals 1:1), Walk-in, Doctors list and doctor new/edit (failed Deactivate now keeps the dialog open with the error), Staff list (error card), Patients list and patient detail, Settings (each Save now sends only its own group, like the web; the services list was removed because the web page has none), Announcements, Audit logs, Sync computers, Receipt number clashes, Settings online-only guard.
- PARTIAL: none in this portal. Leftover unused members remain: `_outbox` in `AdminSettingsService.cs` (kept so tests still construct the service), old members in `AdminPatientsViewModel`, and unused services-tab members in `AdminSettingsViewModel`. An unknown patient shows "Patient unavailable" + Try again (the desktop has no 404 page).

**Shared:** login, forgot password, top-bar sync status, sync conflicts page, online-first proxies, queue blocks, OR clash handling. All coded, none verified against a running server. Module wiring is in `MauiProgram.cs` and `AppShell.xaml.cs` (added at wrap-up).

<!-- JJQR - AI (21 Nov 2026) -->
### Differences from the web
- **Intentional:** Doctor Profile: Name, Specialization, Fee, License #, PTR # and S2 # are read-only on desktop with 'Contact the admin to change this.'; Save sends only Bio via PUT /api/doctors/me. Intentional: the web shows these as editable, but its PUT /api/doctors/{id} is Admin-only and the server rejects the doctor's save (web bug, separate job).
<!-- JJQR - END -->

## 6. Build state
See section 11.

## 7. Backend changes (Project02-be)
- Sync endpoints (`/api/sync`, queue blocks `POST /api/sync/queue-blocks`, pushable patient consent, `or_number_clash` / 409, pull adds consent/labs/docs).
- Visit type and discount on online bookings, plus 3 vaccine fields (optional, backward-compatible).
- OR-number unique filtered index migration. EF migrations are **applied to the local dev DB only**. The hosted DB needs `deploy/migrate-cloud-sync.sql` and then `deploy/migrate-or-number-unique.sql` (preview first with `@apply = 0`). Neither has been run. **Wait for Jessie.**
- Nothing is committed or pushed.

## 8. Known risks
1. Nothing has ever run end to end. Sync, the offline queue, 401 handling and SignalR are unverified.
2. The SignalR client was bumped to `Microsoft.AspNetCore.SignalR.Client` 10.0.11 to match the server, and that compile is done. `Microsoft.AspNetCore.SignalR.Common` 10.0.11 still carries advisory GHSA-334f-vvp4-7g6p. SignalR is still untested against the running server.
3. The hosted DB is missing the cloud-sync and OR-unique migrations, so the desktop sync will fail against production until they're applied.
4. Patient data is unencrypted locally (`DatabaseEncryptionResult.Plaintext` is the default).
5. ClinicNode.Tests probably won't compile or pass after the behavior changes (walk-in wizard, queue numbers).
6. The wiring was only just added (section 11), so DI ordering problems can only be found at runtime.

## 9. Remaining tasks, in order
1. Get Jessie's answer on the doctor Schedule daily-limit field (web is editable; desktop is read-only per G8) and on the desktop-only patient card printout (recommend keep).
2. If time remains: the patient card printout (the web has none; appointment-detail Print summary was already removed).
3. Optional clean-up: leftover unused members in `AdminSettingsService`, `AdminPatientsViewModel` and `AdminSettingsViewModel`.
4. When Jessie says it's done, run the test pass: unit tests, then an app run against `:5077` (never `:5055`), then sync/offline scenarios.
5. Owner-gated steps: hosted migrations, encryption on, Windows/MSIX, push. The SignalR package bump is done (10.0.11); the advisory remains.

## 10. Gotchas
- Build from `Project02-desktop/ClinicNode`: `dotnet build ClinicNode.App/ClinicNode.App.csproj -f net10.0-maccatalyst -nologo -clp:NoSummary`. `rg` is not installed on the Mac, so use grep.
- `ClinicNode.Core.Doctor` is a namespace, which collides with the entity `ClinicApp.Domain.Entities.Doctor`. Fully qualify it.
- `DoctorTemplateEditorPage.cs` sits in `Extra2/` and now uses namespace `ClinicNode.App.Views.Doctor.Extra2` (fixed this batch; compiled clean at 18:32).
- `ClinicClock` is `ClinicApp.Domain.ClinicClock`.
- Nothing may call `AddClinicNodeAdmin()` after `AddAdminModule`. The module lines must stay after `AddClinicNodeOnlineFirst(...)`.
- `queue_number_reassigned` now only means the cloud assigned a number to a booking that had none.
- Desktop-only review codes (not API codes): `online_only`, `reaction_notes_too_long`, `vaccination_rejected`, `local_table_missing`.
- API JSON is snake_case.

## 11. Wrap-up compile result
- Time: 18:15, Asia/Manila, 21 Nov 2026.
- Command (from `Project02-desktop/ClinicNode`): `dotnet build ClinicNode.App/ClinicNode.App.csproj -f net10.0-maccatalyst -nologo -clp:NoSummary`
- Log: `/tmp/allen0_build_1815.log`
- **Build succeeded. 0 errors, 29 warnings.**
- Wiring was added first, by allen0 only, in `ClinicNode.App/MauiProgram.cs` and `ClinicNode.App/AppShell.xaml.cs`. Backups: `/tmp/MauiProgram.cs.prewire.jjqr`, `/tmp/AppShell.xaml.cs.prewire.jjqr`.
  - After `AddClinicNodeOnlineFirst(...)`: `AddClinicNodeShared`, `AddStaffModule`, `AddAdminModule`, `AddDoctorModule`, `AddDoctorExtraModule`, `AddDoctorExtra2Module`.
  - AppShell: `RegisterStaffRoutes`, `RegisterAdminRoutes`, `RegisterDoctorRoutes`, `RegisterDoctorExtra2Routes`.
  - Doctor flyout: Performance and Templates now point at the Extra2 pages. Performance is a real route.
  - Admin System group: Sync computers (`admin-sync-computers`) and Receipt number clashes (`admin-or-clashes`).
- Warnings (do not fail the build): NU1903/NU1902 package advisories (`Microsoft.AspNetCore.SignalR.Common` 8.0.12 in ClinicNode.Api; backend `System.Security.Cryptography.Pkcs`/`Xml` and `Microsoft.AspNetCore.WebUtilities`); CS0618 obsolete `DisplayAlert`/`DisplayActionSheet`; CA1422 obsolete Mac print APIs in `MacPrintRenderer.cs`; a few CS8625/CS0108/CS7095.
- No compile errors to assign. The app has not been run.
- Addendum, 18:16: allen4 landed `ClinicNode.App/Views/Admin/AdminStaffPage.xaml.cs` after the 18:15 compile. Rebuilt once: **Build succeeded. 0 errors, 16 warnings.** Log: `/tmp/allen0_build_1816.log`.
- Batch 1 compile, 18:32, Asia/Manila: `dotnet build ClinicNode.App/ClinicNode.App.csproj -f net10.0-maccatalyst -nologo -clp:NoSummary`. **Build succeeded. 0 errors, 31 warnings.** Log: `/tmp/allen0_build_1832.log`. SignalR.Client is now 10.0.11; SignalR.Common 10.0.11 still warns NU1903 (GHSA-334f-vvp4-7g6p). Other warnings: backend package advisories, CS0618 DisplayAlert, CA1422 Mac print APIs, CS0414 unused fields, CS8625/CS0108/CS7095. The app has not been run.
<!-- JJQR - END -->
