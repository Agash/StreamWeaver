using System.Collections.Concurrent;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using StreamWeaver.Core.Messaging;
using StreamWeaver.Core.Models.Events;
using StreamWeaver.Core.Models.Events.Messages;
using StreamWeaver.Core.Models.Settings;
using StreamWeaver.Core.Plugins;
using TwitchLib.Client;
using TwitchLib.Client.Enums;
using TwitchLib.Client.Events;
using TwitchLib.Client.Models;
using TwitchLib.Communication.Clients;
using TwitchLib.Communication.Events;
using TwitchLib.Communication.Models;

namespace StreamWeaver.Core.Services.Platforms;

/// <summary>
/// Helper class to hold a TwitchClient instance and its associated state,
/// managing the lifecycle and events for a single Twitch connection.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="TwitchClientWrapper"/> class.
/// </remarks>
/// <param name="client">The initialized TwitchClient instance.</param>
/// <param name="accountId">The Twitch User ID associated with this connection.</param>
/// <param name="username">The Twitch username associated with this connection.</param>
/// <param name="logger">The logger instance for logging messages.</param>
internal sealed partial class TwitchClientWrapper(TwitchClient client, string accountId, string username, ILogger logger) : IDisposable
{
    public TwitchClient ClientInstance { get; } = client ?? throw new ArgumentNullException(nameof(client));
    public ConnectionStatus Status { get; set; } = ConnectionStatus.Disconnected;
    public string? StatusMessage { get; set; }
    public string Username { get; } = username ?? throw new ArgumentNullException(nameof(username));
    public string AccountId { get; } = accountId ?? throw new ArgumentNullException(nameof(accountId));

    private readonly ILogger _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public EventHandler<OnConnectedArgs>? OnConnectedHandler { get; set; }
    public EventHandler<OnJoinedChannelArgs>? OnJoinedChannelHandler { get; set; }
    public EventHandler<OnMessageReceivedArgs>? OnMessageReceivedHandler { get; set; }
    public EventHandler<OnDisconnectedEventArgs>? OnDisconnectedHandler { get; set; }
    public EventHandler<OnConnectionErrorArgs>? OnConnectionErrorHandler { get; set; }
    public EventHandler<OnErrorEventArgs>? OnErrorHandler { get; set; }
    public EventHandler<OnNewSubscriberArgs>? OnNewSubscriberHandler { get; set; }
    public EventHandler<OnGiftedSubscriptionArgs>? OnGiftedSubscriptionHandler { get; set; }
    public EventHandler<OnRaidNotificationArgs>? OnRaidNotificationHandler { get; set; }

    private bool _disposed = false;

    /// <summary>
    /// Unhooks all registered event handlers from the TwitchClient instance.
    /// </summary>
    public void UnhookEvents()
    {
        _logger.LogTrace("[{Username}] Unhooking events.", Username);
        try
        {
            if (OnConnectedHandler != null)
                ClientInstance.OnConnected -= OnConnectedHandler;
            if (OnJoinedChannelHandler != null)
                ClientInstance.OnJoinedChannel -= OnJoinedChannelHandler;
            if (OnMessageReceivedHandler != null)
                ClientInstance.OnMessageReceived -= OnMessageReceivedHandler;
            if (OnDisconnectedHandler != null)
                ClientInstance.OnDisconnected -= OnDisconnectedHandler;
            if (OnConnectionErrorHandler != null)
                ClientInstance.OnConnectionError -= OnConnectionErrorHandler;
            if (OnErrorHandler != null)
                ClientInstance.OnError -= OnErrorHandler;
            if (OnNewSubscriberHandler != null)
                ClientInstance.OnNewSubscriber -= OnNewSubscriberHandler;
            if (OnGiftedSubscriptionHandler != null)
                ClientInstance.OnGiftedSubscription -= OnGiftedSubscriptionHandler;
            if (OnRaidNotificationHandler != null)
                ClientInstance.OnRaidNotification -= OnRaidNotificationHandler;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[{Username}] Exception during event unhooking (ignoring).", Username);
        }
    }

    /// <summary>
    /// Disposes the wrapper, unhooking events and attempting to disconnect the client.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _logger.LogDebug("[{Username}] Disposing wrapper...", Username);

        UnhookEvents();
        try
        {
            if (ClientInstance.IsConnected)
            {
                _logger.LogDebug("[{Username}] Client is connected, calling Disconnect().", Username);
                ClientInstance.Disconnect();
                _logger.LogInformation("[{Username}] Disconnected client instance via wrapper Dispose.", Username);
            }
            // Note: TwitchLib's Client doesn't explicitly implement IDisposable.
            // We rely on removing references and GC, after calling Disconnect.
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[{Username}] Error during client disconnect in wrapper Dispose (ignoring): {ErrorMessage}", Username, ex.Message);
        }

        _logger.LogDebug("[{Username}] Dispose finished.", Username);
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Service implementation for managing multiple Twitch chat connections using TwitchLib.Client.
/// Handles connection lifecycle, event parsing, and message sending for individual accounts.
/// </summary>
public partial class TwitchChatService : ITwitchClient, IDisposable
{
    private readonly IMessenger _messenger;
    private readonly IEmoteBadgeService _emoteBadgeService;
    private readonly PluginService _pluginService;
    private readonly ILogger<TwitchChatService> _logger;
    private readonly ConcurrentDictionary<string, TwitchClientWrapper> _activeClients = new();

    private static readonly Dictionary<string, (int Priority, string Color)> s_twitchBadgeColorPriority = new(StringComparer.OrdinalIgnoreCase)
    {
        // Higher priority number = more important
        { "broadcaster", (10, "#E91916") }, // Example Red
        { "admin", (9, "#FAAF19") }, // Example Orange
        { "staff", (9, "#FAAF19") }, // Example Orange
        { "global_mod", (8, "#0AD57F") }, // Example Green
        { "moderator", (7, "#0AD57F") }, // Example Green
        { "vip", (6, "#E005B9") }, // Example Pink/Magenta
        { "partner", (5, "#7533FF") }, // Twitch Purple
        { "subscriber", (4, "#7533FF") }, // Twitch Purple (or specific sub color?)
        { "founder", (3, "#7533FF") }, // Twitch Purple
        // Add others like predictions, hype-train, bits-leader, etc. as needed
    };

    private bool _isDisposed = false;

    /// <summary>
    /// Initializes a new instance of the <see cref="TwitchChatService"/> class.
    /// </summary>
    /// <param name="messenger">The application's messenger service.</param>
    /// <param name="emoteBadgeService">Service for resolving emotes and badges.</param>
    /// <param name="pluginService">Service for routing events and commands to plugins.</param>
    /// <param name="logger">The logger instance.</param>
    /// <exception cref="ArgumentNullException">Thrown if any constructor parameters are null.</exception>
    public TwitchChatService(
        IMessenger messenger,
        IEmoteBadgeService emoteBadgeService,
        PluginService pluginService,
        ILogger<TwitchChatService> logger
    )
    {
        _messenger = messenger ?? throw new ArgumentNullException(nameof(messenger));
        _emoteBadgeService = emoteBadgeService ?? throw new ArgumentNullException(nameof(emoteBadgeService));
        _pluginService = pluginService ?? throw new ArgumentNullException(nameof(pluginService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _logger.LogInformation("Initialized.");
    }

    private static string? CalculateTwitchUsernameColor(List<BadgeInfo> badges)
    {
        if (badges == null || badges.Count == 0)
            return null;

        string? highestPriorityColor = null;
        int highestPriority = -1;

        foreach (BadgeInfo badge in badges)
        {
            // Expected format: twitch/{badge_name}/{version}
            string[] parts = badge.Identifier.Split('/');
            if (parts.Length >= 2 && parts[0].Equals("twitch", StringComparison.OrdinalIgnoreCase))
            {
                string badgeName = parts[1];
                if (s_twitchBadgeColorPriority.TryGetValue(badgeName, out (int Priority, string Color) priorityInfo))
                {
                    if (priorityInfo.Priority > highestPriority)
                    {
                        highestPriority = priorityInfo.Priority;
                        highestPriorityColor = priorityInfo.Color;
                    }
                }
            }
        }

        return highestPriorityColor;
    }

    /// <summary>
    /// Connects a specific Twitch account using its credentials.
    /// </summary>
    /// <param name="accountId">The unique Twitch User ID for the account.</param>
    /// <param name="username">The Twitch username for the account.</param>
    /// <param name="accessToken">A valid OAuth access token (without the "oauth:" prefix).</param>
    /// <returns>A Task resulting in true if the connection process was initiated successfully, false otherwise.</returns>
    public Task<bool> ConnectAsync(string accountId, string username, string accessToken)
    {
        string logUsername = username ?? accountId ?? "N/A"; // Use best available identifier for logging

        if (_isDisposed)
        {
            _logger.LogWarning("[{LogUsername}] Connect failed: Service is disposed.", logUsername);
            return Task.FromResult(false);
        }

        if (string.IsNullOrWhiteSpace(accountId) || string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(accessToken))
        {
            _logger.LogError("[{LogUsername}] Connect failed: Invalid parameters (AccountId, Username, or AccessToken missing/empty).", logUsername);
            return Task.FromResult(false);
        }

        if (
            _activeClients.TryGetValue(accountId, out TwitchClientWrapper? existingWrapper)
            && (existingWrapper.Status is ConnectionStatus.Connected or ConnectionStatus.Connecting)
        )
        {
            _logger.LogInformation(
                "[{Username}] Connect requested but already {ConnectionStatus}.",
                existingWrapper.Username,
                existingWrapper.Status
            );
            return Task.FromResult(existingWrapper.Status == ConnectionStatus.Connected);
        }

        if (existingWrapper != null)
        {
            _logger.LogInformation(
                "[{Username}] Removing previous client wrapper (State: {ConnectionStatus}) before reconnecting.",
                existingWrapper.Username,
                existingWrapper.Status
            );
            if (_activeClients.TryRemove(accountId, out TwitchClientWrapper? removedWrapper))
            {
                removedWrapper?.Dispose();
            }
        }

        _logger.LogInformation("[{Username}] Creating new client instance for Account ID: {AccountId}", username, accountId);

        ConnectionCredentials credentials = new(username, $"oauth:{accessToken}");
        ClientOptions clientOptions = new() { MessagesAllowedInPeriod = 750, ThrottlingPeriod = TimeSpan.FromSeconds(30) };
        WebSocketClient customClient = new(clientOptions);
        TwitchClient newClient = new(customClient);
        newClient.Initialize(credentials, username);

        var wrapper = new TwitchClientWrapper(newClient, accountId, username, _logger)
        {
            Status = ConnectionStatus.Connecting,
            StatusMessage = "Connecting...",
            OnConnectedHandler = (s, e) => Client_OnConnected(accountId, s, e),
        };
        newClient.OnConnected += wrapper.OnConnectedHandler;

        wrapper.OnJoinedChannelHandler = (s, e) => Client_OnJoinedChannel(accountId, s, e);
        newClient.OnJoinedChannel += wrapper.OnJoinedChannelHandler;

        wrapper.OnMessageReceivedHandler = (s, e) => _ = Client_OnMessageReceived(accountId, s, e);
        newClient.OnMessageReceived += wrapper.OnMessageReceivedHandler;

        wrapper.OnDisconnectedHandler = (s, e) => Client_OnDisconnected(accountId, s, e);
        newClient.OnDisconnected += wrapper.OnDisconnectedHandler;

        wrapper.OnConnectionErrorHandler = (s, e) => Client_OnConnectionError(accountId, s, e);
        newClient.OnConnectionError += wrapper.OnConnectionErrorHandler;

        wrapper.OnErrorHandler = (s, e) => Client_OnError(accountId, s, e);
        newClient.OnError += wrapper.OnErrorHandler;

        wrapper.OnNewSubscriberHandler = (s, e) => Client_OnNewSubscriber(accountId, s, e);
        newClient.OnNewSubscriber += wrapper.OnNewSubscriberHandler;
        wrapper.OnGiftedSubscriptionHandler = (s, e) => Client_OnGiftedSubscription(accountId, s, e);
        newClient.OnGiftedSubscription += wrapper.OnGiftedSubscriptionHandler;
        wrapper.OnRaidNotificationHandler = (s, e) => Client_OnRaidNotification(accountId, s, e);
        newClient.OnRaidNotification += wrapper.OnRaidNotificationHandler;

        if (!_activeClients.TryAdd(accountId, wrapper))
        {
            _logger.LogCritical(
                "[{Username}] Failed to add client wrapper to dictionary (race condition?). Aborting connection for Account ID {AccountId}.",
                username,
                accountId
            );
            wrapper.Dispose();
            return Task.FromResult(false);
        }

        try
        {
            newClient.Connect();
            _logger.LogInformation(
                "[{Username}] Connection process initiated for Account ID: {AccountId}. Waiting for OnConnected event.",
                username,
                accountId
            );

            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "[{Username}] Exception during initial client.Connect() call for Account ID {AccountId}: {ErrorMessage}",
                username,
                accountId,
                ex.Message
            );

            wrapper.Status = ConnectionStatus.Error;
            wrapper.StatusMessage = $"Failed to initiate connection: {ex.Message}";

            if (_activeClients.TryRemove(accountId, out _))
            {
                _logger.LogDebug(
                    "[{Username}] Removed failed client wrapper for Account ID {AccountId} after connection exception.",
                    username,
                    accountId
                );
            }

            wrapper.Dispose();
            return Task.FromResult(false);
        }
    }

    /// <summary>
    /// Disconnects a specific Twitch account.
    /// </summary>
    /// <param name="accountId">The Twitch User ID of the account to disconnect.</param>
    /// <returns>A Task representing the asynchronous operation completion.</returns>
    public Task DisconnectAsync(string accountId)
    {
        if (_isDisposed)
        {
            _logger.LogDebug("[{AccountId}] Disconnect skipped: Service is disposed.", accountId);
            return Task.CompletedTask;
        }

        _logger.LogInformation("[{AccountId}] Disconnect requested.", accountId);
        if (_activeClients.TryRemove(accountId, out TwitchClientWrapper? wrapper))
        {
            string username = wrapper.Username;
            _logger.LogInformation("[{Username}] Disposing client wrapper for Account ID: {AccountId}", username, accountId);
            wrapper.Dispose();
            _logger.LogInformation("[{Username}] Client wrapper removed and disposed for Account ID: {AccountId}", username, accountId);

            SystemMessageEvent systemEvent = new()
            {
                Platform = "Twitch",
                OriginatingAccountId = accountId,
                Message = $"Disconnected Twitch account: {username}",
            };
            _messenger.Send(new NewEventMessage(systemEvent));
            _ = _pluginService.RouteEventToProcessorsAsync(systemEvent);
        }
        else
        {
            _logger.LogWarning("[{AccountId}] Client wrapper not found during disconnect request.", accountId);
        }

        _messenger.Send(new ConnectionsUpdatedMessage());
        return Task.CompletedTask;
    }

    /// <summary>
    /// Joins a specified Twitch chat channel using a specific connected account.
    /// </summary>
    /// <param name="accountId">The Twitch User ID of the account that should join the channel.</param>
    /// <param name="channelName">The name of the channel to join (without the '#').</param>
    /// <returns>A Task representing the asynchronous operation completion.</returns>
    public Task JoinChannelAsync(string accountId, string channelName)
    {
        if (_activeClients.TryGetValue(accountId, out TwitchClientWrapper? wrapper))
        {
            if (wrapper.Status == ConnectionStatus.Connected)
            {
                string channel = channelName.ToLowerInvariant();
                _logger.LogInformation("[{Username}] Joining channel: #{ChannelName}", wrapper.Username, channel);
                wrapper.ClientInstance.JoinChannel(channel);
            }
            else
            {
                _logger.LogWarning(
                    "[{Username}] Cannot join channel #{ChannelName}, client not connected (Status: {ConnectionStatus}).",
                    wrapper.Username,
                    channelName,
                    wrapper.Status
                );
            }
        }
        else
        {
            _logger.LogWarning("[{AccountId}] Cannot join channel #{ChannelName}, client wrapper not found.", accountId, channelName);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Leaves a specified Twitch chat channel using a specific connected account.
    /// </summary>
    /// <param name="accountId">The Twitch User ID of the account that should leave the channel.</param>
    /// <param name="channelName">The name of the channel to leave (without the '#').</param>
    /// <returns>A Task representing the asynchronous operation completion.</returns>
    public Task LeaveChannelAsync(string accountId, string channelName)
    {
        if (_activeClients.TryGetValue(accountId, out TwitchClientWrapper? wrapper))
        {
            if (wrapper.Status == ConnectionStatus.Connected)
            {
                string channel = channelName.ToLowerInvariant();
                _logger.LogInformation("[{Username}] Leaving channel: #{ChannelName}", wrapper.Username, channel);
                wrapper.ClientInstance.LeaveChannel(channel);
            }
            else
            {
                _logger.LogWarning(
                    "[{Username}] Cannot leave channel #{ChannelName}, client not connected (Status: {ConnectionStatus}).",
                    wrapper.Username,
                    channelName,
                    wrapper.Status
                );
            }
        }
        else
        {
            _logger.LogWarning("[{AccountId}] Cannot leave channel #{ChannelName}, client wrapper not found.", accountId, channelName);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Sends a chat message to a specified Twitch channel using a specific connected account.
    /// </summary>
    /// <param name="accountId">The Twitch User ID of the account sending the message.</param>
    /// <param name="channelName">The name of the channel to send the message to (without the '#').</param>
    /// <param name="message">The message content to send.</param>
    /// <returns>A Task representing the asynchronous operation completion.</returns>
    public Task SendMessageAsync(string accountId, string channelName, string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            _logger.LogWarning("[{AccountId}] Cannot send empty or whitespace message to channel #{ChannelName}.", accountId, channelName);
            return Task.CompletedTask;
        }

        if (_activeClients.TryGetValue(accountId, out TwitchClientWrapper? wrapper))
        {
            if (wrapper.Status == ConnectionStatus.Connected)
            {
                string channel = channelName.ToLowerInvariant();

                if (wrapper.ClientInstance.JoinedChannels.Any(jc => jc.Channel.Equals(channel, StringComparison.OrdinalIgnoreCase)))
                {
                    _logger.LogDebug("[{Username}] Sending to #{ChannelName}: {Message}", wrapper.Username, channel, message);
                    wrapper.ClientInstance.SendMessage(channel, message);

                    // Create a local echo event for the UI
                    // Note: This assumes the message was successfully sent; Twitch doesn't confirm sends.
                    var localEchoEvent = new ChatMessageEvent
                    {
                        Platform = "Twitch",
                        Timestamp = DateTime.UtcNow,
                        OriginatingAccountId = accountId,
                        UserId = wrapper.AccountId,
                        Username = wrapper.Username,
                        RawMessage = message,
                        ParsedMessage = [new TextSegment { Text = message }],
                        IsActionMessage = message.StartsWith("/me ", StringComparison.OrdinalIgnoreCase),
                        Badges = [],
                    };

                    _messenger.Send(new NewEventMessage(localEchoEvent));
                    _ = _pluginService.RouteEventToProcessorsAsync(localEchoEvent);
                }
                else
                {
                    _logger.LogWarning(
                        "[{Username}] Cannot send message to #{ChannelName}, client is not joined to that channel.",
                        wrapper.Username,
                        channel
                    );
                    // TODO: Provide user feedback? Attempt to join?
                }
            }
            else
            {
                _logger.LogWarning(
                    "[{Username}] Cannot send message to #{ChannelName}, client not connected (Status: {ConnectionStatus}).",
                    wrapper.Username,
                    channelName,
                    wrapper.Status
                );
                // TODO: Provide user feedback
            }
        }
        else
        {
            _logger.LogWarning("[{AccountId}] Cannot send message to #{ChannelName}, client wrapper not found.", accountId, channelName);
            // TODO: Provide user feedback
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Gets the current connection status for a specific Twitch account.
    /// </summary>
    /// <param name="accountId">The Twitch User ID of the account.</param>
    /// <returns>The <see cref="ConnectionStatus"/> for the account.</returns>
    public ConnectionStatus GetStatus(string accountId) => _activeClients.TryGetValue(accountId, out TwitchClientWrapper? wrapper) ? wrapper.Status : ConnectionStatus.Disconnected;

    /// <summary>
    /// Gets the current status message for a specific Twitch account.
    /// </summary>
    /// <param name="accountId">The Twitch User ID of the account.</param>
    /// <returns>The status message string, or a default message if not connected/found.</returns>
    public string? GetStatusMessage(string accountId) => _activeClients.TryGetValue(accountId, out TwitchClientWrapper? wrapper) ? wrapper.StatusMessage : "Account not connected";

    // --- Event Handlers (Modified to accept accountId and update specific wrapper) ---
    /// <summary>
    /// Helper to safely update the status and message within a specific client wrapper.
    /// </summary>
    /// <param name="accountId">The account ID of the wrapper to update.</param>
    /// <param name="status">The new connection status.</param>
    /// <param name="message">The new status message (optional).</param>
    private void UpdateWrapperStatus(string accountId, ConnectionStatus status, string? message = null)
    {
        if (_activeClients.TryGetValue(accountId, out TwitchClientWrapper? wrapper))
        {
            wrapper.Status = status;
            wrapper.StatusMessage = message ?? wrapper.StatusMessage; // Only update message if provided
            _logger.LogInformation(
                "[{Username}] Status updated: {ConnectionStatus} | Message: {StatusMessage}",
                wrapper.Username,
                status,
                wrapper.StatusMessage
            );
            // TODO: Consider firing an AccountStatusChanged event via Messenger if needed.
        }
        else
        {
            // This can happen if the disconnect event fires after the wrapper is removed.
            _logger.LogWarning("[{AccountId}] UpdateWrapperStatus called but wrapper not found (possibly already removed).", accountId);
        }
    }

    private void Client_OnConnected(string accountId, object? sender, OnConnectedArgs e)
    {
        if (_activeClients.TryGetValue(accountId, out TwitchClientWrapper? wrapper))
        {
            _logger.LogInformation("[{Username}] Connected successfully as {BotUsername}.", wrapper.Username, e.BotUsername);
            UpdateWrapperStatus(accountId, ConnectionStatus.Connected, $"Connected as {e.BotUsername}");
            SystemMessageEvent systemEvent = new()
            {
                Platform = "Twitch",
                OriginatingAccountId = accountId,
                Message = $"Connected Twitch account: {wrapper.Username}",
            };

            _ = _pluginService.RouteEventToProcessorsAsync(systemEvent);
            _messenger.Send(new NewEventMessage(systemEvent));
            _messenger.Send(new ConnectionsUpdatedMessage());

            // Automatically join the user's own channel upon connection
            if (!string.IsNullOrWhiteSpace(wrapper.Username))
            {
                _logger.LogDebug("[{Username}] Triggering initial data load and joining own channel.", wrapper.Username);
                _ = _emoteBadgeService.LoadChannelTwitchDataAsync(accountId, accountId);
                JoinChannelAsync(accountId, wrapper.Username);
            }
        }
        else
        {
            // Should be rare if logic is correct
            _logger.LogWarning("[{AccountId}] OnConnected event received for a non-existent client wrapper.", accountId);
        }
    }

    private void Client_OnDisconnected(string accountId, object? sender, OnDisconnectedEventArgs e)
    {
        if (_activeClients.TryGetValue(accountId, out TwitchClientWrapper? wrapper))
        {
            _logger.LogWarning("[{Username}] Disconnected event received.", wrapper.Username);
            if (wrapper.Status != ConnectionStatus.Disconnected)
            {
                UpdateWrapperStatus(accountId, ConnectionStatus.Disconnected, "Connection closed.");
            }

            SystemMessageEvent systemEvent = new()
            {
                Platform = "Twitch",
                OriginatingAccountId = accountId,
                Message = $"Twitch account {wrapper.Username} disconnected.",
                Level = SystemMessageLevel.Warning,
            };
            _ = _pluginService.RouteEventToProcessorsAsync(systemEvent);
            _messenger.Send(new NewEventMessage(systemEvent));
            _messenger.Send(new ConnectionsUpdatedMessage());

            // IMPORTANT: Do not remove the wrapper here. Let DisconnectAsync or a subsequent ConnectAsync handle removal.
            // This allows querying the final 'Disconnected' status.
        }
        else
        {
            _logger.LogDebug("[{AccountId}] OnDisconnected event received for a non-existent/removed wrapper.", accountId);
        }
    }

    private void Client_OnConnectionError(string accountId, object? sender, OnConnectionErrorArgs e)
    {
        if (_activeClients.TryGetValue(accountId, out TwitchClientWrapper? wrapper))
        {
            _logger.LogError("[{Username}] Connection Error: {ErrorMessage}", wrapper.Username, e.Error.Message);
            UpdateWrapperStatus(accountId, ConnectionStatus.Error, $"Connection Error: {e.Error.Message}");
            SystemMessageEvent systemEvent = new()
            {
                Platform = "Twitch",
                OriginatingAccountId = accountId,
                Message = $"Twitch connection error ({wrapper.Username}): {e.Error.Message}",
                Level = SystemMessageLevel.Error,
            };
            _ = _pluginService.RouteEventToProcessorsAsync(systemEvent);
            _messenger.Send(new NewEventMessage(systemEvent));
            _messenger.Send(new ConnectionsUpdatedMessage());
        }
        else
        {
            _logger.LogWarning("[{AccountId}] OnConnectionError event received for a non-existent client wrapper.", accountId);
        }
    }

    private void Client_OnError(string accountId, object? sender, OnErrorEventArgs e)
    {
        if (_activeClients.TryGetValue(accountId, out TwitchClientWrapper? wrapper))
        {
            _logger.LogError(e.Exception, "[{Username}] Communication Error: {ErrorMessage}", wrapper.Username, e.Exception.Message);
            SystemMessageEvent systemEvent = new()
            {
                Platform = "Twitch",
                OriginatingAccountId = accountId,
                Message = $"Twitch communication error ({wrapper.Username}): {e.Exception.Message}",
                Level = SystemMessageLevel.Error,
            };
            _ = _pluginService.RouteEventToProcessorsAsync(systemEvent);
            _messenger.Send(new NewEventMessage(systemEvent));
        }
        else
        {
            _logger.LogWarning("[{AccountId}] OnError event received for a non-existent client wrapper.", accountId);
        }
    }

    private void Client_OnJoinedChannel(string accountId, object? sender, OnJoinedChannelArgs e)
    {
        if (_activeClients.TryGetValue(accountId, out TwitchClientWrapper? wrapper))
        {
            _logger.LogInformation("[{Username}] Joined channel: #{ChannelName}", wrapper.Username, e.Channel);

            // TODO: Implement robust channel name -> ID lookup if joining channels other than the bot's own.
            string channelIdToLoad = accountId;
            bool isOwnChannel = e.Channel.Equals(wrapper.Username, StringComparison.OrdinalIgnoreCase);

            if (!isOwnChannel)
            {
                _logger.LogWarning(
                    "[{Username}] Joined external channel #{ChannelName}. Channel-specific data loading may require Channel ID lookup implementation.",
                    wrapper.Username,
                    e.Channel
                );
                // Placeholder: Trigger lookup if implemented
                // channelIdToLoad = await LookupChannelIdAsync(e.Channel); // Hypothetical method
            }

            // Trigger loading channel-specific badges/emotes in the background
            // Use the connected account's ID (accountId) as the context for the API call.
            _logger.LogDebug(
                "[{Username}] Triggering data load for channel #{ChannelName} (Channel ID: {ChannelId}, Context Account ID: {ContextAccountId})",
                wrapper.Username,
                e.Channel,
                channelIdToLoad,
                accountId
            );
            _ = _emoteBadgeService.LoadChannelTwitchDataAsync(channelIdToLoad, accountId);

            SystemMessageEvent systemEvent = new()
            {
                Platform = "Twitch",
                OriginatingAccountId = accountId,
                Message = $"Joined channel: #{e.Channel} (as {wrapper.Username})",
            };
            _ = _pluginService.RouteEventToProcessorsAsync(systemEvent);
            _messenger.Send(new NewEventMessage(systemEvent));
        }
        else
        {
            _logger.LogWarning("[{AccountId}] OnJoinedChannel event received for non-existent wrapper. Channel: {ChannelName}", accountId, e.Channel);
        }
    }

    private async Task Client_OnMessageReceived(string accountId, object? sender, OnMessageReceivedArgs e)
    {
        if (!_activeClients.ContainsKey(accountId))
        {
            _logger.LogDebug("[{AccountId}] OnMessageReceived skipped: Wrapper no longer active.", accountId);
            return;
        }

        _logger.LogTrace(
            "[{Username}] Received message in #{Channel}: {Message}",
            e.ChatMessage.DisplayName,
            e.ChatMessage.Channel,
            e.ChatMessage.Message
        );

        var baseDetails = new
        {
            Platform = "Twitch",
            Timestamp = DateTime.TryParse(e.ChatMessage.TmiSentTs, out DateTime tmiTime) ? tmiTime.ToUniversalTime() : DateTime.UtcNow,
            OriginatingAccountId = accountId,
        };

        string rawMessage = e.ChatMessage.Message;
        List<MessageSegment> parsedSegments = [];

        // --- Parse Emotes using EmoteSet ---
        if (e.ChatMessage.EmoteSet?.Emotes != null && e.ChatMessage.EmoteSet.Emotes.Count > 0)
        {
            try
            {
                // Sort emotes by start index to process the message linearly
                var sortedEmotes = e.ChatMessage.EmoteSet.Emotes.OrderBy(em => em.StartIndex).ToList();
                int currentPosition = 0;

                foreach (Emote? emote in sortedEmotes)
                {
                    // Add text segment before the current emote
                    if (emote.StartIndex > currentPosition)
                    {
                        parsedSegments.Add(new TextSegment { Text = rawMessage[currentPosition..emote.StartIndex] });
                    }

                    // Add the emote segment
                    parsedSegments.Add(
                        new EmoteSegment
                        {
                            Id = emote.Id,
                            Name = emote.Name,
                            ImageUrl = emote.ImageUrl, // Use the URL provided by TwitchLib
                            Platform = "Twitch",
                        }
                    );

                    // Update current position to be after the current emote
                    currentPosition = emote.EndIndex + 1;
                }

                // Add any remaining text segment after the last emote
                if (currentPosition < rawMessage.Length)
                {
                    parsedSegments.Add(new TextSegment { Text = rawMessage[currentPosition..] });
                }

                _logger.LogTrace("[{Username}] Parsed {EmoteCount} emotes for message.", e.ChatMessage.DisplayName, sortedEmotes.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "[{Username}] Error parsing emotes for message in #{Channel}. Raw Message: {RawMessage}",
                    e.ChatMessage.DisplayName,
                    e.ChatMessage.Channel,
                    rawMessage
                );
                // Fallback: treat the whole message as text if emote parsing fails
                parsedSegments = [new TextSegment { Text = rawMessage }];
            }
        }
        else
        {
            // No emotes found, the whole message is a single text segment
            if (!string.IsNullOrEmpty(rawMessage))
            {
                parsedSegments.Add(new TextSegment { Text = rawMessage });
            }
        }

        // --- Map Badges to BadgeInfo ---
        List<BadgeInfo> badgeInfoList = [];
        bool isOwner = false;
        if (e.ChatMessage.Badges != null)
        {
            foreach (KeyValuePair<string, string> badge in e.ChatMessage.Badges)
            {
                badgeInfoList.Add(new BadgeInfo($"twitch/{badge.Key}/{badge.Value}", null));
                if (badge.Key.Equals("broadcaster", StringComparison.OrdinalIgnoreCase))
                {
                    isOwner = true; // Set owner flag if broadcaster badge exists
                }
            }
        }
        // --- Calculate Username Color ---
        string? usernameColor = CalculateTwitchUsernameColor(badgeInfoList) ?? e.ChatMessage.ColorHex; // Fallback to user's chosen color

        // --- Handle Bits Donation (if applicable) ---
        if (e.ChatMessage.Bits > 0)
        {
            var bitsEvent = new DonationEvent
            {
                Platform = baseDetails.Platform,
                Timestamp = baseDetails.Timestamp,
                OriginatingAccountId = baseDetails.OriginatingAccountId,
                DonationId = e.ChatMessage.Id, // Use message ID as unique ID for the donation event
                UserId = e.ChatMessage.UserId,
                Username = e.ChatMessage.DisplayName,
                Amount = e.ChatMessage.Bits,
                Currency = "Bits",
                RawMessage = rawMessage,
                ParsedMessage = parsedSegments,
                Type = DonationType.Bits,
                Badges = badgeInfoList,
                UsernameColor = usernameColor,
                ProfileImageUrl = null,
                IsOwner = isOwner,
            };

            _logger.LogInformation(
                "[{Username}] Processed bits donation: {BitsAmount} bits from {DonatorUsername}",
                e.ChatMessage.DisplayName,
                bitsEvent.Amount,
                bitsEvent.Username
            );
            _ = _pluginService.RouteEventToProcessorsAsync(bitsEvent);
            _messenger.Send(new NewEventMessage(bitsEvent));

            // Typically, a bits message is *also* a chat message, but we prioritize the DonationEvent.
            // Decide whether to ALSO send a ChatMessageEvent or just the DonationEvent.
            // For now, let's just send the DonationEvent for bits messages.
            return;
        }

        // --- Handle Regular Chat Message ---
        var chatEvent = new ChatMessageEvent
        {
            Platform = baseDetails.Platform,
            Timestamp = baseDetails.Timestamp,
            OriginatingAccountId = baseDetails.OriginatingAccountId,
            // Channel = e.ChatMessage.Channel, // Add channel context
            // MessageId = e.ChatMessage.Id, // Add message ID
            UserId = e.ChatMessage.UserId,
            Username = e.ChatMessage.DisplayName,
            RawMessage = rawMessage,
            ParsedMessage = parsedSegments, // Use the segments generated above
            UsernameColor = usernameColor, // Use calculated color
            Badges = badgeInfoList, // Use BadgeInfo list
            // IsActionMessage = e.ChatMessage.IsAction,
            IsHighlight = e.ChatMessage.IsHighlighted,
            BitsDonated = e.ChatMessage.Bits, // Will be 0 here due to early return above
            ProfileImageUrl = null,
            IsOwner = isOwner, // Set owner flag
        };

        // --- Command Handling ---
        bool supressMessage = await _pluginService.TryHandleChatCommandAsync(chatEvent);

        if (!supressMessage)
        {
            _logger.LogTrace("[{AccountId}] Chat message not suppressed as command. Processing normally.", accountId);
            _ = _pluginService.RouteEventToProcessorsAsync(chatEvent);
            _messenger.Send(new NewEventMessage(chatEvent));
        }
        else
        {
            _logger.LogDebug(
                "[{AccountId}] Chat message in #{Channel} handled and suppressed by command processor.",
                accountId,
                chatEvent.OriginatingAccountId
            );
        }
    }

    private void Client_OnNewSubscriber(string accountId, object? sender, OnNewSubscriberArgs e)
    {
        if (!_activeClients.ContainsKey(accountId))
            return;

        _logger.LogInformation(
            "[{AccountId}] Subscription Event [#{Channel}] User: {Username}, Plan: {Plan}",
            accountId,
            e.Channel,
            e.Subscriber.DisplayName,
            e.Subscriber.SubscriptionPlan.ToString()
        );

        List<BadgeInfo> badgeInfoList = e.Subscriber.Badges?.Select(b => new BadgeInfo($"twitch/{b.Key}/{b.Value}", null)).ToList() ?? [];
        bool isOwner = badgeInfoList.Any(b => b.Identifier.StartsWith("twitch/broadcaster/")); // Check owner from badges
        string? usernameColor = CalculateTwitchUsernameColor(badgeInfoList) ?? e.Subscriber.ColorHex;

        var subEvent = new SubscriptionEvent
        {
            Platform = "Twitch",
            OriginatingAccountId = accountId,
            Timestamp = DateTime.UtcNow, // TODO: Consider parsing TMI timestamp if needed/reliable
            UserId = e.Subscriber.UserId,
            Username = e.Subscriber.DisplayName,
            IsGift = false,
            // SubKind = e.Subscriber.MsgId == "resub" ? SubKind.Resub : SubKind.New, // Use MsgId for sub kind
            Months = 1, // Resub message might contain total months, but this event often signifies the single month action
            CumulativeMonths = int.TryParse(e.Subscriber.MsgParamCumulativeMonths, out int cm) ? cm : (e.Subscriber.MsgId == "resub" ? 1 : 0), // Best guess
            // StreakMonths = int.TryParse(e.Subscriber.MsgParamStreakMonths, out int sm) ? sm : (e.Subscriber.MsgId == "resub" ? 1 : 0), // Best guess
            Tier = MapTwitchSubPlan(e.Subscriber.SubscriptionPlan),
            // PlanName = e.Subscriber.SubscriptionPlan.ToString(), // Store enum name
            Message = e.Subscriber.ResubMessage, // Can be null for new subs
            // ParsedMessage = string.IsNullOrWhiteSpace(e.Subscriber.ResubMessage) ? [] : [new TextSegment { Text = e.Subscriber.ResubMessage }],
            Badges = badgeInfoList, // Use BadgeInfo list
            UsernameColor = usernameColor, // Use calculated color
            ProfileImageUrl = null,
            IsOwner = isOwner,
        };
        _ = _pluginService.RouteEventToProcessorsAsync(subEvent);
        _messenger.Send(new NewEventMessage(subEvent));
    }

    private void Client_OnGiftedSubscription(string accountId, object? sender, OnGiftedSubscriptionArgs e)
    {
        if (!_activeClients.ContainsKey(accountId))
            return;

        // TwitchLib provides details in e.GiftedSubscription
        _logger.LogInformation(
            "[{AccountId}] Gift Subscription Event [#{Channel}] Gifter: {GifterUsername} -> Recipient: {RecipientUsername}, Months: {Months}, Plan: {Plan}",
            accountId,
            e.Channel,
            e.GiftedSubscription.DisplayName,
            e.GiftedSubscription.MsgParamRecipientDisplayName,
            e.GiftedSubscription.MsgParamMultiMonthGiftDuration ?? "1", // Duration if multi-month gift
            e.GiftedSubscription.MsgParamSubPlan.ToString()
        );

        List<BadgeInfo> badgeInfoList = e.GiftedSubscription.Badges?.Select(b => new BadgeInfo($"twitch/{b.Key}/{b.Value}", null)).ToList() ?? [];
        bool isOwner = badgeInfoList.Any(b => b.Identifier.StartsWith("twitch/broadcaster/"));
        string? usernameColor = CalculateTwitchUsernameColor(badgeInfoList) ?? e.GiftedSubscription.Color;

        var giftEvent = new SubscriptionEvent
        {
            Platform = "Twitch",
            OriginatingAccountId = accountId,
            Timestamp = DateTime.UtcNow, // TODO: Consider TMI timestamp
            UserId = e.GiftedSubscription.UserId, // Gifter's User ID
            Username = e.GiftedSubscription.DisplayName, // Gifter's Display Name
            RecipientUsername = e.GiftedSubscription.MsgParamRecipientDisplayName,
            RecipientUserId = e.GiftedSubscription.MsgParamRecipientId,
            IsGift = true,
            Months = int.TryParse(e.GiftedSubscription.MsgParamMultiMonthGiftDuration, out int dur) ? dur : 1, // Duration of the gifted sub(s)
            // GiftCount = ?? // TwitchLib doesn't easily expose if this is part of a mass gift
            // TotalGiftCount = int.TryParse(e.GiftedSubscription.MsgParam.MsgParamSenderCount, out int sc) ? sc : (e.GiftedSubscription.IsAnonymous ? 0 : 1), // Gifter's total gifts in channel (approximation)
            Tier = MapTwitchSubPlan(e.GiftedSubscription.MsgParamSubPlan),
            // PlanName = e.GiftedSubscription.MsgParamSubPlan.ToString(),
            Badges = badgeInfoList, // Use BadgeInfo list (Gifter's badges)
            UsernameColor = usernameColor, // Use calculated color (Gifter's color)
            // IsAnonymous = e.GiftedSubscription.IsAnonymous // Check if the gift was anonymous
            ProfileImageUrl = null,
            IsOwner = isOwner,
        };

        _ = _pluginService.RouteEventToProcessorsAsync(giftEvent);
        _messenger.Send(new NewEventMessage(giftEvent));
    }

    private void Client_OnRaidNotification(string accountId, object? sender, OnRaidNotificationArgs e)
    {
        if (!_activeClients.ContainsKey(accountId))
            return;

        _logger.LogInformation(
            "[{AccountId}] Raid Notification [#{Channel}] Raider: {RaiderUsername}, Viewers: {ViewerCount}",
            accountId,
            e.Channel,
            e.RaidNotification.DisplayName,
            e.RaidNotification.MsgParamViewerCount ?? "0"
        );
        var raidEvent = new RaidEvent
        {
            Platform = "Twitch",
            OriginatingAccountId = accountId,
            Timestamp = DateTime.UtcNow, // TODO: Consider TMI timestamp
            RaiderUsername = e.RaidNotification.DisplayName,
            RaiderUserId = e.RaidNotification.UserId,
            ViewerCount = int.TryParse(e.RaidNotification.MsgParamViewerCount, out int count) ? count : 0,
        };

        _ = _pluginService.RouteEventToProcessorsAsync(raidEvent);
        _messenger.Send(new NewEventMessage(raidEvent));
    }

    /// <summary>
    /// Helper method to map TwitchLib's SubscriptionPlan enum to a string representation.
    /// </summary>
    /// <param name="plan">The TwitchLib SubscriptionPlan enum value.</param>
    /// <returns>A user-friendly string representation of the subscription tier.</returns>
    private static string MapTwitchSubPlan(SubscriptionPlan plan) =>
        plan switch
        {
            SubscriptionPlan.Prime => "Twitch Prime",
            SubscriptionPlan.Tier1 => "Tier 1",
            SubscriptionPlan.Tier2 => "Tier 2",
            SubscriptionPlan.Tier3 => "Tier 3",
            SubscriptionPlan.NotSet => "Tier 1", // Treat NotSet as Tier 1 as per Twitch docs usually
            _ => "Unknown Tier",
        };

    /// <summary>
    /// Disposes the service, ensuring all active client connections are closed and resources released.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
    /// </summary>
    /// <param name="disposing">True if called from Dispose(), false if called from the finalizer.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (_isDisposed)
            return;

        _logger.LogDebug("Dispose({Disposing}) called.", disposing);

        if (disposing)
        {
            // Dispose managed state (managed objects).
            _logger.LogInformation("Disposing TwitchChatService...");
            // Create a list of account IDs to avoid modifying the dictionary while iterating
            var accountIds = _activeClients.Keys.ToList();
            var disconnectTasks = new List<Task>();
            _logger.LogDebug("Initiating disconnection for {ClientCount} active clients...", accountIds.Count);
            foreach (string accountId in accountIds)
            {
                // Call disconnect which handles removal and disposal of the wrapper/client
                disconnectTasks.Add(DisconnectAsync(accountId));
            }
            // Wait for disconnect tasks to complete (with a reasonable timeout)
            try
            {
                if (!Task.WhenAll(disconnectTasks).Wait(TimeSpan.FromSeconds(5))) // Increased timeout
                {
                    _logger.LogWarning("Timeout occurred while waiting for disconnect tasks during dispose.");
                }

                _logger.LogDebug("Finished waiting for disconnect tasks.");
            }
            catch (AggregateException agEx) when (agEx.InnerExceptions.All(ex => ex is TaskCanceledException))
            {
                _logger.LogWarning("One or more disconnect tasks timed out or were cancelled during dispose.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while waiting for disconnect tasks during dispose: {ErrorMessage}", ex.Message);
            }

            _activeClients.Clear(); // Ensure dictionary is cleared after attempting disposal
            _logger.LogInformation("TwitchChatService Dispose finished.");
        }

        // Free unmanaged resources (unmanaged objects) and override finalizer
        // No unmanaged resources directly held by this class.

        _isDisposed = true;
    }
}
