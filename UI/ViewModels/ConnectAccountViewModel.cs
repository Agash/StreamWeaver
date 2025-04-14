using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using StreamWeaver.Core.Models.Settings;
using StreamWeaver.Core.Services.Authentication;
using StreamWeaver.Core.Services.Settings;

namespace StreamWeaver.UI.ViewModels;

/// <summary>
/// Enum representing the different types of accounts that can be connected.
/// </summary>
public enum AccountType
{
    Twitch,
    YouTube,
    Streamlabs,
}

/// <summary>
/// ViewModel for the account connection dialog, handling logic for different account types.
/// </summary>
public partial class ConnectAccountViewModel : ObservableObject
{
    private readonly ILogger<ConnectAccountViewModel> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly AccountType _accountType;

    /// <summary>
    /// Gets or sets the title displayed in the connection dialog.
    /// </summary>
    [ObservableProperty]
    public partial string DialogTitle { get; set; } = "Connect Account";

    /// <summary>
    /// Gets or sets the descriptive text shown in the connection dialog.
    /// </summary>
    [ObservableProperty]
    public partial string Description { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the current connection type is Streamlabs, used to show specific UI elements.
    /// </summary>
    [ObservableProperty]
    public partial bool IsStreamlabs { get; set; } = false;

    /// <summary>
    /// Gets or sets a value indicating whether the current connection type uses OAuth, used to show specific UI elements.
    /// </summary>
    [ObservableProperty]
    public partial bool IsOAuthPlatform { get; set; } = false;

    /// <summary>
    /// Gets or sets the Streamlabs Socket API Token entered by the user. Only relevant for Streamlabs connections.
    /// </summary>
    [ObservableProperty]
    public partial string? StreamlabsToken { get; set; }

    /// <summary>
    /// Gets or sets the error message to display if the connection fails.
    /// </summary>
    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether an error has occurred during the connection process.
    /// </summary>
    [ObservableProperty]
    public partial bool HasError { get; set; } = false;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConnectAccountViewModel"/> class.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="accountType">The type of account being connected.</param>
    /// <param name="serviceProvider">The service provider for resolving dependencies.</param>
    public ConnectAccountViewModel(ILogger<ConnectAccountViewModel> logger, AccountType accountType, IServiceProvider serviceProvider)
    {
        _logger = logger;
        _accountType = accountType;
        _serviceProvider = serviceProvider;
        SetupDialog();
    }

    /// <summary>
    /// Sets up the dialog's title, description, and visibility flags based on the account type.
    /// </summary>
    private void SetupDialog()
    {
        IsStreamlabs = _accountType == AccountType.Streamlabs;
        IsOAuthPlatform = _accountType is AccountType.Twitch or AccountType.YouTube;

        DialogTitle = $"Connect {_accountType}";
        Description = _accountType switch
        {
            AccountType.Streamlabs =>
                "Enter your Streamlabs Socket API Token to receive donation and other alerts. Find this in your Streamlabs dashboard under Settings > API Settings > API Tokens > Socket API Token.",
            AccountType.Twitch => "Authenticate with Twitch to enable chat integration, event handling, and sending messages.",
            AccountType.YouTube => "Authenticate with YouTube to enable live chat integration, event handling, and sending messages.",
            _ => "Connect your account.",
        };
        _logger.LogDebug(
            "Dialog setup complete for {AccountType}. IsStreamlabs={IsStreamlabs}, IsOAuth={IsOAuth}",
            _accountType,
            IsStreamlabs,
            IsOAuthPlatform
        );
    }

    /// <summary>
    /// Handles the primary action (e.g., button click) to initiate the connection process.
    /// </summary>
    /// <returns>True if the initial connection step succeeded, false otherwise.</returns>
    public async Task<bool> HandleConnectAsync()
    {
        ErrorMessage = null;
        HasError = false;
        _logger.LogInformation("Handling connect action for {AccountType}.", _accountType);

        try
        {
            return _accountType switch
            {
                AccountType.Streamlabs => await ConnectStreamlabsAsync(),
                AccountType.Twitch => await ConnectTwitchAsync(),
                AccountType.YouTube => await ConnectYouTubeAsync(),
                _ => throw new InvalidOperationException($"Unsupported account type: {_accountType}"),
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An unexpected error occurred during connect handle for {AccountType}", _accountType);
            SetError($"An unexpected error occurred: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Handles the connection logic specific to Streamlabs (saving the token).
    /// </summary>
    /// <returns>True if the token was saved successfully, false otherwise.</returns>
    private async Task<bool> ConnectStreamlabsAsync()
    {
        if (string.IsNullOrWhiteSpace(StreamlabsToken))
        {
            SetError("Streamlabs Socket API Token cannot be empty.");
            return false;
        }

        _logger.LogInformation("Attempting to save Streamlabs token.");

        ITokenStorageService tokenStorage = (ITokenStorageService)_serviceProvider.GetService(typeof(ITokenStorageService))!;
        ISettingsService settingsService = (ISettingsService)_serviceProvider.GetService(typeof(ISettingsService))!;

        string streamlabsStorageKey = "streamlabs_main_socket_token";

        try
        {
            await tokenStorage.SaveTokensAsync(streamlabsStorageKey, StreamlabsToken, null);

            AppSettings settings = await settingsService.LoadSettingsAsync();
            settings.Connections.StreamlabsTokenId = streamlabsStorageKey;
            settings.Connections.EnableStreamlabs = true;
            await settingsService.SaveSettingsAsync(settings);

            _logger.LogInformation("Streamlabs token saved securely and settings updated to enable Streamlabs.");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save Streamlabs token or update settings.");
            SetError($"Failed to save Streamlabs token: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Handles the connection logic specific to Twitch (initiating OAuth flow).
    /// </summary>
    /// <returns>True if the OAuth flow was initiated successfully, false otherwise.</returns>
    private async Task<bool> ConnectTwitchAsync()
    {
        _logger.LogInformation("Initiating Twitch OAuth flow.");

        TwitchAuthService twitchAuth = (TwitchAuthService)_serviceProvider.GetService(typeof(TwitchAuthService))!;

        try
        {
            bool success = await twitchAuth.InitiateLoginAsync();

            if (!success)
            {
                _logger.LogWarning("Twitch authentication flow did not complete successfully (cancelled or failed).");
                SetError("Twitch authentication failed or was cancelled. Please check logs or try again.");
            }
            else
            {
                _logger.LogInformation("Twitch authentication flow completed successfully.");
            }

            return success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred during Twitch OAuth initiation.");
            SetError($"An error occurred during Twitch login: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Handles the connection logic specific to YouTube (initiating OAuth flow).
    /// **Note:** This method requires refactoring as indicated by TODOs in the original code.
    /// </summary>
    /// <returns>True if the OAuth flow and settings update succeeded, false otherwise.</returns>
    private async Task<bool> ConnectYouTubeAsync()
    {
        _logger.LogInformation("Initiating YouTube OAuth flow.");

        YouTubeAuthService youtubeAuth = (YouTubeAuthService)_serviceProvider.GetService(typeof(YouTubeAuthService))!;

        try
        {
            YouTubeAuthResult authResult = await youtubeAuth.AuthenticateAsync();

            if (authResult.Success && !string.IsNullOrEmpty(authResult.ChannelId))
            {
                _logger.LogInformation(
                    "YouTube authentication successful for {ChannelName} ({ChannelId}). Updating settings.",
                    authResult.ChannelName ?? "Unknown Channel",
                    authResult.ChannelId
                );

                ISettingsService settingsService = (ISettingsService)_serviceProvider.GetService(typeof(ISettingsService))!;
                AppSettings settings = await settingsService.LoadSettingsAsync();

                if (!settings.Connections.YouTubeAccounts.Any(a => a.ChannelId == authResult.ChannelId))
                {
                    settings.Connections.YouTubeAccounts.Add(
                        new YouTubeAccount
                        {
                            ChannelId = authResult.ChannelId,
                            ChannelName = authResult.ChannelName ?? "YouTube User",
                            AutoConnect = true,
                        }
                    );
                    await settingsService.SaveSettingsAsync(settings);
                    _logger.LogInformation(
                        "Added new YouTube account to settings: {ChannelName} ({ChannelId})",
                        authResult.ChannelName ?? "YouTube User",
                        authResult.ChannelId
                    );
                }
                else
                {
                    _logger.LogInformation(
                        "YouTube account already exists in settings: {ChannelName} ({ChannelId})",
                        authResult.ChannelName ?? "Existing User",
                        authResult.ChannelId
                    );
                }

                return true;
            }
            else
            {
                _logger.LogWarning(
                    "YouTube authentication flow did not complete successfully. Error: {ErrorMessage}",
                    authResult.ErrorMessage ?? "No error message provided."
                );
                SetError($"YouTube authentication failed or was cancelled: {authResult.ErrorMessage ?? "Please try again."}");
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred during YouTube OAuth initiation or settings update.");
            SetError($"An error occurred during YouTube login: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Handles the cancellation action (e.g., closing the dialog).
    /// </summary>
    public void HandleCancel() => _logger.LogInformation("Connect account dialog cancelled by user for {AccountType}.", _accountType);

    /// <summary>
    /// Sets the error message and updates the error state flag.
    /// </summary>
    /// <param name="message">The error message to display.</param>
    private void SetError(string message)
    {
        ErrorMessage = message;
        HasError = true;

        _logger.LogError("Connection error set for user: {ErrorMessage}", message);
    }
}
