using System.Net;
using System.Text;
using System.Web;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Json;
using Google.Apis.Services;
using Google.Apis.YouTube.v3;
using Google.Apis.YouTube.v3.Data;
using Microsoft.Extensions.Logging;
using StreamWeaver.Core.Models.Settings;
using StreamWeaver.Core.Services.Settings;
using Windows.System;

namespace StreamWeaver.Core.Services.Authentication;

/// <summary>
/// Represents the result of a YouTube authentication attempt.
/// </summary>
/// <param name="Success">Indicates whether the authentication and channel info retrieval were successful.</param>
/// <param name="ChannelId">The ID of the authenticated YouTube channel, if successful.</param>
/// <param name="ChannelName">The title/name of the authenticated YouTube channel, if successful.</param>
/// <param name="ErrorMessage">An error message if the authentication failed.</param>
public record YouTubeAuthResult(bool Success, string? ChannelId = null, string? ChannelName = null, string? ErrorMessage = null);

/// <summary>
/// Handles the Google OAuth 2.0 flow specifically for YouTube API access,
/// including token acquisition, storage, refresh, and channel information retrieval.
/// </summary>
public class YouTubeAuthService
{
    private readonly ISettingsService _settingsService;
    private readonly ITokenStorageService _tokenStorage;
    private readonly HttpClient _httpClient;
    private readonly ILogger<YouTubeAuthService> _logger;

    /// <summary>
    /// The redirect URI registered in the Google Developer Console for this application.
    /// Must use localhost for installed applications.
    /// </summary>
    private const string GoogleRedirectUri = "http://localhost:5081/callback/google";

    /// <summary>
    /// The OAuth scopes required for YouTube functionality.
    /// </summary>
    private readonly string[] _scopes = [YouTubeService.Scope.YoutubeReadonly, YouTubeService.Scope.YoutubeForceSsl, YouTubeService.Scope.Youtube];

    /// <summary>
    /// Temporary storage for the state parameter during the OAuth flow to prevent CSRF attacks.
    /// </summary>
    private static string? s_oauthState;

    /// <summary>
    /// Initializes a new instance of the <see cref="YouTubeAuthService"/> class.
    /// </summary>
    /// <param name="settingsService">The service for managing application settings.</param>
    /// <param name="tokenStorage">The service for securely storing tokens.</param>
    /// <param name="logger">The logger instance.</param>
    /// <exception cref="ArgumentNullException">Thrown if any constructor parameters are null.</exception>
    public YouTubeAuthService(ISettingsService settingsService, ITokenStorageService tokenStorage, ILogger<YouTubeAuthService> logger)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _tokenStorage = tokenStorage ?? throw new ArgumentNullException(nameof(tokenStorage));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _httpClient = new HttpClient();
        _logger.LogInformation("Initialized. Callback URI: {CallbackUri}", GoogleRedirectUri);
    }

    /// <summary>
    /// Initiates the Google OAuth 2.0 authorization code flow for YouTube authentication.
    /// Launches the user's browser, handles the callback, exchanges the code for tokens,
    /// retrieves channel information, and securely stores the tokens.
    /// </summary>
    /// <returns>
    /// A <see cref="Task"/> resulting in a <see cref="YouTubeAuthResult"/> record containing the outcome:
    /// success status, channel ID, channel name, and an error message if applicable.
    /// Note: This method does *not* directly modify application settings; the caller is responsible
    /// for updating settings based on the result (e.g., adding the YouTubeAccount).
    /// </returns>
    public async Task<YouTubeAuthResult> AuthenticateAsync()
    {
        _logger.LogInformation("Starting YouTube authentication process...");
        (string? ClientId, string? ClientSecret)? credentials = GetCredentialsFromSettings();
        if (credentials == null || credentials.Value.ClientId == null || credentials.Value.ClientSecret == null)
        {
            const string errorMsg = "YouTube Client ID or Secret is missing or invalid in settings.";
            _logger.LogWarning(errorMsg);
            return new(false, ErrorMessage: errorMsg);
        }

        s_oauthState = Guid.NewGuid().ToString("N");
        _logger.LogDebug("Generated OAuth State: {OAuthState}", s_oauthState);

        string scopesString = string.Join(" ", _scopes);
        string authUrl =
            $"https://accounts.google.com/o/oauth2/v2/auth"
            + $"?client_id={credentials.Value.ClientId}"
            + $"&redirect_uri={Uri.EscapeDataString(GoogleRedirectUri)}"
            + $"&response_type=code"
            + $"&scope={Uri.EscapeDataString(scopesString)}"
            + $"&state={s_oauthState}"
            + $"&access_type=offline"
            + $"&prompt=consent select_account";

        string? authorizationCode = null;
        string? errorFromCallback = null;
        HttpListener? listener = null;

        try
        {
            _logger.LogInformation("Starting HttpListener on {RedirectUri}...", GoogleRedirectUri);
            listener = new HttpListener();
            listener.Prefixes.Add(GoogleRedirectUri.EndsWith('/') ? GoogleRedirectUri : GoogleRedirectUri + "/");
            listener.Start();

            _logger.LogInformation("Launching browser for Google/YouTube authentication: {AuthUrl}", authUrl);
            bool successLaunch = await Launcher.LaunchUriAsync(new Uri(authUrl));
            if (!successLaunch)
            {
                const string errorMsg = "Failed to launch the default system browser for authentication.";
                _logger.LogWarning(errorMsg);
                listener.Stop();
                return new(false, ErrorMessage: errorMsg);
            }

            _logger.LogInformation("Waiting for Google OAuth callback...");

            HttpListenerContext context = await listener.GetContextAsync().WaitAsync(TimeSpan.FromMinutes(3));
            HttpListenerRequest request = context.Request;
            HttpListenerResponse response = context.Response;
            try
            {
                _logger.LogDebug("Received callback: {CallbackUrl}", request.Url);
                string? receivedState = request.QueryString["state"];
                string? receivedCode = request.QueryString["code"];
                string? receivedError = request.QueryString["error"];

                string? receivedErrorDesc = request.QueryString["error_description"] ?? receivedError;

                if (!string.IsNullOrEmpty(receivedError))
                {
                    errorFromCallback = $"OAuth Error received from Google: {receivedErrorDesc}";
                    _logger.LogError("OAuth Error received from Google: {Error}", receivedErrorDesc);
                    await SendBrowserResponseAsync(
                        response,
                        $"<html><body>{HttpUtility.HtmlEncode(errorFromCallback)}. Please close this window.</body></html>",
                        HttpStatusCode.BadRequest
                    );
                }
                else if (string.IsNullOrEmpty(receivedState) || receivedState != s_oauthState)
                {
                    errorFromCallback = "OAuth state mismatch error. This could indicate a security issue.";
                    _logger.LogError("OAuth State mismatch! Expected: '{ExpectedState}', Received: '{ReceivedState}'", s_oauthState, receivedState);
                    await SendBrowserResponseAsync(
                        response,
                        $"<html><body>{HttpUtility.HtmlEncode(errorFromCallback)} Please close this window and try logging in again.</body></html>",
                        HttpStatusCode.BadRequest
                    );
                }
                else if (string.IsNullOrEmpty(receivedCode))
                {
                    errorFromCallback = "OAuth callback did not contain the required authorization code.";
                    _logger.LogError("OAuth callback did not contain the required authorization code.");
                    await SendBrowserResponseAsync(
                        response,
                        $"<html><body>{HttpUtility.HtmlEncode(errorFromCallback)} Please close this window.</body></html>",
                        HttpStatusCode.BadRequest
                    );
                }
                else
                {
                    authorizationCode = receivedCode;
                    _logger.LogInformation("OAuth authorization code received successfully.");
                    await SendBrowserResponseAsync(
                        response,
                        "<html><body>Authentication successful! You can close this window and return to StreamWeaver.</body></html>"
                    );
                }

                s_oauthState = null;
            }
            finally
            {
                response?.Close();
            }
        }
        catch (TimeoutException ex)
        {
            const string errorMsg = "Google OAuth callback timed out after 3 minutes. Please try again.";
            _logger.LogWarning(ex, errorMsg);
            s_oauthState = null;
            return new(false, ErrorMessage: errorMsg);
        }
        catch (ObjectDisposedException ode) when (ode.ObjectName == "System.Net.HttpListener")
        {
            const string errorMsg = "HttpListener was disposed while waiting for callback.";
            _logger.LogWarning(ode, errorMsg);
            s_oauthState = null;
            return new(false, ErrorMessage: errorMsg);
        }
        catch (HttpListenerException hle)
        {
            string errorMsg = $"HttpListener error during OAuth callback handling: {hle.Message}";
            _logger.LogError(hle, "HttpListener error during OAuth callback handling");
            s_oauthState = null;
            return new(false, ErrorMessage: errorMsg);
        }
        catch (Exception ex)
        {
            string errorMsg = $"An unexpected error occurred during the OAuth listener/callback phase: {ex.Message}";
            _logger.LogError(ex, "An unexpected error occurred during the OAuth listener/callback phase");
            s_oauthState = null;
            return new(false, ErrorMessage: errorMsg);
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

        if (!string.IsNullOrEmpty(errorFromCallback))
        {
            return new(false, ErrorMessage: errorFromCallback);
        }

        if (string.IsNullOrEmpty(authorizationCode))
        {
            const string errorMsg = "Authentication failed: No authorization code was received after the callback.";
            _logger.LogError(errorMsg);
            return new(false, ErrorMessage: errorMsg);
        }

        _logger.LogInformation("Exchanging Google authorization code for tokens...");
        TokenResponse? tokenResponse = await ExchangeCodeForYouTubeTokenAsync(
            authorizationCode,
            credentials.Value.ClientId,
            credentials.Value.ClientSecret
        );
        if (tokenResponse == null || string.IsNullOrEmpty(tokenResponse.AccessToken) || string.IsNullOrEmpty(tokenResponse.RefreshToken))
        {
            const string errorMsg = "Failed to exchange authorization code for tokens, or the response was missing required tokens (Access/Refresh).";
            _logger.LogError(errorMsg);
            return new(false, ErrorMessage: errorMsg);
        }

        _logger.LogInformation(
            "Tokens obtained successfully. Refresh token received: {HasRefreshToken}",
            !string.IsNullOrEmpty(tokenResponse.RefreshToken)
        );

        _logger.LogInformation("Fetching YouTube channel information...");
        (string? ChannelId, string? Title)? channelInfo = await GetAuthenticatedChannelInfoAsync(tokenResponse.AccessToken);
        if (channelInfo == null || string.IsNullOrEmpty(channelInfo.Value.ChannelId))
        {
            const string errorMsg =
                "Successfully authenticated with Google, but failed to retrieve valid YouTube channel information. Ensure the selected Google account has a YouTube channel.";
            _logger.LogWarning(errorMsg);

            return new(false, ErrorMessage: errorMsg);
        }

        _logger.LogInformation(
            "Successfully retrieved channel info - ID: {ChannelId}, Name: {ChannelName}",
            channelInfo.Value.ChannelId,
            channelInfo.Value.Title
        );

        string storageKey = $"youtube_{channelInfo.Value.ChannelId}";
        try
        {
            await _tokenStorage.SaveTokensAsync(storageKey, tokenResponse.AccessToken, tokenResponse.RefreshToken);
            _logger.LogInformation("Tokens saved securely with storage key: {StorageKey}", storageKey);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save YouTube tokens securely for Channel ID {ChannelId}.", channelInfo.Value.ChannelId);

            return new(
                false,
                ChannelId: channelInfo.Value.ChannelId,
                ChannelName: channelInfo.Value.Title,
                ErrorMessage: $"Failed to save YouTube tokens securely for Channel ID {channelInfo.Value.ChannelId}."
            );
        }

        _logger.LogInformation(
            "YouTube authentication process completed successfully for Channel: {ChannelName} ({ChannelId})",
            channelInfo.Value.Title,
            channelInfo.Value.ChannelId
        );
        return new(true, channelInfo.Value.ChannelId, channelInfo.Value.Title);
    }

    /// <summary>
    /// Retrieves the YouTube Client ID and Client Secret from application settings.
    /// Logs warnings if credentials are missing or appear invalid.
    /// </summary>
    /// <returns>A tuple containing the Client ID and Client Secret, or null if they are not configured correctly.</returns>
    private (string? ClientId, string? ClientSecret)? GetCredentialsFromSettings()
    {
        AppSettings settings = _settingsService.CurrentSettings;
        string? clientId = settings.Credentials?.YouTubeApiClientId;
        string? clientSecret = settings.Credentials?.YouTubeApiClientSecret;
        bool isInvalid =
            string.IsNullOrWhiteSpace(clientId)
            || string.IsNullOrWhiteSpace(clientSecret)
            || clientId.StartsWith("YOUR_")
            || clientId.Length < 10
            || clientSecret.StartsWith("YOUR_")
            || clientSecret.Length < 10;
        if (isInvalid)
        {
            _logger.LogWarning(
                "YouTube Client ID or Client Secret is missing, appears to be a placeholder, or is too short in application settings."
            );

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
    private async Task SendBrowserResponseAsync(HttpListenerResponse response, string content, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        try
        {
            byte[] buffer = Encoding.UTF8.GetBytes(content);
            response.ContentLength64 = buffer.Length;
            response.ContentType = "text/html; charset=utf-8";
            response.StatusCode = (int)statusCode;
            using Stream output = response.OutputStream;
            await output.WriteAsync(buffer);
            _logger.LogDebug("Sent HTTP {StatusCode} response to browser callback.", statusCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send HTTP response back to browser via HttpListener.");
        }
    }

    /// <summary>
    /// Exchanges the authorization code for Google OAuth tokens (access and refresh).
    /// </summary>
    /// <param name="code">The authorization code received from Google.</param>
    /// <param name="clientId">The application's Google Client ID.</param>
    /// <param name="clientSecret">The application's Google Client Secret.</param>
    /// <returns>A <see cref="TokenResponse"/> containing the tokens, or null if exchange fails.</returns>
    private async Task<TokenResponse?> ExchangeCodeForYouTubeTokenAsync(string code, string clientId, string clientSecret)
    {
        string tokenEndpoint = GoogleAuthConsts.TokenUrl;
        var content = new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                { "client_id", clientId },
                { "client_secret", clientSecret },
                { "code", code },
                { "grant_type", "authorization_code" },
                { "redirect_uri", GoogleRedirectUri },
            }
        );
        try
        {
            HttpResponseMessage response = await _httpClient.PostAsync(tokenEndpoint, content);
            string responseBody = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError(
                    "Error exchanging Google code for token. Status: {StatusCode}, Response: {ResponseBody}",
                    response.StatusCode,
                    responseBody
                );
                return null;
            }

            _logger.LogDebug("Google token exchange successful. Parsing response...");

            TokenResponse? tokenResponse = NewtonsoftJsonSerializer.Instance.Deserialize<TokenResponse>(responseBody);
            if (tokenResponse == null || string.IsNullOrEmpty(tokenResponse.AccessToken))
            {
                _logger.LogError("Failed to parse token response or access token was missing. Response Body: {ResponseBody}", responseBody);
                return null;
            }

            if (string.IsNullOrEmpty(tokenResponse.RefreshToken))
            {
                _logger.LogWarning(
                    "Refresh token was missing from the initial token exchange response. Offline access might not be possible later. Response Body: {ResponseBody}",
                    responseBody
                );
            }

            return tokenResponse;
        }
        catch (HttpRequestException httpEx)
        {
            _logger.LogError(httpEx, "HTTP network error during Google token exchange.");
            return null;
        }
        catch (Newtonsoft.Json.JsonException jsonEx)
        {
            _logger.LogError(jsonEx, "Error parsing JSON response during Google token exchange.");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during Google token exchange.");
            return null;
        }
    }

    /// <summary>
    /// Fetches the Channel ID and Title for the YouTube channel associated with the authenticated user's Google account.
    /// </summary>
    /// <param name="accessToken">A valid Google OAuth 2.0 access token with YouTube scopes.</param>
    /// <returns>A tuple containing the Channel ID and Title, or null if retrieval fails or no channel exists.</returns>
    private async Task<(string? Id, string? Title)?> GetAuthenticatedChannelInfoAsync(string accessToken)
    {
        _logger.LogDebug("Fetching authenticated user's YouTube channel info via API...");
        try
        {
            GoogleCredential credential = GoogleCredential.FromAccessToken(accessToken);
            using var youtubeService = new YouTubeService(
                new BaseClientService.Initializer() { HttpClientInitializer = credential, ApplicationName = "StreamWeaver" }
            );

            ChannelsResource.ListRequest request = youtubeService.Channels.List("snippet");
            request.Mine = true;
            ChannelListResponse? response = await request.ExecuteAsync();

            Channel? channel = response?.Items?.FirstOrDefault();
            if (channel != null && !string.IsNullOrEmpty(channel.Id) && !string.IsNullOrEmpty(channel.Snippet?.Title))
            {
                _logger.LogDebug("Found YouTube channel: ID={ChannelId}, Title={ChannelTitle}", channel.Id, channel.Snippet.Title);
                return (channel.Id, channel.Snippet.Title);
            }
            else
            {
                _logger.LogWarning(
                    "Could not find valid channel ID or Title in YouTube API response for the authenticated user (mine=true). The Google account might not have a YouTube channel."
                );
                _logger.LogDebug("YouTube API response items count: {ItemCount}", response?.Items?.Count ?? 0);
                return null;
            }
        }
        catch (Google.GoogleApiException apiEx)
        {
            _logger.LogError(
                apiEx,
                "Google API error fetching YouTube channel info. Status: {StatusCode}, Message: {ErrorMessage}",
                apiEx.HttpStatusCode,
                apiEx.Message
            );
            if (apiEx.HttpStatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                _logger.LogWarning("--> Access token might be invalid, expired, or lack necessary permissions.");
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while fetching YouTube channel info.");
            return null;
        }
    }

    /// <summary>
    /// Attempts to refresh the Google OAuth access token using the stored refresh token.
    /// If successful, the new access token (and potentially refresh token) are saved.
    /// </summary>
    /// <param name="channelId">The YouTube Channel ID associated with the tokens to refresh.</param>
    /// <returns>True if the token was successfully refreshed and saved, false otherwise.</returns>
    public async Task<bool> RefreshTokenAsync(string channelId)
    {
        _logger.LogInformation("Attempting to refresh Google token for YouTube Channel ID: {ChannelId}", channelId);
        (string? ClientId, string? ClientSecret)? credentials = GetCredentialsFromSettings();
        if (credentials == null || credentials.Value.ClientId == null || credentials.Value.ClientSecret == null)
        {
            _logger.LogError("Cannot refresh token for Channel ID {ChannelId}: Client credentials missing or invalid.", channelId);
            return false;
        }

        string storageKey = $"youtube_{channelId}";
        (string? _, string? RefreshToken) = await _tokenStorage.GetTokensAsync(storageKey);

        if (string.IsNullOrEmpty(RefreshToken))
        {
            _logger.LogWarning(
                "No refresh token found stored for Channel ID: {ChannelId}. Cannot refresh. User needs to re-authenticate.",
                channelId
            );

            return false;
        }

        string tokenEndpoint = GoogleAuthConsts.TokenUrl;
        var content = new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                { "client_id", credentials.Value.ClientId },
                { "client_secret", credentials.Value.ClientSecret },
                { "refresh_token", RefreshToken },
                { "grant_type", "refresh_token" },
            }
        );

        try
        {
            HttpResponseMessage response = await _httpClient.PostAsync(tokenEndpoint, content);
            string responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError(
                    "Error refreshing Google token for Channel ID {ChannelId}. Status: {StatusCode}, Response: {ResponseBody}",
                    channelId,
                    response.StatusCode,
                    responseBody
                );

                if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized && responseBody.Contains("invalid_grant"))
                {
                    _logger.LogWarning(
                        "Refresh token for Channel ID {ChannelId} is invalid or revoked ('invalid_grant'). Performing local logout.",
                        channelId
                    );
                    await LogoutAsync(channelId);
                }

                return false;
            }

            _logger.LogInformation("Google token refresh successful for Channel ID {ChannelId}. Parsing response...", channelId);
            TokenResponse? tokenResponse = NewtonsoftJsonSerializer.Instance.Deserialize<TokenResponse>(responseBody);

            if (string.IsNullOrEmpty(tokenResponse?.AccessToken))
            {
                _logger.LogError(
                    "Failed to parse refresh response or new access token was missing for Channel ID {ChannelId}. Response Body: {ResponseBody}",
                    channelId,
                    responseBody
                );
                return false;
            }

            string? newRefreshTokenToStore = tokenResponse.RefreshToken ?? RefreshToken;
            bool receivedNewRefreshToken = tokenResponse.RefreshToken != null;
            _logger.LogDebug("New access token received. Received new refresh token: {ReceivedNewRefreshToken}", receivedNewRefreshToken);

            _logger.LogInformation("Saving updated tokens from refresh for Channel ID {ChannelId}...", channelId);
            await _tokenStorage.SaveTokensAsync(storageKey, tokenResponse.AccessToken, newRefreshTokenToStore);

            _logger.LogInformation("Tokens updated successfully via refresh for Channel ID {ChannelId}.", channelId);
            return true;
        }
        catch (HttpRequestException httpEx)
        {
            _logger.LogError(httpEx, "HTTP network error during Google token refresh for Channel ID {ChannelId}.", channelId);
            return false;
        }
        catch (Newtonsoft.Json.JsonException jsonEx)
        {
            _logger.LogError(jsonEx, "Error parsing JSON response during Google token refresh for Channel ID {ChannelId}.", channelId);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during Google token refresh for Channel ID {ChannelId}.", channelId);
            return false;
        }
    }

    /// <summary>
    /// Logs out the specified YouTube channel by attempting to revoke the Google token,
    /// deleting the stored tokens locally.
    /// </summary>
    /// <param name="channelId">The YouTube Channel ID of the account to log out.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous logout operation.</returns>
    /// <remarks>
    /// This method focuses on local cleanup and best-effort revocation.
    /// It does *not* modify application settings (e.g., removing the YouTubeAccount);
    /// that responsibility lies with the caller (like UnifiedEventService or a ViewModel).
    /// </remarks>
    public async Task LogoutAsync(string channelId)
    {
        _logger.LogInformation("Logging out YouTube Channel ID: {ChannelId}", channelId);
        string storageKey = $"youtube_{channelId}";
        (string? AccessToken, string? RefreshToken) = await _tokenStorage.GetTokensAsync(storageKey);

        string? tokenToRevoke = RefreshToken ?? AccessToken;

        if (!string.IsNullOrEmpty(tokenToRevoke))
        {
            string revokeEndpoint = GoogleAuthConsts.RevokeTokenUrl;
            var content = new FormUrlEncodedContent(new Dictionary<string, string> { { "token", tokenToRevoke } });
            try
            {
                _logger.LogDebug("Attempting to revoke Google token (Refresh or Access) for Channel ID {ChannelId}...", channelId);
                HttpResponseMessage response = await _httpClient.PostAsync(revokeEndpoint, content);

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation(
                        "Google token revoke request sent successfully (Status: {StatusCode}) for Channel ID {ChannelId}.",
                        response.StatusCode,
                        channelId
                    );
                }
                else
                {
                    string responseBody = await response.Content.ReadAsStringAsync();
                    _logger.LogWarning(
                        "Google token revoke request failed with status {StatusCode} for Channel ID {ChannelId}. Response: {ResponseBody}",
                        response.StatusCode,
                        channelId,
                        responseBody
                    );
                }
            }
            catch (HttpRequestException httpEx)
            {
                _logger.LogWarning(
                    httpEx,
                    "HTTP network error while attempting to revoke Google token for Channel ID {ChannelId}. Continuing local logout.",
                    channelId
                );
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Error occurred during Google token revocation for Channel ID {ChannelId}. Continuing local logout.",
                    channelId
                );
            }
        }
        else
        {
            _logger.LogDebug("Skipping Google token revocation for Channel ID {ChannelId}: No access or refresh token found locally.", channelId);
        }

        _logger.LogDebug("Deleting locally stored tokens for key: {StorageKey}", storageKey);
        await _tokenStorage.DeleteTokensAsync(storageKey);

        _logger.LogInformation("Local token cleanup complete for YouTube Channel ID: {ChannelId}.", channelId);
    }

    /// <summary>
    /// Validates the current Google access token by calling the token info endpoint.
    /// If the token is invalid or expired, it attempts to refresh it using the stored refresh token.
    /// </summary>
    /// <param name="channelId">The YouTube Channel ID whose token needs validation.</param>
    /// <returns>True if the token is valid or was successfully refreshed, false otherwise.</returns>
    public async Task<bool> ValidateAndRefreshAccessTokenAsync(string channelId)
    {
        _logger.LogInformation("Validating Google token for YouTube Channel ID: {ChannelId}", channelId);
        string storageKey = $"youtube_{channelId}";
        (string? AccessToken, string? RefreshToken) = await _tokenStorage.GetTokensAsync(storageKey);

        if (string.IsNullOrEmpty(AccessToken))
        {
            _logger.LogDebug("No access token found locally for Channel ID: {ChannelId}.", channelId);

            if (!string.IsNullOrEmpty(RefreshToken))
            {
                _logger.LogInformation(
                    "No access token found, but refresh token exists. Attempting refresh for Channel ID {ChannelId}...",
                    channelId
                );
                return await RefreshTokenAsync(channelId);
            }
            else
            {
                _logger.LogWarning("No access or refresh token found locally for Channel ID {ChannelId}. Cannot validate or refresh.", channelId);
                return false;
            }
        }

        string validateEndpoint = $"{GoogleAuthConsts.TokenInfoUrl}?access_token={Uri.EscapeDataString(AccessToken)}";
        try
        {
            HttpResponseMessage response = await _httpClient.GetAsync(validateEndpoint);
            string responseBody = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                _logger.LogDebug("Token for Channel ID {ChannelId} is valid according to tokeninfo endpoint.", channelId);
                return true;
            }
            else if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized)
            {
                _logger.LogInformation(
                    "Token validation failed for Channel ID {ChannelId} (Status: {StatusCode}). Assuming invalid/expired. Attempting refresh...",
                    channelId,
                    response.StatusCode
                );
                _logger.LogDebug("Tokeninfo failure response: {ResponseBody}", responseBody);
                return await RefreshTokenAsync(channelId);
            }
            else
            {
                _logger.LogWarning(
                    "Unexpected token validation response for Channel ID {ChannelId}. Status: {StatusCode}. Assuming invalid, attempting refresh...",
                    channelId,
                    response.StatusCode
                );
                _logger.LogDebug("Tokeninfo failure response: {ResponseBody}", responseBody);

                return await RefreshTokenAsync(channelId);
            }
        }
        catch (HttpRequestException httpEx)
        {
            _logger.LogWarning(httpEx, "HTTP network error during token validation for Channel ID {ChannelId}. Attempting refresh...", channelId);
            return await RefreshTokenAsync(channelId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during token validation for Channel ID {ChannelId}. Attempting refresh...", channelId);
            return await RefreshTokenAsync(channelId);
        }
    }
}

/// <summary>
/// Helper class for Google OAuth constants.
/// </summary>
internal static class GoogleAuthConsts
{
    /// <summary>
    /// Google's OAuth 2.0 token endpoint URL.
    /// </summary>
    public const string TokenUrl = "https://oauth2.googleapis.com/token";

    /// <summary>
    /// Google's OAuth 2.0 token revocation endpoint URL.
    /// </summary>
    public const string RevokeTokenUrl = "https://oauth2.googleapis.com/revoke";

    /// <summary>
    /// Google's OAuth 2.0 token information endpoint URL.
    /// </summary>
    public const string TokenInfoUrl = "https://www.googleapis.com/oauth2/v3/tokeninfo";

    /// <summary>
    /// Google's OAuth 2.0 authorization endpoint URL.
    /// </summary>
    public const string AuthorizationUrl = "https://accounts.google.com/o/oauth2/v2/auth";
}
