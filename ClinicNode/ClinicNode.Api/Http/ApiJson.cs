//JJQR - AI (Nov 21, 2026) - wire formats: data resources are snake_case with enums by name (DOTNET_FRONTEND_CONTRACT §4/§6); /api/auth/* is camelCase.
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClinicNode.Api.Http;

public static class ApiJson
{
    /// <summary>snake_case data resources (bookings, doctors, reports, performance …).</summary>
    public static readonly JsonSerializerOptions Data = Make(JsonNamingPolicy.SnakeCaseLower);

    /// <summary>camelCase auth layer (login, refresh-token, me …).</summary>
    public static readonly JsonSerializerOptions Auth = Make(JsonNamingPolicy.CamelCase);

    private static JsonSerializerOptions Make(JsonNamingPolicy naming)
    {
        var o = new JsonSerializerOptions
        {
            PropertyNamingPolicy = naming,
            PropertyNameCaseInsensitive = true,
            NumberHandling = JsonNumberHandling.AllowReadingFromString,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        };
        o.Converters.Add(new JsonStringEnumConverter());
        return o;
    }
}
//JJQR - END
