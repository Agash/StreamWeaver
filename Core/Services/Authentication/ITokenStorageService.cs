namespace StreamWeaver.Core.Services.Authentication;

/// <summary>
/// Defines the contract for a service that handles secure storage and retrieval of authentication tokens.
/// </summary>
public interface ITokenStorageService
{
    /// <summary>
    /// Saves or updates the access and refresh tokens associated with a specific key.
    /// </summary>
    /// <param name="key">The unique key identifying the token set.</param>
    /// <param name="accessToken">The access token to store.</param>
    /// <param name="refreshToken">The refresh token to store (optional).</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation completion.</returns>
    Task SaveTokensAsync(string key, string accessToken, string? refreshToken);

    /// <summary>
    /// Retrieves the access and refresh tokens associated with the specified key.
    /// </summary>
    /// <param name="key">The unique key identifying the token set.</param>
    /// <returns>
    /// A <see cref="Task"/> resulting in a tuple containing the access token and refresh token.
    /// Either or both tokens can be null if they are not found.
    /// </returns>
    Task<(string? AccessToken, string? RefreshToken)> GetTokensAsync(string key);

    /// <summary>
    /// Deletes the access and refresh tokens associated with the specified key.
    /// </summary>
    /// <param name="key">The unique key identifying the token set to delete.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation completion.</returns>
    Task DeleteTokensAsync(string key);
}
