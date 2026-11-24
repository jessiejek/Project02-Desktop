# Offline mode audit (23 Nov 2026)

Why: during the owner's first offline test on the Mac, "Register a new patient" failed with "Could not register this patient". The owner asked for a full assessment and a perfect offline mode.

## How offline mode works (design, verified in code)
- Only the **doctor** can switch the computer to Offline mode, and only on a computer registered with the clinic server. In Offline mode **only the doctor signs in** (staff and admin are refused). Everything is saved on the computer and queued in the outbox; **Sync now** sends it later.
- Online mode never saves quietly on the computer: if the server cannot be reached the save is refused ("Could not save ... switch to Offline mode").
- Queue numbers: the computer holds a reserved block per day and lane (20 regular R + 20 senior/PWD S per day, for the next 7 days), reserved whenever it was online. When a block runs out or was never reserved, the visit is a **paper-queue** patient.
- Patient codes: from a block reserved on the computer (MF-100000...). Receipt numbers: typed from the paper booklet.
- Internet-only on purpose (a Local service says so): clinic settings and fees, receipt-number clashes, password reset, Sync review and merge, refunds, and (new) opening patient files.

## What was verified end to end
A complete offline clinic day as the doctor, run in an automated test on the app's real service graph with no server: register a patient, check in (queue number), call in, full consultation (vitals, ICD-10, prescription, follow-up, professional fee), complete, mark payment received with a booklet receipt number, receipt, print, amend, then confirm patient / booking / consultation / payment / amend are all waiting in the outbox.

## Bugs found and fixed
| # | Bug | Effect | Fix |
|---|---|---|---|
| 1 | **Patient code block stuck.** The Mac's block (MF-100000..100099) was reserved on an empty database, then the server's patients MF-100000..100096 were pulled in. Every offline registration took MF-100000, hit the unique index, rolled back and left the counter in place | **Registration offline always failed** (the owner's screenshot), for the next 97 patients | The allocator starts above the highest MF- code on file and moves the block forward when server codes passed it. 2 new tests |
| 2 | **Amend failed offline** whenever the fee followed the normal total: "Finish Professional Fee Decision to save the amendment" | A doctor could not amend a completed visit offline | Amend now fills the fee in first, like Complete does |
| 3 | **Errors hidden**: the register screen and 131 other screens catch every error and show a generic sentence | Nobody could tell why a save failed | Every failed service call is now recorded in `~/Library/ClinicNode/errors.log` (service, method, message); the register screen shows the real reason when the app has one |
| 4 | Date picker opened at the top-left corner of the window the first time on a page (inside dialogs too) | Looked broken | Falls back to the page size when the pop-up layer is not measured yet |
| 5 | Medical certificate print: underlines ran past the right margin; web and desktop differed | Print mismatch | Same margin fix on the web builder; test data regenerated; parity tests 38/38 |

## Desktop test suite: 18 old failures reduced to 5
Fixed: 10 tests failed only because their test computer had no reserved queue numbers (test setup), 1 offline-day test described the old design (staff offline) and never went offline, 2 queue-slip tests expected a "reprint flag" that the product deliberately removed on 21 Nov, 2 print-form tests (bug 5).
Remaining 5, none about offline saving:
- `Waive_requires_reason...`: the admin waive code issues a receipt number while the test and the web say waived payments get none. **Owner decision needed** (admin, online only).
- 3 x `StaffOrIndexTests`: written for an older rule about legacy duplicate receipt numbers (the unique index is now stricter). Stale; the real database already has the safe "skip index if duplicates" path.
- `DoctorDashboardLegacyDbTests`: depends on an old database in this Mac's sandbox folder. Not a product problem.

## Limits and risks to know (not bugs)
1. **20 regular + 20 senior numbers per day.** A busier offline day continues on the paper queue. Raise the block size on the server if the clinic is busier.
2. **A computer that was never online has no queue numbers** (all paper queue) and cannot go Offline (it must be registered first).
3. **Staff and admin cannot work offline.** Decision pending: allow staff offline with a one-writer rule, or doctor only (current).
4. Offline work is only as safe as the computer: the local database is not encrypted; keep FileVault / BitLocker on.
5. The offline day has now been proven in an automated test, but not yet for a whole real clinic day with a printer, a power cut or a slow network. Next real test: a half-day pilot with the doctor.
6. After sync, a printed queue number that clashes is **kept** (no reprint); the clash appears as a Sync review item for the admin.

## Recommended next steps
1. Release these fixes, then repeat the Mac test (Wi-Fi off, register, check in, consult, pay, print, Wi-Fi on, Sync now, check the web).
2. Decide the staff-offline policy and the waive receipt rule.
3. Add a "Copy diagnostics" button that bundles errors.log and the sync history.
4. Run a half-day offline pilot with the doctor.

## Second audit pass (same day, before deploying)
Checked the owner's real database after the offline test, and every read operation offline.

**The owner's offline work really reached the server.** All 24 queued changes were acknowledged by the 13:04 sync (sent 21, accepted 14, merged 7, 0 rejected). Nothing was lost.

**New bug found and fixed: false "review" items that never went away.** The Sync review screen showed 3 items (2 payments, 1 consultation) whose "This computer" and "Clinic server" columns held the same values (500 and 500.00; the same confirmed time with 4 versus 7 decimals; a consultation identical on both sides). Pressing Sync did not clear them. Cause: timestamps were compared as text. Fixes:
1. Timestamps are compared as moments (to the millisecond), so identical payments are no longer flagged. Test added (fails without the fix).
2. After every successful sync, review items of the two kinds seen ("not_owner" payments, "finished_immutable" consultations) whose copies hold the same content clear themselves (and are not sent to the admin). Applied to a copy of the owner's database: the 3 items in the screenshot -> 0. Real differences, and every other kind of item, stay.
Not done (deliberately): merging repeated autosave "update" operations into one. The server uses each update's list of changed fields, so merging could lose fields; with fix 2 the harmless leftovers now clear themselves.

**Every read works offline.** A new test calls every read operation of every app service as the doctor with no server (94 operations, 4 skipped for complex arguments): 0 failures.

**Final desktop test state:** Api tests 127/127; main suite 5 failing, none about offline: the waive receipt-number question, 3 old receipt-index tests, and a test that needs an old database on this Mac.
