//JJQR - AI (Nov 21, 2026)
// Aggregate snapshots (push, contract §3.1/§5) and applying pulled / server-returned aggregates
// to the local SQLite copy with the node-side conflict rules:
//   consultation  node wins; a finished (Completed/Amended) consultation is immutable locally
//   payment       single writer: the node's payment is never overwritten by a cloud edit (flagged);
//                 only an online admin refund (cloud Refunded) is taken
//   vital_reading, audit_log   append-only: insert if absent, never update/delete
//   patient       node's pending edits are kept (the cloud field-merges our push, §5.1);
//                 a replaced MF-T-/taken patient code is applied and put on the review list
//   booking       BookingStatusMachine decides: keep the furthest-progressed legal state
//   settings, templates, catalogs, accounts (incl. password hashes)   cloud wins
using System.Text.Json;
using System.Text.Json.Nodes;
using ClinicApp.Auth;
using ClinicApp.Domain;
using ClinicApp.Domain.Entities;
using ClinicApp.Domain.Enums;
using ClinicApp.Infrastructure;
using ClinicNode.Data;
using ClinicNode.Sync.Wire;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using static ClinicNode.Sync.Local.EntityRowCodec;

namespace ClinicNode.Sync.Local;

public enum ApplyMode
{
    /// <summary>A change from GET /api/sync/pull: node-side conflict rules apply.</summary>
    Pull,
    /// <summary>The cloud's <c>current</c> copy from a push result (merged/conflict): the server already decided.</summary>
    ServerResult,
}

public static class Aggregates
{
    public const string Patient = "patient";
    public const string Booking = "booking";
    public const string Payment = "payment";
    public const string Consultation = "consultation";
    public const string VitalReading = "vital_reading";
    public const string AuditLog = "audit_log";
    public const string User = "user";
    public const string StaffAccount = "staff_account";
    public const string PrescriptionTemplate = "prescription_template";
    //JJQR - AI (21 Nov 2026) - pushable append-only consents (contract: data = {id, patient_id, consent_version, decision, decided_at}) and
    // the pull-only patient records the feed now carries (lab values, lab-result / document METADATA; files stay online)
    public const string PatientConsent = "patient_consent";
    public const string PatientLabValue = "patient_lab_value";
    public const string PatientLabResult = "patient_lab_result";
    public const string PatientDocument = "patient_document";
    //JJQR - END

    /// <summary>Contract §6: pull-only, cloud wins.</summary>
    public static readonly IReadOnlyDictionary<string, Type> CloudOwned = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
    {
        //JJQR - AI (Nov 21, 2026) - backend SyncAggregates.PullOnly also emits "profile" (not in contract §6 yet): user rows reference it
        ["profile"] = typeof(Profile),
        //JJQR - END
        //JJQR - AI (Nov 21, 2026) - catalogs/reference rows the backend pulls too (icd10 is ~12k rows)
        ["icd10_code"] = typeof(Icd10Code),
        ["doctor_favorite_medicine"] = typeof(DoctorFavoriteMedicine),
        ["doctor_builtin_template_copy"] = typeof(DoctorBuiltinTemplateCopy),
        ["announcement"] = typeof(Announcement),
        ["doctor_day_status"] = typeof(DoctorDayStatus),
        //JJQR - END
        ["clinic_setting"] = typeof(ClinicSetting),
        ["clinic_operating_hour"] = typeof(ClinicOperatingHour),
        ["clinic_accepted_payment_method"] = typeof(ClinicAcceptedPaymentMethod),
        ["service"] = typeof(ClinicApp.Domain.Entities.Service),
        ["doctor_service"] = typeof(DoctorService),
        [User] = typeof(User),
        [StaffAccount] = typeof(ClinicApp.Domain.Entities.StaffAccount),
        ["doctor"] = typeof(Doctor),
        ["doctor_schedule"] = typeof(DoctorSchedule),
        ["doctor_blocked_date"] = typeof(DoctorBlockedDate),
        ["vital_field_template"] = typeof(VitalFieldTemplate),
        ["medicine"] = typeof(Medicine),
        ["lab_test_catalog"] = typeof(LabTestCatalog),
        ["soap_phrase"] = typeof(SoapPhrase),
        ["soap_template"] = typeof(SoapTemplate),
        ["medical_certificate_template"] = typeof(MedicalCertificateTemplate),
        ["doctor_diagnosis_template"] = typeof(DoctorDiagnosisTemplate),
        [PrescriptionTemplate] = typeof(ClinicApp.Domain.Entities.PrescriptionTemplate),
        //JJQR - AI (21 Nov 2026) - pull-only patient rows (cloud wins; never pushed). patient_consent is NOT here: it is append-only + pushable.
        [PatientLabValue] = typeof(ClinicApp.Domain.Entities.PatientLabValue),
        [PatientLabResult] = typeof(ClinicApp.Domain.Entities.PatientLabResult),
        [PatientDocument] = typeof(ClinicApp.Domain.Entities.PatientDocument),
        //JJQR - END
    };

    private static readonly string[] ApplyOrder =
    {
        "profile", User, StaffAccount, "doctor", "icd10_code", "service", "doctor_service", "doctor_schedule", "doctor_blocked_date",
        "clinic_setting", "clinic_operating_hour", "clinic_accepted_payment_method",
        "vital_field_template", "medicine", "lab_test_catalog", "soap_phrase", "soap_template",
        "medical_certificate_template", "doctor_diagnosis_template", PrescriptionTemplate,
        "doctor_favorite_medicine", "doctor_builtin_template_copy", "announcement", "doctor_day_status",
        Patient, Booking, Payment, Consultation, VitalReading, AuditLog,
        //JJQR - AI (21 Nov 2026) - children of patient (after it, so the patient row exists first)
        PatientConsent, PatientLabValue, PatientLabResult, PatientDocument,
        //JJQR - END
    };

    /// <summary>Parents before children for upserts; reverse for deletes.</summary>
    public static int Rank(string aggregate)
    {
        var i = Array.FindIndex(ApplyOrder, a => string.Equals(a, aggregate, StringComparison.OrdinalIgnoreCase));
        return i < 0 ? ApplyOrder.Length : i;
    }
}

public static class AggregateDocuments
{
    // ── push snapshots ────────────────────────────────────────────────────

    //JJQR - AI (Nov 21, 2026) - reuse the backend's ClinicApp.Infrastructure.Sync.SyncDocuments.BuildAsync (reachable
    // through ClinicNode.Data's project reference, no edits outside Sync), so push snapshots are exactly the shape the
    // cloud emits on pull and accepts on push: REST JSON names, children keys always present (null / [] = remove).
    /// <summary>Full snapshot for <c>payload.data</c>, from the current local rows. Null if the row is gone.</summary>
    public static async Task<JsonNode?> BuildSnapshotAsync(ClinicAppDbContext db, SyncOutboxEntry e, CancellationToken ct = default)
    {
        var agg = e.Aggregate.ToLowerInvariant();
        if (agg is Aggregates.Patient or Aggregates.Booking or Aggregates.Payment or Aggregates.Consultation or Aggregates.VitalReading or Aggregates.AuditLog
            or Aggregates.PatientConsent) //JJQR - AI (21 Nov 2026) - backend builds the consent row ({id, patient_id, consent_version, decision, decided_at}) //JJQR - END
        {
            var doc = await ClinicApp.Infrastructure.Sync.SyncDocuments.BuildAsync(db, agg, e.EntityId.ToString("D"), ct);
            return doc is null ? null : JsonSerializer.SerializeToNode(doc.Data, ClinicApp.Infrastructure.Sync.SyncJson.Options);
        }
        // Unknown to this client: forward the raw payload; the cloud answers rejected/unknown_aggregate.
        try { return JsonNode.Parse(e.PayloadJson); } catch (JsonException) { return null; }
    }
    //JJQR - END

    // ── applying cloud copies ─────────────────────────────────────────────

    /// <summary>
    /// Applies one aggregate change and saves. Returns false when the change was skipped by a rule.
    /// Throws on data errors (caller rolls back to a savepoint and records apply_failed).
    /// </summary>
    public static async Task<bool> ApplyAsync(ClinicAppDbContext db, PulledChange ch, ApplyMode mode, CancellationToken ct = default)
    {
        var agg = ch.Aggregate.ToLowerInvariant();
        var data = ch.Data as JsonObject;
        switch (agg)
        {
            case Aggregates.Patient: return await ApplyPatientAsync(db, ch, data, mode, ct);
            case Aggregates.Booking: return await ApplyBookingAsync(db, ch, data, mode, ct);
            case Aggregates.Payment: return await ApplyPaymentAsync(db, ch, data, mode, ct);
            case Aggregates.Consultation: return await ApplyConsultationAsync(db, ch, data, mode, ct);
            case Aggregates.VitalReading: return await ApplyAppendOnlyAsync(db, typeof(PatientVitalReading), ch, data, mode, ct);
            case Aggregates.AuditLog: return await ApplyAppendOnlyAsync(db, typeof(AuditLog), ch, data, mode, ct);
            //JJQR - AI (21 Nov 2026)
            case Aggregates.PatientConsent:
                if (!await LocalTableExistsAsync(db, typeof(PatientConsent), ch, ct)) return false;
                return await ApplyAppendOnlyAsync(db, typeof(PatientConsent), ch, data, mode, ct);
            //JJQR - END
        }
        if (Aggregates.CloudOwned.TryGetValue(agg, out var clr))
        {
            //JJQR - AI (21 Nov 2026) - a node DB without the table (old file, schema repair not run) records "local_table_missing" instead of failing the page
            if (agg is Aggregates.PatientLabValue or Aggregates.PatientLabResult or Aggregates.PatientDocument
                && !await LocalTableExistsAsync(db, clr, ch, ct)) return false;
            //JJQR - END
            return await ApplyCloudOwnedAsync(db, clr, ch, data, ct);
        }

        await SyncLocalStore.AddConflictAsync(db, new SyncConflict
        {
            Aggregate = ch.Aggregate, EntityId = ch.EntityId, Kind = "skipped", ReasonCode = "unknown_aggregate",
            Message = $"This node does not know aggregate '{ch.Aggregate}'; change ignored.",
            CloudJson = ch.Data?.ToJsonString(),
        }, ct);
        return false;
    }

    //JJQR - AI (Nov 21, 2026) - cloud wins on a natural-key clash: a local row with a DIFFERENT id but the same unique-index values
    // (e.g. a demo-seeded doctor_builtin_template_copy) is removed before the cloud row is inserted. Not for accounts/doctors (FKs).
    private static async Task RemoveUniqueClashesAsync(ClinicAppDbContext db, Type clr, JsonObject row, CancellationToken ct)
    {
        if (clr == typeof(User) || clr == typeof(ClinicApp.Domain.Entities.StaffAccount) || clr == typeof(Doctor)) return;
        var et = EntityType(db, clr);
        var pk = et.FindPrimaryKey()!;
        foreach (var index in et.GetIndexes().Where(i => i.IsUnique && i.Properties.Count > 0 && !i.Properties.SequenceEqual(pk.Properties)))
        {
            var idx = Index(row);
            var values = new List<(IProperty Prop, object? Value)>();
            foreach (var p in index.Properties)
            {
                if (!(idx.TryGetValue(Norm(ColumnName(p)), out var node) || idx.TryGetValue(Norm(p.Name), out node)) || node is null) { values.Clear(); break; }
                values.Add((p, Convert(node, p.ClrType)));
            }
            if (values.Count == 0) continue;
            var param = System.Linq.Expressions.Expression.Parameter(clr, "e");
            System.Linq.Expressions.Expression? body = null;
            foreach (var (p, v) in values)
            {
                var eq = System.Linq.Expressions.Expression.Equal(System.Linq.Expressions.Expression.Property(param, p.Name),
                    System.Linq.Expressions.Expression.Convert(System.Linq.Expressions.Expression.Constant(v), p.ClrType));
                body = body is null ? eq : System.Linq.Expressions.Expression.AndAlso(body, eq);
            }
            var key = KeyFromJson(et, row);
            var clashes = await LoadByPredicateAsync(db, clr, param, body!, ct);
            foreach (var c in clashes)
            {
                var ck = pk.Properties.Select(p => db.Entry(c).Property(p.Name).CurrentValue!).ToArray();
                if (key is null || !ck.SequenceEqual(key)) db.Remove(c);
            }
        }
    }

    private static readonly System.Reflection.MethodInfo LoadByPredicateOpen =
        typeof(AggregateDocuments).GetMethod(nameof(LoadByPredicateGeneric), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

    private static async Task<List<object>> LoadByPredicateAsync(ClinicAppDbContext db, Type clr, System.Linq.Expressions.ParameterExpression param, System.Linq.Expressions.Expression body, CancellationToken ct) =>
        await (Task<List<object>>)LoadByPredicateOpen.MakeGenericMethod(clr).Invoke(null, new object[] { db, System.Linq.Expressions.Expression.Lambda(body, param), ct })!;

    private static async Task<List<object>> LoadByPredicateGeneric<T>(ClinicAppDbContext db, System.Linq.Expressions.LambdaExpression lambda, CancellationToken ct) where T : class =>
        (await db.Set<T>().Where((System.Linq.Expressions.Expression<Func<T, bool>>)lambda).ToListAsync(ct)).Cast<object>().ToList();
    //JJQR - END

    //JJQR - AI (Nov 21, 2026) - batched cloud-owned upserts: one query to load existing rows, one SaveChanges, one multi-row version write
    public static bool IsBulkable(PulledChange ch)
    {
        if (ch.IsDelete || ch.Data is not JsonObject) return false;
        if (!Aggregates.CloudOwned.TryGetValue(ch.Aggregate.ToLowerInvariant(), out var clr)) return false;
        if (clr == typeof(User) || clr == typeof(ClinicApp.Domain.Entities.PrescriptionTemplate)) return false;
        //JJQR - AI (21 Nov 2026) - patient children need their patient first (bulk runs before the ordered per-change path)
        if (clr == typeof(PatientLabValue) || clr == typeof(PatientLabResult) || clr == typeof(PatientDocument)) return false;
        //JJQR - END
        return clr.GetProperties().Length > 0; // key shape checked in ApplyBulkAsync (single-column keys only)
    }

    /// <summary>Applies a run of changes of ONE cloud-owned aggregate in one go. Returns the changes it applied; empty if the key shape
    /// is not bulkable (composite key) - the caller then uses the per-change path. Throws on data errors (caller rolls back to its savepoint).</summary>
    public static async Task<int> ApplyBulkAsync(ClinicAppDbContext db, string aggregate, IReadOnlyList<PulledChange> changes, CancellationToken ct = default)
    {
        var clr = Aggregates.CloudOwned[aggregate.ToLowerInvariant()];
        var et = EntityType(db, clr);
        var pk = et.FindPrimaryKey()!;
        if (pk.Properties.Count != 1) return -1;
        var keyProp = pk.Properties[0];

        // last change per key wins (changes arrive in cursor order)
        var byKey = new Dictionary<string, (object Key, JsonObject Row)>();
        foreach (var ch in changes)
        {
            var row = (JsonObject)((JsonObject)ch.Data!).DeepClone();
            var key = KeyFromJson(et, row) ?? KeyFromId(et, ch.EntityId) ?? throw new InvalidDataException($"{aggregate} change without key");
            byKey[key[0].ToString()!.ToLowerInvariant()] = (key[0], row);
        }

        var existing = new Dictionary<string, object>();
        foreach (var part in byKey.Values.Select(v => v.Key).Chunk(500))
            foreach (var e in await LoadByKeysAsync(db, clr, keyProp, part, ct))
                existing[db.Entry(e).Property(keyProp.Name).CurrentValue!.ToString()!.ToLowerInvariant()] = e;

        var modified = new List<(object Entity, JsonObject Row)>();
        var hasExtraUnique = et.GetIndexes().Any(i => i.IsUnique && !i.Properties.SequenceEqual(pk.Properties));
        foreach (var (k, (key, row)) in byKey)
        {
            if (existing.TryGetValue(k, out var e)) { Apply(db, e, row, isNew: false); modified.Add((e, row)); }
            else
            {
                if (hasExtraUnique) { await RemoveUniqueClashesAsync(db, clr, row, ct); await db.SaveChangesAsync(ct); }
                var created = Activator.CreateInstance(clr)!; Apply(db, created, row, isNew: true); db.Add(created);
            }
        }
        await db.SaveChangesAsync(ct);
        foreach (var (entity, row) in modified.Where(m => m.Entity is IHasUpdatedAt))
            await RestoreUpdatedAtAsync(db, entity, UpdatedAtFromJson(row), ct);
        return byKey.Count;
    }

    private static readonly System.Reflection.MethodInfo LoadByKeysOpen =
        typeof(AggregateDocuments).GetMethod(nameof(LoadByKeysGeneric), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

    private static async Task<List<object>> LoadByKeysAsync(ClinicAppDbContext db, Type clr, Microsoft.EntityFrameworkCore.Metadata.IProperty keyProp, object[] keys, CancellationToken ct) =>
        await (Task<List<object>>)LoadByKeysOpen.MakeGenericMethod(clr).Invoke(null, new object[] { db, keyProp.Name, keyProp.ClrType, keys, ct })!;

    private static async Task<List<object>> LoadByKeysGeneric<T>(ClinicAppDbContext db, string property, Type keyType, object[] keys, CancellationToken ct) where T : class
    {
        var array = Array.CreateInstance(keyType, keys.Length);
        for (var i = 0; i < keys.Length; i++) array.SetValue(keys[i], i);
        var param = System.Linq.Expressions.Expression.Parameter(typeof(T), "e");
        var member = System.Linq.Expressions.Expression.Property(param, property);
        var contains = System.Linq.Expressions.Expression.Call(typeof(Enumerable), nameof(Enumerable.Contains), new[] { keyType },
            System.Linq.Expressions.Expression.Constant(array), member);
        var predicate = System.Linq.Expressions.Expression.Lambda<Func<T, bool>>(contains, param);
        return (await db.Set<T>().Where(predicate).ToListAsync(ct)).Cast<object>().ToList();
    }
    //JJQR - END

    // cloud wins (§6), including users + password hashes for offline login
    private static async Task<bool> ApplyCloudOwnedAsync(ClinicAppDbContext db, Type clr, PulledChange ch, JsonObject? data, CancellationToken ct)
    {
        var et = EntityType(db, clr);
        if (ch.IsDelete)
        {
            var key = KeyFromId(et, ch.EntityId) ?? (data is null ? null : KeyFromJson(et, data));
            if (key is null) return false;
            var existing = await FindAsync(db, clr, key, ct);
            if (existing is null) return false;
            if (existing is User u)
            {
                // Never hard-delete an account row (FKs from profiles/staff/audit); make it unusable.
                u.PasswordHash = "";
                foreach (var s in await db.StaffAccounts.Where(s => s.UserId == u.Id).ToListAsync(ct))
                    s.Status = StaffStatus.Inactive;
            }
            else
            {
                db.Remove(existing);
            }
            await db.SaveChangesAsync(ct);
            return true;
        }

        if (data is null) return false;
        JsonArray? items = null;
        var row = data;
        if (clr == typeof(ClinicApp.Domain.Entities.PrescriptionTemplate) && data["items"] is JsonArray arr)
        {
            items = (JsonArray)arr.DeepClone();
            row = (JsonObject)data.DeepClone();
            row.Remove("items");
        }

        // Local lockout counters belong to this node (wrong passwords typed here).
        ISet<string>? keep = clr == typeof(User)
            ? new HashSet<string> { nameof(User.FailedLoginCount), nameof(User.LockedUntil) }
            : null;
        //JJQR - AI (Nov 21, 2026)
        if (await FindAsync(db, clr, KeyFromJson(et, row) ?? [], ct) is null) { await RemoveUniqueClashesAsync(db, clr, row, ct); await db.SaveChangesAsync(ct); }
        //JJQR - END
        var (entity, _) = await UpsertAsync(db, clr, row, ct, keep);
        await db.SaveChangesAsync(ct);
        await RestoreUpdatedAtAsync(db, entity, UpdatedAtFromJson(row), ct);
        if (items is not null)
        {
            var templateId = db.Entry(entity).Property(nameof(ClinicApp.Domain.Entities.PrescriptionTemplate.TemplateId)).CurrentValue!;
            await ReplaceSetAsync(db, typeof(PrescriptionTemplateItem), nameof(PrescriptionTemplateItem.TemplateId), templateId, items, ct);
            await db.SaveChangesAsync(ct);
        }
        return true;
    }

    //JJQR - AI (Nov 22, 2026) - step 4.4 (desktop side)
    private static async Task<bool> ApplyMergedAwayPatientAsync(ClinicAppDbContext db, PulledChange ch, CancellationToken ct)
    {
        var et = EntityType(db, typeof(Patient));
        if (KeyFromId(et, ch.EntityId) is not { } key) return false;
        //JJQR - AI (2026-11-26) - GAV-12: same rules as the merged upsert (RetireMergedPatientAsync); a delete carries no kept id, so nothing is moved here
        await RetireMergedPatientAsync(db, (Guid)key[0], MergedIntoOf(ch.Data as JsonObject), addConflict: true, ct);
        return true;
        //JJQR - END
    }
    //JJQR - END

    //JJQR - AI (2026-11-26) - GAV-12: "Merge to existing patient" (desktop side)
    public const string MergedKeptKind = "patient_merged_kept";

    static Guid? MergedIntoOf(JsonObject? data) =>
        data?["merged_into_patient_id"] is JsonValue v && v.TryGetValue<string>(out var s) && Guid.TryParse(s, out var g) && g != Guid.Empty ? g : null;

    /// <summary>The kept patient's id inside "merged_into_patient_id:{id}" (push result conflict_fields) or {"merged_into_patient_id": id} (a kept review item).</summary>
    public static Guid? KeptIdFrom(IEnumerable<string>? conflictFields, string? cloudJson = null)
    {
        foreach (var f in conflictFields ?? [])
            if (f.StartsWith("merged_into_patient_id:", StringComparison.OrdinalIgnoreCase) && Guid.TryParse(f["merged_into_patient_id:".Length..], out var g)) return g;
        if (!string.IsNullOrWhiteSpace(cloudJson))
            try { return MergedIntoOf(JsonNode.Parse(cloudJson) as JsonObject); } catch (JsonException) { }
        return null;
    }

    /// <summary>Moves this computer's rows of a merged-away patient to the kept patient (when the kept one is here), then removes the copy.
    /// Kept (and listed once as patient_merged_kept, unless <paramref name="addConflict"/> is false) while an unsent change of that patient is waiting.
    /// The moves are local only (never pushed): the server already moved the same rows. Returns true when the copy is gone.</summary>
    public static async Task<bool> RetireMergedPatientAsync(ClinicAppDbContext db, Guid goneId, Guid? keptId, bool addConflict, CancellationToken ct)
    {
        if (!await db.Patients.AnyAsync(p => p.PatientId == goneId, ct)) return true;
        if (keptId is Guid k && k != goneId && await db.Patients.AnyAsync(p => p.PatientId == k, ct))
        {
            await db.Bookings.Where(x => x.PatientId == goneId).ExecuteUpdateAsync(s => s.SetProperty(x => x.PatientId, k), ct);
            await db.Consultations.Where(x => x.PatientId == goneId).ExecuteUpdateAsync(s => s.SetProperty(x => x.PatientId, k), ct);
            await db.MedicalCertificates.Where(x => x.PatientId == goneId).ExecuteUpdateAsync(s => s.SetProperty(x => x.PatientId, k), ct);
            await db.PatientVitalReadings.Where(x => x.PatientId == goneId).ExecuteUpdateAsync(s => s.SetProperty(x => x.PatientId, k), ct);
            await db.FollowUps.Where(x => x.PatientId == goneId).ExecuteUpdateAsync(s => s.SetProperty(x => x.PatientId, k), ct);
            await db.LabOrders.Where(x => x.PatientId == goneId).ExecuteUpdateAsync(s => s.SetProperty(x => x.PatientId, k), ct);
            await db.PatientLabValues.Where(x => x.PatientId == goneId).ExecuteUpdateAsync(s => s.SetProperty(x => x.PatientId, k), ct);
            await db.PatientLabResults.Where(x => x.PatientId == goneId).ExecuteUpdateAsync(s => s.SetProperty(x => x.PatientId, k), ct);
            await db.PatientDocuments.Where(x => x.PatientId == goneId).ExecuteUpdateAsync(s => s.SetProperty(x => x.PatientId, k), ct);
            await db.PatientVaccinations.Where(x => x.PatientId == goneId).ExecuteUpdateAsync(s => s.SetProperty(x => x.PatientId, k), ct);
            await db.PrescriptionGroups.Where(x => x.PatientId == goneId).ExecuteUpdateAsync(s => s.SetProperty(x => x.PatientId, k), ct);
            await db.PatientConsents.Where(x => x.PatientId == goneId).ExecuteUpdateAsync(s => s.SetProperty(x => x.PatientId, k), ct);
            await db.DataRequests.Where(x => x.PatientId == goneId).ExecuteUpdateAsync(s => s.SetProperty(x => x.PatientId, k), ct);
            await db.Reviews.Where(x => x.PatientId == goneId).ExecuteUpdateAsync(s => s.SetProperty(x => x.PatientId, k), ct);
            // allergies: an allergen the kept patient already has here is not kept twice (the kept patient's own document replaces the list on its next pull)
            var have = (await db.PatientAllergies.AsNoTracking().Where(a => a.PatientId == k).Select(a => new { a.Allergen, a.IsNkda }).ToListAsync(ct))
                .Select(a => a.IsNkda ? "\u0000nkda" : (a.Allergen ?? "").Trim().ToLowerInvariant()).ToHashSet();
            foreach (var a in await db.PatientAllergies.Where(a => a.PatientId == goneId).ToListAsync(ct))
            {
                if (have.Contains(a.IsNkda ? "\u0000nkda" : (a.Allergen ?? "").Trim().ToLowerInvariant())) db.PatientAllergies.Remove(a);
                else a.PatientId = k;
            }
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
        }
        string? why = null;
        if (await SyncLocalStore.HasPendingAsync(db, Aggregates.Patient, goneId.ToString(), ct)) why = "a change to this patient has not been sent yet";
        else if (await db.Bookings.AnyAsync(b => b.PatientId == goneId, ct)) why = "this computer still has visits recorded under this patient";
        if (why is null)
        {
            try
            {
                var gone = await db.Patients.FirstOrDefaultAsync(p => p.PatientId == goneId, ct);
                if (gone is not null)
                {
                    db.PatientAllergies.RemoveRange(await db.PatientAllergies.Where(a => a.PatientId == goneId).ToListAsync(ct));
                    db.Patients.Remove(gone);
                    await db.SaveChangesAsync(ct);
                }
                return true;
            }
            catch (DbUpdateException) { db.ChangeTracker.Clear(); why = "other records on this computer still point to this patient"; }
        }
        // kept for now: hidden from the lists (merged_into set) and listed once
        await db.Patients.Where(p => p.PatientId == goneId).ExecuteUpdateAsync(s => s.SetProperty(p => p.MergedIntoPatientId, keptId), ct);
        if (addConflict)
            await SyncLocalStore.AddConflictAsync(db, new SyncConflict
            {
                Aggregate = Aggregates.Patient, EntityId = goneId.ToString(), Kind = MergedKeptKind,
                Message = $"This patient was merged into another record on the clinic server, but it was kept on this computer because {why}. It clears by itself after the next sync.",
                CloudJson = keptId is Guid kk ? new JsonObject { ["merged_into_patient_id"] = kk.ToString() }.ToJsonString() : null,
            }, ct);
        return false;
    }

    /// <summary>After every good sync: a merged-away patient kept earlier is retried; its review item clears by itself once the copy is gone.</summary>
    public static async Task<int> RetryMergedKeptAsync(ClinicAppDbContext db, CancellationToken ct)
    {
        var cleared = 0;
        foreach (var c in await SyncLocalStore.ListOpenConflictsAsync(db, 500, ct))
        {
            if (!string.Equals(c.Kind, MergedKeptKind, StringComparison.OrdinalIgnoreCase) || !Guid.TryParse(c.EntityId, out var goneId)) continue;
            if (await RetireMergedPatientAsync(db, goneId, KeptIdFrom(null, c.CloudJson), addConflict: false, ct)) { await SyncLocalStore.ResolveConflictAsync(db, c.Id, ct); cleared++; }
        }
        return cleared;
    }
    //JJQR - END

    private static async Task<bool> ApplyPatientAsync(ClinicAppDbContext db, PulledChange ch, JsonObject? data, ApplyMode mode, CancellationToken ct)
    {
        //JJQR - AI (Nov 22, 2026) - offline-mode plan step 4.4 (desktop side): the clinic server only ever deletes a patient when an admin MERGED them into another. Its records arrive first (a pull applies changes before deletes),
        // so the merged-away patient can leave this computer's lists. If anything here still points at it (an unsent change, a record not moved yet) it is kept and listed instead.
        if (ch.IsDelete) return await ApplyMergedAwayPatientAsync(db, ch, ct);
        //JJQR - END
        if (data is null) return false; // delete_not_allowed
        //JJQR - AI (2026-11-26) - GAV-12 why: the server sends a merged-away patient as an upsert carrying merged_into_patient_id (not a delete). This computer
        // moves its own rows of that patient to the kept record and then drops the copy (or keeps and lists it while a change here is still unsent).
        if (MergedIntoOf(data) is Guid keptId && KeyFromId(EntityType(db, typeof(Patient)), ch.EntityId) is { } goneKey)
        {
            await RetireMergedPatientAsync(db, (Guid)goneKey[0], keptId, addConflict: true, ct);
            return true;
        }
        //JJQR - END
        var et = EntityType(db, typeof(Patient));
        var key = KeyFromJson(et, data) ?? KeyFromId(et, ch.EntityId) ?? throw new InvalidDataException("patient without patient_id");
        var local = (Patient?)await FindAsync(db, typeof(Patient), key, ct);
        var id = (Guid)key[0];

        if (local is not null && mode == ApplyMode.Pull && await SyncLocalStore.HasPendingAsync(db, Aggregates.Patient, id.ToString(), ct))
            return false; // our pending push will be field-merged by the cloud; its result brings the merged copy

        var oldCode = local?.PatientCode;
        var newCode = data["patient_code"]?.GetValue<string>();
        ISet<string>? keep = null;
        if (newCode is not null && newCode != oldCode
            && await db.Patients.AnyAsync(p => p.PatientCode == newCode && p.PatientId != id, ct))
        {
            keep = new HashSet<string> { nameof(Patient.PatientCode) };
            await SyncLocalStore.AddConflictAsync(db, new SyncConflict
            {
                Aggregate = Aggregates.Patient, EntityId = id.ToString(), Kind = "conflict", ReasonCode = "patient_code_taken",
                Message = $"Cloud patient code {newCode} is already used by another local patient; local code {oldCode} kept.",
                CloudJson = data.ToJsonString(),
            }, ct);
        }

        var json = (JsonObject)data.DeepClone();
        //JJQR - AI (Nov 21, 2026) - contract v1.1 §5.1: allergies ride with the patient. Key ABSENT = keep the local list; null or [] clears it.
        var hasAllergies = json.ContainsKey("allergies");
        var allergies = json["allergies"] as JsonArray;
        json.Remove("allergies");
        //JJQR - END
        //JJQR - AI (21 Nov 2026) - patient document gained "vaccinations": the patient's vaccinations with NO consultation (consultation-linked ones ride in the consultation)
        var vaccinations = json["vaccinations"] as JsonArray;
        json.Remove("vaccinations");
        //JJQR - END
        if (local is null)
        {
            var (entity, _) = await UpsertAsync(db, typeof(Patient), json, ct);
            await db.SaveChangesAsync(ct);
            if (hasAllergies) await ReplaceAllergiesAsync(db, id, allergies, ct);
            if (vaccinations is not null) await UpsertStandaloneVaccinationsAsync(db, id, vaccinations, ct); //JJQR - AI (21 Nov 2026) //JJQR - END
            return true;
        }
        Apply(db, local, json, isNew: false, keep);
        await db.SaveChangesAsync(ct);
        await RestoreUpdatedAtAsync(db, local, UpdatedAtFromJson(json), ct);
        if (hasAllergies) await ReplaceAllergiesAsync(db, id, allergies, ct);
        if (vaccinations is not null) await UpsertStandaloneVaccinationsAsync(db, id, vaccinations, ct); //JJQR - AI (21 Nov 2026) //JJQR - END

        if (oldCode is not null && local.PatientCode != oldCode)
        {
            var temp = oldCode.StartsWith("MF-T-", StringComparison.OrdinalIgnoreCase);
            await SyncLocalStore.AddConflictAsync(db, new SyncConflict
            {
                Aggregate = Aggregates.Patient, EntityId = id.ToString(), Kind = "patient_code_replaced",
                ReasonCode = temp ? "temporary_code_replaced" : "patient_code_changed",
                Message = $"Patient code {oldCode} was replaced by the cloud with {local.PatientCode}. Reprint the patient card.",
                LocalJson = JsonSerializer.Serialize(new { patient_code = oldCode }),
                CloudJson = JsonSerializer.Serialize(new { patient_code = local.PatientCode }),
            }, ct);
            //JJQR - AI (Nov 21, 2026)
            await SyncLocalStore.AddReprintFlagAsync(db, new ReprintFlag
            {
                Kind = "patient_card", EntityId = id.ToString(), OldValue = oldCode, NewValue = local.PatientCode,
                Message = $"Patient code changed from {oldCode} to {local.PatientCode}: reprint the patient card.",
            }, ct);
            //JJQR - END
        }
        return true;
    }

    //JJQR - AI (Nov 21, 2026)
    /// <summary>
    /// Standalone vaccinations from the patient document: insert / update each cloud row (cloud wins). A LOCAL standalone row that the cloud list
    /// does not contain is NOT deleted here: the push engine ignores this key, so such a row may exist only on this computer (deleting it is an owner decision).
    /// </summary>
    private static async Task UpsertStandaloneVaccinationsAsync(ClinicAppDbContext db, Guid patientId, JsonArray items, CancellationToken ct)
    {
        if (!await TableExistsAsync(db, typeof(PatientVaccination), ct)) return;
        foreach (var o in items.OfType<JsonObject>())
        {
            var row = (JsonObject)o.DeepClone();
            row["patient_id"] = patientId.ToString();
            row["consultation_id"] = null;
            await UpsertAsync(db, typeof(PatientVaccination), row, ct);
            //JJQR - AI (21 Nov 2026) - pulled from the cloud → never POSTed back by StandaloneVaccinationPush
            if (Guid.TryParse(row["id"]?.ToString(), out var vid))
                await SyncLocalStore.MarkSweptAsync(db, SyncLocalStore.StandaloneVaccinationSweptKey, vid.ToString(), ct);
            //JJQR - END
        }
        await db.SaveChangesAsync(ct);
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> KnownTables = new(StringComparer.OrdinalIgnoreCase);

    private static async Task<bool> TableExistsAsync(ClinicAppDbContext db, Type clr, CancellationToken ct)
    {
        var table = EntityType(db, clr).GetTableName();
        if (string.IsNullOrEmpty(table)) return false;
        if (KnownTables.TryGetValue(table, out var known) && known) return true; // only a positive answer is cached (schema repair can add it later)
        var n = await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM sqlite_master WHERE type = 'table' AND name = {0}", table).SingleAsync(ct);
        if (n > 0) KnownTables[table] = true;
        return n > 0;
    }

    private static async Task<bool> LocalTableExistsAsync(ClinicAppDbContext db, Type clr, PulledChange ch, CancellationToken ct)
    {
        if (await TableExistsAsync(db, clr, ct)) return true;
        await SyncLocalStore.AddConflictAsync(db, new SyncConflict
        {
            Aggregate = ch.Aggregate, EntityId = ch.EntityId, Kind = "skipped", ReasonCode = "local_table_missing",
            Message = $"This computer's database has no table for '{ch.Aggregate}' yet; the change was skipped (restart the app to repair the schema).",
        }, ct);
        return false;
    }
    //JJQR - END

    private static async Task ReplaceAllergiesAsync(ClinicAppDbContext db, Guid patientId, JsonArray? items, CancellationToken ct)
    {
        // never keep NKDA next to a real allergen (the cloud already removes it; defend against any odd copy)
        if (items is not null && items.OfType<JsonObject>().Any(o => !(o["is_nkda"]?.GetValue<bool>() ?? false)))
        {
            var real = new JsonArray();
            foreach (var o in items.OfType<JsonObject>().Where(o => !(o["is_nkda"]?.GetValue<bool>() ?? false))) real.Add(o.DeepClone());
            items = real;
        }
        // each row belongs to this patient even if the copy omits patient_id
        if (items is not null)
            foreach (var o in items.OfType<JsonObject>())
            {
                o["patient_id"] = patientId.ToString();
                var stamp = DateTimeOffset.UtcNow.ToString("O");
                o["created_at"] ??= stamp; o["updated_at"] ??= stamp;
            }
        await ReplaceSetAsync(db, typeof(PatientAllergy), nameof(PatientAllergy.PatientId), patientId, items, ct);
        await db.SaveChangesAsync(ct);
    }
    //JJQR - END

    private static async Task<bool> ApplyBookingAsync(ClinicAppDbContext db, PulledChange ch, JsonObject? data, ApplyMode mode, CancellationToken ct)
    {
        if (ch.IsDelete || data is null) return false; // delete_not_allowed (cancel instead)
        var row = (data["booking"] as JsonObject ?? data).DeepClone().AsObject();
        var services = data["booking_services"] as JsonArray;
        var et = EntityType(db, typeof(Booking));
        var key = KeyFromJson(et, row) ?? KeyFromId(et, ch.EntityId) ?? throw new InvalidDataException("booking without booking_id");
        var id = (Guid)key[0];
        var local = (Booking?)await FindAsync(db, typeof(Booking), key, ct);

        var fullApply = true;
        ISet<string>? keep = null;
        if (local is not null && mode == ApplyMode.Pull)
        {
            var pending = await SyncLocalStore.HasPendingAsync(db, Aggregates.Booking, id.ToString(), ct);
            var cloud = (BookingStatus?)Convert(row["status"], typeof(BookingStatus)) ?? local.Status;
            var mine = local.Status;
            if (mine == cloud || BookingStatusMachine.CanTransition(mine, cloud))
            {
                // cloud is equal or further along: take its status; other fields only if we have nothing unpushed
                if (pending)
                {
                    local.Status = cloud;
                    fullApply = false;
                }
            }
            else if (BookingStatusMachine.CanTransition(cloud, mine))
            {
                // we are further along: keep our status (our push moves the cloud forward)
                keep = new HashSet<string> { nameof(Booking.Status) };
                fullApply = !pending;
                if (!fullApply) return false;
            }
            else
            {
                await SyncLocalStore.AddConflictAsync(db, new SyncConflict
                {
                    Aggregate = Aggregates.Booking, EntityId = id.ToString(), Kind = "illegal_transition", ReasonCode = "illegal_transition",
                    Message = $"Booking is {mine} on this computer but {cloud} in the cloud; neither can follow the other. Kept {mine} for review.",
                    LocalJson = ToJson(db, local).ToJsonString(), CloudJson = data.ToJsonString(),
                }, ct);
                return false;
            }
        }

        //JJQR - AI (21 Nov 2026) - HARD RULE (Jessie): a printed queue number is never renumbered. A pull or a push result (merged /
        // queue_number_reassigned / ServerResult) never overwrites a local booking's non-empty queue_number; the cloud's other number is listed
        // for review ("Queue number clash: flagged for admin"). A booking new to this computer, or one with no number yet, takes the cloud's number.
        if (local is not null && !string.IsNullOrWhiteSpace(local.QueueNumber))
        {
            var cloudQueue = row["queue_number"] is JsonValue qv && qv.TryGetValue<string>(out var q) ? q : null;
            if (row.ContainsKey("queue_number") && !string.Equals(cloudQueue, local.QueueNumber, StringComparison.Ordinal))
            {
                keep ??= new HashSet<string>();
                keep.Add(nameof(Booking.QueueNumber));
                if (!string.IsNullOrWhiteSpace(cloudQueue))
                    await SyncLocalStore.AddConflictAsync(db, new SyncConflict
                    {
                        Aggregate = Aggregates.Booking, EntityId = id.ToString(), Kind = SyncReasonCodes.QueueNumberClash, ReasonCode = SyncReasonCodes.QueueNumberClash,
                        Message = $"Queue number clash: flagged for admin. This computer kept the printed number {local.QueueNumber}; the clinic server has {cloudQueue}.",
                        LocalJson = JsonSerializer.Serialize(new { queue_number = local.QueueNumber }),
                        CloudJson = JsonSerializer.Serialize(new { queue_number = cloudQueue }),
                    }, ct);
            }
        }
        //JJQR - END

        object entity;
        //JJQR - AI (Nov 21, 2026) - contract §5.2: queue_number on apply.
        // (21 Nov 2026 update) A non-empty local (printed) number is never changed (keep rule above); only a booking with no number yet takes the
        // cloud's (queue_number_reassigned). The printed number stays valid, so nothing here asks for a slip to be printed again.
        var oldQueue = local?.QueueNumber;
        if (local is null) entity = (await UpsertAsync(db, typeof(Booking), row, ct)).Entity;
        else
        {
            entity = local;
            if (fullApply) Apply(db, local, row, isNew: false, keep);
        }
        await db.SaveChangesAsync(ct);
        //JJQR - END
        //JJQR - AI (21 Nov 2026) - REPRINT COPY: no "reprint the queue slip" flag after a queue-number clash. The printed number stays valid
        // (server reason_code queue_number_clash: number kept, both bookings audited, no renumber); the keep rule above means a non-empty local
        // number is never changed here, so the old renumber → reprint flag is removed. The clash shows only as the review pill.
        _ = oldQueue;
        //JJQR - END
        if (fullApply)
        {
            await RestoreUpdatedAtAsync(db, entity, UpdatedAtFromJson(row), ct);
            if (services is not null)
            {
                await ReplaceSetAsync(db, typeof(BookingService), nameof(BookingService.BookingId), id, services, ct);
                await db.SaveChangesAsync(ct);
            }
        }
        return true;
    }

    private static readonly HashSet<string> IgnoredForCompare = new(StringComparer.OrdinalIgnoreCase)
    { "updated_at", "created_at", "row_version", "origin_node_id", "deleted_at" };

    private static async Task<bool> ApplyPaymentAsync(ClinicAppDbContext db, PulledChange ch, JsonObject? data, ApplyMode mode, CancellationToken ct)
    {
        if (ch.IsDelete || data is null) return false; // delete_not_allowed
        var et = EntityType(db, typeof(Payment));
        var key = KeyFromJson(et, data) ?? KeyFromId(et, ch.EntityId) ?? throw new InvalidDataException("payment without payment_id");
        var local = (Payment?)await FindAsync(db, typeof(Payment), key, ct);
        //JJQR - AI (Nov 21, 2026) - payments.booking_id is UNIQUE (1:1). A demo/seeded booking shared with the cloud can carry a
        // different local payment_id: an untouched local row (Unpaid, nothing queued) gives way to the cloud's; otherwise keep ours and flag it.
        if (local is null && Convert(data["booking_id"], typeof(Guid)) is Guid cloudBookingId)
        {
            var twin = await db.Set<Payment>().FirstOrDefaultAsync(x => x.BookingId == cloudBookingId, ct);
            if (twin is not null)
            {
                if (twin.Status == PaymentStatus.Unpaid && !await SyncLocalStore.HasPendingAsync(db, Aggregates.Payment, twin.PaymentId.ToString(), ct))
                {
                    db.Remove(twin);
                    await db.SaveChangesAsync(ct);
                }
                else
                {
                    await SyncLocalStore.AddConflictAsync(db, new SyncConflict
                    {
                        Aggregate = Aggregates.Payment, EntityId = twin.PaymentId.ToString(), Kind = "cloud_edit_flagged", ReasonCode = "payment_booking_clash",
                        Message = "The cloud has a different payment for this visit. This computer's payment was kept.",
                        LocalJson = ToJson(db, twin).ToJsonString(), CloudJson = data.ToJsonString(),
                    }, ct);
                    return false;
                }
            }
        }
        //JJQR - END
        if (local is null || mode == ApplyMode.ServerResult)
        {
            var (entity, inserted) = await UpsertAsync(db, typeof(Payment), (JsonObject)data.DeepClone(), ct);
            await db.SaveChangesAsync(ct);
            if (!inserted) await RestoreUpdatedAtAsync(db, entity, UpdatedAtFromJson(data), ct);
            return true;
        }

        var localJson = ToJson(db, local);
        if (SameValues(localJson, data)) return false;

        var cloudStatus = (PaymentStatus?)Convert(data["status"], typeof(PaymentStatus));
        if (cloudStatus == PaymentStatus.Refunded && local.Status != PaymentStatus.Refunded)
        {
            // Refunds are admin-online only (study §5.5), so a cloud refund is authoritative.
            Apply(db, local, (JsonObject)data.DeepClone(), isNew: false);
            await db.SaveChangesAsync(ct);
            await RestoreUpdatedAtAsync(db, local, UpdatedAtFromJson(data), ct);
            await SyncLocalStore.AddConflictAsync(db, new SyncConflict
            {
                Aggregate = Aggregates.Payment, EntityId = local.PaymentId.ToString(), Kind = "cloud_refund_applied",
                Message = "This payment was refunded in the cloud by an admin.", LocalJson = localJson.ToJsonString(), CloudJson = data.ToJsonString(),
            }, ct);
            return true;
        }

        // Single writer: the node's payment wins; the cloud edit is flagged, never merged.
        await SyncLocalStore.AddConflictAsync(db, new SyncConflict
        {
            Aggregate = Aggregates.Payment, EntityId = local.PaymentId.ToString(), Kind = "cloud_edit_flagged", ReasonCode = "not_owner",
            Message = "The cloud copy of this payment differs from the one recorded here. This computer's payment was kept.",
            LocalJson = localJson.ToJsonString(), CloudJson = data.ToJsonString(),
        }, ct);
        return false;
    }

    //JJQR - AI (Nov 23, 2026) - owner: "what is the point of showing it if after pressing Sync it is still there". A review item whose two columns hold the SAME values (500 and 500.00, the same time with more decimals, a consultation that is identical on both sides) tells nobody anything.
    // True when the computer's copy and the server's copy of the record carry the same content, ignoring who wrote it and the version counter.
    internal static bool IsHarmlessDifference(SyncConflict c)
    {
        try
        {
            // only the two alarms seen in practice: a payment the server also holds ("not_owner") and a consultation the server already finished ("finished_immutable").
            // Anything else (e.g. a DIFFERENT record for the same visit) always stays for a person to look at.
            if (c.ReasonCode is not ("not_owner" or "finished_immutable")) return false;
            if (string.IsNullOrWhiteSpace(c.LocalJson) || string.IsNullOrWhiteSpace(c.CloudJson)) return false;
            if (JsonNode.Parse(c.LocalJson) is not JsonObject local || JsonNode.Parse(c.CloudJson) is not JsonObject cloud) return false;
            // a consultation conflict carries the whole document: compare the consultation record itself
            if (local["consultation"] is JsonObject l && cloud["consultation"] is JsonObject r) { local = l; cloud = r; }
            return SameValues(local, cloud);
        }
        catch { return false; }
    }
    //JJQR - END

    private static bool SameValues(JsonObject local, JsonObject cloud)
    {
        foreach (var kv in cloud)
        {
            if (IgnoredForCompare.Contains(kv.Key) || !local.ContainsKey(kv.Key)) continue;
            if (!JsonNode.DeepEquals(Normalize(local[kv.Key]), Normalize(kv.Value))) return false;
        }
        return true;
    }

    private static JsonNode? Normalize(JsonNode? n)
    {
        if (n is JsonValue v && v.TryGetValue<decimal>(out var d)) return JsonValue.Create(d / 1.0000000000000000000000000000m);
        if (n is JsonValue s && s.TryGetValue<string>(out var str) && Guid.TryParse(str, out var g)) return JsonValue.Create(g.ToString());
        //JJQR - AI (Nov 23, 2026) - offline audit: after every online payment the admin saw a "the cloud copy of this payment differs" review item although both copies were identical (Paid, same amount, same receipt number).
        // The only difference was the timestamp text: this computer stores 4 decimals ("...10.5329+00:00"), the server sends 7 ("...10.5329904+00:00"). Compare timestamps as moments, to the millisecond.
        if (n is JsonValue t && t.TryGetValue<string>(out var ts) && ts.Length >= 20 && ts[4] == '-' && ts[10] == 'T'
            && DateTimeOffset.TryParse(ts, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var moment))
            return JsonValue.Create(moment.UtcDateTime.Ticks / TimeSpan.TicksPerMillisecond);
        //JJQR - END
        return n?.DeepClone();
    }

    private static async Task<bool> ApplyConsultationAsync(ClinicAppDbContext db, PulledChange ch, JsonObject? data, ApplyMode mode, CancellationToken ct)
    {
        if (ch.IsDelete || data is null) return false; // delete_not_allowed
        var isDocument = data["consultation"] is JsonObject;
        var row = (data["consultation"] as JsonObject ?? data).DeepClone().AsObject();
        var et = EntityType(db, typeof(Consultation));
        var key = KeyFromJson(et, row) ?? KeyFromId(et, ch.EntityId) ?? throw new InvalidDataException("consultation without consultation_id");
        var id = (Guid)key[0];
        var local = (Consultation?)await FindAsync(db, typeof(Consultation), key, ct);

        if (local is not null && mode == ApplyMode.Pull)
        {
            //JJQR - AI (Nov 21, 2026) - v1.1: finished stays immutable locally, except a cloud AMENDMENT (status Amended) of it when we have nothing unpushed
            if (local.Status is ConsultationStatus.Completed or ConsultationStatus.Amended)
            {
                var cloudStatus = row["status"]?.GetValue<string>();
                var cloudAmended = string.Equals(cloudStatus, nameof(ConsultationStatus.Amended), StringComparison.OrdinalIgnoreCase);
                if (!cloudAmended || await SyncLocalStore.HasPendingAsync(db, Aggregates.Consultation, id.ToString(), ct))
                    return false; // finished consultations are immutable on the node
            }
            //JJQR - END
            if (await SyncLocalStore.HasPendingAsync(db, Aggregates.Consultation, id.ToString(), ct))
                return false; // node wins: our draft goes up, the cloud takes it (§5.4)
        }

        //JJQR - AI (Nov 21, 2026) - consultations.booking_id is UNIQUE: a cloud consultation with ANOTHER id for a visit that already has
        // one here (e.g. a reset node re-consulting a shared demo booking) is flagged for review instead of failing the save. Clinical data is never dropped.
        if (local is null && Convert(row["booking_id"], typeof(Guid)) is Guid cloudBookingId)
        {
            var twin = await db.Consultations.FirstOrDefaultAsync(x => x.BookingId == cloudBookingId && x.ConsultationId != id, ct);
            if (twin is not null)
            {
                await SyncLocalStore.AddConflictAsync(db, new SyncConflict
                {
                    Aggregate = Aggregates.Consultation, EntityId = twin.ConsultationId.ToString(), Kind = "conflict", ReasonCode = "consultation_booking_clash",
                    Message = "The cloud has a different consultation for this visit. This computer's consultation was kept; ask an admin which one is correct.",
                    LocalJson = ToJson(db, twin).ToJsonString(), CloudJson = data.ToJsonString(),
                }, ct);
                return false;
            }
        }
        //JJQR - END
        var (entity, inserted) = await UpsertAsync(db, typeof(Consultation), row, ct);
        await db.SaveChangesAsync(ct);
        if (!inserted) await RestoreUpdatedAtAsync(db, entity, UpdatedAtFromJson(row), ct);
        var consult = (Consultation)entity;
        if (!isDocument) return true;

        //JJQR - AI (Nov 21, 2026) - contract §11: a child key that is ABSENT keeps the local set; null or [] removes it
        if (data.ContainsKey("diagnoses"))
            await ReplaceSetAsync(db, typeof(ConsultationDiagnosis), nameof(ConsultationDiagnosis.ConsultationId), id, data["diagnoses"] as JsonArray, ct);
        if (data.ContainsKey("lab_orders"))
            await ReplaceSetAsync(db, typeof(LabOrder), nameof(LabOrder.ConsultationId), id, data["lab_orders"] as JsonArray, ct);
        if (data.ContainsKey("vaccinations"))
            await ReplaceSetAsync(db, typeof(PatientVaccination), nameof(PatientVaccination.ConsultationId), id, data["vaccinations"] as JsonArray, ct);
        if (data.ContainsKey("follow_up"))
            await ReplaceSetAsync(db, typeof(FollowUp), nameof(FollowUp.ConsultationId), id, One(data["follow_up"]), ct);
        if (data.ContainsKey("medical_certificate"))
            await ReplaceSetAsync(db, typeof(MedicalCertificate), nameof(MedicalCertificate.ConsultationId), id, One(data["medical_certificate"]), ct);

        // prescription: one group per booking, line items replaced as a set
        if (data.ContainsKey("prescription"))
        {
            var rx = data["prescription"] as JsonObject;
            var groupJson = rx?["group"] as JsonObject;
            var groups = await db.PrescriptionGroups.Where(g => g.BookingId == consult.BookingId).ToListAsync(ct);
            var keepGroupId = groupJson is null ? (Guid?)null : (Guid?)Convert(groupJson["group_id"], typeof(Guid));
            foreach (var g in groups.Where(g => g.GroupId != keepGroupId))
            {
                db.PrescriptionLineItems.RemoveRange(await db.PrescriptionLineItems.Where(i => i.GroupId == g.GroupId).ToListAsync(ct));
                db.PrescriptionGroups.Remove(g);
            }
            await db.SaveChangesAsync(ct);
            if (groupJson is not null)
            {
                var (groupEntity, _) = await UpsertAsync(db, typeof(PrescriptionGroup), (JsonObject)groupJson.DeepClone(), ct);
                await db.SaveChangesAsync(ct);
                var groupId = ((PrescriptionGroup)groupEntity).GroupId;
                // line_items absent inside a present group keeps the local lines
                if (rx!.ContainsKey("line_items"))
                    await ReplaceSetAsync(db, typeof(PrescriptionLineItem), nameof(PrescriptionLineItem.GroupId), groupId, rx["line_items"] as JsonArray, ct);
            }
        }
        //JJQR - END
        await db.SaveChangesAsync(ct);
        return true;
    }

    private static JsonArray One(JsonNode? n) => n is JsonObject o ? new JsonArray(o.DeepClone()) : new JsonArray();

    private static async Task<bool> ApplyAppendOnlyAsync(ClinicAppDbContext db, Type clr, PulledChange ch, JsonObject? data, ApplyMode mode, CancellationToken ct)
    {
        if (ch.IsDelete || data is null) return false; // append-only: never delete
        var et = EntityType(db, clr);
        var key = KeyFromJson(et, data) ?? throw new InvalidDataException($"{clr.Name} without id");
        var existing = await FindAsync(db, clr, key, ct);
        if (existing is not null) return false; // never update an appended row

        if (clr == typeof(PatientVitalReading))
        {
            var reading = (PatientVitalReading)Activator.CreateInstance(clr)!;
            Apply(db, reading, data, isNew: true);
            var clash = await db.PatientVitalReadings.AnyAsync(v => v.BookingId == reading.BookingId && v.TemplateId == reading.TemplateId, ct);
            if (clash)
            {
                await SyncLocalStore.AddConflictAsync(db, new SyncConflict
                {
                    Aggregate = Aggregates.VitalReading, EntityId = ch.EntityId, Kind = "conflict", ReasonCode = "already_recorded",
                    Message = "A different reading for this vital and visit is already recorded on this computer.", CloudJson = data.ToJsonString(),
                }, ct);
                return false;
            }
            db.Add(reading);
        }
        else
        {
            await UpsertAsync(db, clr, (JsonObject)data.DeepClone(), ct);
        }
        await db.SaveChangesAsync(ct);
        //JJQR - AI (Nov 21, 2026) - a pulled audit row came from the cloud: never push it back
        if (clr == typeof(AuditLog)) await SyncLocalStore.MarkSweptAsync(db, Aggregates.AuditLog, ((Guid)key[0]).ToString(), ct);
        //JJQR - END
        //JJQR - AI (21 Nov 2026) - same for a pulled consent (it is in the cloud already)
        if (clr == typeof(PatientConsent)) await SyncLocalStore.MarkSweptAsync(db, Aggregates.PatientConsent, ((Guid)key[0]).ToString(), ct);
        //JJQR - END
        return true;
    }
}
//JJQR - END
