using System.Collections.Concurrent;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using StreamWeaver.Core.Messaging;
using StreamWeaver.Core.Models.Events;
using StreamWeaver.Core.Models.Events.Messages;
using StreamWeaver.Core.Models.Settings;
using StreamWeaver.Core.Plugins;
using StreamWeaver.Core.Services.Authentication;
using StreamWeaver.Core.Services.Platforms;
using StreamWeaver.Core.Services.Settings;

namespace StreamWeaver.Core.Services;

/// <summary>
/// Central service to manage platform connections and distribute events via the Messenger.
/// Orchestrates authentication, connection, disconnection, and message sending across multiple accounts and platforms.
/// </summary>
public partial class UnifiedEventService(
    ILogger<UnifiedEventService> logger,
    ISettingsService settingsService,
    IMessenger messenger,
    ITokenStorageService tokenStorage,
    ITwitchClient twitchClient,
    TwitchAuthService twitchAuthService,
    TwitchApiService twitchApiService,
    IYouTubeClient youTubeClient,
    YouTubeAuthService youTubeAuthService,
    IStreamlabsClient streamlabsClient,
    IEmoteBadgeService emoteBadgeService,
    PluginService pluginService,
    DispatcherQueue dispatcherQueue
) : IDisposable
{
    private readonly ILogger<UnifiedEventService> _logger = logger;
    private readonly ISettingsService _settingsService = settingsService;
    private readonly IMessenger _messenger = messenger;
    private readonly ITokenStorageService _tokenStorage = tokenStorage;
    private readonly ITwitchClient _twitchClient = twitchClient;
    private readonly TwitchAuthService _twitchAuthService = twitchAuthService;
    private readonly TwitchApiService _twitchApiService = twitchApiService;
    private readonly IYouTubeClient _youTubeClient = youTubeClient;
    private readonly YouTubeAuthService _youTubeAuthService = youTubeAuthService;
    private readonly IStreamlabsClient _streamlabsClient = streamlabsClient;
    private readonly IEmoteBadgeService _emoteBadgeService = emoteBadgeService;
    private readonly PluginService _pluginService = pluginService;
    private readonly DispatcherQueue _dispatcherQueue = dispatcherQueue;

    // Dictionary mapping a unique account key (e.g., "twitch_12345", "youtube_UC...")
    // to the corresponding UI model object (TwitchAccount, YouTubeAccount).
    // Used for updating UI status based on backend events.
    private readonly ConcurrentDictionary<string, object> _accountMap = new();

    private AppSettings _currentSettings = new();
    private bool _isDisposed = false;

    /// <summary>
    /// Initializes the service, loads settings, global data, and connects configured accounts.
    /// </summary>
    public async Task InitializeAsync()
    {
        _logger.LogInformation("Initializing UnifiedEventService...");
        await LoadAndProcessInitialSettingsAsync();

        try
        {
            await _emoteBadgeService.LoadGlobalTwitchDataAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading global emote/badge data on init");
        }

        await ConnectAllConfiguredAsync(true);
        _messenger.Send(new ConnectionsUpdatedMessage());

        _logger.LogInformation("UnifiedEventService Initialization complete.");
    }

    /// <summary>
    /// Loads initial settings, builds the account map, and subscribes to settings changes.
    /// </summary>
    private async Task LoadAndProcessInitialSettingsAsync()
    {
        _currentSettings = await _settingsService.LoadSettingsAsync();
        _settingsService.SettingsUpdated += OnSettingsUpdated;
        BuildAccountMap();
        HookCollectionChangedHandlers(_currentSettings.Connections);
    }

    /// <summary>
    /// Populates the _accountMap dictionary based on accounts present in _currentSettings.
    /// </summary>
    private void BuildAccountMap()
    {
        _accountMap.Clear();
        if (_currentSettings.Connections?.TwitchAccounts != null)
        {
            foreach (TwitchAccount acc in _currentSettings.Connections.TwitchAccounts)
            {
                if (!string.IsNullOrEmpty(acc.UserId))
                {
                    _accountMap[$"twitch_{acc.UserId}"] = acc;
                }
            }
        }

        if (_currentSettings.Connections?.YouTubeAccounts != null)
        {
            foreach (YouTubeAccount acc in _currentSettings.Connections.YouTubeAccounts)
            {
                if (!string.IsNullOrEmpty(acc.ChannelId))
                {
                    _accountMap[$"youtube_{acc.ChannelId}"] = acc;
                }
            }
        }

        _logger.LogDebug("Account map rebuilt. Count: {Count}", _accountMap.Count);
    }

    /// <summary>
    /// Attaches CollectionChanged event handlers to the account lists within ConnectionSettings.
    /// </summary>
    private void HookCollectionChangedHandlers(ConnectionSettings? connections)
    {
        if (connections?.TwitchAccounts != null)
        {
            connections.TwitchAccounts.CollectionChanged -= TwitchAccounts_CollectionChanged;
            connections.TwitchAccounts.CollectionChanged += TwitchAccounts_CollectionChanged;
        }

        if (connections?.YouTubeAccounts != null)
        {
            connections.YouTubeAccounts.CollectionChanged -= YouTubeAccounts_CollectionChanged;
            connections.YouTubeAccounts.CollectionChanged += YouTubeAccounts_CollectionChanged;
        }
    }

    /// <summary>
    /// Detaches CollectionChanged event handlers from the account lists.
    /// </summary>
    private void UnhookCollectionChangedHandlers(ConnectionSettings? connections)
    {
        if (connections?.TwitchAccounts != null)
        {
            connections.TwitchAccounts.CollectionChanged -= TwitchAccounts_CollectionChanged;
        }

        if (connections?.YouTubeAccounts != null)
        {
            connections.YouTubeAccounts.CollectionChanged -= YouTubeAccounts_CollectionChanged;
        }
    }

    // --- Collection Change Handlers ---
    private async void TwitchAccounts_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        _logger.LogInformation("TwitchAccounts collection changed (Action: {Action})", e.Action);
        if (_isDisposed)
            return;
        await ProcessAccountCollectionChangesAsync<TwitchAccount>(
            e,
            _twitchClient,
            ConnectTwitchAccountAsync,
            LogoutTwitchAccountAsync,
            a => a?.UserId
        );
        _messenger.Send(new ConnectionsUpdatedMessage());
    }

    private async void YouTubeAccounts_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        _logger.LogInformation("YouTubeAccounts collection changed (Action: {Action})", e.Action);
        if (_isDisposed)
            return;
        await ProcessAccountCollectionChangesAsync<YouTubeAccount>(
            e,
            _youTubeClient,
            ConnectYouTubeAccountAsync,
            LogoutYouTubeAccountAsync,
            a => a?.ChannelId
        );
        _messenger.Send(new ConnectionsUpdatedMessage());
    }

    /// <summary>
    /// Generic handler for processing add/remove changes in account collections.
    /// Connects newly added accounts (if AutoConnect is true) and logs out removed accounts.
    /// </summary>
    /// <typeparam name="T">The type of account model (e.g., TwitchAccount, YouTubeAccount).</typeparam>
    /// <param name="e">The collection change event arguments.</param>
    /// <param name="platformClient">The client interface for the platform (e.g., ITwitchClient, IYouTubeClient).</param>
    /// <param name="connectFunc">A function to connect a specific account of type T.</param>
    /// <param name="logoutFunc">A function to log out/disconnect an account by its ID.</param>
    /// <param name="idSelector">A function to extract the unique ID from an account model of type T.</param>
    private async Task ProcessAccountCollectionChangesAsync<T>(
        NotifyCollectionChangedEventArgs e,
        object platformClient, // Not directly used here now, logic relies on funcs
        Func<T, Task> connectFunc,
        Func<string, Task> logoutFunc,
        Func<T?, string?> idSelector
    )
        where T : ObservableObject
    {
        if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems != null)
        {
            foreach (T? account in e.NewItems.OfType<T>())
            {
                if (account == null)
                    continue;
                string? accountId = idSelector(account);
                if (accountId == null)
                    continue;
                string mapKey = $"{GetPlatformPrefix(account)}_{accountId}";
                if (account is YouTubeAccount ytAcc) // Update map with correct type
                {
                    _accountMap[mapKey] = ytAcc;
                }
                else if (account is TwitchAccount twitchAcc)
                {
                    _accountMap[mapKey] = twitchAcc;
                }

                _logger.LogInformation("Account added via collection change ({MapKey}). Checking AutoConnect...", mapKey);
                if (GetAutoConnect(account))
                {
                    await connectFunc(account); // Attempt connection
                }
                else
                {
                    UpdateAccountModelStatus(accountId, ConnectionStatus.Disconnected, "Manual connection required.");
                }
            }
        }
        else if (e.Action == NotifyCollectionChangedAction.Remove && e.OldItems != null)
        {
            List<Task> logoutTasks = [];
            foreach (T? account in e.OldItems.OfType<T>())
            {
                if (account == null)
                    continue;
                string? accountId = idSelector(account);
                if (accountId == null)
                    continue;
                string mapKey = $"{GetPlatformPrefix(account)}_{accountId}";
                _accountMap.TryRemove(mapKey, out _);

                _logger.LogInformation("Account removed via collection change ({MapKey}). Logging out...", mapKey);
                // Logout function handles token removal AND client disconnection
                logoutTasks.Add(logoutFunc(accountId));
            }

            await Task.WhenAll(logoutTasks);
            _logger.LogInformation("Completed {Count} account removal/logout tasks.", logoutTasks.Count);
        }
        else if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            _logger.LogWarning("Account list reset detected for {AccountType}. Rebuilding connections.", typeof(T).Name);
            await DisconnectAllAsync(); // Ensure clean slate
            BuildAccountMap(); // Rebuild map from the (now likely empty) collection

            await ConnectAllConfiguredAsync();
        }
    }

    /// <summary>
    /// Gets a platform prefix string based on the account model type.
    /// </summary>
    /// <param name="account">The account model object.</param>
    /// <returns>A lowercase platform prefix ("twitch", "youtube") or "unknown".</returns>
    private static string GetPlatformPrefix(object account) =>
        account switch
        {
            TwitchAccount _ => "twitch",
            YouTubeAccount _ => "youtube",
            _ => "unknown",
        };

    /// <summary>
    /// Handles updates to the application settings. Compares old and new settings
    /// to intelligently connect/disconnect accounts or update platform clients.
    /// </summary>
    private async void OnSettingsUpdated(object? sender, EventArgs e)
    {
        if (_isDisposed)
            return;
        _logger.LogInformation("Settings updated event received. Processing changes...");

        AppSettings newSettings = _settingsService.CurrentSettings;
        ConnectionSettings? oldConnections = _currentSettings?.Connections;
        ConnectionSettings? newConnections = newSettings.Connections;

        if (newConnections == null)
        {
            _logger.LogWarning("Received settings update but new ConnectionSettings are null. Aborting processing.");
            return;
        }

        UnhookCollectionChangedHandlers(oldConnections);

        bool twitchCredsChanged =
            _currentSettings?.Credentials?.TwitchApiClientId != newSettings.Credentials?.TwitchApiClientId
            || _currentSettings?.Credentials?.TwitchApiClientSecret != newSettings.Credentials?.TwitchApiClientSecret;
        bool youtubeCredsChanged =
            _currentSettings?.Credentials?.YouTubeApiClientId != newSettings.Credentials?.YouTubeApiClientId
            || _currentSettings?.Credentials?.YouTubeApiClientSecret != newSettings.Credentials?.YouTubeApiClientSecret;
        bool youtubeDebugIdChanged = oldConnections?.DebugYouTubeLiveChatId != newConnections.DebugYouTubeLiveChatId;

        if (twitchCredsChanged)
            _logger.LogInformation("Twitch credentials changed detected.");
        if (youtubeCredsChanged)
            _logger.LogInformation("YouTube credentials changed detected.");
        if (youtubeDebugIdChanged)
            _logger.LogInformation("YouTube Debug LiveChatId changed detected.");

        // Compare Twitch Accounts
        await CompareAndProcessAccountsAsync(
            oldConnections?.TwitchAccounts,
            newConnections.TwitchAccounts,
            _twitchClient,
            ConnectTwitchAccountAsync,
            LogoutTwitchAccountAsync,
            a => a?.UserId,
            twitchCredsChanged,
            (a1, a2) => a1.AutoConnect == a2.AutoConnect
        );

        // Compare YouTube Accounts (add OverrideVideoId to comparison)
        await CompareAndProcessAccountsAsync(
            oldConnections?.YouTubeAccounts,
            newConnections.YouTubeAccounts,
            _youTubeClient,
            ConnectYouTubeAccountAsync,
            LogoutYouTubeAccountAsync,
            a => a?.ChannelId,
            youtubeCredsChanged || youtubeDebugIdChanged,
            (a1, a2) => a1.AutoConnect == a2.AutoConnect && a1.OverrideVideoId == a2.OverrideVideoId
        );

        // Compare Streamlabs Settings
        bool streamlabsEnableChanged = oldConnections?.EnableStreamlabs != newConnections.EnableStreamlabs;
        bool streamlabsTokenChanged = oldConnections?.StreamlabsTokenId != newConnections.StreamlabsTokenId;
        bool streamlabsActionNeeded = streamlabsEnableChanged || (newConnections.EnableStreamlabs && streamlabsTokenChanged);

        if (streamlabsActionNeeded)
        {
            _logger.LogInformation(
                "Streamlabs settings changed (Enabled: {Enabled}, Token Changed: {TokenChanged})",
                newConnections.EnableStreamlabs,
                streamlabsTokenChanged
            );
            if (_streamlabsClient.Status != ConnectionStatus.Disconnected)
                await _streamlabsClient.DisconnectAsync();
            if (newConnections.EnableStreamlabs)
                await ConnectStreamlabsAsync();
        }
        else if (newConnections.EnableStreamlabs && _streamlabsClient.Status == ConnectionStatus.Disconnected)
        {
            _logger.LogInformation("Streamlabs enabled but disconnected, attempting reconnect on settings update...");
            await ConnectStreamlabsAsync();
        }

        _currentSettings = newSettings;
        HookCollectionChangedHandlers(newConnections);
        BuildAccountMap();

        _messenger.Send(new ConnectionsUpdatedMessage());
        _logger.LogInformation("Finished processing settings changes.");
    }

    /// <summary>
    /// Generic comparison logic for account lists (e.g., TwitchAccounts, YouTubeAccounts).
    /// Detects added, removed, or modified accounts based on settings changes.
    /// </summary>
    /// <typeparam name="T">The type of account model (e.g., TwitchAccount, YouTubeAccount).</typeparam>
    /// <param name="oldAccounts">The collection of accounts from the previous settings state.</param>
    /// <param name="newAccounts">The collection of accounts from the current settings state.</param>
    /// <param name="platformClient">The client interface for the platform.</param>
    /// <param name="connectFunc">A function to connect a specific account of type T.</param>
    /// <param name="logoutFunc">A function to log out/disconnect an account by its ID.</param>
    /// <param name="idSelector">A function to extract the unique ID from an account model of type T.</param>
    /// <param name="forceReconnect">Flag indicating if all relevant accounts should be reconnected regardless of other changes.</param>
    /// <param name="areConnectionSettingsSame">A function to compare relevant connection-related settings between two account models.</param>
    private async Task CompareAndProcessAccountsAsync<T>(
        ICollection<T>? oldAccounts,
        ICollection<T>? newAccounts,
        object platformClient,
        Func<T, Task> connectFunc,
        Func<string, Task> logoutFunc,
        Func<T?, string?> idSelector,
        bool forceReconnect,
        Func<T, T, bool> areConnectionSettingsSame
    )
        where T : ObservableObject
    {
        Dictionary<string, T> oldDict = oldAccounts?.Where(a => idSelector(a) != null).ToDictionary(a => idSelector(a)!, a => a) ?? [];

        Dictionary<string, T> newDict = newAccounts?.Where(a => idSelector(a) != null).ToDictionary(a => idSelector(a)!, a => a) ?? [];

        // --- Process Removed Accounts ---
        List<Task> logoutTasks = [];
        foreach (KeyValuePair<string, T> oldPair in oldDict)
        {
            if (!newDict.ContainsKey(oldPair.Key))
            {
                string accountId = oldPair.Key;
                string mapKey = $"{GetPlatformPrefix(oldPair.Value)}_{accountId}";
                _logger.LogInformation("Account removed or ID missing ({MapKey}), logging out...", mapKey);
                logoutTasks.Add(logoutFunc(accountId));
                _accountMap.TryRemove(mapKey, out _);
            }
        }

        if (logoutTasks.Count > 0)
        {
            await Task.WhenAll(logoutTasks);
            _logger.LogInformation("Completed {Count} account removal/logout tasks.", logoutTasks.Count);
        }

        List<Task> connectTasks = [];
        List<Task> modifyTasks = [];

        foreach (KeyValuePair<string, T> newPair in newDict)
        {
            string accountId = newPair.Key;
            T newAccount = newPair.Value;
            string mapKey = $"{GetPlatformPrefix(newAccount)}_{accountId}";
            if (newAccount is YouTubeAccount ytAccMod)
                _accountMap[mapKey] = ytAccMod;
            else if (newAccount is TwitchAccount twitchAccMod)
                _accountMap[mapKey] = twitchAccMod;

            if (!oldDict.TryGetValue(accountId, out T? oldAccount))
            {
                _logger.LogInformation("New account added ({MapKey}). Checking AutoConnect...", mapKey);
                if (GetAutoConnect(newAccount))
                {
                    connectTasks.Add(connectFunc(newAccount));
                }
                else
                {
                    UpdateAccountModelStatus(accountId, ConnectionStatus.Disconnected, "Manual connection required.");
                }
            }
            else if (
                forceReconnect
                || GetAutoConnect(newAccount) != GetAutoConnect(oldAccount!)
                || !areConnectionSettingsSame(oldAccount!, newAccount)
            )
            {
                _logger.LogInformation("Account modified or reconnect forced ({MapKey}). Processing...", mapKey);
                bool shouldBeConnected = GetAutoConnect(newAccount);

                ConnectionStatus currentStatus = ConnectionStatus.Disconnected;
                if (platformClient is ITwitchClient tc)
                {
                    currentStatus = tc.GetStatus(accountId);
                }
                else if (platformClient is IYouTubeClient yc)
                {
                    currentStatus = yc.GetStatus(accountId);
                }

                // Simplify: Always disconnect/logout first if modification detected, then connect if needed.
                modifyTasks.Add(
                    Task.Run(async () =>
                    {
                        if (forceReconnect || currentStatus != ConnectionStatus.Disconnected)
                        {
                            _logger.LogDebug(
                                "--> [{MapKey}] Modification/force detected (Current: {CurrentStatus}). Logging out/disconnecting first...",
                                mapKey,
                                currentStatus
                            );
                            await logoutFunc(accountId);
                        }
                        else
                        {
                            _logger.LogDebug("--> [{MapKey}] Modification detected but already disconnected. Will only connect if needed.", mapKey);
                        }

                        if (shouldBeConnected)
                        {
                            _logger.LogDebug("--> [{MapKey}] Reconnecting...", mapKey);
                            await connectFunc(newAccount);
                        }
                        else
                        {
                            UpdateAccountModelStatus(accountId, ConnectionStatus.Disconnected, "Auto-connect disabled.");
                        }
                    })
                );
            }
        }

        if (connectTasks.Count > 0)
        {
            await Task.WhenAll(connectTasks);
            _logger.LogInformation("Completed {Count} new account connection tasks.", connectTasks.Count);
        }

        if (modifyTasks.Count > 0)
        {
            await Task.WhenAll(modifyTasks);
            _logger.LogInformation("Completed {Count} account modification tasks.", modifyTasks.Count);
        }
    }

    /// <summary>
    /// Helper to dynamically get the AutoConnect property value from an account object.
    /// </summary>
    /// <typeparam name="T">The type of the account object.</typeparam>
    /// <param name="account">The account object instance.</param>
    /// <returns>True if the AutoConnect property exists and is true, false otherwise.</returns>
    private bool GetAutoConnect<T>(T account)
        where T : ObservableObject
    {
        System.Reflection.PropertyInfo? propInfo = typeof(T).GetProperty("AutoConnect");
        if (propInfo != null && propInfo.PropertyType == typeof(bool))
        {
            return (bool)(propInfo.GetValue(account) ?? false);
        }

        _logger.LogWarning("Could not find 'AutoConnect' property on type {AccountType}. Assuming false.", typeof(T).Name);
        return false;
    }

    /// <summary>
    /// Attempts to connect all accounts marked for AutoConnect in the current settings.
    /// </summary>
    /// <param name="isInitialConnect">Flag indicating if this is the first connection attempt on startup.</param>
    public async Task ConnectAllConfiguredAsync(bool isInitialConnect = false)
    {
        // Reload settings only if not the initial call, to ensure we have the latest config
        if (!isInitialConnect)
        {
            _currentSettings = await _settingsService.LoadSettingsAsync();
            BuildAccountMap();
        }

        _logger.LogInformation("Connecting all configured accounts... (Initial: {IsInitial})", isInitialConnect);
        List<Task> tasks = [];

        // --- Twitch Connections ---
        if (_currentSettings.Connections?.TwitchAccounts != null)
        {
            foreach (TwitchAccount twitchAccount in _currentSettings.Connections.TwitchAccounts.ToList())
            {
                if (twitchAccount.AutoConnect && !string.IsNullOrEmpty(twitchAccount.UserId))
                {
                    tasks.Add(ConnectTwitchAccountAsync(twitchAccount));
                }
                else if (!string.IsNullOrEmpty(twitchAccount.UserId))
                {
                    UpdateAccountModelStatus(twitchAccount.UserId!, ConnectionStatus.Disconnected, "Auto-connect disabled.");
                }
            }
        }

        // --- YouTube Connections ---
        if (_currentSettings.Connections?.YouTubeAccounts != null)
        {
            foreach (YouTubeAccount ytAccount in _currentSettings.Connections.YouTubeAccounts.ToList())
            {
                if (ytAccount.AutoConnect && !string.IsNullOrEmpty(ytAccount.ChannelId))
                {
                    tasks.Add(ConnectYouTubeAccountAsync(ytAccount));
                }
                else if (!string.IsNullOrEmpty(ytAccount.ChannelId))
                {
                    UpdateAccountModelStatus(ytAccount.ChannelId!, ConnectionStatus.Disconnected, "Auto-connect disabled.");
                }
            }
        }

        // --- Streamlabs Connection ---
        if (_currentSettings.Connections?.EnableStreamlabs ?? false)
        {
            tasks.Add(ConnectStreamlabsAsync());
        }
        else if (_streamlabsClient.Status != ConnectionStatus.Disconnected)
        {
            tasks.Add(_streamlabsClient.DisconnectAsync());
        }

        await Task.WhenAll(tasks);
        _logger.LogInformation("Finished ConnectAllConfiguredAsync. Initiated {Count} connection tasks.", tasks.Count);
    }

    /// <summary>
    /// Connects a specific Twitch account. Validates token, gets token, calls ITwitchClient.ConnectAsync.
    /// </summary>
    /// <param name="account">The Twitch account details.</param>
    public async Task ConnectTwitchAccountAsync(TwitchAccount account)
    {
        if (_isDisposed || account?.UserId == null)
            return;

        ConnectionStatus currentStatus = _twitchClient.GetStatus(account.UserId);
        if (currentStatus is ConnectionStatus.Connected or ConnectionStatus.Connecting)
        {
            UpdateAccountModelStatus(account.UserId, currentStatus, _twitchClient.GetStatusMessage(account.UserId) ?? currentStatus.ToString());
            return;
        }

        UpdateAccountModelStatus(account.UserId, ConnectionStatus.Connecting, "Validating token...");
        _logger.LogInformation("Attempting Twitch connect for {Username} (ID: {UserId})", account.Username, account.UserId);

        bool tokenIsValidOrRefreshed = await _twitchAuthService.ValidateAndRefreshAccessTokenAsync(account.UserId);
        if (!tokenIsValidOrRefreshed)
        {
            UpdateAccountModelStatus(account.UserId, ConnectionStatus.Error, "Token invalid/expired. Please reconnect.");
            _logger.LogWarning("Token validation/refresh failed for Twitch user {Username} (ID: {UserId})", account.Username, account.UserId);
            _messenger.Send(new ConnectionsUpdatedMessage());
            return;
        }

        string storageKey = $"twitch_{account.UserId}";
        (string? AccessToken, _) = await _tokenStorage.GetTokensAsync(storageKey);

        if (!string.IsNullOrEmpty(AccessToken))
        {
            UpdateAccountModelStatus(account.UserId, ConnectionStatus.Connecting, "Connecting to chat...");
            bool connectInitiated = await _twitchClient.ConnectAsync(account.UserId, account.Username!, AccessToken);

            if (connectInitiated)
            {
                ConnectionStatus statusAfterInit = _twitchClient.GetStatus(account.UserId);
                string? msgAfterInit = _twitchClient.GetStatusMessage(account.UserId);
                UpdateAccountModelStatus(account.UserId, statusAfterInit, msgAfterInit ?? statusAfterInit.ToString());
                _logger.LogInformation("Twitch connect initiated for {Username}. Current status: {Status}", account.Username, statusAfterInit);
            }
            else
            {
                UpdateAccountModelStatus(account.UserId, ConnectionStatus.Error, "Failed to initiate connection.");
                _logger.LogError("Twitch connect initiation failed for {Username} (ID: {UserId})", account.Username, account.UserId);
            }
        }
        else
        {
            UpdateAccountModelStatus(account.UserId, ConnectionStatus.Error, "Token missing after validation.");
            _logger.LogError("No valid token found after validation for Twitch user {Username} (ID: {UserId})", account.Username, account.UserId);
        }

        _messenger.Send(new ConnectionsUpdatedMessage());
    }

    /// <summary>
    /// Connects a specific YouTube account. Validates token, gets token, potentially finds the
    /// live stream ID (respecting overrides), and starts monitoring chat via YTLiveChat.
    /// Handles Limited state gracefully for chat monitoring.
    /// </summary>
    /// <param name="account">The YouTube account details.</param>
    public async Task ConnectYouTubeAccountAsync(YouTubeAccount account)
    {
        if (_isDisposed || account?.ChannelId == null)
            return;

        ConnectionStatus currentStatus = _youTubeClient.GetStatus(account.ChannelId);
        if (currentStatus is ConnectionStatus.Connected or ConnectionStatus.Limited or ConnectionStatus.Connecting)
        {
            // If already monitoring the *correct* ID, do nothing. If ID changed, need to stop/start.
            string? currentlyMonitoredId = _youTubeClient.GetActiveVideoId(account.ChannelId);
            string? desiredMonitorId = GetEffectiveLiveIdToMonitor(account); // Gets the ID we *should* be monitoring

            if (currentlyMonitoredId == desiredMonitorId && desiredMonitorId != null)
            {
                _logger.LogDebug(
                    "ConnectYouTubeAccountAsync for {ChannelName} skipped: Already {Status} and monitoring correct Live ID ({LiveId}).",
                    account.ChannelName,
                    currentStatus,
                    desiredMonitorId
                );
                UpdateAccountModelStatus(
                    account.ChannelId,
                    currentStatus,
                    _youTubeClient.GetStatusMessage(account.ChannelId) ?? currentStatus.ToString()
                );
                return;
            }

            _logger.LogInformation(
                "ConnectYouTubeAccountAsync for {ChannelName}: Status is {Status}, but monitored Live ID ('{CurrentId}') needs update to ('{DesiredId}'). Proceeding...",
                account.ChannelName,
                currentStatus,
                currentlyMonitoredId ?? "None",
                desiredMonitorId ?? "None"
            );
        }

        UpdateAccountModelStatus(account.ChannelId, ConnectionStatus.Connecting, "Validating token...");
        _logger.LogInformation("Attempting YouTube connect/monitor for {ChannelName} (ID: {ChannelId})", account.ChannelName, account.ChannelId);

        // --- Token Validation ---
        bool tokenIsValidOrRefreshed = await _youTubeAuthService.ValidateAndRefreshAccessTokenAsync(account.ChannelId);
        if (!tokenIsValidOrRefreshed)
        {
            UpdateAccountModelStatus(account.ChannelId, ConnectionStatus.Error, "Token invalid/expired. Please reconnect.");
            _logger.LogWarning(
                "Token validation/refresh failed for YouTube channel {ChannelName} (ID: {ChannelId})",
                account.ChannelName,
                account.ChannelId
            );
            _messenger.Send(new ConnectionsUpdatedMessage());
            return;
        }

        string storageKey = $"youtube_{account.ChannelId}";
        (string? AccessToken, _) = await _tokenStorage.GetTokensAsync(storageKey);

        if (string.IsNullOrEmpty(AccessToken))
        {
            UpdateAccountModelStatus(account.ChannelId, ConnectionStatus.Error, "Token missing after validation.");
            _logger.LogError(
                "No valid token found after validation for YouTube channel {ChannelName} (ID: {ChannelId})",
                account.ChannelName,
                account.ChannelId
            );
            _messenger.Send(new ConnectionsUpdatedMessage());
            return;
        }

        // --- Initialize Official API Client (Handle Quota Check) ---
        UpdateAccountModelStatus(account.ChannelId, ConnectionStatus.Connecting, "Connecting API...");
        bool apiConnectSuccessOrLimited = await _youTubeClient.ConnectAsync(account.ChannelId, AccessToken);
        ConnectionStatus statusAfterApiConnect = _youTubeClient.GetStatus(account.ChannelId);

        if (!apiConnectSuccessOrLimited && statusAfterApiConnect != ConnectionStatus.Limited)
        {
            // ConnectAsync failed for a reason other than quota limit
            UpdateAccountModelStatus(
                account.ChannelId,
                ConnectionStatus.Error,
                _youTubeClient.GetStatusMessage(account.ChannelId) ?? "API connection failed."
            );
            _logger.LogError("YouTube API connection failed (non-quota) for {ChannelName} (ID: {ChannelId})", account.ChannelName, account.ChannelId);
            _messenger.Send(new ConnectionsUpdatedMessage());
            return;
        }

        _logger.LogInformation("YouTube official API status after connect call: {Status}", statusAfterApiConnect);

        // --- Determine Live ID for Monitoring ---
        string? liveIdToMonitor = null;
        // 1. Check Account Override
        if (!string.IsNullOrWhiteSpace(account.OverrideVideoId))
        {
            _logger.LogInformation("[{ChannelId}] Using account-specific OverrideVideoId: {LiveId}", account.ChannelId, account.OverrideVideoId);
            liveIdToMonitor = account.OverrideVideoId;
        }
        else
        {
            // 2. Check Global Debug Override
            string? globalDebugId = _currentSettings.Connections?.DebugYouTubeLiveChatId;
            if (!string.IsNullOrWhiteSpace(globalDebugId))
            {
                _logger.LogInformation("[{ChannelId}] Using global DebugYouTubeLiveChatId: {LiveId}", account.ChannelId, globalDebugId);
                liveIdToMonitor = globalDebugId;
            }
            else
            {
                // 3. Attempt API Lookup (Only if NOT in Limited state)
                if (statusAfterApiConnect != ConnectionStatus.Limited)
                {
                    UpdateAccountModelStatus(account.ChannelId, ConnectionStatus.Connecting, "Finding active stream...");
                    liveIdToMonitor = await _youTubeClient.FindActiveVideoIdAsync(account.ChannelId); // This returns Video ID
                    statusAfterApiConnect = _youTubeClient.GetStatus(account.ChannelId); // Re-check status after API call
                    if (
                        string.IsNullOrEmpty(liveIdToMonitor)
                        && statusAfterApiConnect != ConnectionStatus.Limited
                        && statusAfterApiConnect != ConnectionStatus.Error
                    )
                    {
                        // API call succeeded but no active stream found
                        UpdateAccountModelStatus(account.ChannelId, ConnectionStatus.Connected, "Ready (No Stream Active)");
                        _logger.LogInformation(
                            "Could not find active YouTube Live ID for {ChannelName} (ID: {ChannelId}).",
                            account.ChannelName,
                            account.ChannelId
                        );
                    }
                }
                else
                {
                    // In Limited state and no overrides set, cannot find Live ID
                    _logger.LogWarning("[{ChannelId}] Cannot find Live ID: API is in Limited state and no override is set.", account.ChannelId);
                    UpdateAccountModelStatus(account.ChannelId, ConnectionStatus.Limited, "Read-Only (Manual Live ID Needed)");
                }
            }
        }

        // --- Start Monitoring if an ID was determined ---
        if (!string.IsNullOrEmpty(liveIdToMonitor))
        {
            _logger.LogInformation("[{ChannelId}] Proceeding to start monitoring for Live ID: {LiveId}", account.ChannelId, liveIdToMonitor);
            await _youTubeClient.StartPollingAsync(account.ChannelId, liveIdToMonitor);
        }
        else
        {
            _logger.LogInformation("[{ChannelId}] No Live ID determined for monitoring. Stopping any existing monitoring.", account.ChannelId);
            await _youTubeClient.StopPollingAsync(account.ChannelId);
        }

        _messenger.Send(new ConnectionsUpdatedMessage());
    }

    /// <summary>
    /// Logs out a specific Twitch account, disconnecting its client and removing tokens.
    /// </summary>
    /// <param name="userId">The Twitch User ID of the account to log out.</param>
    public async Task LogoutTwitchAccountAsync(string userId)
    {
        if (string.IsNullOrEmpty(userId))
            return;
        _logger.LogInformation("Logging out Twitch User ID {UserId}", userId);
        await _twitchClient.DisconnectAsync(userId);
        await _twitchAuthService.LogoutAsync(userId);
        UpdateAccountModelStatus(userId, ConnectionStatus.Disconnected, "Logged out.");
        _logger.LogInformation("Logout steps complete for Twitch User ID {UserId}. Settings removal pending caller.", userId);
    }

    /// <summary>
    /// Logs out a specific YouTube account, disconnecting its client (if active) and removing tokens.
    /// </summary>
    /// <param name="channelId">The YouTube Channel ID of the account to log out.</param>
    public async Task LogoutYouTubeAccountAsync(string channelId)
    {
        if (string.IsNullOrEmpty(channelId))
            return;
        _logger.LogInformation("Logging out YouTube Channel ID {ChannelId}", channelId);
        await _youTubeClient.DisconnectAsync(channelId);
        await _youTubeAuthService.LogoutAsync(channelId);
        UpdateAccountModelStatus(channelId, ConnectionStatus.Disconnected, "Logged out.");
        _logger.LogInformation("Logout steps complete for YouTube Channel ID {ChannelId}. Settings removal pending caller.", channelId);
    }

    /// <summary>
    /// Disconnects all active platform clients.
    /// </summary>
    public async Task DisconnectAllAsync()
    {
        _logger.LogInformation("Disconnecting all clients...");
        List<Task> tasks = [];
        List<string> twitchIds = [.. _accountMap.Keys.Where(k => k.StartsWith("twitch_")).Select(k => k[7..])];
        foreach (string? id in twitchIds)
        {
            if (!string.IsNullOrEmpty(id) && _twitchClient.GetStatus(id) != ConnectionStatus.Disconnected)
                tasks.Add(_twitchClient.DisconnectAsync(id));
        }

        List<string> youtubeIds = [.. _accountMap.Keys.Where(k => k.StartsWith("youtube_")).Select(k => k[8..])];
        foreach (string? id in youtubeIds)
        {
            if (!string.IsNullOrEmpty(id) && _youTubeClient.GetStatus(id) != ConnectionStatus.Disconnected)
                tasks.Add(_youTubeClient.DisconnectAsync(id));
        }

        if (_streamlabsClient.Status != ConnectionStatus.Disconnected)
            tasks.Add(_streamlabsClient.DisconnectAsync());
        await Task.WhenAll(tasks);
        foreach (string key in _accountMap.Keys.ToList())
        {
            string accountId = key.Contains('_') ? key.Split('_', 2)[1] : key;
            string platform = key.Contains('_') ? key.Split('_', 2)[0] : "unknown";
            UpdateAccountModelStatus(accountId, ConnectionStatus.Disconnected, "Disconnected.");
        }

        _logger.LogInformation("Disconnected all clients completed ({Count} tasks).", tasks.Count);
    }

    /// <summary>
    /// Helper to update the Status and StatusMessage properties on account model objects.
    /// Ensures UI updates happen on the correct thread if necessary (delegated to ViewModel).
    /// </summary>
    /// <param name="accountId">The account ID (Twitch User ID or YouTube Channel ID).</param>
    /// <param name="status">The new connection status.</param>
    /// <param name="message">The corresponding status message.</param>
    private void UpdateAccountModelStatus(string accountId, ConnectionStatus status, string? message)
    {
        string platformPrefix = "unknown";
        string twitchMapKey = $"twitch_{accountId}";
        string youtubeMapKey = $"youtube_{accountId}";

        if (_accountMap.TryGetValue(twitchMapKey, out object? accountModel) && accountModel is TwitchAccount)
        {
            platformPrefix = "twitch";
        }
        else if (_accountMap.TryGetValue(youtubeMapKey, out accountModel) && accountModel is YouTubeAccount)
        {
            platformPrefix = "youtube";
        }

        _dispatcherQueue.TryEnqueue(() =>
        {
            if (_isDisposed)
                return;

            string mapKey = $"{platformPrefix}_{accountId}";
            if (_accountMap.TryGetValue(mapKey, out object? model))
            {
                bool changed = false;
                if (model is TwitchAccount ta)
                {
                    if (ta.Status != status)
                    {
                        ta.Status = status;
                        changed = true;
                    }

                    if (ta.StatusMessage != message)
                    {
                        ta.StatusMessage = message;
                        changed = true;
                    }

                    if (changed)
                        _logger.LogTrace("Dispatched status update for Twitch {Username}: {Status} ('{Message}')", ta.Username, status, message);
                }
                else if (model is YouTubeAccount ya)
                {
                    if (ya.Status != status)
                    {
                        ya.Status = status;
                        changed = true;
                    }

                    if (ya.StatusMessage != message)
                    {
                        ya.StatusMessage = message;
                        changed = true;
                    }

                    if (changed)
                        _logger.LogTrace(
                            "Dispatched status update for YouTube {ChannelName}: {Status} ('{Message}')",
                            ya.ChannelName,
                            status,
                            message
                        );
                }
            }
            else
            {
                if (platformPrefix == "unknown")
                {
                    _logger.LogWarning("Could not find account model for ID {AccountId} with either prefix to update UI status.", accountId);
                }
                else
                {
                    _logger.LogWarning("Could not find account model for key {MapKey} to update UI status (likely removed).", mapKey);
                }
            }
        });
    }

    /// <summary>
    /// Sends a chat message using the appropriate platform client and account.
    /// Also checks if the message is a command and routes it to plugins if necessary.
    /// </summary>
    /// <param name="platform">The target platform ("Twitch", "YouTube").</param>
    /// <param name="senderAccountId">The ID of the account *sending* the message.</param>
    /// <param name="targetChannelOrChatId">The destination Channel Name (Twitch) or LiveChatID (YouTube).</param>
    /// <param name="message">The message content.</param>
    public async Task SendChatMessageAsync(string platform, string senderAccountId, string targetChannelOrChatId, string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;
        if (string.IsNullOrWhiteSpace(senderAccountId))
        {
            _logger.LogWarning("Cannot send message to {Platform}/{Target}: senderAccountId is missing.", platform, targetChannelOrChatId);
            return;
        }

        string senderDisplayName = GetSenderDisplayName(platform, senderAccountId);
        ChatMessageEvent potentialCommandEvent = new()
        {
            Platform = platform,
            Timestamp = DateTime.UtcNow,
            OriginatingAccountId = senderAccountId,
            UserId = senderAccountId,
            Username = senderDisplayName,
            RawMessage = message,
            ParsedMessage = [new TextSegment { Text = message }],
        };

        if (PluginService.IsChatCommand(potentialCommandEvent))
        {
            bool suppressMessage = await _pluginService.TryHandleChatCommandAsync(potentialCommandEvent);
            _logger.LogDebug("Local message from {SenderAccountId} handled as command. Routing original command event.", senderAccountId);
            await _pluginService.RouteEventToProcessorsAsync(potentialCommandEvent).ConfigureAwait(false);
            _messenger.Send(new NewEventMessage(potentialCommandEvent));
            if (suppressMessage)
            {
                _logger.LogDebug("Command handler suppressed original message for {SenderAccountId}.", senderAccountId);
                return;
            }

            _logger.LogInformation(
                "Command from {SenderAccountId} was handled by plugin, but not suppressed. Original message will NOT be sent by default.",
                senderAccountId
            );
            return;
        }

        _logger.LogInformation(
            "Sending message '{MessageContent}' via account {SenderAccountId} to {Platform}/{Target}",
            message,
            senderAccountId,
            platform,
            targetChannelOrChatId
        );

        try
        {
            switch (platform.ToLowerInvariant())
            {
                case "twitch":
                    await _twitchClient.SendMessageAsync(senderAccountId, targetChannelOrChatId, message);
                    break;
                case "youtube":
                    await _youTubeClient.SendMessageAsync(senderAccountId, targetChannelOrChatId, message);
                    break;
                default:
                    _logger.LogWarning("Sending messages to platform '{Platform}' is not supported.", platform);
                    return;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending message via platform client {Platform}", platform);
            SystemMessageEvent errorEvent = new()
            {
                Level = SystemMessageLevel.Error,
                Message = $"Failed to send message via {platform}: {ex.Message}",
            };
            _messenger.Send(new NewEventMessage(errorEvent));
        }
    }

    /// <summary>
    /// Helper to get the display name for a sending account.
    /// </summary>
    /// <param name="platform">The platform ("twitch", "youtube").</param>
    /// <param name="accountId">The account ID.</param>
    /// <returns>The display name or a default identifier.</returns>
    private string GetSenderDisplayName(string platform, string accountId)
    {
        string mapKey = $"{platform.ToLowerInvariant()}_{accountId}";
        if (_accountMap.TryGetValue(mapKey, out object? accountModel))
        {
            return (accountModel as TwitchAccount)?.Username
                ?? (accountModel as YouTubeAccount)?.ChannelName
                ?? $"Bot({accountId[..Math.Min(accountId.Length, 5)]}...)";
        }

        return "StreamWeaver";
    }

    /// <summary>
    /// Initiates the Twitch OAuth login flow.
    /// </summary>
    public Task TriggerTwitchLoginAsync()
    {
        _logger.LogInformation("Triggering Twitch Login Flow...");
        return _twitchAuthService.InitiateLoginAsync();
    }

    /// <summary>
    /// Initiates the YouTube OAuth login flow. Adds account to settings and connects on success.
    /// </summary>
    public async Task TriggerYouTubeLoginAsync()
    {
        _logger.LogInformation("Triggering YouTube Login Flow...");
        YouTubeAuthResult authResult = await _youTubeAuthService.AuthenticateAsync();

        if (authResult.Success && !string.IsNullOrEmpty(authResult.ChannelId))
        {
            _logger.LogInformation(
                "YouTube auth successful for {ChannelName} ({ChannelId}). Updating settings & connecting...",
                authResult.ChannelName,
                authResult.ChannelId
            );

            if (_currentSettings.Connections.YouTubeAccounts == null)
            {
                _currentSettings.Connections.YouTubeAccounts = [];
                HookCollectionChangedHandlers(_currentSettings.Connections);
            }

            YouTubeAccount? existingAccount = _currentSettings.Connections.YouTubeAccounts.FirstOrDefault(a => a.ChannelId == authResult.ChannelId);
            YouTubeAccount accountToConnect;
            bool settingsChanged = false;

            if (existingAccount == null)
            {
                accountToConnect = new YouTubeAccount
                {
                    ChannelId = authResult.ChannelId,
                    ChannelName = authResult.ChannelName ?? "YouTube User",
                    AutoConnect = true,
                    Status = ConnectionStatus.Disconnected,
                };
                _currentSettings.Connections.YouTubeAccounts.Add(accountToConnect);
                _accountMap[$"youtube_{accountToConnect.ChannelId}"] = accountToConnect;
                settingsChanged = true;
                _logger.LogInformation("--> Added new YouTube account: {ChannelName}", accountToConnect.ChannelName);
            }
            else
            {
                accountToConnect = existingAccount;
                if (accountToConnect.ChannelName != authResult.ChannelName && !string.IsNullOrEmpty(authResult.ChannelName))
                {
                    accountToConnect.ChannelName = authResult.ChannelName;
                    settingsChanged = true;
                }

                if (!accountToConnect.AutoConnect)
                {
                    accountToConnect.AutoConnect = true;
                    settingsChanged = true;
                }

                _logger.LogInformation("--> Found existing YouTube account: {ChannelName}", accountToConnect.ChannelName);
            }

            if (settingsChanged)
            {
                await _settingsService.SaveSettingsAsync(_currentSettings);
            }

            _logger.LogInformation("--> Initiating connection for {ChannelName}...", accountToConnect.ChannelName);
            await ConnectYouTubeAccountAsync(accountToConnect);
        }
        else
        {
            _logger.LogError("YouTube authentication failed or was cancelled. Error: {ErrorMessage}", authResult.ErrorMessage);
            SystemMessageEvent errorEvent = new()
            {
                Level = SystemMessageLevel.Error,
                Message = $"YouTube Login Failed: {authResult.ErrorMessage ?? "Authentication cancelled or failed."}",
            };
            _messenger.Send(new NewEventMessage(errorEvent));
        }
    }

    /// <summary>
    /// Connects to Streamlabs using the configured Socket API token.
    /// </summary>
    public async Task ConnectStreamlabsAsync()
    {
        if (_isDisposed)
            return;
        if (_streamlabsClient.Status is ConnectionStatus.Connected or ConnectionStatus.Connecting)
        {
            _logger.LogDebug("ConnectStreamlabsAsync skipped: Already {Status}.", _streamlabsClient.Status);
            return;
        }

        string? tokenId = _currentSettings.Connections?.StreamlabsTokenId;
        if (string.IsNullOrWhiteSpace(tokenId))
        {
            _logger.LogWarning("Cannot connect Streamlabs, Token ID missing in settings.");
            return;
        }

        _logger.LogInformation("Attempting Streamlabs connect using Token ID: {TokenId}", tokenId);
        (string? AccessToken, _) = await _tokenStorage.GetTokensAsync(tokenId);

        if (!string.IsNullOrEmpty(AccessToken))
        {
            await _streamlabsClient.ConnectAsync(AccessToken);
        }
        else
        {
            _logger.LogError("Streamlabs Socket Token not found in secure storage for ID: {TokenId}.", tokenId);
        }
    }

    /// <summary>
    /// Disables Streamlabs integration by disconnecting, clearing the token ID from settings,
    /// saving settings, and removing the token from secure storage.
    /// </summary>
    public async Task DisableStreamlabsAsync()
    {
        if (_isDisposed)
            return;
        _logger.LogInformation("Disabling Streamlabs...");

        if (_streamlabsClient.Status != ConnectionStatus.Disconnected)
            await _streamlabsClient.DisconnectAsync();

        bool changed = false;
        if (_currentSettings.Connections.EnableStreamlabs)
        {
            _currentSettings.Connections.EnableStreamlabs = false;
            changed = true;
        }

        string? tokenId = _currentSettings.Connections.StreamlabsTokenId;
        if (!string.IsNullOrEmpty(tokenId))
        {
            _currentSettings.Connections.StreamlabsTokenId = null;
            changed = true;
        }

        if (changed)
        {
            await _settingsService.SaveSettingsAsync(_currentSettings);
            _logger.LogDebug("Streamlabs settings updated (disabled/token removed).");
        }

        if (!string.IsNullOrEmpty(tokenId))
        {
            await _tokenStorage.DeleteTokensAsync(tokenId);
            _logger.LogInformation("Removed Streamlabs token from secure storage (ID: {TokenId}).", tokenId);
        }

        _logger.LogInformation("Streamlabs disabled.");
    }

    /// <summary>
    /// Helper to get the Live ID that should be monitored based on overrides and settings.
    /// </summary>
    private string? GetEffectiveLiveIdToMonitor(YouTubeAccount account) =>
        !string.IsNullOrWhiteSpace(account.OverrideVideoId) ? account.OverrideVideoId
        : !string.IsNullOrWhiteSpace(_currentSettings?.Connections?.DebugYouTubeLiveChatId) ? _currentSettings.Connections.DebugYouTubeLiveChatId
        : _youTubeClient.GetActiveVideoId(account.ChannelId!);

    /// <summary>
    /// Releases resources used by the UnifiedEventService.
    /// Unsubscribes from events and disconnects clients.
    /// </summary>
    public void Dispose()
    {
        if (_isDisposed)
            return;
        _isDisposed = true;
        _logger.LogInformation("Disposing UnifiedEventService...");
        _settingsService.SettingsUpdated -= OnSettingsUpdated;
        UnhookCollectionChangedHandlers(_currentSettings?.Connections);
        try
        {
            Task.Run(DisconnectAllAsync).Wait(TimeSpan.FromSeconds(5));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during DisconnectAll on dispose.");
        }

        _accountMap.Clear();
        _logger.LogInformation("UnifiedEventService Dispose finished.");
        GC.SuppressFinalize(this);
    }
}
