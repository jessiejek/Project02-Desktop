# START HERE — hand-over for whoever (or whichever Claude) works on this next

Written 24 Nov 2026. This is the short version. The long version is in the numbered files next to this one. **No passwords or keys are in this repo.** They are kept outside git (owner's `Allyn/_secrets`).

## 1. What this is
Clinic system for **Grace Medical Clinic** (one doctor + front desk). Four repos under `github.com/jessiejek/`:

| Repo | What | Deploys by |
|---|---|---|
| `Project02-be` | ASP.NET Core .NET 10 API + SQL Server (myASP.NET host) | **a push to `main` deploys LIVE** |
| `Project02-fe` | Next.js 16 web (Vercel) | a push to `main` deploys (about 1 min) |
| `Project02-Desktop` (this repo) | .NET MAUI app, Mac Catalyst + Windows, project folder `ClinicNode/` | a **tag `vX.Y.Z`** builds Mac + Windows in the cloud, signs it and publishes the update feed in `Project02-releases` (about 20–25 min; Windows updates itself silently) |
| `Project02-releases` | public update feed + last 3 installers | written by the release workflow only |

**Where an older numbered file disagrees with this page, this page wins** (for example `7-OFFLINE-MODE-AUDIT.md` still describes the old reserved queue blocks, and `1-SYSTEM-OVERVIEW.md` still lists patient self-registration). Read `1-SYSTEM-OVERVIEW.md` for the picture, `2-OPERATIONS-RUNBOOK.md` for how to operate, `5-TODO-AND-RISKS.md` for the open list.

## 2. State on 25 Nov 2026 evening (production: desktop v0.2.45, backend main e4494a3, web main bd0f449 — all shipped 24 Nov)

**Full detail: `HANDOFF.md` at the repo root (same file in all three repos). Where this page and HANDOFF.md disagree, HANDOFF.md wins.**

**Committed on branch `handoff/claude-to-grok-2026-11-25` (desktop + web), NOT merged, NOT released — next release = desktop v0.2.46 + web update; backend unchanged:**
- **Templates (desktop):** the editor opens in the app frame and closes however it was opened (`LeaveAsync`); never stays stuck; a double-tap opens one editor. Opt-in live QA `ClinicNode.Api.Tests/TemplatesLiveServerQaTests.cs` (localhost only), 6/6.
- **Uploads tagged to ONE consultation (desktop + web):** every file belongs to exactly one visit (booking) — never "the latest visit".
  - Consultation step **4. Lab Orders**: **"Upload results" in the step title bar (right)**; layout **75% tests / 25% "Uploaded results"** for this consultation (Open). 1–10 PDF/JPG/PNG, 10 MB each, one title (prefilled with the ordered tests), "(1 of 3)" added per file. Desktop: `ConsultationPage.LabUploads.cs`, `Extra/PatientFileUpload.cs`; web: `components/doctor/VisitUploadModal.tsx`, `VisitFilesPanel.tsx`, `lib/visit-upload.ts`.
  - Patient chart: each visit's details has **"Results & files"** (that visit only) + Upload result / Upload document. No uploads on Cancelled / NoShow / Expired / Rescheduled visits. Lab Results / Documents tabs show which visit each file belongs to (and Open); they do not upload.
  - Consultation header: **"Patient chart"** right after "Patient history" — opens the chart WITHOUT the booking (no prescription editing there). Web opens it in a new tab.
  - Uses the existing endpoints `POST /api/patient-lab-results` / `/api/patient-documents` (bookingId required by the server since Sep 2026). **No backend change.**
- **Reprint (desktop + web):** a **"Reprint" button in the chart's left panel** (desktop: under "Print patient card"; the web has no patient card). Lists every visit with something printable: each prescription on its own ("Prescription 1 / 2"), lab request, medical certificate. Desktop prints from this computer's synced copy and says "not on this computer yet" when a visit is missing; per-group print via `IPrintDocumentFactory.PrescriptionGroupAsync`.
- **Pop-ups close first, then answer (desktop):** `StaffDialog`, `CancelReasonDialog`, `RecordPaymentPage`, `QuickRegisterPage`, Reschedule, the two Prescription-form dialogs, `AdminDialog`, `ClinicDialog` (`Views/Staff/ModalClose.cs`). While their work is being sent, Back / X / Esc do nothing (no duplicate uploads, no double payment).
- **Code reviews:** desktop 3 rounds + web 1 round, every finding fixed. Desktop tests: only the known 11 GoldenParity (CRLF) + 1 PaperQueueTests fail; web `tsc` clean + `visit-upload.test.mjs` pass.
- **Web J-1 done 25 Nov on a local fake-data stack** (never the live Preview): all checks passed. Two fixes added — several files with no suggested title pre-fill "Lab results <visit date>" / "Documents <visit date>" (desktop `2283986`, web `4d033ac`, same rule); web upload times in Manila time (`4d033ac`). Web tests: the same 13 old failures as web `main`; `tsc` clean.
- **Old bugs FIXED tonight (25 Nov, owner: "fix all"; see HANDOFF.md F):** Rx preview Sig line now matches the printed form (web `1a83ebf`); "Consultation saved" shows the amount charged, not the opening fee (web `cde95ec`, desktop `b79fe4a`); "This visit already has a prescription. Replace it?" before a save replaces one (web `107bfdb`, desktop `b6a74bc`); iPad Safari print windows open in the tap, blocked = "Allow pop-ups" (web `185e7fe`). Desktop `b6a74bc` + `b79fe4a` are not compiled yet: build on Windows.
- **Not yet verified on screen:** desktop pop-up fixes, the last review fixes and `2283986` (Windows QA copy, after clinic hours). **Users are on Windows (desktop app) + iPad (the website in Safari; no iPad app). Mac is not a user platform and MacBoy is no longer part of the workflow**; all work is on the Windows laptop. The cloud release still builds Mac (Xcode 26.0) and a failed Mac build publishes nothing, so it would block the Windows update (HANDOFF.md D).
- **Next:** Windows build + QA copy click-through (J-3) and the iPad check on the website (HANDOFF.md J-2); the prescription-overwrite warning is done (J-6).
- **QA copy on the Windows laptop:** worktree `D:\sidequest\Project02-Desktop-qa` (manifest Identity Name `ph.clinicnode.qa`, never commit it), run with `CLINIC_NODE_CLOUD_URL=http://localhost:5055` against a local Development backend with fake `*@clinic.test` accounts. Never test on screen while the doctor uses the laptop.

**SHIPPED 24 Nov 2026 late evening (v0.2.45, owner tried it: "Works, ship it"): what a consultation needs before Complete is now an Admin choice.**
- **Admin → Settings → Consultation** (tab after Payments; web and desktop): tick boxes Clinical notes / Vital signs / Prescription, saved right away; **Professional fee always required** (shown ticked and locked). Default (nothing chosen yet) = Clinical notes + fee, so **vital signs are no longer required** (owner: the doctor does not want them required).
- Stored in `clinic_settings.consult_required` (comma list `notes,vitals,prescription`; null = `notes`; `""` = fee only), additive migration `AddConsultRequired`, changed only by `PUT /api/settings/consult-required` (Admin). The server does NOT enforce it; the web (`requiredSectionsFor` in consultation2/page.tsx) and the desktop (`ConsultSteps.RequiredFrom`, `ConsultSteps.ConfiguredRequired` set whenever clinic settings are read, including the offline synced copy) both apply it.
- **Follow-up never blocks**: no follow-up date = no follow-up. Offline save no longer creates a follow-up dated today when "Schedule date" has no date (online already behaved this way).
- Tests: backend 523 pass (+3 new) and the 4 known old queue-clash failures; desktop consultation tests 12/12; web tsc clean (`diagnosis-hidden.test.mjs` updated for the new code; web node tests not run here: Node 16).

**SHIPPED 24 Nov 2026 ~9 PM (owner said "Ship", no backup by owner's choice):** backend deploy 145 live 20:59 with migration `AddAutoPaidOnComplete` (`/api/settings` shows `auto_paid_on_complete: false`); web pushed; desktop **v0.2.44** on the update feed 21:10. It contains everything below marked "built 24 Nov evening" (Templates fix, auto-paid switch, doctor Walk-In and Cancel, fast Mark all reviewed). The "NOT released" notes below are history. Still to do: the owner tries the auto-paid switch on the live server (it is OFF).

Done and live: admin Set-password for staff/doctor; clinic-wide **Printing settings** (Admin → Settings → Printing: mode PDF/native/off/each computer, per-document on/off, background pictures, editable lab-test list); A5 lab request, crisp traced pictures, license number printed when filled, "Include PTR in print" tick (default off, desktop only); patient **self-registration closed** (backend refuses; web shows a notice; reopen with host variable `Registration__PatientSelfSignUp=true`); **queue numbers now start at R-001 each day and an offline computer carries on from the last number it knows** (no more reserved blocks of 20; the server raises its counter to the number a computer pushes; a duplicate number from two offline computers is flagged for the admin, never renumbered); web now applies the same print settings, leaves PTR off, and on iPhone/iPad makes a real A5 PDF.

**Checked on the Windows laptop, 24 Nov 2026 evening:**
- **Templates: fixed and confirmed by the owner (committed locally, NOT released yet; ships with the next release).** The v0.2.43 fix was not enough: it still crashed (hard exit `0xC000027B`, nothing in `errors.log`) when switching tab / "This tab–All tabs" / "List–Cards". Real cause, caught with a temporary first-chance trace: `RenderSoon()` cleared the list and redrew 30 ms later; by then WinUI had disconnected the old toolbar, so removing `_search` from it no longer removed the native view. Fix: `DetachSearch()` runs **before** `_list.Children.Clear()` and also removes the native view from any native parent; typing redraws via `UiTurn.Next`. Lesson: detach reused views before clearing their container, not after.
- Hard crashes (`0xC000027B`) write nothing to `errors.log`. To find one: add a temporary `AppDomain.CurrentDomain.FirstChanceException` logger in `Platforms/Windows/App.xaml.cs` (do not commit), reproduce, read the last `COMException` stack.

**New, built 24 Nov 2026 evening, NOT deployed / released (branches `auto-paid-on-complete` in Project02-be, Project02-fe and this repo, committed locally, not pushed):**
- **Admin → Settings → Payments: "Mark the visit paid when the doctor completes it"** (off by default; saves right away, no Save button; web and desktop). When on, `PUT /api/queue/{id}/complete` (web and desktop both call it) reconciles the fee and then marks the still-unpaid payment **Paid, Cash, exact fee, no OR number**, audited, `PaymentUpdated` broadcast. Waived and ₱0 visits are skipped. Stored in `clinic_settings.auto_paid_on_complete` (additive migration `AddAutoPaidOnComplete`), changed only by `PUT /api/settings/auto-paid-on-complete` (Admin), so an older app saving the whole settings form cannot switch it off. Offline completions are NOT auto-paid (offline the doctor still types the booklet receipt number).
- Tests: backend 4 new pass; backend suite 520 pass + the 4 known old queue-clash failures. Web `tsc` clean; ESLint could not run on this laptop (Node 16 too old for the config).
- **Ship order (matters):** 1) merge + push Project02-be (deploys live, adds the column) → 2) merge + push Project02-fe → 3) merge the desktop branch and tag. The desktop build compiles against Project02-be `main`, so tagging before step 1 breaks the release build.

**SHIPPED 24 Nov 2026 evening (desktop v0.2.44; owner tried each in the test build):**
- **Doctor adds walk-ins online too**: sidebar *Patient care* is now Today · **Walk-In** · Appointments · Patient charts (route `doctor-walk-in` → `StaffWalkInPage`; for the doctor "View booking details" opens the consultation). The server already allows `POST /api/queue` for the doctor.
- **Doctor cancels a booking**: red **Cancel** right after **Start** on Today's queue (waiting rows only; on Windows it stacks under Start), right of **Open consultation →** on Appointments (not seen yet, unpaid), and **Cancel booking** at the bottom of the appointment page's actions card. Reason dialog `CancelReasonDialog`: radio choices Patient left / Patient changed their mind / Duplicate entry / **Others** (Others enables the text box, 5+ characters). Uses `IStaffBookingService.CancelAsync` → `PUT bookings/{id}/status` Cancelled (server: doctor only her own bookings; a paid one is refused). Owner: "Good ui".
- **Sync review → Mark all reviewed** was slow (a full status refresh + page redraw per item). Now `ISyncStatus.ResolveConflictsAsync`: one transaction, one refresh, one redraw; the button says "Marking reviewed…".
- Known, not fixed: some page sends ~9 requests the server refuses (403) on every load (seen 24 Nov, 16 loads in 3 minutes; quietly shown as empty, not a crash). Which page/account is not identified yet.
- Desktop tests on this Windows laptop: 466 pass; 11 `GoldenParityTests` fail only because git checks the golden files out with CRLF here (`core.autocrlf=true`); 1 `PaperQueueTests` test is stale since v0.2.42 (expects "no number", now always R-001). Neither is caused by these changes.

**Still unverified on a real Windows machine:**
- v0.2.42 queue numbering (offline → next number after the highest local one for that day/lane).
- The A5 PDF on a real iPad (print screen should show A5).
- Printing on Windows is **perfect** according to the owner. Do not change the Windows print path without a reason.

## 3. Hard-won rules (the owner will be angry if these are broken)
1. **Never deploy or tag without the owner saying "ship" / "release" / "push".** Batch many requests into ONE release (each release = a 20-minute wait and a silent Windows update).
2. **Say where a UI control goes in one line and build exactly that** ("beside the title", not "below"). Do clearly-wanted things; don't turn them into questions.
3. **The owner types all passwords.** Never type or repeat one. Never put real data in logs or commits.
4. Plain-English TLDR, short bullets. The owner is not a programmer and is often tired and direct.
5. The owner cannot run terminal commands comfortably: for database work give **copy-paste SQL** for the host's SQL Studio (it rejects transactions, `IF`, `THROW`, `SET`; plain `DELETE`/`SELECT` only).
6. Source of truth for form layout is the **web** `Project02-fe/src/lib/print-forms-html.ts`; the desktop `ClinicNode.Core/Printing/ClinicFormsHtml.cs` must match **byte for byte**. Golden tests: `ClinicNode/ClinicNode.Tests` (generated by `generate.py` → `FormsCss.g.cs`, `generate-golden.mjs` → `forms.json`). If you change one side, regenerate and change the other.
7. Comment convention in this codebase: wrap new code in `//JJQR - AI (date) - why … //JJQR - END`.
8. **Shipping only 11:00 PM–12:00 midnight Manila. NEVER 8:00 AM–9:00 PM (clinic hours), even if the owner says "ship".** "Ship" = pushing `main` in any repo, a `v*` tag, merging into `main`, or running a deploy/release workflow. Pushing a non-main branch by exact name is fine any time.
   - **Say the current Manila time out loud before every ship step**; if it is outside 11:00 PM–midnight, stop and tell the owner.
   - **Never ask "are you sure?"** when the owner says "ship" at the wrong time: refuse, say the time and when the window opens, and hold firm (owner, 25 Nov 2026).
9. **If the owner says "ship" outside the window: refuse.** Say the current Manila time and when the window opens ("It's 3:40 PM — clinic hours; we ship at 11:00 PM"), and hold firm even if he insists.

## 4. WinUI (Windows) crash patterns — the real source of most "it crashes on Windows" bugs
The Mac is forgiving; Windows is not. Check every page for these:
- **A view with two parents.** A control reused across redraws (`_search`, a header, a pill) must be removed from its old parent (`if (x.Parent is Layout p) p.Children.Remove(x);`) before it is added to a new one. Symptom: `COMException: No installed components were detected` / "Today empty". Fixed so far in WalkInWizardView, Today, Templates.
- **Redrawing inside your own handler.** Clearing/rebuilding the layout from inside the click/tap/TextChanged handler of a control that is being removed crashes. Use `UiTurn.Next(...)` (let the handler finish first) or the `BusyButton` Windows branch with `await Task.Yield()`.
- `e.Handled = true` crash-log safety net exists (Windows only) so an unhandled UI exception writes to `errors.log` instead of closing the app.
- You cannot compile the Windows branch on a Mac; on the Windows laptop run it for real.

## 5. Where things are
- Logs: `errors.log`, `print-errors.log` next to `clinic-node.db` (Mac: `~/Library/ClinicNode/`; Windows: the app's data folder next to its `clinic-node.db`).
- Update feed: `https://raw.githubusercontent.com/jessiejek/Project02-releases/main/latest.json` (cached ~5 min).
- Print settings are stored on the server (`print_configs` table, `/api/print-config`) and cached per computer in `{dataDir}/print-policy`.
- Queue numbering: server `ClinicApp.Infrastructure/QueueSequencer.cs` (per-date, per-lane counter, Manila time); desktop `ClinicNode.Sync/Local/QueueBlocks.cs` (`TryTakeAsync` = highest + 1).
- Release procedure: runbook section "Release", short form: commit on `main`, `git tag vX.Y.Z`, `git push origin main vX.Y.Z`, wait for `latest.json` to show the version, give the owner the installer links.

## 6. Open items (nothing is blocking the clinic)
- Mac print window defaults to US Letter although the printer lists A5 (Catalyst ignores in-app paper hints; Windows is fine). Options: silent print straight to `ipp://10.128.2.50/ipp/print`, or set the printer default on the Mac, or pick A5 by hand. Owner said fix later.
- Offline queue: two computers offline at once can issue the same number (flagged, not renumbered). With one front desk this is rare.
- Web cannot print the PTR (the "Include PTR" tick exists only in the desktop app).
- Old failing tests: backend 4 (old queue-clash tests), web 13 (old consultation-UI tests). They fail with and without recent changes.
- Server speed: the host is in the USA (6–9 s per request seen on 24 Nov afternoon). The desktop keeps working (local first). Better hosting is the real fix.
- No backups schedule yet (tooling exists in `accounts-tools`, never run by the assistant); no password-reset email; no uptime monitor.

## 7. How to work with the owner
Short answers. Show proof (a screenshot or a log line) before saying "done". If something cannot be verified on this machine, say so plainly. Prefer to finish everything related in one go instead of leaving follow-ups.
