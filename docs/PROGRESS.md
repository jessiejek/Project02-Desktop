<!-- JJQR - AI (Nov 21, 2026) -->
# Clinic Node — progress

Updated: **21 November 2026, 2:20 PM Asia/Manila**.

This is the overall rollup for the native MAUI clinic node in `Project02-desktop/ClinicNode/`. The rebuild is split across bots; this file summarises their status notes plus the shared app and doctor screens.

Screen inventory: [`NATIVE-SCREEN-INVENTORY.md`](./NATIVE-SCREEN-INVENTORY.md) (42 in-scope screens). Architecture: [`MAUI-ARCHITECTURE.md`](./MAUI-ARCHITECTURE.md). Visual tokens: [`STYLE-GUIDE.md`](./STYLE-GUIDE.md).

## Bot split

| Bot | Scope | Status file |
|---|---|---|
| Shared app (this rollup’s author for design/shell) | `Resources/Styles`, fonts, `AppShell`, `MauiProgram` DI, doctor screens, Light theme, Mac Catalyst launch | (this file + doctor notes below) |
| allen1 | Project02-be sync API | [`status/allen1.md`](./status/allen1.md) |
| allen2 | `ClinicNode.Sync` + `Tests/Sync` | [`status/allen2.md`](./status/allen2.md) |
| allen3 | Staff views, view-models, Core/Staff, Tests/Staff | [`status/allen3.md`](./status/allen3.md) |
| allen4 | Admin views, view-models, Core/Admin, Tests/Admin | [`status/allen4.md`](./status/allen4.md) |
| allen5 | Printing implementation (`Services/Print*`, `MauiPrinter`, `Core/Printing`, Windows) and `Project02-desktop/packaging` | [`status/allen5.md`](./status/allen5.md) |

When a status file exists, the snapshot below incorporates its “last finished / next / blocked” lines. If a file is missing, that lane has not reported yet. Feature bots that need a new AppShell route or DI registration should relay it through the parent rather than editing `AppShell` / `MauiProgram` themselves.

## How completion is counted

Each inventoried screen has a size **S / M / L**. Weights are **S = 1, M = 2, L = 3**. The headline percentage is size-weighted across all 42 screens. A screen counts as complete only when its primary actions work against local SQLite and its layout follows [`STYLE-GUIDE.md`](./STYLE-GUIDE.md). Placeholders do not count.

Approximate P0 weight: **18** (login S, doctor Today M, consultation L, staff dashboard M, payments L, walk-in wizard L, queue M, staff patients M).

<!-- JJQR - AI (21 Nov 2026) - allen0 Day 0 VERIFIED state; supersedes the snapshot numbers below (kept as history). -->
## Verified state — 21 Nov 2026, ~16:30 (UTC+8), allen0 Day 0

> **STALE below:** the "~18% / P1 ~0% / Tests 33" snapshot further down predates the staff, admin, doctor-extra, sync and printing work. Do not quote it.

| Check | Command (Project02-desktop/ClinicNode unless noted) | Result |
|---|---|---|
| Build | `dotnet build ClinicNode.App/ClinicNode.App.csproj -f net10.0-maccatalyst` | exit 0 — 0 errors, 49 warnings (incremental); `--no-incremental`: 0 errors, 23 warnings (incl. NU1902/NU1903 vulnerable packages) |
| Desktop tests | `dotnet test ClinicNode.Tests/ClinicNode.Tests.csproj` | 374 passed / 5 skipped (env-gated) / 0 failed (379) |
| API client tests | `dotnet test ClinicNode.Api.Tests/ClinicNode.Api.Tests.csproj` | 49 passed / 0 skipped / 0 failed |
| Backend tests | `dotnet test tests/ClinicApp.Api.Tests` (Project02-be) | 480 passed / 0 skipped / 0 failed |
| GUI smoke | `scripts/smoke-maccatalyst.sh` | not run on Day 0 |
| TODO/NotImplemented (non-test *.cs/*.xaml) | grep | 43 (all `TODO(allen)` wiring notes) |
| Staff/admin parity (PARITY-staff-admin.md summary) | — | 62 done / 13 partial / 1 missing of 76 (row count does not reconcile yet: 71/6/1) |

State in one line: offline clinic day (login, doctor Today, full consultation, staff desk, admin screens, receipts, 8 prints, sync client + backend sync API, backup/restore) is built and tested against **local SQLite**. **Online-first is not live**: the API layer (`ClinicNode.Api`, `AddOnlineFirst`, 2 converted doctor services) exists, but login is still local-only, so the app never holds an API session and every screen runs locally. Doctor extras are read-only; Doctor Performance has no page; forgot password is a stub; encryption is OFF; no Windows build; `deploy/migrate-cloud-sync.sql` lacks `SyncSelfEditStamps` (`sync_field_stamps`). Nothing is in git (desktop) / uncommitted (be 69, fe 43 entries). Zip snapshots: `/Users/jessiejayrubi/Documents/Allyn-snapshots/20261121/`.

Plan and task board: [`DAY0-PLAN.md`](./DAY0-PLAN.md).
<!-- JJQR - END -->

## Snapshot

| | |
|---|---|
| Size-weighted completion of 42 screens | **~18%** (STALE as of 21 Nov 2026 — see "Verified state") |
| P0 (clinic day, offline) | **~75%** of P0 weight |
| P1 | **~0%** (shell placeholders, except where a bot later reports otherwise) |
| P2 | **0%** |
| Last finished (shared + doctor) | Light theme + web design system; `docs/STYLE-GUIDE.md`; consultation **Print Prescription / Lab Orders / Certificate / queue slip** via `IPrinter` + `IPrintDocumentFactory` only; staff Payments / Queue / Walk-In already routed (owned going forward by allen3) |
| Next (doctor) | Closer 1:1 on Today (compact print on rows) and the walk-in wizard; P1 doctor appointments / charts / profile / availability / templates remain placeholders |
| Tests | 33 passing as of the previous slice (no new Core tests in this slice) |

## Status rollup (other bots)

_No `docs/status/allen1.md` … `allen5.md` found yet._

**Build note (21 Nov 2026, ~2:20 PM Asia/Manila):** `ClinicNode.App` Mac Catalyst build is currently **red**, blocked by allen2 work in `ClinicNode.Sync` (not edited by the shared/doctor bot). `SyncService.cs` still calls `_cloud.PushAsync(pending, ct)` and `_cloud.PullAsync(cursor, ct)` while `ICloudSyncClient` now expects different argument types (`CS1503` at SyncService.cs:75 and :94). A rebuild after the consultation print changes could not be signed or launched until that compiles again.

## Visual system

See [`STYLE-GUIDE.md`](./STYLE-GUIDE.md). Always Light. Staff/admin sidebar is inverse-surface on purpose. Sidebar is 260px and locked after login.

## Already usable (offline, local SQLite)

- **Sign in** (S-LOGIN) — Remember email only; AuthShell layout.
- **Doctor Today** (D-TODAY) — today’s visits, walk-in entry, open consult, live refresh. Compact Print Rx/Labs on the list is not in yet.
- **Consultation** (D-CONSULT) — stepper, Save draft / Save & next / Review & complete; print bar calls `IPrinter` after saving the live draft (Rx A5, lab 4.3×6.4 in, medical certificate A5, queue slip). Print buttons enable when the draft has the matching data.
- **Walk-in (partial)** — find or register a patient and start a consult (shared page under `Views/Doctor`). Not yet the web 3-step wizard + slip at confirm.
- **Staff dashboard / payments / queue** — implemented and styled; further staff work is allen3’s.

Platform: node patient-code allocator, queue and payment services, print document factory, event bus, Mac Catalyst launch fix (see crash note below).

## Mac Catalyst launch (crash root cause and fix)

A fresh `dotnet run --project ClinicNode.App -f net10.0-maccatalyst` must stay up, with no new `~/Library/Logs/DiagnosticReports/ClinicNode.App-*.ips`.

**Root cause (21 Nov 2026, 1:55–1:57 PM Asia/Manila):** SIGKILL / Code Signature Invalid.

1. `EnableCodeSigning=false` left the bundle linker-signed without sealed resources.
2. After a proper ad-hoc sign, launch still died because `Microsoft.Identity.Client.NativeInterop` injected `msalruntime_arm64.dylib` as a **macOS** (not Mac Catalyst) library, Microsoft-signed and broken under Catalyst, referenced as `LC_LOAD_DYLIB` so dyld killed the process at start.

**Fix (still in the tree):** `StripMacOsOnlyMsalRuntime` removes that `NativeReference` before it is linked; `AdHocSignAndVerifyMacCatalystApp` ad-hoc-signs the finished `.app` with sealed resources (`scripts/sign-maccatalyst-app.sh`). Official MAUI signing stays off to avoid provisioning fights. `scripts/smoke-maccatalyst.sh` is the regression gate (codesign --verify, fail if a macOS msalruntime remains, launch, assert the process stays up and no new .ips). First scene creation can take ~30–50s at high CPU (Mono interpreter JIT); that is not a crash.

## Still to port

P0 remaining: 3-step walk-in wizard + slip at confirm (shared page; coordinate with allen3/allen4), staff Patients (allen3), Today compact print, any remaining consult 1:1 chrome.

P1 doctor: appointments, charts, profile, availability, templates.

Do not commit, push, or deploy. Do not touch the running stack on `:3000` / `:5055`. Keep the Photino `ClinicDesktop/` spike.


## Doctor functional slice log (allen-core) — 21 Nov 2026, Asia/Manila

<!-- JJQR - AI (Nov 21, 2026) -->
| # | Item | State | Notes |
|---|------|-------|-------|
| 1 | `scripts/run-mac.sh` | done (2:50 PM) | One command: `DOTNET_CLI_HOME=/Users/jessiejayrubi`, builds, ad-hoc signing is part of the normal build (`AdHocSignAndVerifyMacCatalystApp`, AfterTargets=Build), re-signs if needed, launches. `--no-launch` to verify only. |
| 2 | Consultation vs web | done (build green) | Next (no save), Prev, Save & next; completion checklist overlay (per-section summary, Go to, Complete visit); patient banner (age/sex) + History overlay + last-visit SOAP with copy; ICD-10 search + free text + diagnosis templates (reached from More); SOAP templates + phrase buttons; medicine catalogue search + SIG (meals, times, duration, route, PRN, indication) + Rx templates; lab catalogue; vaccinations (More); auto-Senior at 60+ on the fee step; IPrinter-only print bar. Core: `ClinicNode.Core.Practice.DoctorCatalogService`. Not yet: amend of completed visits. |
| 3 | Today dashboard | built, handed to allen4 | Core `IDoctorDashboardService` (day status, up-next Start/Resume, Needs attention, report tiles, recent bookings, analytics month/year/custom) + code-built `Views/Doctor/TodayPage.cs` (works now, test passes). Parent moved the Today dashboard to allen4 (Views/Doctor/Today); this version stays as the working placeholder until their page is wired. |
| 4 | Top bar search / bell / badge | handed to allen4 | Core `IGlobalSearchService` (patients/doctors/bookings) is in and registered. My draft TopBarView + search/notifications modals is parked at `docs/handoff/TopBar.cs.txt` (not compiled, one syntax slip in the draft) for allen4 to reuse. Shell wiring reverted. |
| 5 | Appointments, charts, profile, availability, templates | handed to allen3 | Views/Doctor/Extra, ViewModels/Doctor/Extra, Core/DoctorExtra. Routes will be wired here when allen3 sends them. |
| 2b | Consultation view / amend | done | `?mode=view` (read-only) and `?mode=amend` (Save amendment; Core `AmendAsync`: clinical content only, fee/payment/booking untouched, status Amended); a finished visit opens read-only with an Amend button. 5 consult/dashboard tests pass. |

Also wired this hour: sync loop start/stop, sync banner + Sync now + conflicts page, admin Register-this-node, SecureStorage node credential store, staff and admin DI/routes/flyouts, receipt printing hook, Revoked sign-out.
<!-- JJQR - END -->

## Update checklist (every slice)

1. Recalculate size-weighted % of 42 and the current phase %.
2. Read `docs/status/allen1.md` … `allen5.md` if present and refresh the rollup section.
3. Set **Last finished** and **Next**.
4. Note test count and whether a fresh `dotnet run -f net10.0-maccatalyst` (or the smoke script) was run, if the app or signing changed.
<!-- JJQR - END -->
