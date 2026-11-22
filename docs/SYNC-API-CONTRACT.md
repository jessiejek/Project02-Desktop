<!-- JJQR - AI (Nov 21, 2026) - cloud sync API contract for the Clinic Node (written by the backend sync worker). -->

# Cloud sync API — contract v1

Status: **DRAFT v1, 21 Nov 2026 (Asia/Manila)**. Design settled; backend implementation in progress in `Project02-be` (local only, not deployed). If anything below changes, this file is updated and the change is listed in §11.

Base URL: the cloud API (e.g. `https://<api-host>`). Locally for testing: `http://localhost:5077` (never `:5055`).

All JSON uses the API's existing conventions: **snake_case** property names, enums as their **exact C# member names** (`"Completed"`, `"PayAtClinic"`), GUIDs as lowercase strings, timestamps as ISO-8601 with offset (`2026-11-21T06:15:00+00:00`), `DateOnly` as `"2026-11-21"`, `TimeOnly` as `"09:30:00"`.

---

## 1. Protocol header and versioning

Every sync request (except node registration, which is an admin call) sends:

```
X-Sync-Protocol: 1
```

- Missing header → treated as `1`.
- Unsupported value → **426** `{ "error": "protocol_unsupported", "supported": [1] }`. The node must update before syncing.

`GET /api/sync/handshake` (node token) → 200:

```json
{ "protocol_version": 1, "min_protocol_version": 1, "node_id": "6f1c…", "server_time": "2026-11-21T06:15:00+00:00", "node_status": "Active" }
```

Use `server_time` (not the local clock) for "last synced at" and to detect clock skew.

---

## 2. Node registration and auth

One node per clinic is the intended topology. A node is registered once **by a cloud Admin**, receives a **node secret** once, and exchanges it for short-lived **node access tokens**.

### 2.1 Register a node — `POST /api/sync/nodes` (Admin user JWT)

```
Authorization: Bearer <cloud admin access token>
```

Request (`node_id` optional — send the node's own `node_identity.node_id` so ids match):

```json
{ "node_id": "6f1c2a7e-1d1b-4c55-9a34-2a1f8f2b9d10", "name": "Front desk PC", "platform": "maccatalyst", "app_version": "0.1.0" }
```

201:

```json
{ "node_id": "6f1c2a7e-1d1b-4c55-9a34-2a1f8f2b9d10", "name": "Front desk PC", "node_secret": "Zt8…base64url-43-chars…", "status": "Active", "registered_at": "2026-11-21T06:15:00+00:00" }
```

- `node_secret` is returned **only here** (and on rotate). The cloud stores only a hash. Store it on the node like `node-secret.key` (never log it).
- Node id already registered → **409** `{ "error": "node_exists" }`.
- Non-admin → 403.

Other admin calls (Admin JWT):

| Call | Result |
|---|---|
| `GET /api/sync/nodes` | 200 `[ { "node_id", "name", "platform", "app_version", "status", "registered_at", "last_seen_at", "revoked_at" } ]` |
| `POST /api/sync/nodes/{nodeId}/rotate-secret` | 200 `{ "node_id", "node_secret" }` (old secret stops working at once) |
| `POST /api/sync/nodes/{nodeId}/revoke` | 200 `{ "node_id", "status": "Revoked", "revoked_at" }` (all further sync calls from it fail) |

### 2.2 Get a node token — `POST /api/sync/token` (anonymous)

```json
{ "node_id": "6f1c2a7e-1d1b-4c55-9a34-2a1f8f2b9d10", "node_secret": "Zt8…" }
```

200:

```json
{ "access_token": "eyJhbGciOi…", "token_type": "Bearer", "expires_in": 3600, "expires_at": "2026-11-21T07:15:00+00:00", "node_id": "6f1c2a7e-1d1b-4c55-9a34-2a1f8f2b9d10" }
```

- Wrong id/secret → **401** `{ "error": "invalid_node_credentials" }`.
- Revoked node → **403** `{ "error": "node_revoked" }`. The node must stop syncing and lock (study §5.8).

The token is a JWT signed by the cloud (claims: `role=SyncNode`, `node_id`). It is **only** accepted by `/api/sync/*`; user endpoints reject it. When a sync call returns 401, get a new token and retry once.

All of §1, §3, §4 use:

```
Authorization: Bearer <node access token>
X-Sync-Protocol: 1
```

A revoked node's existing token is also refused: **403** `{ "error": "node_revoked" }`.

---

## 3. Push — `POST /api/sync/push`

Uploads outbox rows. **Batch** of 1–200 operations, applied **in array order** (send in `created_at` order). Each operation is applied in its own transaction; one bad operation never blocks the others.

### 3.1 Request

```json
{
  "operations": [
    {
      "operation_id": "0b7d9a52-5d0e-4a1e-9d7e-7a4e3f2c1b00",
      "aggregate": "patient",
      "entity_id": "3c2b8f0e-6a8e-4d3d-8f5e-0a9b1c2d3e4f",
      "operation_type": "create",
      "base_row_version": null,
      "created_at": "2026-11-21T01:02:03+00:00",
      "payload": { "data": { "patient_id": "3c2b8f0e-…", "patient_code": "MF-000201", "first_name": "Ana", "last_name": "Cruz", "date_of_birth": "1950-04-02", "sex": "Female", "...": "..." }, "changed_fields": null }
    }
  ]
}
```

| Field | Meaning |
|---|---|
| `operation_id` | The outbox `operation_id`. **Idempotency key** (global, per node). |
| `aggregate` | `patient` · `booking` · `payment` · `consultation` · `vital_reading` · `audit_log` (§5.1–5.6) · `doctor` · `doctor_schedule` · `doctor_blocked_date` (self-edits, §5.7). Anything else → `rejected` / `unknown_aggregate`. Cloud-owned aggregates (§6) → `rejected` / `cloud_owned`. |
| `entity_id` | Root id of the aggregate (patient_id, booking_id, payment_id, consultation_id, vital reading id, audit log id). Must equal the id inside `payload.data`. |
| `operation_type` | `create`, `update`, `upsert`, `replace`, `delete`, or the node's verbs (`status`, `cancel`, `confirm`, `confirm-waiver`, `complete`). **Sync is state-based**: every type except `delete` means "here is the node's current full snapshot of this aggregate"; the verb is only recorded for audit. |
| `base_row_version` | The cloud `row_version` the node last saw for this aggregate (from a pull or a previous push result). `null` = node never saw a cloud copy (offline create). Used for conflict detection. |
| `created_at` | When the change happened on the node (node clock). |
| `payload.data` | **Full snapshot** of the aggregate (shapes in §5). |
| `payload.changed_fields` | Optional, `patient` only: snake_case field names the node changed since `base_row_version` (enables field-level merge). |
<!-- JJQR - AI (Nov 21, 2026) doctor self-edit -->
| `actor_user_id` | **Required** for `doctor`, `doctor_schedule`, `doctor_blocked_date` (§5.7), ignored elsewhere. The cloud user id (`users.id`) of the person who made the edit on the node. Missing → `rejected` / `actor_required`. |
| `payload.field_updated_at` | Optional, `doctor` / `doctor_schedule` / `doctor_blocked_date` only: `{ "<field>": "<ISO time>" }` = when the node last edited that field (node clock). Used for the per-field newest-wins merge. Missing field → `payload.data.updated_at`, then the op's `created_at`. A time more than 5 minutes in the future is treated as "now". |
<!-- JJQR - END -->

**Important for the node client:** the current node outbox payloads are thin (`{"patientId","patientCode"}`). Build `payload.data` at push time from the **current local row(s)** of `entity_id` (or enrich at enqueue). Several queued operations for the same entity can be collapsed into the newest one, but each `operation_id` still needs to be acked (send them all; older ones simply come back `applied` too).

### 3.2 Response — 200 (even when some operations conflict or are rejected)

```json
{
  "server_time": "2026-11-21T06:15:00+00:00",
  "results": [
    { "operation_id": "0b7d9a52-…", "aggregate": "patient", "entity_id": "3c2b8f0e-…", "status": "applied", "row_version": 1, "reason_code": null, "message": null, "conflict_fields": null, "current": null, "replayed": false }
  ]
}
```

Batch-level errors: 400 `{ "error": "empty_batch" | "batch_too_large" | "invalid_operation", "detail": "…" }` (nothing applied), 401/403 as §2, 426 as §1.

### 3.3 Per-operation `status`

| status | Meaning | Node action |
|---|---|---|
| `applied` | Snapshot written. `row_version` = new cloud version. | Ack. Store `row_version` as the new base. |
| `merged` | Partly applied; cloud kept some values (listed in `conflict_fields`; or booking status kept because the cloud was further). `current` = the cloud copy after merge. | Ack. Overwrite local row with `current`; show the item on the conflict list. |
| `duplicate` | Already applied earlier (same `operation_id`, or identical append-only row already exists). `replayed: true` when it is an exact `operation_id` repeat: the **original** result fields are returned. | Ack. |
| `conflict` | Not applied; cloud copy wins under the aggregate rule (§5). `current` = cloud copy, `reason_code` says why. | Ack. Replace local copy with `current` (or keep a local note) and put it on the conflict list. Do **not** retry. |
| `rejected` | Invalid or forbidden (validation, cloud-owned, append-only update, refund offline …). `reason_code` + `message`. | Ack, record `last_error`, show on the conflict/error list. Do **not** retry. |
| `retry` | Temporarily not applicable — usually `missing_dependency` (e.g. booking pushed before its patient). Nothing stored, `operation_id` **not** consumed. | Do **not** ack. Retry later with backoff (same `operation_id`). |

Transport errors, 5xx, 401 (after one token refresh), 429 → nothing acked, retry the whole batch with backoff (safe: idempotent).

`reason_code` values: `unknown_aggregate`, `cloud_owned`, `entity_id_mismatch`, `invalid_payload`, `missing_dependency`, `finished_immutable`, `illegal_transition`, `cloud_further`, `not_owner`, `refund_offline_forbidden`, `amount_mismatch`, `append_only`, `already_recorded`, `delete_not_allowed`, `patient_code_taken`, `cloud_edit_overwritten`, `invalid_operation`, `stale_amendment`, `payment_settled`, `allergy_union`, `queue_number_reassigned`, `actor_required`, `actor_unknown`, `actor_not_allowed`, `field_not_allowed`, `cloud_field_newer`, `natural_key_remapped`.

### 3.4 Conflict example

```json
{
  "operation_id": "9e1f…", "aggregate": "consultation", "entity_id": "a1b2…",
  "status": "conflict", "row_version": 7, "reason_code": "finished_immutable",
  "message": "Consultation is already completed in the cloud; finished consultations are immutable.",
  "conflict_fields": null, "replayed": false,
  "current": { "consultation": { "consultation_id": "a1b2…", "status": "Completed", "...": "..." }, "diagnoses": [], "prescription": null, "lab_orders": [], "vaccinations": [], "follow_up": null, "medical_certificate": null }
}
```

---

## 4. Pull — `GET /api/sync/pull?since=<cursor>&limit=<n>&include_own=<bool>`

| Param | Default | Meaning |
|---|---|---|
| `since` | empty | Opaque cursor from the previous `next_cursor`. Empty / `0` = from the beginning (full bootstrap). |
| `limit` | 500 | 1–1000 changes per page. |
| `include_own` | `false` | When false, changes whose last writer was this node are skipped (the node already has them from push results). The cursor still advances past them. |

**Cursor format:** an opaque string. (Today it is the decimal sequence number of the cloud change log, e.g. `"18342"`, but clients must store and send it back verbatim and must not compare or do arithmetic on it.) Invalid cursor → **400** `{ "error": "invalid_cursor" }`.

200:

```json
{
  "server_time": "2026-11-21T06:15:00+00:00",
  "next_cursor": "18342",
  "has_more": true,
  "changes": [
    {
      "aggregate": "booking",
      "entity_id": "5d4c…",
      "operation": "upsert",
      "row_version": 12,
      "origin_node_id": null,
      "changed_at": "2026-11-21T05:59:10+00:00",
      "data": { "booking": { "booking_id": "5d4c…", "status": "Confirmed", "queue_number": "R-004", "...": "..." }, "booking_services": [ { "booking_id": "5d4c…", "service_id": "…", "price_at_booking": 500.0 } ] }
    },
    { "aggregate": "medicine", "entity_id": "77aa…", "operation": "delete", "row_version": 3, "origin_node_id": null, "changed_at": "2026-11-21T05:59:11+00:00", "data": null }
  ]
}
```

- `operation`: `upsert` (apply `data` as the full current state; insert or overwrite) or `delete` (tombstone; delete or soft-delete locally; `data` is null).
- `data` is the **current** state at pull time, so one aggregate appears at most once per page, at its latest change. Keep pulling while `has_more` is true; save `next_cursor` only after the page is applied (same local transaction).
- `origin_node_id` = node that last wrote it (null = written on the cloud web app).
- Patient-portal user accounts are never sent.

---

## 5. Pushable aggregates — snapshot shapes and conflict rules

Field names in `data` are the entity's snake_case columns (same names as the existing REST API / the SQLite columns of `ClinicApp.Domain`). Navigation objects are ignored. The server sets `row_version`, `origin_node_id`, and keeps its own `updated_at` for web edits; send the node's `created_at` / `updated_at`.

### 5.1 `patient` — field-level merge
`data`: one `patients` row (`patient_id`, `patient_code`, `first_name`, `middle_name`, `last_name`, `date_of_birth`, `sex`, `civil_status`, `address`, `city`, … `created_at`, `updated_at`).

- Not in cloud → insert (`applied`). `patient_code` must be unique: a code already used by another patient, or a temporary `MF-T-…` code, is replaced by a **cloud-allocated** `MF-` code → status `merged`, `conflict_fields: ["patient_code"]`, `current` holds the new code (node must update the local row and reprint the card).
- Exists, `base_row_version == cloud row_version` → full overwrite (`applied`).
- Exists, cloud changed since base → field-level merge: fields in `changed_fields` (or every field whose value differs, if `changed_fields` is null) are taken from the node **only if** node `updated_at` ≥ cloud `updated_at`; the rest stay cloud → `merged` with the kept fields in `conflict_fields` (or `applied` if nothing was kept).
- `delete` → `rejected` / `delete_not_allowed`.
<!-- JJQR - AI (Nov 21, 2026) allergies -->
- **Allergies** are part of the patient document: `data.allergies` = `[ { "id", "allergen", "reaction", "severity", "is_nkda" } ]` (pull sends it too, sorted by allergen). Key **absent** = leave the cloud's allergies alone. Safety rule — an allergy is never lost to a stale push:
  - node saw the latest copy (`base_row_version == cloud row_version`, or insert) → the node's list **replaces** the cloud's (removals honoured; `null`/`[]` clears);
  - cloud changed since → the lists are **united** by allergen name (case-insensitive; the node's reaction/severity win for an allergen both have) → `merged`, reason `allergy_union`, `conflict_fields` contains `"allergies"`; `current.allergies` is the result;
  - in either case a real allergen removes the `NKDA` row (`is_nkda: true`), so a stale "no known allergies" can never hide a real one.
  Cloud-side allergy edits bump the patient's `row_version` and appear on pull as a change to the **patient**.
  The node pushes this as a patient op with `operation_type: "update"` and `payload.changed_fields: ["allergies"]` (plus `data.allergies` and `base_row_version`). There is no allergy-specific verb. `changed_fields: ["allergies"]` leaves the other patient columns alone; the allergies list is still applied (it is not a patient column, so it is not itself field-merged).
<!-- JJQR - END -->

### 5.2 `booking` — state machine decides
`data`: `{ "booking": { bookings row }, "booking_services": [ { "booking_id", "service_id", "price_at_booking" } ] }`

- Not in cloud → insert with the node's id and `queue_number` (patient/doctor must exist, else `retry` / `missing_dependency`).
- Exists → status: equal or a legal transition cloud→node: node status applied. Cloud is further (legal node→cloud): cloud status kept, other fields applied → `merged`, `conflict_fields: ["status"]`, `reason_code: "cloud_further"`. Neither direction legal (e.g. `Cancelled` vs `Completed`) → whole op `conflict` / `illegal_transition` (review list).
- Non-status fields: node wins when `base_row_version` matches or node `updated_at` ≥ cloud `updated_at`; otherwise cloud kept (`merged`).
- `booking_services` replaced as a set when the node's booking fields are applied.
- `delete` → `rejected` / `delete_not_allowed` (cancel instead).
<!-- JJQR - AI (Nov 21, 2026) queue-number reconciliation -->
- **Queue numbers** (`R-001` regular / `S-001` senior-PWD; one sequence per day per lane; the cloud counter is `queue_counters`). While offline the node is the only issuer, so its numbers can collide with online bookings the cloud numbered meanwhile. Rule, applied on every booking insert and on a changed `queue_number`:
  1. **The node's number stays with the node's booking**, and the cloud counter for that day+lane is raised to at least that number — the cloud never issues it (or anything below it) again.
  2. If another booking **of the same date** already holds the number:
     <!-- JJQR - AI (21 Nov 2026) --> **Current rule (replaces the old renumber text, which is obsolete):** no booking is renumbered, whoever holds the number (a cloud booking, another node's booking, or one already `CheckedIn`/`InProgress`/`OnHold`/`Completed`). The printed number is kept, BOTH bookings are flagged with an audit entry `Queue number clash`, and the node's op is `applied` with `reason_code: "queue_number_clash"`. The desk sorts the duplicate out; the node does NOT reprint the slip. <!-- JJQR - END -->
     <!-- JJQR - AI (21 Nov 2026) --> (obsolete, no longer happens: renumbering the incoming booking, `merged` + `queue_number_reassigned` for a clash, and the 'node must reprint the slip' instruction.) <!-- JJQR - END -->
  3. A booking pushed without a number gets the next cloud number (`merged`, `queue_number_reassigned`; nothing had been printed yet).
  4. On an **update**, a changed `queue_number` is taken only when `base_row_version` equals the cloud's (the node saw the current number) and is then reconciled as above; on a stale base it is kept as the cloud's (`merged`, `conflict_fields: ["queue_number"]`). Normal status/field updates that leave the number unchanged are unaffected.
  Result (current rule): a printed number is never changed, so two bookings can share a date + queue number; each such pair is flagged `queue_number_clash` for the desk. Lanes are independent. Consequence for online patients: a number shown to them for a future day can be shared with a node booking (flagged `queue_number_clash`) — the desk sorts it out at check-in; no slip is reprinted. After a pull the node should raise its local counter for the day/lane to at least the highest pulled number (optional: duplicates are flagged on push, not renumbered).
<!-- JJQR - END -->

### 5.3 `payment` — single writer
`data`: one `payments` row.

- The first node that pushes a payment becomes its **owner** (`origin_node_id`). Pushes from another node → `rejected` / `not_owner`.
- Not in cloud → insert (booking must exist, else `retry`). If the cloud already has a different payment for the same booking: the node's payment replaces the cloud one **only if** the cloud one is still `Unpaid` and was never confirmed; otherwise `conflict` / `cloud_further`.
- Exists → the node's values win (never field-merged). If the cloud row was edited since `base_row_version`, the node still wins, the result is `applied` with `reason_code: "cloud_edit_overwritten"`, and the cloud writes an audit log entry (flag for review).
- Status must follow the payment state machine from the cloud value (`Unpaid→Paid|Waived`). `Refunded` from a node → `rejected` / `refund_offline_forbidden`. Cloud already `Refunded` → `conflict` / `cloud_further`.
- `Paid` with `amount_received` ≠ `amount` → `rejected` / `amount_mismatch` (exact-amount rule).
- `delete` → `rejected` / `delete_not_allowed`.

### 5.4 `consultation` — node wins, finished = immutable
`data`:

```json
{
  "consultation": { "consultation_id": "…", "booking_id": "…", "patient_id": "…", "doctor_id": "…", "status": "Completed", "chief_complaint": "…", "subjective": "…", "objective": "…", "assessment": "…", "plan": "…", "doctor_notes": null, "pf_decision": "Charge", "pf_amount": 500.0, "pf_waive_reason": null, "completed_by_user_id": "…", "completed_at": "…", "created_at": "…", "updated_at": "…" },
  "diagnoses": [ { "id": "…", "consultation_id": "…", "icd10_code": "J06.9", "custom_description": null, "type": "Primary", "created_at": "…" } ],
  "prescription": { "group": { "group_id": "…", "patient_id": "…", "doctor_id": "…", "booking_id": "…", "created_at": "…", "updated_at": "…" }, "line_items": [ { prescription_line_items row } ] },
  "lab_orders": [ { lab_orders row } ],
  "vaccinations": [ { patient_vaccinations row } ],
  "follow_up": { follow_ups row } ,
  "medical_certificate": { medical_certificates row }
}
```

(`prescription`, `follow_up`, `medical_certificate` may be null; arrays may be empty. Vitals are **not** part of this document — see 5.5.)

- Not in cloud → insert all (booking must exist, else `retry`).
- Cloud copy `Draft` → **node wins**: consultation row overwritten; children **replaced as a set** (missing children are deleted = tombstones on pull). `applied`.
- Cloud copy `Completed`/`Amended` → **immutable**: `conflict` / `finished_immutable`, nothing changes, `current` = cloud document. (A push identical to an op already applied is `duplicate`.)
- Pushing `status: "Completed"` completes it; from then on it is immutable except through an **amendment**.
<!-- JJQR - AI (Nov 21, 2026) amendments -->
- **Amendment** = a consultation op with `operation_type: "update"` (there is **no** `"amend"` verb) and the full snapshot whose `consultation.status` is `"Amended"`, for a `Completed`/`Amended` cloud copy. Applied (`applied`) only if:
  - the node saw the latest cloud copy: `base_row_version == cloud row_version`, **or the node itself was the last writer** (so it may amend repeatedly without pulling); otherwise `conflict` / `stale_amendment` (nothing changes, `current` = cloud document — pull, re-apply, push again);
  - `patient_id`, `doctor_id`, `booking_id` are unchanged (else `rejected` / `invalid_payload`);
  - the fee decision does not contradict a settled payment (waived fee → `Charge`, paid fee → `Waive`, or a paid `Charge` amount restated): else `conflict` / `payment_settled`.
  On success children are replaced as a set, an audit entry `Amended` is written (sections changed, no clinical text) and it syncs like any change. A snapshot that changes nothing → `duplicate`. `Amended` → `Completed`/`Draft` is still `conflict` / `finished_immutable`. `completed_at` / `completed_by_user_id` never change.
<!-- JJQR - END -->
- `delete` → `rejected` / `delete_not_allowed`.

### 5.5 `vital_reading` — append-only
`data`: one `patient_vital_readings` row (`id`, `booking_id`, `patient_id`, `template_id`, `value`, `recorded_at`, `recorded_by_user_id`, `created_at`, `updated_at`).

- New id and no reading for the same (`booking_id`, `template_id`) → insert, `applied`.
- Same id already present → `duplicate` (never overwritten).
- Another reading exists for the same (`booking_id`, `template_id`) → `conflict` / `already_recorded`.
- `delete` or a changed value for an existing id → `rejected` / `append_only`.

### 5.6 `audit_log` — append-only
`data`: one `audit_logs` row (`id`, `entity_type`, `entity_id`, `action`, `performed_by_user_id`, `details`, `performed_at`). New id → `applied`; existing id → `duplicate`; `delete` → `rejected` / `append_only`.

<!-- JJQR - AI (Nov 21, 2026) doctor self-edit -->
### 5.7 `doctor`, `doctor_schedule`, `doctor_blocked_date` — doctor self-edits (field merge, newest edit wins)

A doctor must be able to edit his own availability and profile offline. These three aggregates are pushable **only** with a valid `actor_user_id` (every other cloud-owned aggregate in §6 stays pull-only). All three use a **per-field merge**: for every field the node's value differs on, the node's edit time (`payload.field_updated_at[field]` → `data.updated_at` → `created_at`) is compared with the time the cloud last changed that field (web edits are stamped with the cloud clock, earlier syncs with the node clock). The newer one wins, per field. Equal values are skipped.

**Actor validation** (checked before anything is read or written):

| Check | Failure |
|---|---|
| `actor_user_id` present | `rejected` / `actor_required` |
| It is an existing `users` row that is an **active** staff / doctor / admin account (`staff_accounts.status = Active`, or an Admin profile) | `rejected` / `actor_unknown` (unknown or deactivated) |
| Role `Admin` → allowed for any doctor. Role `Doctor` → `actor_user_id` must be the user of the target doctor (`staff_accounts.user_id = doctor_id`). Role `Staff` / anyone else → never | `rejected` / `actor_not_allowed` |

Every failure writes an `audit_logs` row (`entity_type = Doctor`, `entity_id` = target doctor, action `Sync: self-edit rejected`, details with `reason` and `actor_user_id`); an accepted edit that changes something writes `Sync: doctor profile edited`, `Sync: doctor schedule changed` or `Sync: blocked date removed` with `performed_by_user_id = actor`, the old → new values per field and the node id. Nodes may also push their own `audit_log` rows as before (§5.6).

**Allowed fields**

| aggregate | doctor actor | admin actor |
|---|---|---|
| `doctor` | `specialization`, `bio`, `staff_account.full_name`, `staff_account.contact_number` | the doctor fields **plus** `consultation_fee`, `license_number`, `ptr_number`, `s2_number`, `slot_duration_minutes`, `slot_capacity`, `daily_patient_limit` |
| `doctor_schedule` | `is_active`, `start_time`, `end_time` (`doctor_id`, `day_of_week` identify the row and never change) | same |
| `doctor_blocked_date` | `reason` (`doctor_id`, `blocked_date` never change) | same |

`profile` stays cloud-owned (§6); the doctor's personal fields travel in `doctor`. A doctor actor that sends a different value for a restricted field gets that field **kept at the cloud value** and listed in `conflict_fields` with `field_not_allowed`; the rest of the snapshot still merges. The cloud never creates or deletes a `doctor`.

**Result codes**

| status / reason | When |
|---|---|
| `applied` | At least one field changed and nothing was kept back (or a new schedule row / blocked date was created, or a blocked date deleted) |
| `duplicate` | Nothing differs (or the blocked date to delete is already gone) |
| `merged` | At least one field applied, others kept: `reason_code` `field_not_allowed` (restricted for this actor) or `cloud_field_newer` (cloud edit newer), or `natural_key_remapped`. `conflict_fields` lists the kept fields (`["id"]` for a remap); `current` = cloud row after the merge, overwrite the local row with it |
| `conflict` | Nothing could be applied and at least one field was kept (`field_not_allowed` / `cloud_field_newer`) |
| `rejected` | `actor_required`, `actor_unknown`, `actor_not_allowed`, `invalid_payload` (limits below, bad time / weekday, unknown doctor, immutable key field changed), `entity_id_mismatch`, `delete_not_allowed` (`doctor`, `doctor_schedule`), `cloud_owned` (`doctor` create for an id the cloud does not have) |
| `retry` / `missing_dependency` | not used here |

`doctor` validation: `specialization` required, ≤ 100; `bio` ≤ 2000; `license_number` / `ptr_number` / `s2_number` ≤ 50; `consultation_fee` 0–1,000,000; `slot_duration_minutes` 5–240; `slot_capacity` 1–50; `daily_patient_limit` null or 1–500; `full_name` required ≤ 200; `contact_number` ≤ 50. `doctor_schedule`: `day_of_week` 0 (Sunday)–6, `start_time` < `end_time` when `is_active`. `doctor_blocked_date`: `reason` ≤ 500, `blocked_date` required. Only fields the actor may not touch are exempt from validation.

**Natural keys.** There is at most one `doctor_schedule` row per (`doctor_id`, `day_of_week`) and one `doctor_blocked_date` per (`doctor_id`, `blocked_date`). If the node created its own row (own `id`) for a key the cloud already has under another id, the cloud row is kept and updated by field merge, the result is `merged` / `natural_key_remapped` and `current.id` is the cloud id — **the node must replace its local id by `current.id`**.

**`doctor` — update** (`create` is accepted only for an id the cloud already has and is treated as `update`)

```json
{
  "operation_id": "…", "aggregate": "doctor", "entity_id": "<doctor_id>", "operation_type": "update",
  "actor_user_id": "<users.id of that doctor>", "base_row_version": 3, "created_at": "2026-11-21T08:00:00+00:00",
  "payload": {
    "data": {
      "doctor_id": "<doctor_id>", "specialization": "Pediatrics", "bio": "Updated bio",
      "consultation_fee": 450, "license_number": "LIC-1",
      "staff_account": { "full_name": "Dr Ana Reyes", "contact_number": "09171234567" },
      "updated_at": "2026-11-21T07:59:30+00:00"
    },
    "field_updated_at": { "bio": "2026-11-21T07:59:30+00:00", "specialization": "2026-11-21T07:41:00+00:00" }
  }
}
```

(Restricted fields may be present in the snapshot — send the whole local row — they only matter when the value differs from the cloud's.) `delete` → `rejected` / `delete_not_allowed`.

**`doctor_schedule` — create / update** (one row per weekday; switch a day off with `is_active:false` instead of deleting)

```json
{
  "operation_id": "…", "aggregate": "doctor_schedule", "entity_id": "<schedule row id>", "operation_type": "create",
  "actor_user_id": "<doctor's users.id>", "base_row_version": null, "created_at": "2026-11-21T08:00:00+00:00",
  "payload": {
    "data": { "id": "<schedule row id>", "doctor_id": "<doctor_id>", "day_of_week": 1, "is_active": true, "start_time": "09:00:00", "end_time": "12:00:00", "updated_at": "2026-11-21T08:00:00+00:00" },
    "field_updated_at": { "start_time": "2026-11-21T08:00:00+00:00", "end_time": "2026-11-21T08:00:00+00:00" }
  }
}
```

`update` has the same shape with `"operation_type": "update"` (and the cloud `base_row_version`). `delete` → `rejected` / `delete_not_allowed`.

**`doctor_blocked_date` — create / update / delete**

```json
{
  "operation_id": "…", "aggregate": "doctor_blocked_date", "entity_id": "<blocked date row id>", "operation_type": "create",
  "actor_user_id": "<doctor's users.id>", "base_row_version": null, "created_at": "2026-11-21T08:00:00+00:00",
  "payload": { "data": { "id": "<row id>", "doctor_id": "<doctor_id>", "blocked_date": "2026-12-24", "reason": "Conference", "created_at": "2026-11-21T08:00:00+00:00" } }
}
```

`update`: same shape, `"operation_type": "update"`, new `reason` (field merge on `reason`). `delete`: `"operation_type": "delete"`, `"payload": { "data": null }` (a thin payload is fine — the cloud row decides which doctor it belongs to); result `applied`, or `duplicate` if it is already gone.

**Node clock.** Because the merge compares node and cloud times, node clocks should be NTP-synced; a node time more than 5 minutes ahead of the cloud is clamped to "now".

<!-- JJQR - END -->
---

## 6. Cloud-owned (pull-only) aggregates — cloud wins

Pushing any of these returns `rejected` / `cloud_owned` — except the three doctor self-edit aggregates marked below (§5.7). They arrive through pull; treat the local copy as a read-only cache.

| aggregate | data |
|---|---|
| `clinic_setting` | `clinic_settings` row (id 1) |
| `clinic_operating_hour`, `clinic_accepted_payment_method` | rows |
| `service`, `doctor_service` | rows |
<!-- JJQR - AI (Nov 21, 2026) doctor self-edit -->
| `doctor`, `doctor_schedule`, `doctor_blocked_date` | rows. **Self-editable from a node** with a validated `actor_user_id` (§5.7); a push without one is `rejected` / `actor_required`. `doctor` create/delete and schedule delete stay cloud-only. |
<!-- JJQR - END -->
| `staff_account` | row. Cloud-owned; a doctor's own `full_name` / `contact_number` travel inside the `doctor` push (§5.7). |
| `user` | `users` row **including `password_hash`** for staff / doctor / admin accounts only (offline login). Status changes (deactivation) arrive here. |
<!-- JJQR - AI (Nov 21, 2026) profile is pull-only (cloud-owned) -->
| `profile` | `id`, `created_at`, `role`. Staff / doctor / admin profiles only — patient-portal profiles are never sent. |
<!-- JJQR - END -->
| `vital_field_template`, `medicine`, `lab_test_catalog` | rows |
| `soap_phrase`, `soap_template`, `medical_certificate_template`, `doctor_diagnosis_template`, `prescription_template` (with `items`) | rows |
<!-- JJQR - AI (Nov 21, 2026) lookup catalogs -->
| `icd10_code` | WHO ICD-10 catalog, ~12.2k rows, `entity_id` = the code (e.g. `J45.9`): `code`, `description`, `level`, `is_leaf`. Page with `limit=1000`. |
| `doctor_favorite_medicine` | `id`, `doctor_id`, `medicine_id`, `generic_name`, `dosage`, `quantity`, `instruction`, `sort_order`, `created_at` |
| `doctor_builtin_template_copy` | `id`, `doctor_id`, `kind`, `builtin_key`, `copied_at` (which built-in templates a doctor already copied) |
| `announcement` | `id`, `title`, `body`, `is_active`, `posted_by_user_id`, `created_at`, `updated_at` |
| `doctor_day_status` | `id`, `doctor_id`, `status_date`, `status`, `running_late_minutes`, `created_at`, `updated_at` |

Field lists of the catalog aggregates (snake_case, `data` = one row; `entity_id` = primary key, composite keys joined with `:`): `medicine` (`medicine_id`, `generic_name`, `created_at`) · `lab_test_catalog` (`lab_test_id`, `name`, `is_default`, `sort_order`, `created_at`) · `vital_field_template` (`template_id`, `form_key`, `description`, `unit`, `icon`, `is_default`, `created_at`) · `service` (`service_id`, `name`, `category`, `description`, `price`, `is_active`, …) · `doctor_service` (`doctor_id`, `service_id`, `duration_minutes`, `created_at`) · `soap_phrase` (`id`, `doctor_id`, `field`, `label`, `body`, `sort_order`, `source_builtin_key`, …) · `soap_template` (`id`, `doctor_id`, `title`, `is_system_template`, `chief_complaint`, `subjective`, `objective`, `assessment`, `plan`, `source_builtin_key`, …) · `medical_certificate_template` (`id`, `doctor_id`, `title`, `is_system_template`, `diagnosis_text`, `recommendations`, `purpose_exception`, `source_builtin_key`, …) · `doctor_diagnosis_template` (`id`, `doctor_id`, `label`, `icd10_code`, `body`, `source_builtin_key`, …) · `prescription_template` (`template_id`, `doctor_id`, `title`, `is_system_template`, `source_builtin_key`, … + `items`: `id`, `template_id`, `medicine_id`, `generic_name`, `dosage`, `quantity`, `instruction`, `is_controlled_substance`, `timing`, `meal_relation`, `duration_kind`, `duration_value`, `indication`, `route`, `as_needed`, `created_at`).

On a database that existed before a catalog was added to sync (or whose rows were inserted without going through the API), the cloud logs every entity that has no change row at start-up, so a first pull (`since` empty) always returns the whole catalog.
<!-- JJQR - END -->

Pull also carries the pushable aggregates (§5) as changed on the cloud (online bookings, web edits, other writers), in the same shapes as push.

---

## 7. Sync columns on the cloud (FYI)

On `patients`, `bookings`, `payments`, `consultations` and their children, `patient_vital_readings`, `audit_logs`:

| column | type | meaning |
|---|---|---|
| `row_version` | bigint, default 0 | Incremented on every committed change (web or sync). The value the node sends back as `base_row_version`. |
| `deleted_at` | datetimeoffset null | Tombstone marker. |
| `origin_node_id` | uniqueidentifier null | Node that last wrote the row (null = web). For payments, the owning node. |

New tables: `sync_nodes`, `sync_operations` (idempotency log), `sync_changes` (change log behind the cursor).

The node may keep the same columns locally; ClinicNode reuses `ClinicAppDbContext`, so after the backend change they exist in the node's SQLite file too (`EnsureCreated`). The node's own `node_sync_*` tables are unaffected.

---

## 8. Client-supplied ids on REST creates

<!-- JJQR - AI (Nov 21, 2026) §8 as implemented -->
Create endpoints accept an optional client id. Missing/empty → server generates one (unchanged). Supplied and new → used.
- **Bookings** (`POST /api/bookings`, `/walk-in`, `/book`) and **queue check-in** (`POST /api/queue`): `booking_id`, `payment_id`. Same `booking_id` again for the **same patient** → 200 with the existing booking/ticket (safe retry); other patient, or a taken `payment_id` → **409** `{ "error": "id_conflict" }`.
- **Single-row creates** → 409 `id_conflict` if the id is taken: consultation upsert `consultation_id`, follow-up `id`, medical certificate `certificate_id`, Rx group `group_id`, Rx template `template_id`, favorite medicine `id`, vaccination `id`, review `review_id`, privacy data request `id`, SOAP phrase/template `id`, diagnosis template `id`, certificate template `id`, and entity-body creates (announcement `id`, service `service_id`, doctor schedule / blocked date `id`). Patients: `patient_id` (unchanged).
- **Replace-all child lists** (diagnoses, Rx line items/template items, lab orders, vaccinations in the consultation save, vital readings): each item's `id` / `lab_order_id` is kept; if it is already taken by another row a new id is issued (no error).
- Reschedule (`PUT /api/bookings/{id}/reschedule`): optional `new_booking_id`, `new_payment_id` for the new booking/payment. The same request again (same ids, same patient and date) → 200 with the earlier result; an id used by anything else → 409 `id_conflict`.
- Allergy replace (`PUT /api/patients/{id}/allergies`): each item may carry `id` (kept unless already used → new id).
- Not yet: patient file uploads, auth register/invite.
<!-- JJQR - END -->

---

## 9. Recommended client loop

1. `POST /api/sync/token` if no token / expired.
2. `GET /api/sync/handshake` (optional per cycle; required after app update).
3. Push pending outbox rows in `created_at` order, ≤ 200 per call. Ack per §3.3.
4. Pull from the stored cursor until `has_more` is false; apply each page + store `next_cursor` in one local transaction; set `last_successful_sync_at = server_time`.
5. Backoff on failure (1 s, 2 s, 4 s … cap 5 min, jitter).

## 10. Not in v1

Queue-number reconciliation for online bookings made during an outage (they keep the cloud number; the clinic rule is still open), patient documents/files, refunds offline (forbidden by design).

## 11. Changelog

- v1 draft — 21 Nov 2026: first contract.
<!-- JJQR - AI (Nov 21, 2026) doctor self-edit -->
- v1.3 — 21 Nov 2026: **doctor self-edits pushable** (§5.7): `doctor`, `doctor_schedule`, `doctor_blocked_date` with the new op field `actor_user_id` and optional `payload.field_updated_at`; per-field newest-edit-wins merge; consultation fee and admin fields (license / PTR / S2 numbers, slot duration, slot capacity, daily patient limit) stay cloud-owned for a doctor actor; new reason codes `actor_required`, `actor_unknown`, `actor_not_allowed`, `field_not_allowed`, `cloud_field_newer`, `natural_key_remapped`; full audit of accepted and rejected edits. `profile` unchanged (cloud-owned). Cloud web edits of these rows are now stamped per field. Additive: nodes that never send these aggregates are unaffected.
<!-- JJQR - END -->
<!-- JJQR - AI (Nov 21, 2026) changes made while implementing -->
<!-- JJQR - AI (Nov 21, 2026) queue numbers + catalogs -->
- v1.2 — 21 Nov 2026: **queue-number reconciliation** for node vs online bookings (§5.2; new reason `queue_number_reassigned`); **pull-only catalogs added**: `icd10_code`, `doctor_favorite_medicine`, `doctor_builtin_template_copy`, `announcement`, `doctor_day_status` (§6); the start-up change-log backfill is now per entity (it previously skipped an aggregate that already had any change row, which left seed rows such as part of `medicine` out of a first pull).
<!-- JJQR - AI (Nov 21, 2026) contract clarifications for the node client -->
- v1.2.1 — 21 Nov 2026: §6 lists `profile` (`id`, `created_at`, `role`; cloud-owned, pull-only; patient-portal profiles are never sent). Allergy pushes are `operation_type: "update"` with `changed_fields: ["allergies"]` (§5.1). Amendments are `operation_type: "update"` with `consultation.status = "Amended"` — there is no `"amend"` verb (§5.4).
<!-- JJQR - END -->
<!-- JJQR - END -->
<!-- JJQR - AI (Nov 21, 2026) part B -->
- v1.1 — 21 Nov 2026: **amendments** pushable (§5.4: `stale_amendment`, `payment_settled`); **allergies** travel in the patient document with union-merge (§5.1: `allergy_union`); reschedule accepts `new_booking_id` / `new_payment_id` (§8). No change to the protocol header, endpoints or existing statuses.
<!-- JJQR - END -->
- v1 — 21 Nov 2026 (implemented):
  - Snapshot field names = the REST JSON names (snake_case of the property, e.g. `icd10_code`); the DB column name (`icd10code`) is also accepted on push.
  - Consultation children: key **absent** = keep the cloud set; `null`/`[]` = remove all.
  - Pull holds back changes younger than ~2 s (`Sync:PullSettleSeconds`) so a slower in-flight write is never skipped by the cursor.
  - `origin_node_id`: on the entity = first node that wrote it (payment = owner node); on a pull change = the last writer (null = cloud).
  - Pull `entity_id` is always a string (composite keys joined with `:`, e.g. doctor_service `doctorId:serviceId`).
  - New reason codes: `illegal_transition` (payment), `cloud_edit_overwritten` (payment applied over a cloud edit; flagged in the audit log), `invalid_operation` (operation_id reused by another node, or unknown type).
  - Node tokens are refused (401) outside `/api/sync/*`.
  - §8 rewritten to match what was built.
  - Node helper: `ClinicApp.Infrastructure.Sync.SyncDocuments.BuildAsync(db, aggregate, id)` builds the exact push snapshot from a local DB.
<!-- JJQR - END -->

<!-- JJQR - END -->

<!-- JJQR - AI (21 Nov 2026) -->
**Update (queue numbers, supersedes the renumber rules in §5.2):** a node's printed queue number is never reassigned. A clash with any other booking of the same day returns `applied` + `reason_code: "queue_number_clash"` and writes an audit row `Queue number clash` on both bookings for desk review. `queue_number_reassigned` only occurs when the node sent no number.
<!-- JJQR - END -->

<!-- JJQR - AI (21 Nov 2026) -->
**Queue numbers, final wording:** `reason_code: "queue_number_clash"` (status `applied`) = the printed number was kept as-is, BOTH bookings are flagged with a `Queue number clash` audit row, nothing is renumbered, and the desk must NOT reprint the slip. The renumbering text in §5.2 above (lines about `queue_number_reassigned` for a live holder or another node) is obsolete. `queue_number_reassigned` now only applies when the node sent no number.
<!-- JJQR - END -->

<!-- JJQR - AI (21 Nov 2026) -->
**Consistency pass (21 Nov 2026), checked against the code:**
- Add to the `reason_code` list: `or_number_clash` (payment accepted as printed, both flagged) and `queue_number_clash` (booking accepted with the printed number, both flagged, no renumber, no reprint). `queue_number_reassigned` only when the node sent no queue number.
- Pushable aggregates now also include `patient_consent` (append-only; delete = `append_only`; replay = `duplicate`) and `doctor_day_status` (Admin / Staff / that doctor; newest edit wins per field; delete rejected `delete_not_allowed`). `patient_lab_value`, `patient_document`, `patient_lab_result` remain pull-only.
- Queue blocks: `POST /api/sync/queue-blocks` (sync-node auth, `X-Sync-Protocol`), `size` default 20 clamped 5-50, no date-window check.
<!-- JJQR - END -->
- 22 Nov 2026 (offline-mode plan step 0.3): `POST /api/patients` with a client `patient_id` is now safe to repeat: the same id for the same person (first name, last name, birth date) returns **200** with the existing patient; the same id for a different person is **409 `id_conflict`** (it used to be a 500). `POST /api/queue` already behaved this way for `booking_id`. The desktop now makes `booking_id`, `payment_id` (walk-in check-in) and `patient_id` (quick registration) itself before the call.
