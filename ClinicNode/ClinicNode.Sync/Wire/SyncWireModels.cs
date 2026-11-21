//JJQR - AI (Nov 21, 2026)
// Wire DTOs for docs/SYNC-API-CONTRACT.md v1 (snake_case JSON, X-Sync-Protocol: 1).
// Everything that depends on the exact JSON shape lives in this folder, so a contract
// change is a change here only.
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace ClinicNode.Sync.Wire;

// §2.1
public sealed class RegisterNodeRequestDto
{
    public Guid? NodeId { get; set; }
    public string Name { get; set; } = "";
    public string? Platform { get; set; }
    public string? AppVersion { get; set; }
}

public sealed class RegisterNodeResponseDto
{
    public Guid NodeId { get; set; }
    public string? Name { get; set; }
    public string NodeSecret { get; set; } = "";
    public string? Status { get; set; }
    public DateTimeOffset? RegisteredAt { get; set; }
}

// §2.2
public sealed class TokenRequestDto
{
    public Guid NodeId { get; set; }
    public string NodeSecret { get; set; } = "";
}

public sealed class TokenResponseDto
{
    public string AccessToken { get; set; } = "";
    public string? TokenType { get; set; }
    public int? ExpiresIn { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public Guid? NodeId { get; set; }
}

// §1
public sealed class HandshakeResponseDto
{
    public int ProtocolVersion { get; set; }
    public int MinProtocolVersion { get; set; }
    public Guid? NodeId { get; set; }
    public DateTimeOffset? ServerTime { get; set; }
    public string? NodeStatus { get; set; }
}

public sealed class ErrorDto
{
    public string? Error { get; set; }
    public string? Detail { get; set; }
}

// §3.1
public sealed class PushRequestDto
{
    public List<PushOperationDto> Operations { get; set; } = new();
}

public sealed class PushOperationDto
{
    public Guid OperationId { get; set; }
    public string Aggregate { get; set; } = "";
    public Guid EntityId { get; set; }
    public string OperationType { get; set; } = "";
    public long? BaseRowVersion { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public PushPayloadDto Payload { get; set; } = new();
}

public sealed class PushPayloadDto
{
    public JsonNode? Data { get; set; }
    public List<string>? ChangedFields { get; set; }
}

// §3.2
public sealed class PushResponseDto
{
    public DateTimeOffset? ServerTime { get; set; }
    public List<PushOperationResultDto>? Results { get; set; }
}

public sealed class PushOperationResultDto
{
    public Guid OperationId { get; set; }
    public string? Aggregate { get; set; }
    public Guid? EntityId { get; set; }
    /// <summary>applied | merged | duplicate | conflict | rejected | retry</summary>
    public string Status { get; set; } = "";
    public long? RowVersion { get; set; }
    public string? ReasonCode { get; set; }
    public string? Message { get; set; }
    public List<string>? ConflictFields { get; set; }
    public JsonNode? Current { get; set; }
    public bool? Replayed { get; set; }
}

// §4
public sealed class PullResponseDto
{
    public DateTimeOffset? ServerTime { get; set; }
    public string? NextCursor { get; set; }
    public bool HasMore { get; set; }
    public List<PulledChangeDto> Changes { get; set; } = new();
}

public sealed class PulledChangeDto
{
    public string Aggregate { get; set; } = "";
    public string EntityId { get; set; } = "";
    /// <summary>upsert | delete</summary>
    public string Operation { get; set; } = "upsert";
    public long? RowVersion { get; set; }
    public Guid? OriginNodeId { get; set; }
    public DateTimeOffset? ChangedAt { get; set; }
    public JsonNode? Data { get; set; }
}

public static class SyncWire
{
    public const int ProtocolVersion = 1;
    public const string ProtocolHeader = "X-Sync-Protocol";

    public static readonly JsonSerializerOptions Json = Create();

    private static JsonSerializerOptions Create()
    {
        var o = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower,
            PropertyNameCaseInsensitive = true,
            NumberHandling = JsonNumberHandling.AllowReadingFromString,
        };
        o.Converters.Add(new TolerantEnumConverterFactory());
        return o;
    }
}

/// <summary>Writes enums as their exact C# member names (contract); reads names, snake_case or numbers.</summary>
public sealed class TolerantEnumConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type t) => t.IsEnum;

    public override JsonConverter CreateConverter(Type t, JsonSerializerOptions options) =>
        (JsonConverter)Activator.CreateInstance(typeof(TolerantEnumConverter<>).MakeGenericType(t))!;

    private sealed class TolerantEnumConverter<T> : JsonConverter<T> where T : struct, Enum
    {
        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Number)
                return (T)Enum.ToObject(typeof(T), reader.GetInt64());
            var s = reader.GetString() ?? "";
            var norm = s.Replace("_", "").Replace("-", "").Replace(" ", "");
            if (Enum.TryParse<T>(norm, ignoreCase: true, out var v)) return v;
            throw new JsonException($"'{s}' is not a valid {typeof(T).Name}.");
        }

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToString());
    }
}
//JJQR - END
