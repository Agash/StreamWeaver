using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Web;
using Microsoft.Extensions.Logging;
using StreamWeaver.Core.Models.Settings;
using StreamWeaver.Core.Services.Settings;
using Windows.System;

namespace StreamWeaver.Core.Services.Authentication;

/// <summary>
/// Handles the Twitch OAuth 2.0 Authorization Code Grant Flow, token management (storage, refresh),
/// user info retrieval, and logout procedures.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="TwitchAuthService"/> class.
/// </remarks>
/// <param name="tokenStorage">The service for securely storing tokens.</param>
/// <param name="settingsService">The service for managing application settings.</param>
/// <param name="logger">The logger instance.</param>
public class TwitchAuthService(ITokenStorageService tokenStorage, ISettingsService settingsService, ILogger<TwitchAuthService> logger)
{
    private readonly ITokenStorageService _tokenStorage = tokenStorage ?? throw new ArgumentNullException(nameof(tokenStorage));
    private readonly ISettingsService _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
    private readonly HttpClient _httpClient = new();
    private readonly ILogger<TwitchAuthService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>
    /// The redirect URI registered in the Twitch Developer Console for this application.
    /// Must use localhost for desktop applications.
    /// </summary>
    private const string RedirectUri = "http://localhost:5081/callback/twitch";

    /// <summary>
    /// The OAuth scopes required by the application.
    /// </summary>
    private readonly string[] _scopes =
    [
        "chat:read", // Read chat messages
        "chat:edit", // Send chat messages
        "user:read:email", // Get user's email (often needed for validation or profile)
        "whispers:read", // Read whispers received by the user
        // "channel:read:subscriptions", // Read subscriber list (optional, requires justification)
        // "bits:read"                 // Read bits leaderboard info (optional, requires justification)
        // Add any other scopes required for future features
    ];

    /// <summary>
    /// Temporary storage for the state parameter during the OAuth flow to prevent CSRF attacks.
    /// Static as the auth flow might span across different instances if the service was transient (though likely singleton).
    /// Consider thread-safety if multiple logins could happen concurrently (unlikely for a single user app).
    /// </summary>
    private static string? s_oauthState;

    /// <summary>
    /// Initiates the Twitch login process by opening the authorization URL in the user's browser
    /// and listening for the callback containing the authorization code.
    /// </summary>
    /// <returns>
    /// A <see cref="Task"/> resulting in <c>true</c> if the login process completes successfully
    /// (including obtaining and saving tokens), and <c>false</c> otherwise.
    /// </returns>
    public async Task<bool> InitiateLoginAsync()
    {
        (string? ClientId, string? ClientSecret)? credentials = GetCredentialsFromSettings();
        if (credentials == null)
        {
            _logger.LogWarning("Cannot initiate Twitch login: Client credentials missing or invalid in settings.");
            return false;
        }

        s_oauthState = Guid.NewGuid().ToString("N");
        _logger.LogDebug("Generated OAuth State: {OAuthState}", s_oauthState);

        string scopesString = string.Join(" ", _scopes);
        string authUrl =
            $"https://id.twitch.tv/oauth2/authorize"
            + $"?response_type=code"
            + $"&client_id={credentials.Value.ClientId}"
            + $"&redirect_uri={Uri.EscapeDataString(RedirectUri)}"
            + $"&scope={Uri.EscapeDataString(scopesString)}"
            + $"&state={s_oauthState}";

        string? authorizationCode = null;
        HttpListener? listener = null;
        try
        {
            _logger.LogInformation("Starting HttpListener on {RedirectUri}...", RedirectUri);
            listener = new HttpListener();
            listener.Prefixes.Add(RedirectUri.EndsWith('/') ? RedirectUri : RedirectUri + "/");
            listener.Start();

            _logger.LogInformation("Launching browser for Twitch auth: {AuthUrl}", authUrl);
            bool successLaunch = await Launcher.LaunchUriAsync(new Uri(authUrl));
            if (!successLaunch)
            {
                _logger.LogWarning("Failed to launch the default system browser.");
                listener.Stop();
                return false;
            }

            _logger.LogInformation("Waiting for OAuth callback from Twitch...");

            HttpListenerContext context = await listener.GetContextAsync().WaitAsync(TimeSpan.FromMinutes(2));
            HttpListenerRequest request = context.Request;
            HttpListenerResponse response = context.Response;

            try
            {
                _logger.LogDebug("Received callback: {CallbackUrl}", request.Url);
                string? receivedState = request.QueryString["state"];
                string? receivedCode = request.QueryString["code"];
                string? receivedError = request.QueryString["error"];
                string? receivedErrorDesc = request.QueryString["error_description"];

                if (!string.IsNullOrEmpty(receivedError))
                {
                    _logger.LogError("OAuth Error received from Twitch: {Error} - {ErrorDescription}", receivedError, receivedErrorDesc);
                    await SendResponseAsync(
                        response,
                        $"<html><body>OAuth Error: {HttpUtility.HtmlEncode(receivedErrorDesc ?? receivedError)}. Please close this window.</body></html>",
                        HttpStatusCode.BadRequest
                    );
                    s_oauthState = null;
                    return false;
                }

                if (string.IsNullOrEmpty(receivedState) || receivedState != s_oauthState)
                {
                    _logger.LogError("OAuth State mismatch! Expected: '{ExpectedState}', Received: '{ReceivedState}'", s_oauthState, receivedState);
                    await SendResponseAsync(
                        response,
                        "<html><body>OAuth Security Error (State mismatch). Please close this window and try logging in again.</body></html>",
                        HttpStatusCode.BadRequest
                    );
                    s_oauthState = null;
                    return false;
                }

                s_oauthState = null;

                if (string.IsNullOrEmpty(receivedCode))
                {
                    _logger.LogError("OAuth callback did not contain the required authorization code.");
                    await SendResponseAsync(
                        response,
                        "<html><body>OAuth callback error: Authorization code missing. Please close this window.</body></html>",
                        HttpStatusCode.BadRequest
                    );
                    return false;
                }

                authorizationCode = receivedCode;
                _logger.LogInformation("OAuth authorization code received successfully.");
                await SendResponseAsync(
                    response,
                    "<html><body>Authentication successful! You can close this window and return to StreamWeaver.</body></html>"
                );
            }
            finally
            {
                response?.Close();
            }
        }
        catch (TimeoutException)
        {
            _logger.LogWarning("OAuth callback timed out after 2 minutes.");
            s_oauthState = null;
            return false;
        }
        catch (ObjectDisposedException ode) when (ode.ObjectName == "System.Net.HttpListener")
        {
            _logger.LogWarning("HttpListener was disposed while waiting for callback, possibly due to cancellation or shutdown.");
            s_oauthState = null;
            return false;
        }
        catch (HttpListenerException hle)
        {
            _logger.LogError(hle, "HttpListener error during OAuth callback handling: {ErrorMessage}", hle.Message);
            s_oauthState = null;
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during OAuth listener/callback processing: {ErrorMessage}", ex.Message);
            s_oauthState = null;
            return false;
        }
        finally
        {
            if (listener?.IsListening ?? false)
            {
                listener.Stop();
                _logger.LogInformation("HttpListener stopped.");
            }

            listener?.Close();
        }

        if (!string.IsNullOrEmpty(authorizationCode))
        {
            return await ExchangeCodeForTokenAsync(authorizationCode);
        }
        else
        {
            _logger.LogError("Authorization code was null after listener processing, login failed.");
            return false;
        }
    }

    /// <summary>
    /// Retrieves the Twitch Client ID and Client Secret from application settings.
    /// Logs warnings if credentials are missing or appear invalid.
    /// </summary>
    /// <returns>A tuple containing the Client ID and Client Secret, or null if they are not configured correctly.</returns>
    private (string? ClientId, string? ClientSecret)? GetCredentialsFromSettings()
    {
        AppSettings settings = _settingsService.CurrentSettings;
        string? clientId = settings.Credentials?.TwitchApiClientId;
        string? clientSecret = settings.Credentials?.TwitchApiClientSecret;

        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
        {
            _logger.LogWarning("Twitch Client ID or Client Secret is missing in application settings.");

            return null;
        }

        if (clientId.StartsWith("YOUR_") || clientId.Length < 10 || clientSecret.StartsWith("YOUR_") || clientSecret.Length < 10)
        {
            _logger.LogWarning("Twitch Client ID or Client Secret appears to be a placeholder or invalid value in settings.");
            return null;
        }

        return (clientId, clientSecret);
    }

    /// <summary>
    /// Sends an HTML response back to the browser via the HttpListener.
    /// </summary>
    /// <param name="response">The HttpListenerResponse object.</param>
    /// <param name="content">The HTML content string.</param>
    /// <param name="statusCode">The HTTP status code to send (default is OK).</param>
    private async Task SendResponseAsync(HttpListenerResponse response, string content, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        try
        {
            byte[] buffer = Encoding.UTF8.GetBytes(content);
            response.ContentLength64 = buffer.Length;
            response.ContentType = "text/html; charset=utf-8";
            response.StatusCode = (int)statusCode;
            using Stream output = response.OutputStream;
            await output.WriteAsync(buffer);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send response back to browser via HttpListener.");
        }
    }

    /// <summary>
    /// Exchanges the received authorization code for access and refresh tokens using the Twitch token endpoint.
    /// </summary>
    /// <param name="code">The authorization code received from the callback.</param>
    /// <returns>A <see cref="Task"/> resulting in <c>true</c> if tokens are successfully obtained and saved, <c>false</c> otherwise.</returns>
    private async Task<bool> ExchangeCodeForTokenAsync(string code)
    {
        _logger.LogInformation("Exchanging Twitch authorization code for tokens...");
        (string? ClientId, string? ClientSecret)? credentials = GetCredentialsFromSettings();
        if (credentials == null || credentials.Value.ClientId == null || credentials.Value.ClientSecret == null)
        {
            _logger.LogError("Cannot exchange code for token: Client credentials missing or invalid.");
            return false;
        }

        string tokenEndpoint = "https://id.twitch.tv/oauth2/token";
        var content = new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                { "client_id", credentials.Value.ClientId },
                { "client_secret", credentials.Value.ClientSecret },
                { "code", code },
                { "grant_type", "authorization_code" },
                { "redirect_uri", RedirectUri },
            }
        );

        try
        {
            HttpResponseMessage response = await _httpClient.PostAsync(tokenEndpoint, content);
            string responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError(
                    "Error exchanging Twitch code for token. Status: {StatusCode}, Response: {ResponseBody}",
                    response.StatusCode,
                    responseBody
                );
                return false;
            }

            _logger.LogInformation("Token exchange successful. Parsing response...");
            using JsonDocument jsonDoc = JsonDocument.Parse(responseBody);
            JsonElement root = jsonDoc.RootElement;

            string? accessToken = root.TryGetProperty("access_token", out JsonElement accessTokenElement) ? accessTokenElement.GetString() : null;
            string? refreshToken = root.TryGetProperty("refresh_token", out JsonElement refreshTokenElement) ? refreshTokenElement.GetString() : null;

            if (string.IsNullOrEmpty(accessToken) || string.IsNullOrEmpty(refreshToken))
            {
                _logger.LogError("Error parsing token response: Access token or refresh token missing. Response: {ResponseBody}", responseBody);
                return false;
            }

            _logger.LogInformation("Access and refresh tokens received. Getting user info...");

            (string UserId, string Login)? userInfo = await GetAuthenticatedUserInfoAsync(accessToken, credentials.Value.ClientId);
            if (userInfo == null)
            {
                _logger.LogError("Failed to get user info after successful token exchange. Cannot save authentication.");

                return false;
            }

            _logger.LogInformation("User info obtained: ID={UserId}, Login={UserLogin}", userInfo.Value.UserId, userInfo.Value.Login);

            return await SaveAuthenticationAsync(userInfo.Value.UserId, userInfo.Value.Login, accessToken, refreshToken);
        }
        catch (JsonException jsonEx)
        {
            _logger.LogError(jsonEx, "Error parsing JSON response during token exchange.");
            return false;
        }
        catch (HttpRequestException httpEx)
        {
            _logger.LogError(httpEx, "HTTP network error during token exchange.");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during token exchange.");
            return false;
        }
    }

    /// <summary>
    /// Retrieves the authenticated user's ID and login name using the provided access token.
    /// </summary>
    /// <param name="accessToken">The valid Twitch API access token.</param>
    /// <param name="clientId">The Twitch Client ID.</param>
    /// <returns>A tuple containing the User ID and Login name, or null if retrieval fails.</returns>
    private async Task<(string UserId, string Login)?> GetAuthenticatedUserInfoAsync(string accessToken, string clientId)
    {
        _logger.LogDebug("Fetching authenticated user's info from Twitch API...");
        string usersEndpoint = "https://api.twitch.tv/helix/users";
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, usersEndpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request.Headers.Add("Client-Id", clientId);

            HttpResponseMessage response = await _httpClient.SendAsync(request);
            string responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError(
                    "Error getting user info from Twitch API. Status: {StatusCode}, Response: {ResponseBody}",
                    response.StatusCode,
                    responseBody
                );
                return null;
            }

            using JsonDocument jsonDoc = JsonDocument.Parse(responseBody);
            if (!jsonDoc.RootElement.TryGetProperty("data", out JsonElement usersArray) || usersArray.ValueKind != JsonValueKind.Array)
            {
                _logger.LogError("Invalid JSON structure in user info response: 'data' array not found. Response: {ResponseBody}", responseBody);
                return null;
            }

            if (usersArray.GetArrayLength() == 0)
            {
                _logger.LogError("User info API response contained an empty 'data' array. Response: {ResponseBody}", responseBody);
                return null;
            }

            JsonElement userObject = usersArray[0];
            string? userId = userObject.TryGetProperty("id", out JsonElement idElement) ? idElement.GetString() : null;
            string? loginName = userObject.TryGetProperty("login", out JsonElement loginElement) ? loginElement.GetString() : null;

            if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(loginName))
            {
                _logger.LogError("User ID or Login name missing in user info API response object. Response: {ResponseBody}", responseBody);
                return null;
            }

            _logger.LogDebug("Successfully retrieved user info: ID={UserId}, Login={UserLogin}", userId, loginName);
            return (userId, loginName);
        }
        catch (JsonException jsonEx)
        {
            _logger.LogError(jsonEx, "Error parsing JSON response when getting user info.");
            return null;
        }
        catch (HttpRequestException httpEx)
        {
            _logger.LogError(httpEx, "HTTP network error when getting user info.");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error when getting user info.");
            return null;
        }
    }

    /// <summary>
    /// Saves the obtained authentication tokens securely and updates the application settings
    /// with the new or updated Twitch account information.
    /// </summary>
    /// <param name="userId">The Twitch User ID.</param>
    /// <param name="username">The Twitch login name (username).</param>
    /// <param name="accessToken">The access token.</param>
    /// <param name="refreshToken">The refresh token.</param>
    /// <returns>A <see cref="Task"/> resulting in <c>true</c> if saving was successful, <c>false</c> otherwise.</returns>
    private async Task<bool> SaveAuthenticationAsync(string userId, string username, string accessToken, string refreshToken)
    {
        _logger.LogInformation("Saving authentication data for User ID: {UserId}, Username: {Username}", userId, username);
        try
        {
            string storageKey = $"twitch_{userId}";
            await _tokenStorage.SaveTokensAsync(storageKey, accessToken, refreshToken);
            _logger.LogDebug("Tokens saved to secure storage with key: {StorageKey}", storageKey);

            AppSettings settings = await _settingsService.LoadSettingsAsync();
            TwitchAccount? existingAccount = settings.Connections.TwitchAccounts.FirstOrDefault(a => a.UserId == userId);

            if (existingAccount != null)
            {
                _logger.LogDebug("Updating existing Twitch account in settings for Username: {Username}", username);
                existingAccount.Username = username;
                existingAccount.AutoConnect = true;
            }
            else
            {
                _logger.LogDebug("Adding new Twitch account to settings for Username: {Username}", username);
                settings.Connections.TwitchAccounts.Add(
                    new TwitchAccount
                    {
                        UserId = userId,
                        Username = username,
                        AutoConnect = true,
                    }
                );
            }

            await _settingsService.SaveSettingsAsync(settings);
            _logger.LogInformation("Authentication data saved successfully for Username: {Username}", username);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving authentication data (tokens or settings) for User ID: {UserId}", userId);

            return false;
        }
    }

    /// <summary>
    /// Attempts to refresh the access token for a given Twitch user using the stored refresh token.
    /// If successful, the new tokens are saved. If the refresh token is invalid, the account may be logged out.
    /// </summary>
    /// <param name="userId">The Twitch User ID for whom to refresh the token.</param>
    /// <returns>A <see cref="Task"/> resulting in <c>true</c> if the token was successfully refreshed and saved, <c>false</c> otherwise.</returns>
    public async Task<bool> RefreshTokenAsync(string userId)
    {
        _logger.LogInformation("Attempting to refresh token for Twitch User ID: {UserId}", userId);

        (string? ClientId, string? ClientSecret)? credentials = GetCredentialsFromSettings();
        if (credentials == null || credentials.Value.ClientId == null || credentials.Value.ClientSecret == null)
        {
            _logger.LogError("Cannot refresh token for User ID {UserId}: Client credentials missing or invalid.", userId);
            return false;
        }

        string storageKey = $"twitch_{userId}";
        (string? _, string? RefreshToken) = await _tokenStorage.GetTokensAsync(storageKey);

        if (string.IsNullOrEmpty(RefreshToken))
        {
            _logger.LogWarning("No refresh token found for User ID: {UserId}. Cannot refresh. User may need to log in again.", userId);

            return false;
        }

        string tokenEndpoint = "https://id.twitch.tv/oauth2/token";
        var content = new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                { "client_id", credentials.Value.ClientId },
                { "client_secret", credentials.Value.ClientSecret },
                { "grant_type", "refresh_token" },
                { "refresh_token", RefreshToken },
            }
        );

        try
        {
            HttpResponseMessage response = await _httpClient.PostAsync(tokenEndpoint, content);
            string responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError(
                    "Error refreshing Twitch token for User ID {UserId}. Status: {StatusCode}, Response: {ResponseBody}",
                    userId,
                    response.StatusCode,
                    responseBody
                );

                if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized)
                {
                    _logger.LogWarning("Refresh token for User ID {UserId} is likely invalid or revoked. Logging out account.", userId);
                    await LogoutAsync(userId);
                }

                return false;
            }

            _logger.LogInformation("Token refresh successful for User ID {UserId}. Parsing response...", userId);
            using JsonDocument jsonDoc = JsonDocument.Parse(responseBody);
            JsonElement root = jsonDoc.RootElement;

            string? newAccessToken = root.TryGetProperty("access_token", out JsonElement accessTokenElement) ? accessTokenElement.GetString() : null;
            string? newRefreshToken = root.TryGetProperty("refresh_token", out JsonElement refreshTokenElement)
                ? refreshTokenElement.GetString()
                : null;

            if (string.IsNullOrEmpty(newAccessToken) || string.IsNullOrEmpty(newRefreshToken))
            {
                _logger.LogError(
                    "Error parsing refresh response for User ID {UserId}: New access or refresh token missing. Response: {ResponseBody}",
                    userId,
                    responseBody
                );
                return false;
            }

            _logger.LogInformation("New tokens received for User ID {UserId}. Saving updated tokens...", userId);

            await _tokenStorage.SaveTokensAsync(storageKey, newAccessToken, newRefreshToken);

            _logger.LogInformation("Tokens updated successfully via refresh for User ID {UserId}.", userId);
            return true;
        }
        catch (JsonException jsonEx)
        {
            _logger.LogError(jsonEx, "Error parsing JSON response during token refresh for User ID {UserId}.", userId);
            return false;
        }
        catch (HttpRequestException httpEx)
        {
            _logger.LogError(httpEx, "HTTP network error during token refresh for User ID {UserId}.", userId);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during token refresh for User ID {UserId}.", userId);
            return false;
        }
    }

    /// <summary>
    /// Logs out the specified Twitch user by attempting to revoke the token on Twitch's side,
    /// deleting the stored tokens, and removing the account from application settings.
    /// </summary>
    /// <param name="userId">The Twitch User ID of the account to log out.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous logout operation.</returns>
    public async Task LogoutAsync(string userId)
    {
        _logger.LogInformation("Logging out Twitch User ID: {UserId}", userId);

        (string? ClientId, string? ClientSecret)? cred = GetCredentialsFromSettings();

        string storageKey = $"twitch_{userId}";
        (string? AccessToken, string? _) = await _tokenStorage.GetTokensAsync(storageKey);

        if (!string.IsNullOrEmpty(AccessToken) && !string.IsNullOrEmpty(cred?.ClientId))
        {
            string revokeEndpoint = "https://id.twitch.tv/oauth2/revoke";
            var content = new FormUrlEncodedContent(
                new Dictionary<string, string> { { "client_id", cred.Value.ClientId }, { "token", AccessToken } }
            );
            try
            {
                _logger.LogDebug("Attempting to revoke Twitch token for User ID {UserId}...", userId);
                HttpResponseMessage response = await _httpClient.PostAsync(revokeEndpoint, content);

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogDebug(
                        "Token revoke request sent successfully (Status: {StatusCode}) for User ID {UserId}.",
                        response.StatusCode,
                        userId
                    );
                }
                else
                {
                    string responseBody = await response.Content.ReadAsStringAsync();
                    _logger.LogWarning(
                        "Token revoke request failed with status {StatusCode} for User ID {UserId}. Response: {ResponseBody}",
                        response.StatusCode,
                        userId,
                        responseBody
                    );
                }
            }
            catch (HttpRequestException httpEx)
            {
                _logger.LogWarning(httpEx, "HTTP network error while attempting to revoke token for User ID {UserId}. Continuing logout.", userId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error occurred during token revocation for User ID {UserId}. Continuing logout.", userId);
            }
        }
        else
        {
            _logger.LogDebug("Skipping token revocation for User ID {UserId}: Access token or Client ID missing.", userId);
        }

        _logger.LogDebug("Deleting locally stored tokens for key: {StorageKey}", storageKey);
        await _tokenStorage.DeleteTokensAsync(storageKey);

        try
        {
            AppSettings settings = await _settingsService.LoadSettingsAsync();
            TwitchAccount? accountToRemove = settings.Connections.TwitchAccounts.FirstOrDefault(a => a.UserId == userId);
            if (accountToRemove != null)
            {
                _logger.LogDebug("Removing account {Username} (ID: {UserId}) from settings.", accountToRemove.Username, userId);
                settings.Connections.TwitchAccounts.Remove(accountToRemove);
                await _settingsService.SaveSettingsAsync(settings);
                _logger.LogDebug("Account removed from settings successfully.");
            }
            else
            {
                _logger.LogDebug("Account for User ID {UserId} not found in settings, nothing to remove.", userId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error removing account from settings for User ID {UserId} during logout.", userId);
        }

        _logger.LogInformation("Logout process completed for User ID: {UserId}", userId);
    }

    /// <summary>
    /// Validates the current access token for the given user by calling the Twitch validation endpoint.
    /// If the token is invalid or expired, it attempts to refresh it using the stored refresh token.
    /// </summary>
    /// <param name="userId">The Twitch User ID whose token needs validation.</param>
    /// <returns>
    /// A <see cref="Task"/> resulting in <c>true</c> if the token is currently valid or was successfully refreshed,
    /// <c>false</c> otherwise (e.g., no token found, validation failed and refresh failed).
    /// </returns>
    public async Task<bool> ValidateAndRefreshAccessTokenAsync(string userId)
    {
        _logger.LogInformation("Validating token for Twitch User ID: {UserId}", userId);
        string storageKey = $"twitch_{userId}";
        (string? AccessToken, string? _) = await _tokenStorage.GetTokensAsync(storageKey);

        if (string.IsNullOrEmpty(AccessToken))
        {
            _logger.LogDebug("No access token found locally for User ID: {UserId}. Cannot validate.", userId);
            return false;
        }

        string validateEndpoint = "https://id.twitch.tv/oauth2/validate";
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, validateEndpoint);

            request.Headers.Authorization = new AuthenticationHeaderValue("OAuth", AccessToken);

            HttpResponseMessage response = await _httpClient.SendAsync(request);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogDebug("Token for User ID {UserId} is currently valid.", userId);
                return true;
            }
            else if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                string responseBody = await response.Content.ReadAsStringAsync();
                _logger.LogInformation(
                    "Token for User ID {UserId} is invalid/expired (Status: {StatusCode}). Attempting refresh...",
                    userId,
                    response.StatusCode
                );
                _logger.LogDebug("Validation failure response: {ResponseBody}", responseBody);
                return await RefreshTokenAsync(userId);
            }
            else
            {
                string responseBody = await response.Content.ReadAsStringAsync();
                _logger.LogWarning(
                    "Unexpected validation response for User ID {UserId}. Status: {StatusCode}. Assuming token invalid, attempting refresh...",
                    userId,
                    response.StatusCode
                );
                _logger.LogDebug("Validation failure response: {ResponseBody}", responseBody);

                return await RefreshTokenAsync(userId);
            }
        }
        catch (HttpRequestException httpEx)
        {
            _logger.LogWarning(httpEx, "HTTP network error during token validation for User ID {UserId}. Attempting refresh...", userId);
            return await RefreshTokenAsync(userId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during token validation for User ID {UserId}. Attempting refresh...", userId);
            return await RefreshTokenAsync(userId);
        }
    }
}
