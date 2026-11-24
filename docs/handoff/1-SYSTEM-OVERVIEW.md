# Clinic Node: complete system overview

**Dr. Grace Gavino Medical Clinic** (Grace Medical Clinic). Written 22 Nov 2026, from the code, the live servers and the work done so far.
This file contains **no passwords or keys**. Where secrets live is in `Allyn/_secrets/SECRETS-INVENTORY.md` (kept outside every repo). How to operate everything is in `2-OPERATIONS-RUNBOOK.md`.

> **Update 23 Nov 2026 (read this first).** Since 22 Nov: desktop is at **0.2.23**; **Mac and Windows installers are built in the cloud** by one tag (`git tag -a vX.Y.Z` + push), and Windows installs and updates itself silently (it reads its version from the registry). The public releases repo keeps only the **newest 3 versions** (history is squashed on each release). New on the desktop: **open patient files**, **Sync review (merge/dismiss)** for the admin, the **privacy-notice tick** at front-desk registration (web and desktop; backend endpoint `POST /api/patients/{id}/privacy-notice-shown`), **live sync steps** (connect, send, receive, finish), a **name menu with Settings/Logout**, and a **Connection panel** redesign. **Offline mode** was audited and fixed (see `7-OFFLINE-MODE-AUDIT.md`); web vs desktop screens were compared (`6-WEB-DESKTOP-PARITY.md`). Backend is at deploy **137** with a combined login cap (60/min). Privacy drafts: `8-PRIVACY-MANUAL-DRAFT.md`, `9-BREACH-RESPONSE-CHECKLIST.md`. Where this file below disagrees, this note wins; the open-issues list in section 11 is replaced by the one here.
> Still open: no backups, no password-reset email, no uptime monitor, no pre-deploy test gate, 2FA off, two temporary rate-limit host variables, Windows fixes (0.2.21+) not yet confirmed by the owner on every screen.

---

## 1. What the system is, in one page
A clinic system for one doctor and a front desk. Patients can book online; the front desk checks people in, runs the queue and collects payment; the doctor writes the consultation (notes, vitals, prescriptions, labs, vaccines, follow-up, medical certificate, professional fee) and prints the forms; the admin runs the clinic.

It has **three programs that share one server**:

```
 Patients / Staff / Doctor / Admin           Clinic computers (Mac now, Windows next)
        in a browser                              the installed "Clinic Node" app
              |                                                 |
   WEB  (Next.js on Vercel)                       DESKTOP (.NET MAUI)  --- keeps its own copy of the
   https://project02-fe.vercel.app                 data in SQLite, works OFFLINE, syncs later
              \                                                /
               \______________   HTTPS  _____________________/
                              |
                   BACKEND API (ASP.NET Core .NET 10)  on myASP.NET (Windows/IIS, USA)
                   https://chococheese0660-001-site1.gtempurl.com
                              |
                   SQL Server database on myASP.NET (58 tables)
```
- The **web** and the **desktop** both talk to the same **API** and the same **database**.
- The desktop is **online-first**: it uses the live server; when the connection is bad the doctor can switch it to **Offline mode** and the work is saved on that computer and sent later (sync).
- Updates for the desktop come from a small public GitHub repo (the signed "mailbox", section 9).

## 2. The repositories and folders
| Name | Where | Contains | Visibility |
|---|---|---|---|
| **Project02-be** | github.com/jessiejek/Project02-be | Backend API, database model and 34 migrations, backend tests (635 test methods), deploy workflow, `deploy/` SQL scripts | private |
| **Project02-fe** | github.com/jessiejek/Project02-fe | Web app (Next.js 16), unit + e2e tests | private |
| **Project02-Desktop** | github.com/jessiejek/Project02-Desktop | Desktop app (.NET MAUI: Mac and Windows), packaging scripts, release + Windows workflows, all desktop docs | private |
| **Project02-releases** | github.com/jessiejek/Project02-releases | **Only** the signed update feed (`latest.json` + `.sig`), the installers (.dmg/.zip/Setup.exe) of the **last 3 versions only**, and `install.sh` | **public** (so installed apps can read it without a login) |

Other folders in `~/Documents/Allyn` (not part of the live system): `accounts-tools` (account tools, section 10), `_secrets` (update signing key and the secrets inventory), `db-backups` (old SQL Server backups, 209 MB, contain legacy clinic data), `clinic-node-local-backup-2026-11-22` (this Mac's old local database/credentials from the test period), `Project01-BE` / `Project01-FE` (the first generation of the system, 2.5 GB, no longer used), `Project02-fe-e2e` and `Project02-tests` (end-to-end test projects), plus review screenshots and design mock-ups.

## 3. Where everything runs
| Part | Provider | Details |
|---|---|---|
| **API** | **myASP.NET / SmarterASP-style host (site4now)**, plan "Pro .NET (W1050-US)" | Windows + IIS, ASP.NET Core module (OutOfProcess), app pool ".Net Core" 64-bit. Server WIN8152 in the USA. Site `site1`. Address `https://chococheese0660-001-site1.gtempurl.com` (a temporary-style address; no own domain yet) |
| **Database** | same host | SQL Server, database name `db_ace1d6_gavinodb` on `sql5111.site4now.net`. The only way to run SQL by hand is the panel's SQL portal (no dynamic SQL, no GO) or a command-line tool such as `sqlcmd` from the Mac |
| **Web** | **Vercel** (Hobby plan), team `jessiejeks-projects`, project `project02-fe` | Deploys automatically from the `main` branch. Address `https://project02-fe.vercel.app` |
| **Source + automation** | **GitHub** (account `jessiejek`) | 4 repos, GitHub Actions for deploy and releases |
| **Updates for the desktop** | GitHub (public repo, raw files) | free, no login needed to read |
| **Email sending** | **not connected** (Gmail is acceptable for now; needs an app password) | forgot-password and staff invitation emails do not send yet (section 11) |
| **Old data service** | Supabase | used by the very first version only; the system now runs entirely on the .NET API + SQL Server |

**Speed fact (measured):** the server is on the other side of the Pacific: about 0.19 s per request and 0.6 s to open a new connection from the Philippines. The database itself answers in 1–3 ms. The desktop reduces this with HTTP/2, one shared connection, and loading many things at the same time. The host confirmed there is no Asia location on this plan.

## 4. People and roles
| Role | Who | Can do |
|---|---|---|
| **Patient** | self-registered (web only) | own bookings, records, prescriptions, vaccinations, documents, privacy choices |
| **Staff** | front desk | queue, walk-ins, payments (collect; waiver only if the doctor decided), patient list, announcements |
| **Doctor** | the doctor | own appointments, patient charts, consultations, prescriptions, templates, schedule, performance, profile (name, specialization, fee, license, PTR, S2, bio, email, password) |
| **Admin** | clinic admin | everything: bookings, doctors, staff, patients, settings, audit logs, reports, refunds, sync review and merges, registered computers |

Rules are enforced on the **server**, not in the screens (documented in `Project02-be/docs/AUTHZ_MATRIX.md`, covered by tests). Passwords need 12+ characters, are hashed, and accounts lock after repeated wrong tries.

---

## 5. WEB: everything it can do
Address `https://project02-fe.vercel.app`. Works on desktop and phone sizes. The web has **no offline mode**.

### Public pages
`/login` · `/register` (patient self-registration with consent) · `/forgot-password` (sends an email: **not working until email is connected**) · `/privacy` (privacy notice).

### Patient portal (`/patient/…`)
| Page | What it does |
|---|---|
| Dashboard | upcoming visit, quick actions, announcements |
| Your Doctor / Doctors / Doctor detail | the clinic's doctor, schedule, fee, reviews |
| My Bookings / Booking detail | book a visit for a day (or join today's queue), see the queue number and what has been paid, cancel a booking, leave a review after the visit |
| Medical Records | past visits and diagnoses |
| Prescriptions | view/print own prescriptions |
| Lab results | view uploaded lab results |
| Vaccinations | vaccination history |
| Documents | own uploaded files |
| Privacy & consent | consent record, withdraw consent, download my data, request correction or deletion |
| Profile | personal info, change password, **change email** |

### Staff (front desk) (`/staff/…`)
Dashboard (arrivals today, ready for payment) · **Payments** (collect cash/other methods, exact amount, official receipt numbers; waiver confirm) · **Walk-In** (find or register a patient, pick doctor, queue number) · **Queue** (live board, lanes R- regular and S- senior/PWD/pregnant, call/hold/no-show/cancel) · Patients list + patient detail (+ quick register) · Bookings + booking detail (reschedule, cancel) · **Doctor Status** (available / running late / unavailable) · Announcements · Profile (name, phone, change password, **change email**).

### Doctor (`/doctor/…`)
Dashboard "Today" (queue, needs-attention items, practice analytics) · Appointments + appointment detail · Patient charts (visits, vitals, prescriptions, lab results, documents, vaccinations) · **Consultation** (SOAP notes, vitals, diagnoses with ICD-10, prescriptions with medicine catalog and safety checks, lab orders, follow-up, medical certificate, vaccinations, professional-fee decision; two layouts exist) · Prescription create/edit · Performance (earnings, completed visits, unpaid) · Availability (weekly hours, blocked dates) · **Templates** (SOAP, diagnosis, prescription, medical-certificate templates) · Profile (**name, specialization, fee, license, PTR, S2, bio, email, password**).

### Admin (`/admin/…`)
Dashboard · Bookings (+ detail with reschedule/cancel/payment actions, + calendar) · Walk-In · **Doctors** (invite, edit, schedule, services, deactivate) · Patients (+ detail) · **Staff** (invite, deactivate) · Announcements · **Settings** (tabs: General, Hours, Fees, Payments, Privacy & consent, Branding; plus the privacy data-requests panel) · **Audit Logs** · Reports (daily booking summary, unpaid completed visits, pending follow-ups, doctor earnings and ratings) · **Sync review** (possible duplicate patients and problems sent by desktops; compare and **merge** or dismiss) · **My account** (change password and email).

### Behind the web (also true for the desktop)
Realtime updates by SignalR (queue, payments, check-ins) · file storage for lab results and documents · audit trail of changes · privacy requests (consent, data export, correction, deletion).

---

## 6. DESKTOP: everything it can do
The installed "Clinic Node" app (version **0.2.23** at 23 Nov 2026; Mac Apple-Silicon DMG and Windows `Setup.exe`, both built in the cloud). Sign in with the same email and password as the web.

### Sidebar per role
- **Doctor:** Today · Appointments · Patient charts · Performance · Availability · Templates · Profile · Logout
- **Staff:** Dashboard · Payments · Walk-In · Queue · Patients · Doctor Status · Announcements · Profile · Logout
- **Admin:** Dashboard · Bookings · Walk-In · Doctors · Patients · Staff · Announcements · Settings · Audit Logs · Reports · **Sync computers** · **Receipt clashes** · **This computer** · **My account** · Logout
- Footer always shows **Clinic Node v<version>**. Top bar: search, **connection pill** (Online/Offline + speed), update chip, sync badge, notifications, your name.

### What each does (same as the web unless noted)
Today/dashboards, appointments, patient charts, consultation (all steps, autosave every 30 s, Save & next with a spinner, Review & complete), prescriptions, templates, performance, availability, payments, walk-in wizard (register patient in the same flow), queue board, patient lists, announcements, bookings, doctors, staff, settings, audit logs, reports. The desktop screens load many things at the same time and show **skeleton placeholders** while data loads; buttons show "Saving…" and ignore a second click.

### Desktop-only features
| Feature | What it does |
|---|---|
| **Offline mode** | The doctor switches it on (never automatic). All work is saved in a local SQLite database on that computer and sent to the server when sync runs. Only the doctor works in Offline mode (Staff/Admin are blocked); patients register and book at the doctor's account; payments are confirmed by the doctor with the **booklet receipt number** typed in; queue numbers come from blocks reserved online (R- and S- lanes); offline sign-in works up to 7 days after the last sync |
| **Connection monitor** | live speed chart, status words Good / Slow / Unstable / Down, suggests (never forces) Offline mode |
| **Sync** | automatic at the day's first sign-in, background every 30 s, **Sync now**, results screen, run history; warns before sign-out/quit/switch-to-Online if something is not on the server; sync problems upload to the server as **review items** for the admin |
| **Register this node** | an admin registers each computer once (uses the admin's online sign-in); admin can see, rotate or revoke every computer in **Sync computers** |
| **Receipt clashes** | two payments with the same receipt number are flagged for the admin |
| **Native printing** | the print window opens **inside the app** (Mac print dialog; Windows print dialog or straight to a named printer); prescription and certificate on A5, lab form, official receipt, **queue slip** (paper queue wording offline, or raw ESC/POS thermal printer by network) |
| **This computer** (admin) | server address, registration (green "registered" badge), printers and paper, backups |
| **Updates (OTA)** | checks a signed feed every 6 hours; shows "⬆ Update X" chip; **Check for updates** in the Connection panel; downloads, verifies and opens the installer |
| **Local safety** | the local database repairs itself after updates; app version shown; crash log written on start-up failures |

---

## 7. Web versus desktop at a glance
| Capability | Web | Desktop |
|---|---|---|
| Patient self-registration, online booking, patient portal, privacy self-service | **yes (only here)** | no |
| Staff, doctor and admin daily work | yes | yes |
| Works without internet | no | **yes (Offline mode)** |
| Local copy of the data | no | **yes (SQLite)** |
| Native printing and thermal queue slip | browser print | **native** |
| Admin: merge duplicate patients (Sync review) | yes | **yes** (online only) |
| Admin: register/rotate/revoke computers, receipt clashes, This computer | no | **yes (only here)** |
| Change own password and email | yes (all roles) | yes (staff, doctor, admin) |
| Automatic updates | automatic (Vercel) | signed feed, installer step |
| Speed from the Philippines | fast (loads from a nearby edge) | slower per screen because every call goes to the US server (improved a lot, still limited by distance) |

---

## 8. Backend (the API) in numbers
- **ASP.NET Core 10**, EF Core 10 on SQL Server, **58 tables**, **34 migrations**, **36 controllers**, SignalR hub `/hubs/clinic`.
- Main route groups: `auth` (login, register, change/reset password, change email), `doctors`, `bookings`, `queue`, `payments`, `patients` (+ merge), `consultations`, `prescriptions`, `soap`/templates, `lab-orders`, `vitals`, `follow-ups`, `medical-certificates`, `patient-vaccinations`, `patient-files`, `privacy`, `reports`, `audit-logs`, `settings`, `announcements`, `staff-accounts`, `reviews`, `search`, and the sync group `sync` (register node, push, pull, queue blocks, review items).
- Health: `/health` (alive), `/health/ready` (alive + database), `/api/version` (version, deploy time, database migration).
- Safety: JWT bearer tokens, role checks on every route, audit log of changes, rate limiting per client (settings `GlobalPermitPerMinute` and `AuthPermitPerMinute`; behind IIS clients are told apart by `X-Forwarded-For`), CORS limited to the web address.
- Tests: about 519 backend tests run on every change; 4 old ones are known to fail (queue numbering in sync tests).

## 9. How changes reach people (pipelines)
| What | Trigger | What happens | Result |
|---|---|---|---|
| **Backend** | push to `Project02-be` `main` | GitHub (Mac runner) checks the database version, **applies only the missing migrations** (refuses ones that delete tables unless allowed), builds, **uploads by FTP** to the host (with the "deploying" holding page, slow gentle uploads and retries) | new API live in about 3–5 minutes |
| **Web** | push to `Project02-fe` `main` | Vercel builds and deploys | live in about 1 minute |
| **Desktop release (Mac + Windows)** | push a tag like `v0.2.24` to `Project02-Desktop` (the tag message is the release notes) | GitHub builds the Mac app and the Windows `Setup.exe`, signs the feed, publishes to `Project02-releases` and prunes to the last 3 versions | installed apps show the update chip about 5-15 minutes later. The three secrets are added |
| **Desktop manual release** | `packaging/release/make-release.sh "notes"` on the Mac | same result, from this Mac | works today |
| **Windows build** | part of the release workflow (also `build-windows.yml`) | produces `ClinicNode-Setup-<ver>.exe`, installs per user, updates silently | **works** |

## 10. People and account tools
- Live logins: **Admin** `jessiejayr@gmail.com`, **Doctor** `gavino@clinic.test` (Dr. Allyn Grace España-Gavino), **Staff** `staff@gmail.com`. Create or reset accounts with `~/Documents/Allyn/accounts-tools/` (see its README): `create-account.sh`, `reset-password.sh`.
- The live database holds **8 patients** kept after the 22 Nov purge plus any test visits made since.

## 11. What is NOT done or not working (honest list, 23 Nov 2026)
1. **No backups** (no paid backups; a free nightly export is still to build). The database is the whole clinic.
2. **Email sending** is not connected: forgot-password and staff invitations do not send. Admin resets passwords with the tool.
3. **Windows**: installer, silent update and version label work; the logout menu, fast close and "Today" blank-screen fixes need the owner's confirmation. Printers on real hardware not tested.
4. **True Release Mac build** crashes at start (trimming + AOT); the DMG is the normal build with production defaults. Mac self-replace is impossible under the sandbox: the app opens a one-click installer.
5. **Offline** works end to end in tests but a half-day pilot with the doctor and a printer is not done. Staff and admin cannot work offline (policy decision pending).
6. **Monitoring**: no uptime alert; no pre-deploy test gate (4 old backend tests fail); 2FA off; two temporary rate-limit host variables still set.
7. **Privacy paperwork**: drafts exist (files 8 and 9); the DPO, legal name and adviser review are missing.
8. **Slow distance**: the API is in the USA (0.19 s per call); no own domain.
9. **Old tests failing**: backend 4, desktop 5, web 12 (none new).
10. **Small leftovers**: typing a date of birth directly, skeletons inside dialogs, the Mac "underlined buttons" (probably the macOS Show button shapes setting), field-by-field web/desktop comparison.
11. Specialization typo "Mediicne" in the doctor's profile.

## 12. Where to read more (inside the repos)
`Project02-be/AI-HANDOFF.md` (backend deep dive), `Project02-be/docs/AUTHZ_MATRIX.md` (who can do what), `Project02-desktop/docs/OFFLINE-MODE-PLAN.md` (the 19 offline requirements and the build log), `ONLINE-FIRST.md`, `SYNC-API-CONTRACT.md`, `OFFLINE-DAY-RUNBOOK.md`, `DISTRIBUTION-AND-OTA-PLAN.md`, `RELEASE-GATE-PHASE4.md`, `CLINIC-CHEATSHEET.md` (one page for the front desk), `WINDOWS-INSTALLER-STUDY.md`, `API-AND-INSTALLER-STUDY.md`, and `Project02-fe/docs/` (design audits).
