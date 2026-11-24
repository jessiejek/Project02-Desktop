# UPDATE 24 Nov 2026 (read first)
- Desktop test suite is now **green: 477 pass, 0 fail** (6 skipped on purpose). The admin "Waive payment" no longer issues a receipt number (doctor waives, front desk confirms: no OR, same as the web). The legacy-database tests only run on an explicit copy (`CLINIC_COPY_DIR`), never on live clinic data.
- Still red: backend 4 old queue-clash sync tests, web 12 old consultation-UI tests.
- Printing, lab request A5, PTR tick, A5 default paper: see runbook sections 2b and 2c.

---

# STATUS 23 Nov 2026 (read first)
Decisions made: **no paid backups**; Gmail is fine for email for now; test passwords are fine; privacy must follow the **Philippine Data Privacy Act (RA 10173)**, not US rules.
**Done since 22 Nov:** rate-limit spoof fixed (combined `/api/auth/` cap, tested 60 pass / 10 blocked); cloud Mac+Windows releases; Windows install/update/version label; Windows sidebar/close/Today fixes (pending the owner's confirmation); offline mode audit and fixes; web-vs-desktop gaps closed (open files, Sync review/merge, privacy-notice tick, backend endpoint deployed); sync live steps; name menu; releases repo pruned to 3 versions and auto-pruned; `account-list.txt` no longer tracked; privacy manual and breach checklist **drafts** written (files 8 and 9); docs 1-4 refreshed.
**Only the owner can do:** name the DPO and the legal name, have an adviser review files 8-9, check NPC registration, decide staff-offline and waived-receipt rules, remove the two temporary rate-limit host variables, turn on 2FA, give a Gmail app password for reset emails.
**Still to build (me):** free nightly database export; uptime monitor (scheduled GitHub check on `/health/ready`, emails on failure); pre-deploy test gate (needs the 4 old backend tests fixed first); password-reset and invitation email; date-of-birth typing; skeletons in dialogs; field-by-field parity; Windows verification; half-day offline pilot.
Items B-5 (privacy) is now "drafted, needs decisions". Item B-9 rate limits: tests done, host variables still to remove. Item 19 (documents) is done by this refresh.

---

# Clinic Node: what we still need to do (compiled 22 Nov 2026)

Source: the review of our documents + my own checks. **Not code, just the plan.**
Legend: **YOU** = only you/the clinic can do it · **ME** = I do it in a session · **BOTH** = together.

---

## A. Where the review was right, wrong, or partly right
| # | Review point | Verdict |
|---|---|---|
| 1 | No working backup, no restore steps | **RIGHT.** Section 3 of the runbook has no restore procedure and none was ever tried |
| 2 | A deploy could wipe uploaded lab files | **WRONG for the deploy:** `publish.sh` only adds or overwrites files, never deletes or mirrors, and never touches `App_Data/uploads` (it says so in its header). **RIGHT that the uploads are not backed up anywhere** |
| 3 | No Data Privacy Act paperwork | **RIGHT** (nothing in the system documents it; the app itself has consent, data-export and deletion features) |
| 4 | Patient data lying around the Mac | **PARTLY.** FileVault is **ON**. iCloud "Desktop & Documents" sync looks **OFF** (please confirm in System Settings → Apple ID → iCloud). Still true: `db-backups/` (legacy patient data) is unencrypted inside Documents, `_secrets` is too, and `Project01-BE` contains a dev SQLite file (`clinicapp.dev.db`) |
| 5 | Supabase may still hold a copy | **RIGHT, unknown.** We never confirmed it was shut down |
| 6 | Fake email domains on live accounts | **RIGHT.** `staff@gmail.com` is not clinic-owned; `gavino@clinic.test` can never receive mail |
| 7 | The clinic owns nothing; you are the single point | **RIGHT** |
| 8 | Deploy has no test step, no branch protection | **RIGHT** (I proposed it earlier and have not built it yet) |
| 9 | Migration runs before upload: new database with old code if upload fails | **RIGHT.** Needs a rule: every database change must work with both old and new code |
| 10 | No rollback or alert when the post-deploy check fails | **RIGHT** |
| 11 | Failing tests treated as normal | **RIGHT.** And my words "by design" were wrong: they are old failures, not intended |
| 12 | The tag-release automation skips the "install and start it first" rule | **RIGHT** |
| 13 | JWT secret shared with Vercel (symmetric) | **RIGHT as a design weakness** (the web can create valid tokens). Not urgent |
| 14 | Rate limit can be dodged with a fake `X-Forwarded-For`; 120 logins/min is too high | **PARTLY, UNVERIFIED.** The code trusts forwarded headers from any sender (it relies on IIS being the only door). I started a test and did not finish it. **The 120/min is my temporary workaround and should be lowered now** that per-client limiting is deployed |
| 15 | SQL Server open to the internet | **RIGHT** (shared-host limit; mitigation: IP allow-list if myASP offers it) |
| 16 | Update-signing key is a single point of failure | **RIGHT.** If lost, installed apps are stranded |
| 17 | Unsigned apps train staff to click past warnings | **RIGHT, a conscious trade-off** (no subscription). Revisit when more computers exist |
| 18 | `/api/version` reveals details | **RIGHT, minor** |
| 19 | Offline-day plan untested but written as proven | **RIGHT.** Mark it UNTESTED. Also: staff cannot sign in offline, so the doctor runs the desk: a real policy question |
| 20 | "Could not save" leaves no evidence | **RIGHT** (the packaged app has the developer log off; only a crash log exists) |
| 21 | No uptime alert; first patient hits a cold start | **RIGHT.** (Our plan is the **paid** "Pro .NET" plan; my line "free-host" in the overview was a **wording mistake**. IIS still puts idle apps to sleep) |
| 22 | Every forgotten password comes to you | **RIGHT** (no email service) |
| 23 | Hosting described two ways | **My wording error** (see 21) |
| 24 | Numbers disagree (635 vs 519 tests; failing counts) | **My error.** 635 counts test attributes; **519** is what actually runs (theory rows included). Failing: 4 backend, 18 desktop, 12 web |
| 25 | Repo spelled `Project02-Desktop` and `Project02-desktop` | **RIGHT.** GitHub name has a capital D; the local folder does not; the `../../../` paths depend on the folder names |
| 26 | Clinic name vs doctor name differ | **RIGHT.** Needs your decision: "Dr. Grace Gavino Medical Clinic" (sidebar) vs "Grace Medical Clinic" vs the doctor "Dr. Allyn Grace T. España-Gavino" |
| 27–29 | Doc errors, stale numbers, mixed readers | **RIGHT.** I will fix the documents |

---

## B. P0: before real patients use it (do these first)
| # | Task | Who |
|---|---|---|
| 1 | **Backups:** turn on myASP Automated Backups; take one manual backup; **restore it once into a test database**; write the restore steps into the runbook; also copy the host's `App_Data/uploads` folder by FTP | **YOU** (panel) + **ME** (write steps) |
| 2 | **Real clinic-owned email addresses** (one for admin, one for the doctor, one for staff, e.g. on a clinic Google/Microsoft account). Then change the three logins' emails (Profile → email) | **YOU** create; then app does it |
| 3 | **Continuity / ownership:** second owner on GitHub, Vercel and myASP; clinic-owned recovery email; write who to call; fill in renewal dates (hosting) | **YOU** |
| 4 | **Reset all test passwords;** delete `deploy-credentials.md`, `project02-fe-jwt.env`, the `.env.local.bak-…` file; enable **2FA** (GitHub, Vercel, Apple/Google, myASP) | **YOU** |
| 5 | **Data Privacy Act basics:** named Data Protection Officer, NPC registration check, breach procedure (72-hour notice), retention rule, processor agreements with myASP/Vercel/GitHub. I am not a lawyer: ask the clinic's adviser; **I can draft a privacy manual and breach checklist** as a starting point | **YOU** (decisions) + **ME** (drafts) |
| 6 | **Supabase:** confirm the project is deleted or paused and its data wiped | **YOU** |
| 7 | **Old patient data on the Mac:** move `db-backups/` and `clinic-node-local-backup-…` into an encrypted disk image (or delete); check `Project01-BE` dev database; confirm iCloud Desktop & Documents is off; keep FileVault on | **YOU** (I can give exact steps) |
| 8 | **Email for password reset** (SMTP via the host mail or a free provider) so users stop depending on you | **BOTH** |
| 9 | **Lower the rate limits:** remove the two temporary host variables (`…Global…=6000`, `…Auth…=120`) so the safe defaults (300 and 10 per minute) apply; finish the test that fake `X-Forwarded-For` headers do not help an attacker | **YOU** (panel) + **ME** (test) |

## C. P1: this week
| # | Task | Who |
|---|---|---|
| 10 | **Deploy gate:** run the tests before the migration and upload; turn on **branch protection** on `main` (pull request + passing tests required) | **ME** (workflow) + **YOU** (GitHub setting) |
| 11 | **Green tests:** fix or delete the 4 backend, 18 desktop and 12 web failing tests so any red means something | **ME** |
| 12 | **Safe database changes rule:** every migration must work with old and new code (add first, remove later); post-deploy health failure must alert you and keep the old files recoverable | **ME** |
| 13 | **Uptime monitor** (free) on `/health/ready`, email/SMS alert, also keeps the app awake | **YOU** (account) + **ME** (settings) |
| 14 | **Offline clinic-day dry run** with the doctor (about 10 patients, a real printed page, power-cut and slow-network cases). Mark the offline runbook **UNTESTED** until done. Decide the policy for who works the desk offline | **BOTH** |
| 15 | **Release safety:** tag releases publish to 10 percent first, then a promote step; keep a beta channel for your own Mac; add a **second backup update key** to the app so the signing key can be replaced without the old one; back the private key up offline | **ME** + **YOU** (backup) |
| 16 | **Windows installer** built and installed on your work laptop; printers tested | **BOTH** |
| 17 | **Diagnostics in the packaged app:** a log file and a "Copy diagnostics" button so a "Could not save" leaves evidence | **ME** |
| 18 | **Self-registration abuse:** email verification or a simple captcha on patient sign-up | **ME** |
| 19 | Fix the documents (numbers with dates, hosting wording, repo case, release rules, split into "developer" and "clinic admin" versions) | **ME** |

## D. P2: later
| # | Task |
|---|---|
| 20 | Asymmetric JWT signing so the web can only verify tokens (the signing key stays on the API host) |
| 21 | SQL Server IP allow-list (if myASP supports it) |
| 22 | Hide version/migration details from `/api/version` |
| 23 | Apple notarization and a Windows certificate when the number of clinic computers grows |
| 24 | Encrypt the local SQLite copy on clinic computers; require FileVault/BitLocker and auto-lock on every clinic computer |
| 25 | Own domain, then Cloudflare, or move the API to an Asian host |
| 26 | Automatic Windows in-place updates |

## E. Decisions only you can make
1. The **legal name** to show everywhere (clinic and doctor).
2. Which **email provider** the clinic will own.
3. **Staff in offline mode:** allowed (with a one-writer rule) or doctor-only?
4. Whether to buy a **domain** now.
5. Who is the **Data Protection Officer**.

## F. If you only do three things
1. **A backup, restored once** (including the uploads folder).
2. **Clinic-owned email addresses for every login, and a second owner on each service.**
3. **Tests before deploy + branch protection** (I build the first half; you click the second).
