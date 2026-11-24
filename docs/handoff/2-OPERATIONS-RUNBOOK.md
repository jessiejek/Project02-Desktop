# Clinic Node: operations runbook (how to run and fix everything)

No secrets in this file. Where each secret lives: `Allyn/_secrets/SECRETS-INVENTORY.md`. What the system is: `1-SYSTEM-OVERVIEW.md`.

## 0. Quick health check (30 seconds)
| Check | Address | Healthy answer |
|---|---|---|
| API alive | `https://chococheese0660-001-site1.gtempurl.com/health` | `{"status":"ok"}` |
| API + database | `…/health/ready` | `"db_ok":true` (database answers in ms) |
| API version + database version | `…/api/version` | the deploy number and the newest migration name |
| Web | `https://project02-fe.vercel.app/login` | the login page |
| Update feed | `https://raw.githubusercontent.com/jessiejek/Project02-releases/main/latest.json` | the newest version number |
In the desktop app: the top-bar pill says **Online**, and **Connection panel → Database on the server: Ready**.

## 1. Shipping changes
### Backend (API + database)
1. Work on a branch, run the tests (`dotnet test tests/ClinicApp.Api.Tests`). 4 old tests fail by design (queue numbering in sync): that is the known baseline.
2. Merge to `main` and push. **This deploys to production.**
3. GitHub Actions "Deploy backend" does, in order: install tools → **update the database** (only missing migrations; it refuses anything that drops tables/columns) → build → upload by FTP (with the "Deploying, back in a moment" page while files are replaced) → check `/health`.
4. If the **database step fails**, the site keeps running the old code (nothing was uploaded). If the **upload step fails** (FTP 425 / 530), the "Deploying" page stays: re-run the job; if it keeps failing see section 6.
5. Roll back: redeploy the previous good commit (Actions → Deploy backend → Run workflow on that commit, or revert and push). A database change cannot be undone by redeploying: restore the backup (section 3).
6. Allowing a destructive database change: Actions → Deploy backend → Run workflow → tick "allow destructive database change" (only after a backup).

### Web
Push to `Project02-fe` `main` → Vercel builds and publishes (about 1 minute). Settings live in Vercel → project02-fe → Settings → Environment Variables (`NEXT_PUBLIC_API_URL`, `JWT_SECRET`). **After changing a variable you must Redeploy.** Roll back: Vercel → Deployments → pick a good one → ⋯ → Promote / Rollback.

### Desktop (Mac and Windows)
- **Everyday release (automatic; secrets are added):** commit, then `git tag -a v0.2.24 -m "what is new, one line per change"` and `git push origin v0.2.24`. GitHub builds the Mac app and the Windows `Setup.exe`, signs the feed, publishes to `Project02-releases` and keeps only the last 3 versions (history squashed). The tag message becomes the release notes. Installed apps show the update chip about 5-15 minutes later (GitHub caches the feed ~5 min). Check progress in the Actions tab; two tags in a row queue, a newer pending run replaces an older pending one.
- **Windows:** installs per user (no admin), updates silently; the version label comes from the registry key written by the installer.
- **Manual release (works today):** in `Project02-desktop` bump `ApplicationDisplayVersion` and `ApplicationVersion` in `ClinicNode.App.csproj`, then
  `UPDATE_KEY=../_secrets/update-signing-private.pem REL_DIR=../Project02-releases packaging/release/make-release.sh "note 1" "note 2"`, then in `Project02-releases` run `git add -A && git commit -m "Clinic Node X.Y.Z" && git push`.
- **A DMG only** (no publishing): `bash packaging/mac/build-dmg.sh` → `packaging/out/ClinicNode-<ver>-arm64.dmg`.
- **Do not publish a version that has not been installed and started once from its DMG.** (A release with an empty update key shipped once; a test now guards it.)

### Desktop (Windows) — manual build (only if the cloud is down)
On a Windows PC: clone `Project02-be` and `Project02-Desktop` side by side, then `powershell -ExecutionPolicy Bypass -File packaging\windows\build-installer.ps1` → `packaging\out\ClinicNode-Setup-<ver>.exe`. Send `packaging\out\build-installer.log` if it fails.

## 2. People and computers
- **Create or reset an account** (staff, doctor, admin): `~/Documents/Allyn/accounts-tools/` (see its README). You type only passwords.
- **Invite from the app** (Admin → Doctors/Staff → invite) works, but the invitation **email is not sent** until email is connected; use the tool and hand over the password in person.
- **Deactivate someone:** Admin → Staff or Doctors → Edit → Status Inactive (ends their sessions). Never delete.
- **Change someone's email:** the person does it in their own Profile (needs their password); an admin can use the staff update API.
- **Register a new computer:** install the app, sign in as admin, **This computer → Register this node** (leave the token box empty). Admin → **Sync computers** lists every computer, its last sync and version; **Rotate secret** or **Revoke** a lost one.
- **Doctor's printer and paper:** This computer → Printers and paper.

## 2b. Printing settings (Admin > Settings > Printing) - built 24 Nov 2026, NOT RELEASED yet (branch `print-settings` in Project02-be and Project02-desktop)
- One clinic-wide choice: **Each computer decides** (default, nothing changes) / **PDF** (saves a PDF and opens it) / **Native** (the computer's own print window) / **Off** (every Print button stops with a message).
- Background picture (PNG or JPG, up to 6 MB, paper-sized) for each of the five documents: prescription, lab request, medical certificate, queue slip, official receipt. The writing prints on top. "Back to default" removes it.
- The settings live on the server (table `print_configs`, not synced); every computer keeps its own copy in `{data folder}/print-policy`, refreshed after sign-in and every 10 minutes at most, so printing and backgrounds work offline.
- To ship: merge both branches, deploy the backend (adds the table, additive migration `AddPrintConfig`), then tag a desktop release. Needs a printer test on Mac and Windows first.

## 2c. Lab request, prescription and certificate forms (24 Nov 2026, branches `forms-a5` in Project02-fe and Project02-desktop, `lab-tests` in Project02-be: NOT RELEASED)
- The lab request is now the **same A5 sheet** as the prescription and the medical certificate (was a small 4.3 x 6.4 in pad), re-laid out: same letterhead, patient lines and signature block.
- **License number** prints on all three forms when it is filled in (admin or doctor Profile); empty = blank line to write on. **PTR number** prints only when the doctor ticks **"Include PTR in print"** (off by default) on the Prescription, Lab Orders or Medical Certificate step, or on the completed-visit record. The tick is kept per visit and per document on that computer (`ptr-print.json` in the data folder). Desktop only for now: the web forms still print a PTR that is filled in.
- The lab list is the clinic's own tests: CBC, Urinalysis, Lipid profile, SGPT, CREA, FBS, BUA, TSH, FT3, FT4, HbA1c, Dengue NS1/IgG/IgM, ECG 12L. A test the doctor orders that is not on the list prints under "Others".
- **Admin > Settings > Printing > Lab request tests:** add and remove tests (up to 26, names up to 40 characters); "Back to default" returns to the paper's list. Stored on the server (`print_configs` key `lab-tests`), copied to each computer.
- The web form builder (`print-forms-html.ts`) is the source of truth; the desktop copy must match it byte for byte (golden test). After changing it: `python3 ClinicNode.Core/Printing/Generated/generate.py`, then `node --experimental-strip-types generate-golden.mjs` in `ClinicNode.Tests/Printing/golden`.
- To ship: backend first (adds endpoints only, no new table), then web, then tag a desktop release. Print one of each form on a real printer first.

## 2d. Mac print window ignores the paper size (A5) - findings 24 Nov 2026, FIX LATER
- Windows prints the prescription / lab request / certificate on A5. On the Mac the system print window opens on **US Letter** even though the printer (10.128.2.50) lists A5.
- Proven with a small test app: the app asking for A5, naming the printer first (`PrintInfo.PrinterID`), and describing the page as A5 (`UIPrintPageRenderer.PaperRect`) were ALL ignored by the Mac window. Its paper-choice callback (`ChoosePaper`) is never called. No in-app code change can set the dialog's paper.
- Options that do work (choose one): **A** print straight to the printer without the window (Settings > This computer > Printers and paper: printer `ipp://10.128.2.50/ipp/print`, print window = never; the app asks for the paper on that path; needs one test sheet to confirm); **B** set the printer's default paper to A5 for this Mac user (`lpoptions -p _10_128_2_50 -o PageSize=A5`; affects every app); **C** pick A5 in the window by hand.
- The app already logs what it asked for to `print-errors.log` ("[paper] Present: asking for paper 420x595 pt").

## 3. Backups (the biggest open risk)
- **Decision 22 Nov: no paid backup.** A free nightly export (scheduled job that dumps the database and keeps copies off the host) is still to be built. Until then take a **manual backup before any database or risky change** and restore it once into a test database to prove it works.
- **Manual backup before risky work:** panel → MSSQL → Advanced Custom Backup (download the .bak).
- Old backups from before go-live are in `Allyn/db-backups/` (legacy data, protect them: patient information).
- Local desktop data: `~/Library/Containers/ph.clinicnode.app/Data/Library/ClinicNode/` (`clinic-node.db`). Settings → This computer has Backup and Restore for it.
- Uploaded patient files (lab results, documents) live on the host in `App_Data/uploads`; they are **not** in the database backup: copy that folder by FTP now and then.

## 4. A clinic day, offline
Read `OFFLINE-DAY-RUNBOOK.md`. Short version: the **doctor** switches **Offline mode** on from the connection pill; staff cannot sign in that day; type the booklet receipt number for each payment; when the internet is back press **Sync now** (or sign out / quit: it syncs first); the admin handles **Sync review** and **Receipt clashes**.

## 5. Updating secrets (rotation recipes)
| Secret | Change it in all of these places, in this order |
|---|---|
| **Database password** | 1) myASP panel → MSSQL → change the login password. 2) Host **Environment Variables** → `ConnectionStrings__DefaultConnection` (new password inside). 3) GitHub `Project02-be` secret `DB_PASS`. 4) You type it fresh in the account tools. Check `/health/ready` |
| **JWT secret** | Host env `Jwt__Secret` **and** Vercel `JWT_SECRET` must be **identical**; then **Redeploy** the web; everyone is signed out. Make it a long random value (the tool used on 22 Nov: 48 random letters and digits) |
| **FTP password** | myASP → FTP Manager → change; GitHub `Project02-be` secret `FTP_PASS` (and `FTP_USER`/`FTP_HOST` if they changed); re-run the deploy |
| **Update signing key** | Only if lost or leaked: make a new pair, put the new PUBLIC key in `ClinicNode.Core/Update/UpdateKeys.cs` **and** in `packaging/mac/install.sh`, release an app version signed under the old key that carries the new key (otherwise installed apps cannot trust new feeds), then switch the GitHub secret |
| **An account password** | `accounts-tools/reset-password.sh email` |

## 6. Troubleshooting (symptom → cause → fix)
| Symptom | Likely cause | Fix |
|---|---|---|
| API answers **502.5** | app pool not ".Net Core" + 64-bit, or the database is behind the code | panel → .NET Version & Pool; turn on `stdoutLogEnabled` in `web.config`, read `/site1/logs/stdout_*`; apply missing migrations (the deploy does this automatically) |
| API shows **"Deploying, back in a moment"** | a deploy stopped halfway (FTP 425/530) | re-run "Deploy backend"; check `FTP_PASS` matches the panel; the script now retries gently |
| Web login spins, then back to `/login` | `JWT_SECRET` on Vercel ≠ `Jwt__Secret` on the host, or no redeploy after changing it | make them equal, Redeploy |
| **429 Too many requests** | rate limit reached | per-client limits (300 general, 10 auth per minute) plus a **combined cap on `/api/auth/` (60/min)** that a fake `X-Forwarded-For` cannot dodge. The two temporary host variables (`RateLimiting__GlobalPermitPerMinute`, `…AuthPermitPerMinute`) are still set and should be removed |
| Desktop "Could not save" | server slow/unreachable or request over 15 s | Developer log is off in the DMG; check the connection pill; switch to Offline mode if the line is bad |
| Desktop red `node_exists` | the computer name is already registered | it shows green now: nothing to do |
| Sync says not accepted | the computer's secret was rotated/revoked | Admin → Sync computers → Rotate, then register again (or another name) |
| "Model building is not supported when publishing with NativeAOT" | a true Release Mac build | use the normal build for the DMG (`MODE=jit`, the default) |
| App closes right after opening | read `~/Library/Containers/ph.clinicnode.app/Data/Library/ClinicNode/startup-crash.log` |
| "Could not save" / a screen empty or odd on the desktop | something failed quietly | read **`errors.log`** next to `clinic-node.db` (service, method, real message). Printing: `print-errors.log` |
| Windows: Today empty, or the name menu in the wrong place | fixed in 0.2.23 (views reused between redraws; menu pinned top-right) | update to the newest version; if it persists, send `errors.log` |
| Update chip never shows | feed cached (wait 5 min) or the app's key does not match | open the feed URL; `latest.json.sig` must verify with `UpdateKeys` |
| Everything slow | distance to the US server (0.19 s per call) | normal; use Offline mode on busy days; ideas in the overview |
| Backend deploy: database step red | a migration failed or a destructive change was refused | read the step; the site is unchanged; fix the migration or allow it on purpose |

## 7. Local development (when you build things)
- Needs: .NET 10 SDK, Docker (SQL Server `clinic-mssql`), Node for the web.
- Backend: `Project02-be` → `dotnet run --project src/ClinicApp.Api` on `localhost:5055`; dev accounts `*@clinic.test` are created **only in Development**.
- Web: `Project02-fe` → `npm install && npm run dev` on `localhost:3000`.
- Desktop: `Project02-Desktop/ClinicNode` → `scripts/run-mac.sh` (Debug build, demo mode on unless `CLINIC_NODE_DEMO=0`; point it at a server with `CLINIC_NODE_CLOUD_URL`).
- The desktop projects reference `../../../Project02-be/src/…`: keep the two folders side by side.

## 8. Costs and renewals (fill in the dates)
| Item | Cost | Renewal |
|---|---|---|
| myASP.NET plan (API + SQL Server) | _(owner: fill in)_ | _(date)_ |
| Vercel (Hobby, web) | free | n/a |
| GitHub (repos + Actions + releases) | free at this size | n/a |
| Domain | none yet | n/a |
| Apple / Windows signing | none (no subscription; ad-hoc signed) | n/a |
| Email sending | not set up (Gmail acceptable) | n/a |
| GitHub fine-grained tokens (`BE_READ_TOKEN`, `RELEASES_TOKEN`) | free | **expire 21 Feb 2027: renew** |

## 9. Standing rules we agreed
No push to the backend `main` without a deliberate go (it deploys live); **no tag or release until the owner says "release"**; the public releases repo is pruned to the last 3 versions; backups before database work; passwords are typed by the owner, never pasted into chat or screenshots; test data only in test accounts; one change at a time; every release is installed and started once before it is announced.
