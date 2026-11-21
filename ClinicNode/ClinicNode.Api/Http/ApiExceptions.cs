//JJQR - AI (Nov 21, 2026) - typed errors of the API layer. Only ApiOfflineException (and ApiNotSupportedException) make the online-first router fall back to the Local service;
// every other ApiException is a real answer from the server and is surfaced to the caller.
namespace ClinicNode.Api.Http;

public class ApiException : Exception
{
    public int Status { get; }
    public string? Body { get; }
    public ApiException(int status, string message, string? body = null, Exception? inner = null) : base(message, inner) { Status = status; Body = body; }
}

/// <summary>Network failure, timeout, no endpoint configured, or a gateway error (502/503/504): the server could not be reached. Triggers the Local fallback.</summary>
public sealed class ApiOfflineException(string message, Exception? inner = null) : ApiException(0, message, null, inner);

/// <summary>This Api implementation does not cover the method yet (partial conversion). The router uses the Local service.</summary>
public sealed class ApiNotSupportedException(string message = "Not available through the API yet.") : Exception(message);

public sealed class ApiUnauthorizedException(string message, string? body = null) : ApiException(401, message, body);
public sealed class ApiForbiddenException(string message, string? body = null) : ApiException(403, message, body);
public sealed class ApiNotFoundException(string message, string? body = null) : ApiException(404, message, body);
public sealed class ApiConflictException(string message, string? body = null) : ApiException(409, message, body);
public sealed class ApiServerException(int status, string message, string? body = null) : ApiException(status, message, body);

/// <summary>400 / 422 with a ProblemDetails or <c>{ message }</c> body. <see cref="Errors"/> holds the per-field messages when present.</summary>
public sealed class ApiValidationException(int status, string message, IReadOnlyDictionary<string, string[]> errors, string? body = null) : ApiException(status, message, body)
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}
//JJQR - END
