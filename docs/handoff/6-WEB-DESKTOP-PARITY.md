# Web versus desktop: do they do the same things? (checked 23 Nov 2026)

How it was checked: (1) every web page (61) and every desktop screen (38) was listed and matched by role; (2) the backend operations each side calls (web 117, desktop 114) were compared; (3) each suspected difference was then checked in the code. This is a **first pass** at the level of screens and operations. It does not yet compare every button and field inside every screen (see "Not yet checked").

## 1. Screens match, role by role
| Role | Web pages | Desktop screens | Result |
|---|---|---|---|
| Admin | dashboard, bookings (+detail), doctors (+new, edit), patients (+detail), staff, announcements, audit logs, reports, settings, walk-in, sync review, my account, calendar | all of these exist except: **sync review merge** (see gap 2); calendar and my-account are covered differently (calendar is a web redirect to bookings; account/password/email is in the profile area) | matches, 1 gap |
| Doctor | dashboard, appointments (+detail), consultation (all steps), patients (+chart), prescriptions (create/edit), schedule, templates, performance, profile | all present (consultation, charts, prescriptions, templates, schedule, performance, profile) | matches, 1 gap (files) |
| Staff | dashboard, queue, walk-in, bookings (+detail), patients (+detail), payments, doctor status, announcements, profile | all present | matches |
| Patient | 14 pages (book, results, records, privacy, reviews, ...) | none, on purpose: patients use the web only | expected |
| Public | login, forgot password, register, privacy notice | login, forgot password | register and privacy notice are web-only, expected |

Web pages that are only redirects (nothing to copy): `admin/calendar`, `staff/bookings`, `doctor/settings`.

## 2. Things only the desktop has (on purpose)
Offline mode and the local copy, sync, "This computer" registration, registered computers (rotate/revoke), receipt clashes, native printing, thermal queue slip, walk-in wizard, update checker. Reports also match: the desktop shows daily booking summary, unpaid visits, pending follow-ups, doctor ratings and doctor earnings.

## 3. Verified gaps (things the web does that the desktop does not)
| # | Gap | Web | Desktop | Impact | Suggested fix |
|---|---|---|---|---|---|
| 1 | **Open a patient's uploaded lab results and documents** | opens the file (download from the server with the sign-in) | lists the file **names only**, cannot open them (`DoctorPatientChartPage.Files`) | the doctor cannot view a scan or referral letter on the desktop | add an "Open" button: download with the sign-in to a temporary file, open in the default viewer (Mac and Windows). Online only |
| 2 | **Merge duplicate patients / resolve sync review items** (admin) | compare and merge, or dismiss | only shows a count; merge is on the web | admin must switch to the website for merges | keep web-only for now (it is risky to merge offline); add an "Open on the web" hint. Already listed in the docs |
| 3 | **Privacy consent at the front desk** | patients consent when they self-register on the web. Staff registration screens do not ask for consent on either side | same | for the Philippine Data Privacy Act, a patient registered by staff should have a privacy notice shown and consent recorded | add a notice and a consent tick to staff registration on BOTH web and desktop (needs the clinic's privacy wording) |

## 4. Not yet checked (needs a deeper pass)
Field-by-field and rule-by-rule inside each screen: every consultation step, validations, error messages, permissions per button, and edge cases (for example editing after completion, amendments). The desktop code already mirrors many web rules by name (duplicate drug check, drug safety, favourites), but that is not proof of equality.

## 5. Suggested next steps
1. Build gap 1 (open patient files on the desktop).
2. Decide gap 3 with the privacy adviser, then add the consent step on both.
3. Run the deeper pass: for each role, walk each screen on web and desktop with the same test data and note differences; turn each into a test. Efficient way: one automated checklist per role.
4. Add a "parity" test list so a new web feature cannot ship without a desktop decision (build it, or mark it web-only on purpose).

## 6. Status after the fixes (23 Nov 2026, in the working folders, NOT committed or released yet)
| # | Gap | Fixed how |
|---|---|---|
| 1 | Open uploaded patient files | Desktop chart: each lab result and document has an **Open** button. It downloads the file with the sign-in to a temporary copy (deleted after an hour) and opens it in the computer's own viewer (Mac and Windows). Online only: offline it says the file is on the server |
| 2 | Merge duplicate patients / resolve sync review | New desktop admin screen **Sync review** (menu, next to Receipt clashes): filters, list, item details, "Compare and merge...", "Not a duplicate", "Mark resolved", "Dismiss", note of at least 5 characters, same wording and rules as the web. Online only. Both-have-accounts merges are blocked, like the web |
| 3 | Privacy notice at the front desk | **Web** (walk-in registration and Admin > Patients > Add) and **desktop** (quick register in Staff/walk-in, and Admin > Patients > Add) now show the Data Privacy Act notice and require the tick "I have shown the patient the privacy notice". The server records it in the audit log (new endpoint `POST /api/patients/{id}/privacy-notice-shown`, Admin/Staff/Doctor only). It is **not** the patient's consent: that stays the patient's own action in the portal (earlier security rule PV-2 kept). Offline desktop registrations record the audit entry locally and it syncs |

Tests: backend privacy tests 7/7 pass (1 new); desktop parity tests 10/10 pass (new); the two desktop suites and the web unit suite are at their usual old-failure baselines (desktop 18, web 12), nothing new; web typecheck and lint clean. The two end-to-end specs that register patients through the web screens were updated for the new tick but were **not run** (they need the full test environment).

Needs the backend deploy before the notice entry appears: the web and desktop call the new endpoint; until the backend with that endpoint is live they simply skip the entry (the registration still works).
