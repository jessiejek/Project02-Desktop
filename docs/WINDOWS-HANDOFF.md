# Windows laptop handoff (written 23 Nov 2026)

Read this first if you are Claude Code starting on the Windows laptop. No secrets in this file.

## What this is
Clinic Node: a .NET MAUI desktop app (Mac Catalyst + Windows) for a one-doctor clinic (Grace Medical Clinic), online-first with an Offline mode (SQLite copy + sync). Solution: `ClinicNode/`. The backend repo `Project02-be` must be cloned NEXT TO this folder (the projects reference `../../../Project02-be/src/...`). Folder names matter: `Project02-desktop` and `Project02-be`.

## Who the user is and how they work
Clinic owner, not a developer. Wants plain English, short bullets, decisions made for them. They type all passwords themselves: never ask for, repeat or store one. They get angry at long jargon and at repeated questions.

## Hard rules
- Do NOT push, tag or release without the owner saying "push" / "release". A tag `vX.Y.Z` triggers the cloud build that ships to the clinic's computers.
- Never push to `Project02-be` main (it deploys the live clinic server).
- Commit locally first, show what changed.
- Do not use real patient data beyond what the app already shows.

## How releases work
`git tag -a vX.Y.Z -m "notes"` then `git push origin vX.Y.Z`. GitHub Actions (`.github/workflows/release-desktop.yml`) builds the Mac app and the Windows `Setup.exe`, signs the update feed, publishes to the public `Project02-releases` repo (keeps the last 3 versions). Installed apps update themselves. Windows installs per user and updates silently; its version label comes from the registry key `HKCU\Software\Clinic Node\Version`. Windows needs `-p:ClinicWindowsOnly=true` when building.

## Data folders and logs
The app data folder holds `clinic-node.db`, `errors.log`, `node-errors.log`, `print-errors.log`. Read these first when something fails.

## Known Windows-only problems (the Mac works)
The author could only test on a Mac. Differences to suspect first:
1. A view that already has a parent being added to a new parent: WinUI throws, the Mac allows it (fixed in TodayPage by detaching old panels before redrawing; other pages may have the same bug).
2. Rebuilding the visual tree inside the tap/pointer/CheckedChanged handler that caused it: crashes on Windows. Use `Dispatcher.Dispatch` to run it on the next turn. `MainThread.BeginInvokeOnMainThread` runs INLINE when already on the UI thread.
3. The top bar is not inside the page, so anchors cannot be measured there (the name menu is pinned top-right with `ClinicPopup.ShowTopRight`).
4. **The UI is too big on Windows.** Windows display scaling (125%/150%) enlarges everything; sizes were only tuned on the Mac. Needs one Windows scale setting in `ClinicTheme`, plus a check of fixed sizes, fonts, dialog sizes, sidebar/top bar heights, window default/minimum size.
5. Open bug: Offline mode, doctor, "New booking" / "New walk-in consult", choosing a patient CRASHES. Suspected cause: the patient table row tap rebuilt the table inside the event (see item 2). A fix is in `Views/Shared/Controls/ClinicUi.cs` (tap runs via Dispatcher) and `Views/Staff/WalkInWizardView.cs` (render posted). Not yet confirmed on Windows. Reproduce, read the exception, fix.

## First tasks
1. Pull, build and run a debug copy on Windows (see the workflow file for the exact Windows build command).
2. Reproduce the crash in item 5 and read the real exception from the logs / Event Viewer.
3. Take screenshots of the main screens and fix the oversized UI (item 4) with one Windows scale setting.
4. Run `dotnet test ClinicNode/ClinicNode.Tests --filter "FullyQualifiedName~Sync|FullyQualifiedName~Update"`.
5. Commit locally, report in plain English, wait for "push".

## Other docs in this repo
`docs/OFFLINE-MODE-PLAN.md`, `ONLINE-FIRST.md`, `SYNC-API-CONTRACT.md`, `OFFLINE-DAY-RUNBOOK.md`, `CLINIC-CHEATSHEET.md`.
