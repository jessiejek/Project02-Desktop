# Release gate (plan step 5.4): putting the offline-mode work on the real server

**Nothing here is done yet. It starts only when the owner says "go".** Pushing the backend `main` deploys to the production host by itself (GitHub Actions `deploy-backend.yml` over FTP).

## What goes live
| Repo | Branch to merge | Contains |
|---|---|---|
| Backend (`Project02-be`) | `plan/phase-4-review-items` | review items, duplicate detection, receipt-clash items, patient merge, `/health/ready` is already on `main` |
| Backend | `plan/step-2.4-doctor-confirm-payment` | the doctor may also confirm payments (own visits only) |
| Web (`Project02-fe`) | `plan/step-4.5-sync-review` | the admin "Sync review" page |
| Desktop (`Project02-Desktop`) | the Phase 0-5 branch chain (latest `plan/phase-5-hardening`) | the desktop app itself (not auto-deployed; installers are built separately) |

## Step 0: find out what production is today (before anything)
- On 22 Nov the old temporary address answered a "Deploying, back in a moment" page (a deploy left its offline page behind), and later did not answer at all. The plan was upgraded on the host since. **Confirm the real production URL** and that `GET /health` and `GET /health/ready` answer with `db_ok:true`.
- If the "Deploying" page is stuck: re-run the GitHub workflow (Actions > Deploy backend > Run workflow), or delete `app_offline.htm` from the `site1` folder with the host's file manager.
- Confirm with the host support (one message): .NET 10 runtime, WebSockets (live queue), maximum database size, and that backups are included.
- Confirm the GitHub repo secrets exist: `FTP_HOST`, `FTP_USER`, `FTP_PASS`, and variables `FTP_REMOTE_DIR`, `BACKEND_URL`. **Change the FTP password first** (it appeared in a chat transcript on 22 Nov) and update the secret.

## Step 1: backup first, and prove the backup restores
1. In the host panel, make a full database backup (or export). Download a copy.
2. Restore it into a spare database (the panel's second database) and open it: the patients table must have the same row count. If that cannot be done, **stop**.
3. Write down the current deployed version (`GET /api/version`) and the date.

## Step 2: migrations, in a quiet window (before any new code)
- Run `deploy/migrate-sync-review.sql` in the host's SQL panel (safe to run twice). It adds the table `sync_review_items` and the column `patients.merged_into_patient_id`. Nothing is deleted or rewritten.
- Check: `SELECT COUNT(*) FROM sync_review_items` answers 0, and `SELECT COL_LENGTH('patients','merged_into_patient_id')` is not NULL.
- The old backend keeps working with the new table and column present (it ignores them).

## Step 3: merge and deploy, one thing at a time
1. Merge the backend branches into `main` (payment branch first, then Phase 4). The push deploys. Watch the GitHub Actions run to the end.
2. Check production: `/health` 200, `/health/ready` `db_ok:true`, `/api/version` newer than before.
3. Merge the web branch and deploy the web app the way it is normally hosted.
4. Install the new desktop build on the clinic computer. On the first start it repairs its local database by itself.

## Step 4: first sync and smoke test (production, one real round trip)
1. Admin registers the clinic computer (Admin > Sync computers) if it is not registered yet.
2. On the desktop: sign in as the doctor; the first sign-in syncs (Skip not pressed). The results screen must say "Sync finished".
3. Make **one booking** (web), **one walk-in and one payment with a typed receipt number** (desktop), then **Sync now**. All three appear on the web.
4. Open Admin > Sync review: it opens (empty or with items). Do **not** merge anything real as a test.
5. Switch the desktop to Offline and back to Online once.

## Rollback plan (written before starting)
| Problem | Do this |
|---|---|
| New backend misbehaves | Re-deploy the previous commit: `git revert` the merge on `main` and push (the workflow deploys the old code). The new table and column are harmless to the old code and can stay. |
| A migration step fails | Stop. Nothing else was changed. The script is safe to run again after the cause is fixed. |
| Data looks wrong | Restore the backup into the spare database, compare, and restore from it only with the owner's say-so. |
| Desktop problem | Reinstall the previous desktop build. Local data stays; the local database is only added to, never rewritten. |
| Site stuck on "Deploying" | Re-run the workflow, or delete `app_offline.htm` from `site1`. |

## Known limits to accept at release
- Dock > Quit, shutdown and the red window button are not intercepted (work stays safe on the computer).
- A merged-away patient that still has unsent records on a desktop stays listed on that desktop until an admin checks it.
- Real-printer output and the full clinic-day run (10 patients, Offline) are the owner's checks.
