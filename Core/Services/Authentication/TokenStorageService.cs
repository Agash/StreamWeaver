using Microsoft.Extensions.Logging;
using Windows.Security.Credentials;

namespace StreamWeaver.Core.Services.Authentication;

/// <summary>
/// Service responsible for securely storing and retrieving authentication tokens
/// using the Windows PasswordVault.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="TokenStorageService"/> class.
/// </remarks>
/// <param name="logger">The logger instance for logging messages.</param>
public class TokenStorageService(ILogger<TokenStorageService> logger) : ITokenStorageService
{
    private readonly PasswordVault _vault = new();
    private readonly ILogger<TokenStorageService> _logger = logger;

    /// <summary>
    /// Represents the resource name under which tokens are grouped within the PasswordVault.
    /// </summary>
    private const string ResourceName = "StreamWeaverTokens";

    /// <summary>
    /// Saves or updates the access and refresh tokens associated with a specific key.
    /// If tokens for the key already exist, they are updated.
    /// If the new refresh token is null or empty, the existing refresh token is removed.
    /// Note: PasswordVault operations are synchronous, but the method returns Task for interface consistency.
    /// </summary>
    /// <param name="key">The unique key identifying the token set (e.g., user ID or service name).</param>
    /// <param name="accessToken">The access token to store.</param>
    /// <param name="refreshToken">The refresh token to store (optional).</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation completion (effectively synchronous).</returns>
    public Task SaveTokensAsync(string key, string accessToken, string? refreshToken)
    {
        string accessKey = key + "_access";
        string refreshKey = key + "_refresh";

        try
        {
            _vault.Add(new PasswordCredential(ResourceName, accessKey, accessToken));
            _logger.LogTrace("Added new access token credential for key {Key}", key);

            if (!string.IsNullOrEmpty(refreshToken))
            {
                _vault.Add(new PasswordCredential(ResourceName, refreshKey, refreshToken));
                _logger.LogTrace("Added new refresh token credential for key {Key}", key);
            }
            else
            {
                try
                {
                    PasswordCredential? existingRefresh = _vault.Retrieve(ResourceName, refreshKey);
                    if (existingRefresh != null)
                    {
                        _vault.Remove(existingRefresh);
                        _logger.LogTrace("Removed existing refresh token for key {Key} as new one is null/empty.", key);
                    }
                }
                catch (Exception ex) when (ex.HResult == -2147023728)
                {
                    _logger.LogTrace("No existing refresh token found to remove for key {Key}.", key);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Failed to remove potentially existing refresh token for key {Key}. This might leave an orphaned refresh token.",
                        key
                    );
                }
            }

            _logger.LogInformation("Tokens saved successfully for key {Key}", key);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to initially add token(s) for key {Key}, attempting update.", key);
            try
            {
                PasswordCredential existingAccess = _vault.Retrieve(ResourceName, accessKey);
                existingAccess.Password = accessToken;
                _vault.Add(existingAccess);
                _logger.LogTrace("Updated existing access token credential for key {Key}", key);

                if (!string.IsNullOrEmpty(refreshToken))
                {
                    try
                    {
                        PasswordCredential existingRefresh = _vault.Retrieve(ResourceName, refreshKey);
                        existingRefresh.Password = refreshToken;
                        _vault.Add(existingRefresh);
                        _logger.LogTrace("Updated existing refresh token credential for key {Key}", key);
                    }
                    catch (Exception refreshEx) when (refreshEx.HResult == -2147023728)
                    {
                        _logger.LogTrace("Existing refresh token not found for key {Key}, adding new one during update.", key);
                        _vault.Add(new PasswordCredential(ResourceName, refreshKey, refreshToken));
                    }
                }
                else
                {
                    try
                    {
                        PasswordCredential? existingRefresh = _vault.Retrieve(ResourceName, refreshKey);
                        if (existingRefresh != null)
                        {
                            _vault.Remove(existingRefresh);
                            _logger.LogTrace("Removed existing refresh token for key {Key} during update process.", key);
                        }
                    }
                    catch (Exception removeEx) when (removeEx.HResult == -2147023728)
                    {
                        _logger.LogTrace("No existing refresh token found to remove during update for key {Key}.", key);
                    }
                    catch (Exception removeEx)
                    {
                        _logger.LogWarning(removeEx, "Failed to remove potentially existing refresh token during update for key {Key}.", key);
                    }
                }

                _logger.LogInformation("Tokens updated successfully for key {Key}", key);
            }
            catch (Exception updateEx)
            {
                _logger.LogError(updateEx, "Failed to save or update tokens for key {Key}", key);
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Retrieves the access and refresh tokens associated with the specified key.
    /// </summary>
    /// <param name="key">The unique key identifying the token set.</param>
    /// <returns>
    /// A <see cref="Task"/> resulting in a tuple containing the access token and refresh token.
    /// Either or both tokens can be null if they are not found in the storage.
    /// </returns>
    public Task<(string? AccessToken, string? RefreshToken)> GetTokensAsync(string key)
    {
        string? accessToken = null;
        string? refreshToken = null;
        string accessKey = key + "_access";
        string refreshKey = key + "_refresh";

        try
        {
            PasswordCredential credAccess = _vault.Retrieve(ResourceName, accessKey);
            credAccess.RetrievePassword();
            accessToken = credAccess.Password;
        }
        catch (Exception ex) when (ex.HResult == -2147023728)
        {
            _logger.LogDebug("Access token not found for key {Key}", key);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to retrieve access token for key {Key}", key);
        }

        try
        {
            PasswordCredential credRefresh = _vault.Retrieve(ResourceName, refreshKey);
            credRefresh.RetrievePassword();
            refreshToken = credRefresh.Password;
        }
        catch (Exception ex) when (ex.HResult == -2147023728)
        {
            _logger.LogDebug("Refresh token not found for key {Key}", key);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to retrieve refresh token for key {Key}", key);
        }

        bool accessFound = !string.IsNullOrEmpty(accessToken);
        bool refreshFound = !string.IsNullOrEmpty(refreshToken);
        _logger.LogDebug(
            "Token retrieval attempt for key {Key} completed. Access found: {AccessFound}, Refresh found: {RefreshFound}",
            key,
            accessFound,
            refreshFound
        );

        return Task.FromResult<(string?, string?)>((accessToken, refreshToken));
    }

    /// <summary>
    /// Deletes the access and refresh tokens associated with the specified key.
    /// Ignores errors if the tokens are not found.
    /// Note: PasswordVault operations are synchronous, but the method returns Task for interface consistency.
    /// </summary>
    /// <param name="key">The unique key identifying the token set to delete.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation completion (effectively synchronous).</returns>
    public Task DeleteTokensAsync(string key)
    {
        string accessKey = key + "_access";
        string refreshKey = key + "_refresh";
        bool deletedAccess = false;
        bool deletedRefresh = false;

        try
        {
            PasswordCredential credAccess = _vault.Retrieve(ResourceName, accessKey);
            _vault.Remove(credAccess);
            deletedAccess = true;
        }
        catch (Exception ex) when (ex.HResult == -2147023728)
        {
            _logger.LogTrace("Access token not found for key {Key} during delete operation.", key);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete access token for key {Key}.", key);
        }

        try
        {
            PasswordCredential credRefresh = _vault.Retrieve(ResourceName, refreshKey);
            _vault.Remove(credRefresh);
            deletedRefresh = true;
        }
        catch (Exception ex) when (ex.HResult == -2147023728)
        {
            _logger.LogTrace("Refresh token not found for key {Key} during delete operation.", key);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete refresh token for key {Key}.", key);
        }

        if (deletedAccess || deletedRefresh)
        {
            _logger.LogInformation(
                "Token deletion attempt completed for key {Key}. Access deleted: {AccessDeleted}, Refresh deleted: {RefreshDeleted}",
                key,
                deletedAccess,
                deletedRefresh
            );
        }
        else
        {
            _logger.LogDebug("Token deletion attempt completed for key {Key}. No tokens found to delete.", key);
        }

        return Task.CompletedTask;
    }
}
