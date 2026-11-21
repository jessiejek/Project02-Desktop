<!-- JJQR - AI (21 Nov 2026) -->
# allen5 status — 21 Nov 2026, ~6:35 PM Asia/Manila (UTC+8)

Current assignment: Doctor **Consultation** and **Today** only (scope narrowed; allen3 owns Appointments, Patients/chart/prescriptions, Schedule, Profile, Templates, Performance, and those printouts). Earlier printing / node-settings work is unchanged and kept below.

- **Stopping point:** clean. No half-edited files. No commit or push. No builds (allen0 is compiling).
- **ClinicClock CS0103:** IN. `ClinicNode.App/Views/Doctor/ConsultationPage.Gaps.cs` line 12 has `using ClinicApp.Domain;` (uses at lines 203, 313, 314).
- **Certificate address → patient profile:** YES, **only when the profile has no address on file**. Never overwrites an existing address. Best-effort after the certificate save (a profile-write failure does not fail the cert). Matches web consultation E1, with two hardenings noted below.

## Batch 3 (~6:20 PM): ALL-1 banner chips + ALL-2 Doctor Profile

**ALL-1, banner chips: DONE (code only, not compiled).** Copied from web `components/doctor/ConsultPatientBanner.tsx` + `consultation2/[bookingId]/page.tsx` (`clinicalBannerProps`, `booking.paymentStatus`):
- Chips in web order, before the allergy chip: **Preg {n}w** (pregnant + LMP), or **LMP {n}w** (not pregnant, LMP set). Next **G{g|?}/P{p|?}** when either is set, then **Guardian: {name} ({relationship})** only when age < 18 and a guardian name exists. A chip is hidden in any state where the web hides it.
- **Payment: {status}** last on the row: "—" for Cancelled / NoShow / Expired bookings, otherwise the payment status (Unpaid / Paid / Waived / Refunded), or "Not recorded".
- OB strip under the banner when pregnant or LMP is set: "OB: Pregnant|LMP recorded · AOG {w}w {d}d · EDD yyyy-MM-dd · G/P · Rh x · {code · sex · dob (age y)}". The web shows this strip on large screens only; the desktop is always wide.
- **Data, online-first via the existing `AddOnlineFirst<IConsultationLibraryService, ApiConsultationLibraryService, ConsultationLibraryService>`:**
  - **Online:**
    - `GET /api/patients/{id}`: is_pregnant, lmp_date, gravida, para, rh_factor, guardian_name, guardian_relationship. These were added to `PatientClinicalFlags`.
    - `GET /api/bookings/{id}`: booking status + embedded `payments.status`, via the new `GetBookingPaymentDisplayAsync`. These are the web's routes; no new endpoints.
  - **Offline:** the local SQLite mirror, `db.Patients` and `db.Bookings` + `Payment`.
- **Files:**
  - `ClinicNode.Core/Doctor/IConsultationLibraryService.cs`: record fields, `ConsultPaymentDisplay`, the interface method.
  - `ClinicNode.Core/Doctor/ConsultationLibraryService.cs`: local side.
  - `ClinicNode.Api/Services/ApiConsultationLibraryService.cs`: API side.
  - `ClinicNode.App/Views/Doctor/ConsultationPage.Ui.cs`: `LoadBannerDataAsync`, chips, OB strip.
  - `ClinicNode.App/Views/Doctor/ConsultationPage.Gaps.cs`: one call in `LoadGapDataAsync`.
- No new DI or route lines. The interface method is picked up by the existing online-first proxy.

**ALL-2, Doctor Profile: NOT EDITED (blocked by scope rule).** The page is `ClinicNode.App/Views/Doctor/Extra/DoctorProfilePage.cs`, and its service is `ClinicNode.Core/DoctorExtra/DoctorProfileService.cs`. Both sit under `Views/Doctor/Extra/**` and `Core/DoctorExtra/**`, which the same batch says not to edit. allen0 needs to decide one of these:
- (a) lift the Extra rule for this one file, or
- (b) accept a new page in my lane plus a flyout switch in AppShell (allen0's line).

Diff list, web `doctor/profile/page.tsx` vs the desktop page:
1. **Editable fields: needs Jessie.** Web: all fields editable. Save = GET+PUT `/api/doctors/{id}` + GET+PUT `/api/staff-accounts/{id}`. Backend `PUT /api/doctors/{id}` is `[Authorize(Roles="Admin")]`, so a doctor saving on the web gets "Some profile changes could not be saved. Please review your details and try again." Desktop (allen3, on purpose): full name / specialization / fee / license / PTR / S2 read-only; Save = bio only via `PUT /api/doctors/me`.
2. Validation: web "Enter your full name and a valid non-negative consultation fee." Desktop has none, because it saves the bio only.
3. Layout:
   - The web is ONE card. "Professional details" (headline-md) + email, then Full Name, Specialization, Bio. Next Consultation Fee / License # / PTR # / S2 # in a 2-column grid, then "Save profile" ("Saving profile…" while busy).
   - In the same card, behind a divider: muted text "Clinic availability" + "Manage availability" link, then the "Change Password" section (headline-sm).
   - The desktop uses three separate cards, with "Clinic availability" as a heading.
4. Load error: web ErrorState "Profile unavailable" / "Your profile could not be loaded. Please try again." + Retry. Desktop: base LoadError / NotLinked.
5. Password:
   - The web uses placeholders equal to the labels, "Updating…" while busy, the error in an error-container block, toast "Password updated.", and `POST /api/auth/change-password`.
   - The desktop has no placeholders and no busy label, and delegates to the staff profile service.

## Profile batch (~6:26 PM): ALL-2 Doctor Profile 1:1 (Extra rule lifted for 2 files only)

**Status: DONE (code only, not compiled).**

**Files:**
- Edited: `ClinicNode/ClinicNode.App/Views/Doctor/Extra/DoctorProfilePage.cs`.
- Not changed: `ClinicNode/ClinicNode.Core/DoctorExtra/DoctorProfileService.cs`. It was allowed, but nothing needed changing.

**Backups:** `/tmp/jjqr-profile/` (both files + this doc, before edit).

**Changes, vs web `Project02-fe/src/app/doctor/profile/page.tsx`:**
- **One card**, not three:
  - "Professional details" (headline-md) + email, then a divider and the save error.
  - Full Name, Specialization, Bio.
  - **2-column grid:** Consultation Fee / License # / PTR # / S2 #.
  - "Save profile", reading **"Saving profile…"** while busy.
  - A divider, then "Clinic availability" (muted text) + "Manage availability" link (opens DoctorSchedulePage).
  - A divider, then the **Change Password** section in the same card.
- **Validation:** web check and wording: "Enter your full name and a valid non-negative consultation fee." Empty fee counts as 0, like the web's `Number("")`.
- **Saved message:** "Profile saved at {h:mm:ss tt}." in Manila time.
- **Load error:** web ErrorState "Profile unavailable" / "Your profile could not be loaded. Please try again." with "Try again", via the base page's existing `LoadErrorState`; the body is hidden while it shows. A non-doctor sign-in still shows "Doctor profile not linked".
- **Password section:**
  - Placeholders equal to the labels: Current Password / New Password / Confirm Password.
  - "Update Password" reads **"Updating…"** while busy.
  - The button is disabled until current is non-empty, new is at least the minimum length, and confirm matches (the web's `canUpdate`).
  - Fields clear on success, then "Password updated.".
- **Password route:** already the web's `POST /api/auth/change-password` when online. The path is `IDoctorProfileService` (the API side throws ApiNotSupported, so the local side runs) → `IStaffProfileService` online-first proxy → `ApiStaffProfileService`. Offline it falls back locally. Unchanged; no new endpoint.

**Web diffs left on purpose (Jessie confirmed at 6:30 PM — final):**
- **Read-only Admin fields:** Full Name, Specialization, Consultation Fee, License #, PTR # and S2 # stay **read-only**.
- **Bio-only save:** Save sends **Bio only via `PUT /api/doctors/me`**.
- **Why:** the web saves everything with GET+PUT `/api/doctors/{id}` + `/api/staff-accounts/{id}`. `PUT /api/doctors/{id}` is Admin-only, so the web save fails for a doctor.

**Other:** no new DI or route lines.

## Profile follow-up (~6:30 PM): Jessie decided, read-only fields stay
- Jessie: keep Full Name, Specialization, Consultation Fee, License #, PTR # and S2 # read-only. Save sends only the Bio (`PUT /api/doctors/me`).
- `ClinicNode/ClinicNode.App/Views/Doctor/Extra/DoctorProfilePage.cs`:
  - The 6 fields are `IsReadOnly` with a muted background (`SurfaceContainerLow`) and muted text (`OnSurfaceVariant`).
  - A muted note reading exactly "Contact the admin to change this." sits under Full Name/Specialization and under the fee/license/PTR/S2 grid.
  - Bio is unchanged and stays editable.
- `docs/HANDOVER.md`: the doc had no web-differences list, so I added a new "### Differences from the web" subsection at the end of section 5 ("Screen state vs the web"), just before "## 6. Build state". The entry is marked intentional (web bug, separate job).
- Backups: `/tmp/jjqr-profile2/` (DoctorProfilePage.cs, allen5.md, HANDOVER.md).

## Files I own

Under `Project02-desktop/ClinicNode`:

- `ClinicNode.App/Views/Doctor/ConsultationPage.xaml` + `.xaml.cs` + `.Gaps.cs` + `.Safety.cs` + `.Ui.cs` (new)
- `ClinicNode.App/Views/Doctor/TodayPage.cs`
- `ClinicNode.App/Doctor/DoctorModule.cs`
- `ClinicNode.App/Doctor/ConsultLocalWriteThrough.cs`
- `ClinicNode.Core/Doctor/` consultation services (`ConsultRules.cs`, `ConsultDraftExtras.cs`, `IConsultationLibraryService.cs`, `ConsultationLibraryService.cs`, `DoctorConsultationLocalService.cs`, `ConsultClinicalAids.cs`, `IConsultationQueueService.cs`, `ConsultSafety.cs`)
- `ClinicNode.Api/Services/ApiConsultationService.cs`, `ApiConsultationLibraryService.cs`, `ApiConsultationQueueService.cs`

Do **not** own: `Views/Doctor/Extra/**`, Extra2, `Core/DoctorExtra/**`, MauiProgram.cs, AppShell, Staff, Admin, shared layout, vaccine outbox / Finish chart/PRN (allen2).

## Done (code only — not compiled by me)

- Today 1:1 vs the web doctor dashboard. Jessie confirmed final (verbatim): keep the desktop-only **New walk-in consult** button, **Print Rx / Print Labs**, and **Announcements** card.
- Consultation line-by-line UI pass vs web `consultation2/[bookingId]`: banner, header (including "?" help before View Patient History), step indicators, section layouts/colours, vitals cards + out-of-range, labs confirm dialog, vaccinations line format, medical-certificate confirm, footer save-status wording, cancel-amendment dialog, completion checklist, after-Complete strip, completed-visit record cards. Professional Fee wording already matched the web; left as-is.
- **UpdatePatientAddressAsync** (`ApiConsultationLibraryService.cs` ~131–141): GET-then-PUT `/api/patients/{id}` (same read-only keys as the web). Caller in `ConsultationPage.xaml.cs` ~1262–1272 runs it only after a successful cert save, and only when `_patientAddressMissing` was true at load. Service now **re-checks** `address` on the fresh GET and **returns false without PUT** if one is already on file (line 140–141) — never overwrite, including an address added after page load (the web only checks at page load). Any failure except cancel returns false.
- Vaccine PUT: does **not** send `NextDoseDate=null` (`ApiConsultationService.cs` ~178, 277). Next-dose is kept on load and sent only when set. Offline, next dose / given date / reaction / status are kept across the replace-all save (`DoctorConsultationLocalService.cs` ~165–200). New staged lines get an id before saving.
- Bare entity name `Doctor` in my files: the one use in `ApiConsultationLibraryService.cs` line 68 is qualified as `ClinicApp.Domain.Entities.Doctor`. Namespace `ClinicNode.Core.Doctor` was **not** renamed (allen2 is qualifying QueueService).

Backups on the Mac: `/tmp/TodayPage.cs.pre-1to1.jjqr`, `/tmp/*.pre-uipass.jjqr` (Consultation page files before the UI pass), `/tmp/jjqr-batch3/` (batch 3 files before edit, incl. this doc).

## Half-done (file and line)

No half-edited files. Remaining gaps are **not started** (no incomplete edits to finish or revert):

- (Batch 3) Banner chips now DONE (see above).
- (Batch 3) Doctor Profile: now DONE in the Profile batch (rule lifted for DoctorProfilePage.cs / DoctorProfileService.cs only).
- (Superseded: allen0 says MauiProgram/AppShell are now wired.) `MauiProgram.cs` still needs `ClinicNode.App.Doctor.DoctorModule.AddDoctorModule(services);` after `AddClinicNodeOnlineFirst(...)`, and `AppShell` still needs `ClinicNode.App.Doctor.DoctorModule.RegisterDoctorRoutes();`. Both files are off-limits to me (allen0, wave-end wiring).

## Web diffs left on purpose

Call next / Start (G9) and the desktop sync label; Today extras as Jessie confirmed; unpaid list still from allen2; specialty chips and vitals-history wording stay desktop; print buttons after Complete use a hidden button for the web's disabled one; Charge/Waive are pills, not underlined tabs; "No follow-up" shows a check as an approximation.

## What I'd do next

1. Fix any compile errors allen0 finds in my files (I did not build).
2. Doctor Profile: only Jessie's save decision remains (Admin-only fields editable + PUT /api/doctors/{id} like the web, or keep Bio via /me).
3. After wave-end wiring in MauiProgram/AppShell: a smoke of Today → Consultation (allen0's compile/run, not mine).

<!-- JJQR - END -->

<!-- JJQR - AI (Nov 21, 2026) - allen5 status: printing + packaging -->
# allen5 — printing & packaging

Phases (owner order): 1) all prints working on Mac Catalyst (content + paper size) → 2) visual parity with web → 3) packaging (DMG, then MSIX).

- Percent: 85%
- Phase 1 (functionality): DONE on Mac (Rx A5, lab, med cert, queue slip web/thermal/ESC-POS, Official Receipt + PENDING SYNC). No physical printing done.
- Phase 2 (visual parity): golden tests DONE (38 cases, byte-identical to the web builders). Mac scale + margin parity FIXED (see below). Remaining: side-by-side with a real Chrome print (no Chrome on this Mac), physical-printer check.
- Phase 3 (packaging): DMG script done and run OK. Windows MSIX written, NOT built (needs a Windows PC).
- Tests: 233 passed / 2 skipped (whole ClinicNode.Tests), printing 82/82. maccatalyst app build: red at last run ONLY from other agents' files (FileSaver.cs NSSavePanel, DoctorSchedulePage.cs ClinicClock); nothing from Printing.
- Last item: Mac parity — WKWebView print layout is 1.25 px/pt (not 96 dpi): every mm/in/px came out 6.67% too big. Fixed with html zoom 0.9375 (PrintPaper.WithWebPageMargin); web 16 mm @page margin applied for slip/receipt. Verified with calibration boxes (calib*.pdf) and pdftotext -bbox (text at 63.36 pt = 16 mm + 24 px).
- Blockers: IPrintDocumentFactory/IPrinter/MauiProgram are outside my paths — allen must apply packaging/README.md "Required shared-file changes".

## Next steps
1. (done) Golden tests: regenerate with `node ClinicNode.Tests/Printing/golden/generate-golden.mjs` when print-forms-html.ts changes; GoldenParityTests re-checks.
2. Visual parity: compare smoke PDFs with a real Chrome print of the web forms (Chrome not installed here); re-run `packaging/print-smoke/run-smoke.sh`.
3. Real printer test on Mac (A5 tray, lab pad custom size, thermal roll driver) using `PrintSmoke out --print`.
4. Windows: build on a PC (`packaging/windows/build-msix.ps1 -DevCert`), fix any compile issues in Platforms/Windows/Printing (never compiled), test PrintManager dialog, silent print, raw spooler.
5. Optional: thermal receipt layout when queueSlipStyle is Thermal*; settings UI for printing.json; "Start with Windows" switch (WindowsStartup).
6. Owner: Developer ID cert + notary profile (Mac); code-signing cert matching Package.appxmanifest Publisher (Windows).

## Node security / backup / settings (RESUMED, ~95%: code + tests done; waiting on allen to paste the snippets below)
- Last item done: Settings page/VM, NodeSettings tests (70 pass), sqlcipher-tests 7/7, maccatalyst build green.
- Next item: allen pastes (1) csproj swap, (2) startup hook, (3) route + nav entry. Then a manual smoke on a COPY of the sandbox DB.
- Files: Core/NodeSettings/{SqlCipher,DatabaseEncryption,DatabaseEncryptionServiceCollectionExtensions,BackupFormat,BackupService}.cs; App/Services/Security/{SecureStorageDatabaseKeyStore,EncryptedDatabaseStartup}.cs; App/Services/Backup/{BackupFileExchange,AppRestart,NodeBackupCoordinator}.cs; App/ViewModels/Settings/Node/NodeSettingsViewModel.cs; App/Views/Settings/Node/NodeSettingsPage.cs; Core/Printing/DocumentPrintSettings.cs; Tests/NodeSettings/*; packaging/sqlcipher-tests.
- Fixed at resume: BackupFormat.WriteAsync now writes the real plaintext length into the header (seekable streams), so Read's size check matches; printing.json enums stay PascalCase ("Prescription", "A5", "Thermal80") like the existing QueueSlipStyle values; ClinicNode.Data's factory became a fixed-options singleton (no AddDbContextFactory), so ConfigureDbContext no longer reached it. AddClinicNodeDatabaseEncryption now replaces the IDbContextFactory<ClinicAppDbContext> registration with a mirror (SQLite + snake_case + SyncChangeLogEnabled=false) that carries the key.

### Snippets for allen (shared files I may not edit)
1. ClinicNode.App.csproj (the one real swap; DI for NodeSettingsPage / coordinator / IBackupFileExchange is ALREADY in MauiProgram.AddClinicNodeAppServices):
```xml
<!-- JJQR - AI (Nov 21, 2026) - allen5: SQLCipher at rest. Replaces SQLitePCLRaw.bundle_e_sqlite3. -->
<PackageReference Include="SQLitePCLRaw.bundle_e_sqlcipher" Version="2.1.11" />
<!-- keep the e_sqlite3 bundle that Microsoft.EntityFrameworkCore.Sqlite pulls in transitively out of the build/native assets -->
<PackageReference Include="SQLitePCLRaw.bundle_e_sqlite3" Version="2.1.12" ExcludeAssets="all" PrivateAssets="all" />
<!-- JJQR - END -->
```
   i.e. DELETE `<PackageReference Include="SQLitePCLRaw.bundle_e_sqlite3" Version="2.1.12" />` and add the two lines above. This is exactly what packaging/sqlcipher-tests uses (7/7). No 2.1.12 exists for sqlcipher, 2.1.11 is the latest. ClinicNode.Tests, ClinicNode.Core, .Data, .Sync stay on plain SQLite (no change). Until this swap, SqlCipher.IsAvailable() is false and the startup hook is a safe no-op (CipherUnavailable, DB untouched).
2. MauiProgram.CreateMauiApp: right after `AddClinicNodeAppServices(...)` and before `builder.Build()`:
```csharp
//JJQR - AI (Nov 21, 2026) - allen5: encrypt the local DB at rest (one-time verified migration; key in SecureStorage) and point EF at it.
// Must run after AddClinicNodeAppServices (it replaces the DbContext factory registration) and before anything opens the DB.
ClinicNode.App.Services.Security.EncryptedDatabaseStartup.AddEncryptedClinicDatabase(builder.Services, options);
//JJQR - END
```
   Keep `SQLitePCL.Batteries_V2.Init()` before it (the bundle's Init loads the cipher). The call throws DatabaseKeyUnavailableException when the DB is encrypted but the key is gone (never resets data); wrap in try/catch only if you want a friendly error page, otherwise let it surface in node-errors.log. ClinicNode.Data needs NO edit.
   Cleaner alternative (needs an edit in ClinicNode.Data, so allen's call): NodeOptions gets `public string? DatabaseKey { get; set; }` and `SqliteConnectionString => DatabaseKey is null ? $"Data Source={DatabasePath}" : ClinicNode.Core.NodeSettings.SqlCipher.ConnectionString(DatabasePath, DatabaseKey)`; then the hook only has to set options.DatabaseKey from the result and AddClinicNodeDatabaseEncryption's factory swap can go.
3. AppShell.xaml.cs, with the other routes:
```csharp
//JJQR - AI (Nov 21, 2026) - allen5: this-computer settings
Routing.RegisterRoute("node-settings", typeof(ClinicNode.App.Views.Settings.Node.NodeSettingsPage));
//JJQR - END
```
   Navigation: `await Shell.Current.GoToAsync("node-settings");` from a sidebar entry, e.g. next to line ~231 `new ClinicNavItem("This computer", "node-settings", true, "settings")` and `AddFlyout("This computer", "node-settings", typeof(NodeSettingsPage));` (page needs DI, so keep the route/DataTemplate resolving it through the container as the other pages do).
4. Tests: DiSmokeTests (not mine) fails for NodeSettingsPage, AdminRegisterNodePage and AppShell because its IsMauiRuntimeGap() does not recognise `NotImplementedInReferenceAssemblyException` / "BindableObject was not instantiated on a thread with a dispatcher". Add `if (m.Contains("ReferenceAssembly", StringComparison.OrdinalIgnoreCase) || m.Contains("dispatcher", StringComparison.OrdinalIgnoreCase)) return true;` to it.

### Risks
- Bundle swap: native lib load order; verified only for macOS/Linux host builds (sqlcipher-tests) and by the packaged native libs list (maccatalyst, osx, win-x64/arm64). The maccatalyst app itself has not been launched with the cipher. First launch on the owner's real DB will migrate it: back up the sandbox DB first. Migration keeps a verified plaintext backup until counts+integrity match, then wipes it.
- A lost Keychain item (new Mac user, keychain reset) makes the DB unreadable; the only path is Restore from a .cnbackup. Mac Catalyst SecureStorage may need the keychain-access-groups entitlement in Entitlements.plist if SetAsync fails (the vault-failure path keeps the DB plaintext and logs it; no data loss).
- Mac sandbox cannot relaunch after Restore: the app quits and must be reopened. Windows picker/AppInstance.Restart/renderer code compiles only on Windows, never built here.
- PBKDF2 600k iterations: a second or two on slow PCs. Backups contain all PHI; the password is not recoverable.

## Test: print + launch (3:35 PM (UTC+8))
- Launch via scripts/run-mac.sh: builds, signature ok, no managed exceptions/crash reports in the log (macOS framework noise only). UI not drivable from Shell (no Accessibility / Screen Recording permission).
- Real-DB print: packaging/print-real (copy of sandbox DB under /tmp only) completes 2 real consults + 2 real payments via the app services, builds all 8 documents with ClinicFormsPrintDocumentFactory: 18/18 steps pass. Rendered through MacPrintRenderer (print-smoke --html-dir): packaging/print-real/out/*.png, all at exact sizes.
- OPEN (not mine): sandbox DB lacks sync_changes / sync_nodes / sync_operations → every EF save throws "no such table: sync_changes" (DemoClinicDay.cs:65 at startup, ConsultationService.cs:401, payments). ClinicNode.Tests does not compile (ClinicDayTests.cs:68/93, StaffRealDbHarness.cs:73-75). Rx line order not stable (ConsultationService.cs:491 stamps one CreatedAt for all lines).
<!-- JJQR - END -->
