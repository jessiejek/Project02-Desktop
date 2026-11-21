//JJQR - AI (Nov 21, 2026)
// Generic EF-metadata-driven row <-> snake_case JSON codec used for push snapshots and for
// applying pulled rows. Only scalar columns are read/written; navigations are ignored
// (contract §5: "Navigation objects are ignored"). Unknown JSON keys (e.g. cloud-only
// row_version / origin_node_id) are ignored unless the local model has that column.
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using ClinicApp.Domain.Entities;
using ClinicNode.Sync.Wire;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace ClinicNode.Sync.Local;

public static class EntityRowCodec
{
    public static IEntityType EntityType(DbContext db, Type clr) =>
        db.Model.FindEntityType(clr) ?? throw new InvalidOperationException($"{clr.Name} is not in the EF model.");

    public static string ColumnName(IProperty p) => p.GetColumnName() ?? JsonNamingPolicy.SnakeCaseLower.ConvertName(p.Name);

    //JJQR - AI (Nov 21, 2026) - contract §11: snapshot names are the REST JSON names (snake_case of the property,
    // e.g. icd10_code), not the physical column name (icd10code). Same rule as ClinicApp.Infrastructure.Sync.SyncRows.Name.
    public static string WireName(IProperty p) => JsonNamingPolicy.SnakeCaseLower.ConvertName(p.Name);
    //JJQR - END

    public static string Norm(string s) => s.Replace("_", "").ToLowerInvariant();

    /// <summary>Scalar columns of a tracked or untracked entity as a snake_case JSON object.</summary>
    public static JsonObject ToJson(DbContext db, object entity)
    {
        var et = EntityType(db, entity.GetType());
        var entry = db.Entry(entity);
        var obj = new JsonObject();
        foreach (var p in et.GetProperties().Where(p => p.PropertyInfo is not null))
        {
            var value = entry.Property(p.Name).CurrentValue;
            obj[WireName(p)] = value is null ? null : JsonSerializer.SerializeToNode(value, p.ClrType, SyncWire.Json);
        }
        return obj;
    }

    public static JsonObject? ToJsonOrNull(DbContext db, object? entity) => entity is null ? null : ToJson(db, entity);

    public static Dictionary<string, JsonNode?> Index(JsonObject json)
    {
        var d = new Dictionary<string, JsonNode?>();
        foreach (var kv in json) d[Norm(kv.Key)] = kv.Value;
        return d;
    }

    private static bool TryGet(Dictionary<string, JsonNode?> idx, IProperty p, out JsonNode? node) =>
        idx.TryGetValue(Norm(ColumnName(p)), out node) || idx.TryGetValue(Norm(p.Name), out node);

    public static object? Convert(JsonNode? node, Type clr) =>
        node is null ? null : node.Deserialize(clr, SyncWire.Json);

    /// <summary>Primary key values from a JSON row; null if any key column is missing.</summary>
    public static object[]? KeyFromJson(IEntityType et, JsonObject json)
    {
        var idx = Index(json);
        var key = et.FindPrimaryKey()!;
        var values = new object[key.Properties.Count];
        for (var i = 0; i < key.Properties.Count; i++)
        {
            if (!TryGet(idx, key.Properties[i], out var n) || n is null) return null;
            values[i] = Convert(n, key.Properties[i].ClrType)!;
        }
        return values;
    }

    /// <summary>Parses an entity_id string into PK values (single key, or "a:b" / "a|b" / "a,b" for composite).</summary>
    public static object[]? KeyFromId(IEntityType et, string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        var props = et.FindPrimaryKey()!.Properties;
        //JJQR - AI (Nov 21, 2026) - pull entity_id is always a string; composite keys are joined with ':' (contract §11)
        var parts = props.Count == 1 ? new[] { id } : id.Split(':', StringSplitOptions.TrimEntries);
        //JJQR - END
        if (parts.Length != props.Count) return null;
        var values = new object[props.Count];
        for (var i = 0; i < props.Count; i++)
        {
            try { values[i] = Convert(JsonValue.Create(parts[i]), props[i].ClrType)!; }
            catch (JsonException)
            {
                try { values[i] = System.Convert.ChangeType(parts[i], Nullable.GetUnderlyingType(props[i].ClrType) ?? props[i].ClrType); }
                catch { return null; }
            }
        }
        return values;
    }

    /// <summary>Copies the JSON columns present onto <paramref name="entity"/> (missing columns keep their value).</summary>
    public static void Apply(DbContext db, object entity, JsonObject json, bool isNew, ISet<string>? keepLocal = null)
    {
        var et = EntityType(db, entity.GetType());
        var idx = Index(json);
        var entry = db.Entry(entity);
        foreach (var p in et.GetProperties())
        {
            if (!isNew && p.IsPrimaryKey()) continue;
            if (keepLocal is not null && keepLocal.Contains(p.Name)) continue;
            if (!TryGet(idx, p, out var node)) continue;
            var value = Convert(node, p.ClrType);
            if (value is null && !p.IsNullable) continue; // never null-out a required column
            var current = entry.Property(p.Name).CurrentValue;
            if (!Equals(current, value)) entry.Property(p.Name).CurrentValue = value;
        }
    }

    public static async Task<object?> FindAsync(DbContext db, Type clr, object[] key, CancellationToken ct) =>
        await db.FindAsync(clr, key, ct);

    /// <summary>Insert-or-overwrite a row. Returns (entity, inserted). Caller saves.</summary>
    public static async Task<(object Entity, bool Inserted)> UpsertAsync(DbContext db, Type clr, JsonObject json, CancellationToken ct, ISet<string>? keepLocal = null)
    {
        var et = EntityType(db, clr);
        var key = KeyFromJson(et, json) ?? throw new InvalidDataException($"{et.ClrType.Name} row has no primary key in data.");
        var existing = await FindAsync(db, clr, key, ct);
        if (existing is not null)
        {
            Apply(db, existing, json, isNew: false, keepLocal);
            return (existing, false);
        }
        var created = Activator.CreateInstance(clr)!;
        Apply(db, created, json, isNew: true);
        db.Add(created);
        return (created, true);
    }

    private static readonly MethodInfo LoadWhereOpen =
        typeof(EntityRowCodec).GetMethod(nameof(LoadWhereGeneric), BindingFlags.NonPublic | BindingFlags.Static)!;

    /// <summary>Rows of <paramref name="clr"/> where property == value.</summary>
    public static async Task<List<object>> LoadWhereAsync(DbContext db, Type clr, string property, object value, CancellationToken ct)
    {
        var task = (Task<List<object>>)LoadWhereOpen.MakeGenericMethod(clr).Invoke(null, new object[] { db, property, value, ct })!;
        return await task;
    }

    private static async Task<List<object>> LoadWhereGeneric<T>(DbContext db, string property, object value, CancellationToken ct) where T : class
    {
        var param = System.Linq.Expressions.Expression.Parameter(typeof(T), "e");
        var member = System.Linq.Expressions.Expression.Property(param, property);
        var constant = System.Linq.Expressions.Expression.Convert(System.Linq.Expressions.Expression.Constant(value), member.Type);
        var predicate = System.Linq.Expressions.Expression.Lambda<Func<T, bool>>(
            System.Linq.Expressions.Expression.Equal(member, constant), param);
        var rows = await db.Set<T>().Where(predicate).ToListAsync(ct);
        return rows.Cast<object>().ToList();
    }

    /// <summary>
    /// Replaces the children of one parent as a set (contract: "children replaced as a set").
    /// Rows not in <paramref name="incoming"/> are deleted, the rest upserted. Caller saves.
    /// </summary>
    public static async Task ReplaceSetAsync(DbContext db, Type clr, string fkProperty, object fkValue, JsonArray? incoming, CancellationToken ct)
    {
        var et = EntityType(db, clr);
        var pk = et.FindPrimaryKey()!;
        var existing = await LoadWhereAsync(db, clr, fkProperty, fkValue, ct);
        var incomingRows = (incoming ?? new JsonArray()).OfType<JsonObject>().ToList();
        var incomingKeys = incomingRows.Select(r => KeyString(KeyFromJson(et, r))).ToHashSet();
        foreach (var row in existing)
        {
            var k = KeyString(pk.Properties.Select(p => db.Entry(row).Property(p.Name).CurrentValue!).ToArray());
            if (!incomingKeys.Contains(k)) db.Remove(row);
        }
        // Deletes first so unique indexes (e.g. one follow-up per consultation) do not collide.
        await db.SaveChangesAsync(ct);
        foreach (var row in incomingRows)
            await UpsertAsync(db, clr, row, ct);
    }

    private static string KeyString(object[]? key) =>
        key is null ? Guid.NewGuid().ToString() : string.Join("|", key.Select(k => k?.ToString()?.ToLowerInvariant()));

    /// <summary>Single-column FK property on <paramref name="child"/> pointing at <paramref name="principal"/>.</summary>
    public static string ForeignKeyTo(DbContext db, Type child, Type principal)
    {
        var fk = EntityType(db, child).GetForeignKeys().FirstOrDefault(f => f.PrincipalEntityType.ClrType == principal)
            ?? throw new InvalidOperationException($"{child.Name} has no FK to {principal.Name}.");
        return fk.Properties[0].Name;
    }

    private static readonly MethodInfo RestoreOpen =
        typeof(EntityRowCodec).GetMethod(nameof(RestoreUpdatedAtGeneric), BindingFlags.NonPublic | BindingFlags.Static)!;

    /// <summary>
    /// ClinicAppDbContext.SaveChanges re-stamps UpdatedAt on every Modified row; a pulled row
    /// must keep the cloud's value (field-level merge compares updated_at), so write it back.
    /// </summary>
    public static async Task RestoreUpdatedAtAsync(DbContext db, object entity, DateTimeOffset? cloudValue, CancellationToken ct)
    {
        if (cloudValue is null || entity is not IHasUpdatedAt) return;
        var et = EntityType(db, entity.GetType());
        var pk = et.FindPrimaryKey()!;
        if (pk.Properties.Count != 1 || pk.Properties[0].ClrType != typeof(Guid)) return;
        var key = (Guid)db.Entry(entity).Property(pk.Properties[0].Name).CurrentValue!;
        var task = (Task)RestoreOpen.MakeGenericMethod(entity.GetType())
            .Invoke(null, new object[] { db, pk.Properties[0].Name, key, cloudValue.Value, ct })!;
        await task;
        ((IHasUpdatedAt)entity).UpdatedAt = cloudValue.Value;
        db.Entry(entity).Property(nameof(IHasUpdatedAt.UpdatedAt)).OriginalValue = cloudValue.Value;
    }

    private static Task<int> RestoreUpdatedAtGeneric<T>(DbContext db, string keyProp, Guid key, DateTimeOffset at, CancellationToken ct) where T : class =>
        db.Set<T>().Where(e => EF.Property<Guid>(e, keyProp) == key)
            .ExecuteUpdateAsync(s => s.SetProperty(e => EF.Property<DateTimeOffset>(e, nameof(IHasUpdatedAt.UpdatedAt)), at), ct);

    public static DateTimeOffset? UpdatedAtFromJson(JsonObject? json)
    {
        if (json is null) return null;
        var n = json["updated_at"] ?? json["updatedAt"];
        try { return n?.Deserialize<DateTimeOffset?>(SyncWire.Json); } catch { return null; }
    }
}
//JJQR - END
