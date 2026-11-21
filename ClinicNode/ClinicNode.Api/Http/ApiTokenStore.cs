//JJQR - AI (Nov 21, 2026) - holds the JWT pair of the signed-in user (memory only; the offline session is cached by the existing AuthService, and only the email is remembered — never the password).
namespace ClinicNode.Api.Http;

public interface IApiTokenStore
{
    string? AccessToken { get; }
    string? RefreshToken { get; }
    void Set(string accessToken, string? refreshToken);
    void Clear();
}

public sealed class InMemoryApiTokenStore : IApiTokenStore
{
    private readonly object _gate = new();
    private string? _access, _refresh;
    public string? AccessToken { get { lock (_gate) return _access; } }
    public string? RefreshToken { get { lock (_gate) return _refresh; } }
    public void Set(string accessToken, string? refreshToken) { lock (_gate) { _access = accessToken; _refresh = refreshToken ?? _refresh; } }
    public void Clear() { lock (_gate) { _access = null; _refresh = null; } }
}
//JJQR - END
