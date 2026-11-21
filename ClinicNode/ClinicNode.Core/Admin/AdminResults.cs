// JJQR - AI (Nov 21, 2026) - shared result types for admin services ported from the API controllers.
namespace ClinicNode.Core.Admin;

public sealed record AdminResult(bool Succeeded, string? Error = null)
{
    public static AdminResult Ok() => new(true);
    public static AdminResult Fail(string error) => new(false, error);
}

public sealed record AdminResult<T>(bool Succeeded, T? Value, string? Error = null)
{
    public static AdminResult<T> Ok(T value) => new(true, value);
    public static AdminResult<T> Fail(string error) => new(false, default, error);
}

public sealed record AdminPage<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize);
// JJQR - END
