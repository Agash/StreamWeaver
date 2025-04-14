namespace StreamWeaver.Core.Services.Authentication;

public interface IAuthService
{
    Task<bool> InitiateLoginAsync();
    Task<bool> RefreshTokenAsync(string accountId);
    Task LogoutAsync(string accountId);
    Task<bool> IsAuthenticatedAsync(string accountId);
    Task<string?> GetAccessTokenAsync(string accountId);
}
