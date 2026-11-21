<!-- JJQR - AI (21 Nov 2026) - allen2: the online-first contract every lane follows -->
# Online-first (Clinic Node desktop)

**Rule:** online → the clinic API (same endpoints as Project02-fe). Offline → the local SQLite copy + the sync outbox. While online the local copy is kept up to date automatically, so going offline mid-shift loses nothing.

## 1. What a lane does (and only this)
1. Write `ClinicNode.Api/Services/ApiXService.cs` implementing the SAME Core interface `IXService`. Constructor takes `IApiClient` only.
   - Call the endpoints the web page calls (`api.GetAsync<TDto>("/api/...", query, ct)`, `api.SendAsync(HttpMethod.Post, ...)`). Data wire is **snake_case** with enums by name (`ApiJson.Data`); `/api/auth/*` is camelCase.
   - Private `record` DTOs, map to the Core models. Not served by the API yet → `throw new ApiNotSupportedException();` (the router uses Local).
2. Register it ONCE in **your own module file** (e.g. `ClinicNode.App/Staff/StaffModule.cs`), after the Core registration:
   ```csharp
   services.AddOnlineFirst<IXService, ApiXService, XService>();
   ```
   Never edit `MauiProgram.cs` / `AppShell` yourself: send allen0 the one line that calls your module.
3. Screens keep injecting `IXService`. Nothing else changes. **Do not** write to SQLite from an Api service, do not call SyncOutbox from screens, do not handle connectivity in screens.

## 2. Reads
- Online + signed in online → `ApiXService`. Result goes to the screen **and** is mirrored into SQLite centrally (`OnlineReadMirror`): a debounced catch-up pull (`/api/sync/pull` from the saved cursor) runs after API reads. Optional speed-up: register `IApiReadCacheWriter<IXService>` to upsert the result rows immediately (upsert by PK, never touch outbox rows).
- Offline, offline sign-in, network error/timeout/502/503/504, or `ApiNotSupportedException` → Local `XService` (SQLite).
- Read vs write comes from the method name: `Get/List/Search/Find/Count/Is/Has/Load/Query/Read/Preview/Build/Can/Lookup/Fetch…` = read; anything else = write. Override with `[OnlineRead]` / `[OnlineWrite]` on the Core interface method.

## 3. Writes (how a write marks itself for the queue)
- Online → API first. On success the router calls `ISyncBridge.RequestSync()`, the next pull brings the saved rows into SQLite.
- API unreachable → the Local service saves it; the Local service already writes the `SyncOutbox` row in the same transaction (existing Core pattern: `SyncOutbox.EnqueueAsync(db, entry)` + `ISyncBridge.RequestSync()`). The router wakes the sync loop; `ClinicNode.Sync` pushes when the connection returns. A NEW local write method must follow that same pattern, nothing more.
- A real server answer (400/401/403/404/409/422/5xx) is **never** retried locally: it reaches the screen as a typed `ApiException`.
- `[OnlineRoute(OnlineRoute.ApiOnly)]` = needs the server (change password, send email, forgot password): offline → `ApiOfflineException`. `[OnlineRoute(OnlineRoute.LocalOnly)]` = device-only (printing, backup, this-computer settings).

## 4. Keeping SQLite current while online (automatic, `LiveSyncCoordinator`)
| Trigger | Action |
|---|---|
| App start / login page | catch-up pull from the saved sync cursor |
| Connection returns (`IApiConnectivity.Changed(true)`) | push queued writes + catch-up pull; hub reconnects |
| Hub event `QueueUpdated` / `PaymentUpdated` / `PatientCheckedIn` | immediate pull |
| Every 60 s (online) | safety-net pull |
| Every successful API read | debounced pull (+ optional cache writer) |

Live screens subscribe to `IClinicLiveEvents` (`Subscribe("QueueUpdated", e => refetch)`, plus `Resync` and the 60 s `Poll`) and dispose the subscription in `OnDisappearing`. Events arrive off the UI thread → `MainThread.BeginInvokeOnMainThread`. The payload is a hint (ids + status), not data.

## 5. Sign-in and session
`OnlineFirstAuthService` (registered by `AddClinicNodeShared`): API login first; 401 → "Invalid email or password." (no local fallback); only an unreachable server falls back to the local password check. 1 h access token, 7-day rolling refresh token in SecureStorage; logout clears both. Only the email is remembered. Patient accounts are refused.

## 6. Error messages (show `ex.Message`; these are the defaults)
| Exception | Screen shows |
|---|---|
| `ApiOfflineException` (ApiOnly method) | "This needs a connection to the clinic server and an online sign-in." |
| `ApiValidationException` | server message; `Errors[field]` next to the field |
| `ApiUnauthorizedException` | "Your session has expired. Please sign in again." |
| `ApiForbiddenException` | "You do not have access to this." |
| `ApiNotFoundException` | "This record no longer exists." + refresh |
| `ApiConflictException` | "Someone else changed this. Reload and try again." |
| `ApiServerException` | "The clinic server had a problem. Try again." |

## 7. Top bar
`OnlineStatusIndicator`: **Online, up to date** · **Syncing...** · **Offline, N changes waiting** (conflicts → "Conflicts", opens the review page).

## 8. Never
Log passwords, tokens or patient data · store a password · add a patient portal or /register · touch the backend (:5055) or web app (:3000).
<!-- JJQR - END -->

<!-- JJQR - AI (21 Nov 2026) -->
## 9. How to: live updates on a page (doctor / admin / staff)
Use `ClinicNode.App.Views.Shared.Live.LiveHubSubscription` — no DI registration, no new hub code.

```csharp
using ClinicNode.Api.Live;
using ClinicNode.App.Views.Shared.Live;

private LiveHubSubscription? _live;

protected override void OnAppearing()
{
    base.OnAppearing();
    _live ??= new LiveHubSubscription(services,
        [ClinicHubEvents.QueueUpdated, ClinicHubEvents.PatientCheckedIn],   // only the events this page shows
        ReloadAsync,                                                        // your existing reload (UI thread)
        e => e.DoctorId is null || e.DoctorId == _doctorId);                // optional filter
    _live.Attach();
    _ = ReloadAsync();
}

protected override void OnDisappearing()
{
    _live?.Detach();
    base.OnDisappearing();
}
```

- **Attach** (on appear): subscribes each event, listens for `Resync` (after every hub reconnect, because missed events are not replayed), and starts the hub (it only connects after an online sign-in).
- **Bursts**: everything inside 300 ms becomes **one** reload on the UI thread. A reload that is still queued when the page closes is skipped.
- **Detach** (on disappear): unsubscribes everything. Both calls are idempotent.
- Events: `QueueUpdated`, `PaymentUpdated` (the server sends these) and `PatientCheckedIn` (reserved). Each carries `BookingId`, `DoctorId`, `PaymentId` and `Status` when the server sends them, so a filter must treat a missing id as "maybe mine".
- Keep the 60 s poll / local DB reload as the fallback. The hub only says *what to refetch*; reads still follow §2.
- Staff pages get all of this from `StaffLivePage`: override `HubEvents` (and `WantsHubEvent` to filter).
<!-- JJQR - END -->

<!-- JJQR - AI (21 Nov 2026) -->
## 10. Queue numbers, consents, OR numbers, extra pull data (sync layer)
- **Queue blocks** (`ClinicNode.Sync/Local/QueueBlocks.cs`): while online, every sync cycle makes sure this computer holds a block for **today through today+6 (7 days)** in **both lanes** (R, S). It calls `POST /api/sync/queue-blocks {date, lane, size: 20}` with the sync-node token and gets back `{date, lane, from, to, size, first, last}`. A lane/day with **5 or fewer** numbers left gets another block. Local bookings take numbers **only** from a block (`QueueBlockNumberSource.NextForPatientAsync`). With nothing left it returns null, and the desk sees `QueueBlocks.NoNumbersMessage`. A number is never invented or reused. Blocks for past days are dropped. Booking code reaches the blocks through the Core interface `ClinicNode.Core.Queue.IQueueNumberSource` (registered in `ClinicNode.Sync/ServiceCollectionExtensions.cs` → `QueueBlockNumberSource`); it is wired into QueueService / StaffBookingService / AdminBookingService (task C, see below).
<!-- JJQR - AI (21 Nov 2026) -->
- **Printed queue number is kept (Jessie, hard rule).** A push result or a pull never overwrites a local booking's `queue_number` once it has one (`AggregateDocuments.ApplyBookingAsync` keeps it). When the cloud copy has a different number, a review item `queue_number_clash` is added: "Queue number clash: flagged for admin". A push result `merged` + `queue_number_reassigned` is NOT a clash: the booking was sent with no number and the clinic server assigned one (review pill "Queue number assigned by the clinic server"). No client-side renumber, override or retry. `queue_number_clash` is NOT in API-SURFACE.md yet; it is the suggested server code (allen1).
- **Payments online:** a 409 `or_number_duplicate` on confirm or waive saves nothing; the screen shows an OR field prefilled with the clashing number (required, trimmed, max 100) and retries online only with the typed number (`ApiStaffPaymentsService.RecordWithOrAsync` / `WaiveWithOrAsync`, waive body `{reason, or_number}`). A 401 during confirm / waive / typed-OR retry never falls back to local: "Your session has expired. Please sign in again. Nothing was saved."
- **Vaccinations:** the consultation PUT now carries `reaction_notes`, `administered_date`, `status` (the sync push already sent all columns). Standalone vaccinations made on this computer go up via `POST /api/patient-vaccinations` (user session, client id; 409 = already there) after their patient is in the cloud (`ClinicNode.App/Shared/StandaloneVaccinationPush.cs`). Never deleted.
  - Results: 200 / 201 and 409 `{error:"id_conflict"}` = done. 400 (`vaccination_rejected`), 401 (`vaccination_session_expired`), 403 (`vaccination_forbidden`), 404 (`vaccination_not_found`) and any other 409 (`vaccination_conflict`): the row stays on this computer (not deleted, not marked pushed), one "skipped" review item per situation with the server message, and the row is parked, not re-posted every run, until it changes (corrected), the sign-in changes (401 only) or the app restarts. 5xx / offline: retried next run.
  - `reaction_notes` over **2000** characters is never cut and never sent (`reaction_notes_too_long` review item); re-checked every run, sent once shortened.
  - Skip checks (already pushed, parked, too long, patient not in the cloud yet) run before the 25-per-run limit, so stuck rows never block newer ones.
- **Queue numbers in Core (task C):** `QueueService` (walk-in check-in), `StaffBookingService` (reschedule) and `AdminBookingService` (create, reschedule) take an optional `IQueueNumberSource` (filled by the Sync DI registration) and get numbers only from it. A null number returns Fail "This computer has no reserved queue numbers left for today. Connect to the internet once so it can reserve more, or check the patient in on a computer that is online." and nothing is saved. `QueueSequencer.NextForPatientAsync` is used only when no source is registered (tests / no sync).
- **Clinic settings are online-only.** Any `clinic_settings` outbox row is dropped at push time (acked, not sent) with a "skipped / online_only" review item. Nothing queues new ones any more (the enqueue was removed from `AdminSettingsService`); the drop clears rows queued earlier.
<!-- JJQR - END -->
- **Consents**: every local `patient_consents` row is pushed once as `patient_consent` "create" (append-only, the id is the consent id). `duplicate` = already there (acked), `retry missing_dependency` = the patient isn't up yet (backoff), deletes are never sent. Pulled consents are never pushed back.
- **OR numbers**: a pushed payment answered `applied` + `or_number_clash` counts as a success (no retry). It is listed on the review page as **or_number_clash**, "Flagged for admin". Online confirm with a used OR returns **409** `{error:"or_number_duplicate", message, or_number}`. Nothing is saved, the desk sees "OR number … is already used on another payment", and there is no local fallback.
- **Pull feed**: `patient_consent` (append-only), `patient_lab_value`, `patient_lab_result` and `patient_document` (metadata only, the files stay online) are applied to their local tables. A missing table is listed as `local_table_missing`. The patient document's `vaccinations` (no consultation) are inserted or updated, and local standalone rows are never deleted. Audit rows: a new computer gets the last 30 days only (nothing assumes full history).
<!-- JJQR - END -->
