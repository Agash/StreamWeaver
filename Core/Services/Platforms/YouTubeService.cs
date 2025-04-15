using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using CommunityToolkit.Mvvm.Messaging;
using Google;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.YouTube.v3;
using Google.Apis.YouTube.v3.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StreamWeaver.Core.Messaging;
using StreamWeaver.Core.Models.Events;
using StreamWeaver.Core.Models.Events.Messages;
using StreamWeaver.Core.Models.Settings;
using StreamWeaver.Core.Plugins;
using StreamWeaver.Core.Services.Settings;
using YTLiveChat.Contracts.Services;
using YTLiveChatChatItem = YTLiveChat.Contracts.Models.ChatItem;
using YTLiveChatMembershipDetails = YTLiveChat.Contracts.Models.MembershipDetails;
using YTLiveChatMessagePart = YTLiveChat.Contracts.Models.MessagePart;
using YTLiveChatSuperchat = YTLiveChat.Contracts.Models.Superchat;

namespace StreamWeaver.Core.Services.Platforms;

// Internal helper class YouTubeClientWrapper
internal sealed partial class YouTubeClientWrapper(string accountId, Google.Apis.YouTube.v3.YouTubeService officialApiService, ILogger logger)
    : IDisposable
{
    public string AccountId { get; } = accountId ?? throw new ArgumentNullException(nameof(accountId));
    public Google.Apis.YouTube.v3.YouTubeService OfficialApiService { get; } =
        officialApiService ?? throw new ArgumentNullException(nameof(officialApiService));
    public ConnectionStatus Status { get; set; } = ConnectionStatus.Disconnected;
    public string? StatusMessage { get; set; }
    public IYTLiveChat? YtChatReaderClient { get; set; }
    public string? ActiveVideoId { get; set; }
    public string? AssociatedLiveChatId { get; set; }
    public bool IsMonitoring => YtChatReaderClient != null;
    private bool _disposed = false;
    private readonly ILogger _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public EventHandler<ChatReceivedEventArgs>? ChatReceivedHandler { get; set; }
    public EventHandler<ErrorOccurredEventArgs>? ErrorOccurredHandler { get; set; }
    public EventHandler<ChatStoppedEventArgs>? ChatStoppedHandler { get; set; }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _logger.LogDebug("[{AccountId}] Disposing YouTubeClientWrapper...", AccountId);
        try
        {
            if (YtChatReaderClient != null)
            {
                if (ChatReceivedHandler != null)
                    YtChatReaderClient.ChatReceived -= ChatReceivedHandler;
                if (ErrorOccurredHandler != null)
                    YtChatReaderClient.ErrorOccurred -= ErrorOccurredHandler;
                if (ChatStoppedHandler != null)
                    YtChatReaderClient.ChatStopped -= ChatStoppedHandler;
            }

            YtChatReaderClient?.Dispose();
            YtChatReaderClient = null;
            ActiveVideoId = null;
            AssociatedLiveChatId = null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[{AccountId}] Exception during YtChatReaderClient disposal.", AccountId);
        }

        try
        {
            OfficialApiService.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[{AccountId}] Exception during OfficialApiService disposal.", AccountId);
        }

        _logger.LogDebug("[{AccountId}] YouTubeClientWrapper disposed.", AccountId);
        GC.SuppressFinalize(this);
    }
}

public partial class YouTubeService : IYouTubeClient, IDisposable
{
    private readonly IMessenger _messenger;
    private readonly IEmoteBadgeService _emoteBadgeService;
    private readonly PluginService _pluginService;
    private readonly IServiceProvider _serviceProvider;
    private readonly ISettingsService _settingsService;
    private readonly ILogger<YouTubeService> _logger;
    private readonly ConcurrentDictionary<string, YouTubeClientWrapper> _activeClients = new();

    private static readonly Dictionary<string, (int Priority, string Color)> s_youTubeBadgeColorPriority = new(StringComparer.OrdinalIgnoreCase)
    {
        { "owner", (10, "#FFD700") },
        { "moderator", (7, "#5E84F1") },
        { "member", (5, "#0F9D58") },
        { "verified", (3, "#AAAAAA") },
    };

    private bool _isDisposed = false;

    public YouTubeService(
        IMessenger messenger,
        IEmoteBadgeService emoteBadgeService,
        PluginService pluginService,
        IServiceProvider serviceProvider,
        ISettingsService settingsService,
        ILogger<YouTubeService> logger
    )
    {
        _messenger = messenger ?? throw new ArgumentNullException(nameof(messenger));
        _emoteBadgeService = emoteBadgeService ?? throw new ArgumentNullException(nameof(emoteBadgeService));
        _pluginService = pluginService ?? throw new ArgumentNullException(nameof(pluginService));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _logger.LogInformation("Initialized.");
    }

    public async Task<bool> ConnectAsync(string accountId, string accessToken)
    {
        string logAccountId = accountId ?? "N/A";
        if (_isDisposed)
        {
            _logger.LogWarning("[{AccountId}] Connect failed: Service is disposed.", logAccountId);
            return false;
        }

        if (string.IsNullOrWhiteSpace(accountId) || string.IsNullOrWhiteSpace(accessToken))
        {
            _logger.LogError("[{AccountId}] Connect failed: Invalid parameters (AccountId or AccessToken missing/empty).", logAccountId);
            return false;
        }

        if (
            _activeClients.TryGetValue(accountId, out YouTubeClientWrapper? existingWrapper)
            && (
                existingWrapper.Status == ConnectionStatus.Connected
                || existingWrapper.Status == ConnectionStatus.Limited
                || existingWrapper.Status == ConnectionStatus.Connecting
            )
        )
        {
            _logger.LogInformation("[{AccountId}] Connect requested but already {ConnectionStatus}.", accountId, existingWrapper.Status);
            return existingWrapper.Status is ConnectionStatus.Connected or ConnectionStatus.Limited;
        }

        if (existingWrapper != null)
        {
            _logger.LogInformation(
                "[{AccountId}] Removing previous client wrapper (State: {ConnectionStatus}) before reconnecting.",
                accountId,
                existingWrapper.Status
            );
            if (_activeClients.TryRemove(accountId, out YouTubeClientWrapper? removed))
                removed?.Dispose();
        }

        _logger.LogInformation("[{AccountId}] Creating/Validating official YouTube API service instance.", accountId);
        UpdateAccountModelStatus(accountId, ConnectionStatus.Connecting, "Initializing API...");
        try
        {
            var credential = GoogleCredential.FromAccessToken(accessToken);
            var officialApiService = new Google.Apis.YouTube.v3.YouTubeService(
                new BaseClientService.Initializer() { HttpClientInitializer = credential, ApplicationName = "StreamWeaver" }
            );
            _logger.LogDebug("[{AccountId}] Performing test API call (Channels.List(mine=true, part=id)) to check token/quota...", accountId);
            ChannelsResource.ListRequest testRequest = officialApiService.Channels.List("id");
            testRequest.Mine = true;
            await testRequest.ExecuteAsync();
            _logger.LogDebug("[{AccountId}] Test API call successful.", accountId);
            var wrapper = new YouTubeClientWrapper(accountId, officialApiService, _logger)
            {
                Status = ConnectionStatus.Connected,
                StatusMessage = "Ready (No Stream Active)",
            };
            if (!_activeClients.TryAdd(accountId, wrapper))
            {
                _logger.LogCritical("[{AccountId}] Failed to add client wrapper to dictionary (race condition?). Aborting connection.", accountId);
                wrapper.Dispose();
                UpdateAccountModelStatus(accountId, ConnectionStatus.Error, "Failed to store connection state.");
                _messenger.Send(new ConnectionsUpdatedMessage());
                return false;
            }

            _logger.LogInformation("[{AccountId}] YouTube official API Service initialized successfully.", accountId);
            UpdateAccountModelStatus(accountId, ConnectionStatus.Connected, "Ready (No Stream Active)");
            _messenger.Send(new ConnectionsUpdatedMessage());
            return true;
        }
        catch (GoogleApiException apiEx) when (IsQuotaError(apiEx))
        {
            _logger.LogWarning(
                apiEx,
                "[{AccountId}] Quota error during API initialization/test call. Entering Limited (Read-Only) state.",
                accountId
            );
            var credential = GoogleCredential.FromAccessToken(accessToken);
            var officialApiService = new Google.Apis.YouTube.v3.YouTubeService(
                new BaseClientService.Initializer() { HttpClientInitializer = credential, ApplicationName = "StreamWeaver" }
            );
            var wrapper = new YouTubeClientWrapper(accountId, officialApiService, _logger)
            {
                Status = ConnectionStatus.Limited,
                StatusMessage = "Read-Only (API Quota Reached)",
            };
            if (!_activeClients.TryAdd(accountId, wrapper))
            {
                _logger.LogCritical(
                    "[{AccountId}] Failed to add client wrapper to dictionary (race condition?) after quota error. Aborting connection.",
                    accountId
                );
                wrapper.Dispose();
                UpdateAccountModelStatus(accountId, ConnectionStatus.Error, "Failed to store limited connection state.");
                _messenger.Send(new ConnectionsUpdatedMessage());
                return false;
            }

            _logger.LogInformation("[{AccountId}] YouTube API Service initialized in LIMITED state due to quota.", accountId);
            UpdateAccountModelStatus(accountId, ConnectionStatus.Limited, "Read-Only (API Quota Reached)");
            _messenger.Send(new ConnectionsUpdatedMessage());
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{AccountId}] Failed to initialize YouTube official API Service: {ErrorMessage}", accountId, ex.Message);
            if (_activeClients.TryRemove(accountId, out YouTubeClientWrapper? failed))
                failed?.Dispose();
            UpdateAccountModelStatus(accountId, ConnectionStatus.Error, $"API Init Failed: {ex.Message}");
            _messenger.Send(new ConnectionsUpdatedMessage());
            return false;
        }
    }

    public Task DisconnectAsync(string accountId)
    {
        if (_isDisposed)
        {
            _logger.LogDebug("[{AccountId}] Disconnect skipped: Service is disposed.", accountId);
            return Task.CompletedTask;
        }

        _logger.LogInformation("[{AccountId}] Disconnect requested.", accountId);
        if (_activeClients.TryRemove(accountId, out YouTubeClientWrapper? wrapper))
        {
            _logger.LogInformation("[{AccountId}] Disposing client wrapper.", accountId);
            wrapper.Dispose();
            _logger.LogInformation("[{AccountId}] Client wrapper removed and disposed.", accountId);
        }
        else
        {
            _logger.LogWarning("[{AccountId}] Client wrapper not found during disconnect request.", accountId);
        }

        UpdateAccountModelStatus(accountId, ConnectionStatus.Disconnected, "Disconnected.");
        _messenger.Send(new ConnectionsUpdatedMessage());

        return Task.CompletedTask;
    }

    public async Task<string?> FindActiveVideoIdAsync(string accountId)
    {
        if (!_activeClients.TryGetValue(accountId, out YouTubeClientWrapper? wrapper))
        {
            _logger.LogWarning("[{AccountId}] Cannot find active video ID, client wrapper not found.", accountId);
            return null;
        }

        if (wrapper.OfficialApiService == null)
        {
            _logger.LogError("[{AccountId}] Cannot find active video ID, internal official API service instance is null.", accountId);
            return null;
        }

        if (wrapper.Status == ConnectionStatus.Limited)
        {
            _logger.LogWarning("[{AccountId}] Cannot find active stream via API: Currently in Limited (Quota) state.", accountId);
            return null;
        }

        _logger.LogInformation("[{AccountId}] Searching for USER'S active broadcast Video ID using official API...", accountId);
        try
        {
            LiveBroadcastsResource.ListRequest request = wrapper.OfficialApiService.LiveBroadcasts.List("id,status");
            request.Mine = true;
            LiveBroadcastListResponse response = await request.ExecuteAsync();
            LiveBroadcast? activeBroadcast = response.Items?.Where(b => b.Status.LifeCycleStatus == "live").FirstOrDefault();
            activeBroadcast ??= response.Items?.Where(b => b.Status.LifeCycleStatus == "liveStarting").FirstOrDefault();
            activeBroadcast ??= response.Items?.Where(b => b.Status.LifeCycleStatus == "ready").FirstOrDefault();
            if (activeBroadcast != null && !string.IsNullOrEmpty(activeBroadcast.Id))
            {
                _logger.LogInformation("[{AccountId}] Found user's active broadcast VideoID={VideoId}", accountId, activeBroadcast.Id);
                return activeBroadcast.Id;
            }
            else
            {
                _logger.LogInformation("[{AccountId}] No active broadcast found for user using official API.", accountId);
                return null;
            }
        }
        catch (GoogleApiException apiEx) when (IsQuotaError(apiEx))
        {
            _logger.LogWarning(apiEx, "[{AccountId}] Quota error during active stream Video ID lookup. Entering Limited state.", accountId);
            UpdateWrapperStatus(wrapper, ConnectionStatus.Limited, "Read-Only (API Quota Reached)");
            _messenger.Send(new ConnectionsUpdatedMessage());
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{AccountId}] Error finding active broadcast Video ID: {ErrorMessage}", accountId, ex.Message);
            UpdateWrapperStatus(wrapper, ConnectionStatus.Error, $"API Error: {ex.Message}");
            _messenger.Send(new ConnectionsUpdatedMessage());
            return null;
        }
    }

    private async Task<bool> GetAndStoreLiveChatIdForVideoAsync(string accountId, string videoId)
    {
        if (!_activeClients.TryGetValue(accountId, out YouTubeClientWrapper? wrapper))
        {
            _logger.LogWarning("[{AccountId}] Cannot get LiveChatId for Video {VideoId}, client wrapper not found.", accountId, videoId);
            return false;
        }

        if (wrapper.OfficialApiService == null)
        {
            _logger.LogError(
                "[{AccountId}] Cannot get LiveChatId for Video {VideoId}, internal official API service instance is null.",
                accountId,
                videoId
            );
            return false;
        }

        if (wrapper.Status == ConnectionStatus.Limited)
        {
            _logger.LogWarning(
                "[{AccountId}] Cannot get LiveChatId for Video {VideoId} via API: Currently in Limited (Quota) state.",
                accountId,
                videoId
            );
            return false;
        }

        _logger.LogInformation("[{AccountId}] Looking up LiveChatId for specific VideoID: {VideoId}...", accountId, videoId);
        try
        {
            VideosResource.ListRequest request = wrapper.OfficialApiService.Videos.List("liveStreamingDetails");
            request.Id = videoId;
            VideoListResponse response = await request.ExecuteAsync();
            Video? video = response.Items?.FirstOrDefault();
            string? foundLiveChatId = video?.LiveStreamingDetails?.ActiveLiveChatId;
            if (!string.IsNullOrEmpty(foundLiveChatId))
            {
                wrapper.AssociatedLiveChatId = foundLiveChatId;
                _logger.LogInformation("[{AccountId}] Found LiveChatId '{LiveChatId}' for VideoID {VideoId}.", accountId, foundLiveChatId, videoId);
                return true;
            }
            else
            {
                _logger.LogWarning(
                    "[{AccountId}] Could not find LiveChatId for VideoID {VideoId}. Video might not be live, have chat enabled, or exist.",
                    accountId,
                    videoId
                );
                wrapper.AssociatedLiveChatId = null;
                return false;
            }
        }
        catch (GoogleApiException apiEx) when (IsQuotaError(apiEx))
        {
            _logger.LogWarning(
                apiEx,
                "[{AccountId}] Quota error during LiveChatId lookup for VideoID {VideoId}. Entering Limited state.",
                accountId,
                videoId
            );
            UpdateWrapperStatus(wrapper, ConnectionStatus.Limited, "Read-Only (API Quota Reached)");
            _messenger.Send(new ConnectionsUpdatedMessage());
            wrapper.AssociatedLiveChatId = null;
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{AccountId}] Error looking up LiveChatId for VideoID {VideoId}: {ErrorMessage}", accountId, videoId, ex.Message);
            wrapper.AssociatedLiveChatId = null;
            UpdateWrapperStatus(wrapper, ConnectionStatus.Error, $"API Error: {ex.Message}");
            _messenger.Send(new ConnectionsUpdatedMessage());
            return false;
        }
    }

    public async Task<string?> LookupAndStoreLiveChatIdAsync(string accountId, string videoId)
    {
        _ = await GetAndStoreLiveChatIdForVideoAsync(accountId, videoId);
        return _activeClients.TryGetValue(accountId, out YouTubeClientWrapper? wrapper) ? wrapper.AssociatedLiveChatId : null;
    }

    public async Task StartPollingAsync(string accountId, string videoId)
    {
        if (!_activeClients.TryGetValue(accountId, out YouTubeClientWrapper? wrapper))
        {
            _logger.LogWarning("[{AccountId}] Cannot start monitoring, client wrapper not found.", accountId);
            return;
        }

        if (string.IsNullOrWhiteSpace(videoId))
        {
            _logger.LogError("[{AccountId}] Cannot start monitoring, Video ID is null or empty.", accountId);
            return;
        }

        if (wrapper.IsMonitoring && wrapper.ActiveVideoId == videoId)
        {
            _logger.LogInformation("[{AccountId}] Already monitoring Video ID: {VideoId}", accountId, videoId);
            return;
        }

        if (wrapper.IsMonitoring)
        {
            _logger.LogInformation("[{AccountId}] Stopping previous monitoring before starting new one for Video ID: {VideoId}", accountId, videoId);
            await StopPollingAsync(accountId);
        }

        // Attempt to lookup the LiveChatId *before* starting the polling if not already known
        if (string.IsNullOrEmpty(wrapper.AssociatedLiveChatId))
        {
            await GetAndStoreLiveChatIdForVideoAsync(accountId, videoId);
        }

        _logger.LogInformation("[{AccountId}] Starting chat monitoring for Video ID: {VideoId}", accountId, videoId);
        ConnectionStatus connectingStatus = wrapper.Status == ConnectionStatus.Limited ? ConnectionStatus.Limited : ConnectionStatus.Connecting;
        UpdateWrapperStatus(wrapper, connectingStatus, $"Monitoring chat for: {videoId}");
        _messenger.Send(new ConnectionsUpdatedMessage());
        try
        {
            wrapper.YtChatReaderClient = _serviceProvider.GetRequiredService<IYTLiveChat>();
            wrapper.ActiveVideoId = videoId;
            wrapper.ChatReceivedHandler = (sender, args) => HandleYtChatReceived(accountId, sender, args);
            wrapper.YtChatReaderClient.ChatReceived += wrapper.ChatReceivedHandler;
            wrapper.ErrorOccurredHandler = (sender, args) => HandleYtChatError(accountId, sender, args);
            wrapper.YtChatReaderClient.ErrorOccurred += wrapper.ErrorOccurredHandler;
            wrapper.ChatStoppedHandler = (sender, args) => HandleYtChatStopped(accountId, sender, args);
            wrapper.YtChatReaderClient.ChatStopped += wrapper.ChatStoppedHandler;
            wrapper.YtChatReaderClient.Start(liveId: videoId);
            ConnectionStatus finalStatus = wrapper.Status == ConnectionStatus.Limited ? ConnectionStatus.Limited : ConnectionStatus.Connected;
            string finalStatusMsg = finalStatus == ConnectionStatus.Limited ? $"Read-Only Monitoring: {videoId}" : $"Monitoring chat: {videoId}";
            UpdateWrapperStatus(wrapper, finalStatus, finalStatusMsg);
            _messenger.Send(new ConnectionsUpdatedMessage());
            SystemMessageEvent systemEvent = new()
            {
                Platform = "YouTube",
                OriginatingAccountId = accountId,
                Message = $"Started monitoring YouTube chat for Video ID: {videoId}",
            };
            _ = _pluginService.RouteEventToProcessorsAsync(systemEvent);
            _messenger.Send(new NewEventMessage(systemEvent));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{AccountId}] Failed to create or start YTLiveChat reader for Video ID: {VideoId}", accountId, videoId);
            UpdateWrapperStatus(wrapper, ConnectionStatus.Error, "Failed to start chat reader.");
            wrapper.YtChatReaderClient?.Dispose();
            wrapper.YtChatReaderClient = null;
            wrapper.ActiveVideoId = null;
            wrapper.AssociatedLiveChatId = null;
            _messenger.Send(new ConnectionsUpdatedMessage());
        }
    }

    public Task StopPollingAsync(string accountId)
    {
        if (!_activeClients.TryGetValue(accountId, out YouTubeClientWrapper? wrapper))
        {
            _logger.LogWarning("[{AccountId}] Cannot stop monitoring, client wrapper not found.", accountId);
            return Task.CompletedTask;
        }

        if (!wrapper.IsMonitoring || wrapper.YtChatReaderClient == null)
        {
            _logger.LogDebug("[{AccountId}] Chat monitoring not active, stop request ignored.", accountId);
            return Task.CompletedTask;
        }

        _logger.LogInformation("[{AccountId}] Stopping chat monitoring for Video ID: {VideoId}...", accountId, wrapper.ActiveVideoId);
        bool statusChanged = false;
        try
        {
            if (wrapper.ChatReceivedHandler != null)
                wrapper.YtChatReaderClient.ChatReceived -= wrapper.ChatReceivedHandler;
            if (wrapper.ErrorOccurredHandler != null)
                wrapper.YtChatReaderClient.ErrorOccurred -= wrapper.ErrorOccurredHandler;
            if (wrapper.ChatStoppedHandler != null)
                wrapper.YtChatReaderClient.ChatStopped -= wrapper.ChatStoppedHandler;
            wrapper.YtChatReaderClient.Stop();
            wrapper.YtChatReaderClient.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "[{AccountId}] Exception occurred while stopping/disposing YTLiveChat reader: {ErrorMessage}",
                accountId,
                ex.Message
            );
        }
        finally
        {
            wrapper.YtChatReaderClient = null;
            wrapper.ActiveVideoId = null;
            wrapper.AssociatedLiveChatId = null;
            ConnectionStatus finalStatus = wrapper.Status == ConnectionStatus.Limited ? ConnectionStatus.Limited : ConnectionStatus.Connected;
            string finalStatusMsg = finalStatus == ConnectionStatus.Limited ? "Read-Only (Monitoring Stopped)" : "Ready (No Stream Active)";
            if (wrapper.Status != finalStatus || wrapper.StatusMessage != finalStatusMsg)
            {
                UpdateWrapperStatus(wrapper, finalStatus, finalStatusMsg);
                statusChanged = true;
            }

            _logger.LogInformation("[{AccountId}] Chat monitoring stopped.", accountId);
            if (statusChanged)
            {
                _messenger.Send(new ConnectionsUpdatedMessage());
            }
        }

        return Task.CompletedTask;
    }

    public async Task SendMessageAsync(string accountId, string liveChatId, string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            _logger.LogWarning("[{AccountId}] Cannot send empty or whitespace message.", accountId);
            return;
        }

        if (!_activeClients.TryGetValue(accountId, out YouTubeClientWrapper? wrapper))
        {
            _logger.LogWarning("[{AccountId}] Cannot send message, client wrapper not found.", accountId);
            return;
        }

        string? targetLiveChatId = liveChatId; // Use the passed-in value first
        if (string.IsNullOrWhiteSpace(targetLiveChatId))
        {
            _logger.LogInformation(
                "[{AccountId}] SendMessageAsync: LiveChatId not provided. Attempting lookup using ActiveVideoId '{ActiveVideoId}'...",
                accountId,
                wrapper.ActiveVideoId ?? "N/A"
            );
            if (!string.IsNullOrEmpty(wrapper.ActiveVideoId))
            {
                targetLiveChatId = await LookupAndStoreLiveChatIdAsync(accountId, wrapper.ActiveVideoId);
            }

            if (string.IsNullOrWhiteSpace(targetLiveChatId))
            {
                _logger.LogError(
                    "[{AccountId}] Cannot send message: Failed to determine target LiveChatId (lookup failed or no active Video ID).",
                    accountId
                );
                SystemMessageEvent errorLookupEvent = new()
                {
                    Level = SystemMessageLevel.Error,
                    Message = $"Cannot send YouTube message from {accountId}: Could not find active chat ID.",
                };
                _messenger.Send(new NewEventMessage(errorLookupEvent));
                return;
            }

            _logger.LogInformation("[{AccountId}] SendMessageAsync: Using looked-up LiveChatId '{LiveChatId}'.", accountId, targetLiveChatId);
        }

        if (wrapper.Status == ConnectionStatus.Limited)
        {
            _logger.LogWarning("[{AccountId}] Cannot send message: Client is in Limited (Read-Only) state due to API quota.", accountId);
            SystemMessageEvent errorEvent = new()
            {
                Level = SystemMessageLevel.Warning,
                Message = $"Cannot send YouTube message from {accountId}: API quota reached.",
            };
            _messenger.Send(new NewEventMessage(errorEvent));
            return;
        }

        if (wrapper.OfficialApiService == null)
        {
            _logger.LogError("[{AccountId}] Cannot send message, official API service instance is null.", accountId);
            SystemMessageEvent errorEvent = new()
            {
                Level = SystemMessageLevel.Error,
                Message = $"Cannot send YouTube message from {accountId}: API Client not ready.",
            };
            _messenger.Send(new NewEventMessage(errorEvent));
            return;
        }

        if (wrapper.Status != ConnectionStatus.Connected)
        {
            _logger.LogWarning(
                "[{AccountId}] Cannot send message, client not in Connected state (Current Status: {ConnectionStatus}).",
                accountId,
                wrapper.Status
            );
            SystemMessageEvent errorEvent = new()
            {
                Level = SystemMessageLevel.Warning,
                Message = $"Cannot send YouTube message from {accountId}: Not connected.",
            };
            _messenger.Send(new NewEventMessage(errorEvent));
            return;
        }

        var liveChatMessage = new LiveChatMessage
        {
            Snippet = new LiveChatMessageSnippet
            {
                LiveChatId = targetLiveChatId,
                Type = "textMessageEvent",
                TextMessageDetails = new LiveChatTextMessageDetails { MessageText = message },
            },
        };
        _logger.LogInformation(
            "[{AccountId}] Sending message via official API to LiveChatId {LiveChatId}: {Message}",
            accountId,
            targetLiveChatId,
            message
        );
        try
        {
            LiveChatMessagesResource.InsertRequest request = wrapper.OfficialApiService.LiveChatMessages.Insert(liveChatMessage, "snippet");
            LiveChatMessage responseMessage = await request.ExecuteAsync();
            _logger.LogDebug("[{AccountId}] Message sent successfully via official API. Message ID: {MessageId}", accountId, responseMessage.Id);
        }
        catch (GoogleApiException apiEx) when (IsQuotaError(apiEx))
        {
            _logger.LogWarning(apiEx, "[{AccountId}] Quota error during message send. Entering Limited (Read-Only) state.", accountId);
            UpdateWrapperStatus(wrapper, ConnectionStatus.Limited, "Read-Only (API Quota Reached)");
            SystemMessageEvent sysErr = new()
            {
                Platform = "YouTube",
                OriginatingAccountId = accountId,
                Message = "Failed to send message: API Quota reached.",
                Level = SystemMessageLevel.Warning,
            };
            _messenger.Send(new NewEventMessage(sysErr));
            _messenger.Send(new ConnectionsUpdatedMessage());
        }
        catch (GoogleApiException apiEx)
        {
            string errorDetail = apiEx.Error?.Message ?? apiEx.Message;
            _logger.LogError(
                apiEx,
                "[{AccountId}] API Error sending message to LiveChatId {LiveChatId}. Status: {StatusCode}, Message: {ErrorDetail}",
                accountId,
                targetLiveChatId,
                apiEx.HttpStatusCode,
                errorDetail
            );
            SystemMessageEvent systemEvent = new()
            {
                Platform = "YouTube",
                OriginatingAccountId = accountId,
                Message = $"Error sending message: {errorDetail}",
                Level = SystemMessageLevel.Error,
            };
            _messenger.Send(new NewEventMessage(systemEvent));
            if (apiEx.HttpStatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                _logger.LogWarning("[{AccountId}] Access token likely expired or forbidden during send. Disconnecting instance.", accountId);
                await DisconnectAsync(accountId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "[{AccountId}] Unexpected error sending message to LiveChatId {LiveChatId}: {ErrorMessage}",
                accountId,
                targetLiveChatId,
                ex.Message
            );
            SystemMessageEvent systemEvent = new()
            {
                Platform = "YouTube",
                OriginatingAccountId = accountId,
                Message = $"Error sending message: {ex.Message}",
                Level = SystemMessageLevel.Error,
            };
            _messenger.Send(new NewEventMessage(systemEvent));
        }
    }

    // --- YTLiveChat Event Handlers ---
    private async void HandleYtChatReceived(string accountId, object? _, ChatReceivedEventArgs args)
    {
        if (!_activeClients.ContainsKey(accountId))
        {
            _logger.LogWarning("[{AccountId}] HandleYtChatReceived skipped: Wrapper not found.", accountId);
            return;
        }

        _logger.LogTrace(
            "[{AccountId}] Received ChatItem from YTLiveChat reader. ID: {ItemId}, Author: {AuthorName}",
            accountId,
            args.ChatItem.Id,
            args.ChatItem.Author.Name
        );
        BaseEvent? commonEvent = MapYtChatItemToCommonEvent(args.ChatItem, accountId);
        if (commonEvent != null)
        {
            if (commonEvent is ChatMessageEvent chatEvent && PluginService.IsChatCommand(chatEvent))
            {
                _logger.LogDebug("[{AccountId}] Checking YouTube message (ID: {YtItemId}) as potential command.", accountId, args.ChatItem.Id);
                bool eventSuppressedOrReplaced = await _pluginService.TryHandleChatCommandAsync(chatEvent);
                // Always route the *original* command event, even if suppressed or replaced.
                // The handling logic inside plugins determines the final action.
                await _pluginService.RouteEventToProcessorsAsync(chatEvent).ConfigureAwait(false);
                _messenger.Send(new NewEventMessage(chatEvent));

                if (eventSuppressedOrReplaced)
                {
                    _logger.LogDebug(
                        "[{AccountId}] YouTube chat message (ID: {YtItemId}) was processed by a command handler and potentially suppressed/replaced.",
                        accountId,
                        args.ChatItem.Id
                    );
                }
            }
            else
            {
                _logger.LogTrace("Routing non-command event {EventType} ({EventId})", commonEvent.GetType().Name, commonEvent.Id);
                await _pluginService.RouteEventToProcessorsAsync(commonEvent).ConfigureAwait(false);
                _messenger.Send(new NewEventMessage(commonEvent));
            }
        }
        else
        {
            _logger.LogWarning("[{AccountId}] Failed to map YTLiveChat ChatItem (ID: {ItemId}) to a common event.", accountId, args.ChatItem.Id);
        }
    }

    private void HandleYtChatError(string accountId, object? _, ErrorOccurredEventArgs args)
    {
        if (!_activeClients.TryGetValue(accountId, out YouTubeClientWrapper? wrapper))
        {
            _logger.LogWarning("[{AccountId}] HandleYtChatError skipped: Wrapper not found.", accountId);
            return;
        }

        _logger.LogError(args.GetException(), "[{AccountId}] Error received from YTLiveChat reader.", accountId);
        UpdateWrapperStatus(wrapper, ConnectionStatus.Error, $"Chat Reader Error: {args.GetException().Message}");
        SystemMessageEvent sysErr = new()
        {
            Platform = "YouTube",
            OriginatingAccountId = accountId,
            Message = $"YouTube chat reader error: {args.GetException().Message}",
            Level = SystemMessageLevel.Error,
        };
        _messenger.Send(new NewEventMessage(sysErr));
        _messenger.Send(new ConnectionsUpdatedMessage());
    }

    private void HandleYtChatStopped(string accountId, object? _, ChatStoppedEventArgs args)
    {
        if (!_activeClients.TryGetValue(accountId, out YouTubeClientWrapper? wrapper))
        {
            _logger.LogWarning("[{AccountId}] HandleYtChatStopped skipped: Wrapper not found.", accountId);
            return;
        }

        _logger.LogWarning("[{AccountId}] YTLiveChat reader stopped unexpectedly. Reason: {Reason}", accountId, args.Reason ?? "Unknown");
        if (wrapper.Status is ConnectionStatus.Connected or ConnectionStatus.Connecting or ConnectionStatus.Limited)
        {
            UpdateWrapperStatus(wrapper, ConnectionStatus.Error, $"Chat Reader Stopped: {args.Reason ?? "Unknown"}");
            SystemMessageEvent sysErr = new()
            {
                Platform = "YouTube",
                OriginatingAccountId = accountId,
                Message = $"YouTube chat reader stopped: {args.Reason}",
                Level = SystemMessageLevel.Warning,
            };
            _messenger.Send(new NewEventMessage(sysErr));
            _messenger.Send(new ConnectionsUpdatedMessage());
        }

        wrapper.ActiveVideoId = null;
        wrapper.AssociatedLiveChatId = null;
        wrapper.YtChatReaderClient = null;
    }

    // --- Mapping Logic ---
    private BaseEvent? MapYtChatItemToCommonEvent(YTLiveChatChatItem ytChatItem, string originatingAccountId)
    {
        _logger.LogTrace("Mapping YTLiveChat Item ID {YtItemId} for Account {AccountId}", ytChatItem.Id, originatingAccountId);
        string authorId = ytChatItem.Author?.ChannelId ?? "UnknownChannelID";
        string authorName = ytChatItem.Author?.Name ?? "Unknown User";
        DateTime utcTimestamp = ytChatItem.Timestamp.UtcDateTime;
        string? profileImageUrl = ytChatItem.Author?.Thumbnail?.Url;
        List<BadgeInfo> badgeInfoList = MapYtAuthorToBadges(ytChatItem);
        bool isOwner = ytChatItem.IsOwner;
        string? usernameColor = CalculateYouTubeUsernameColor(badgeInfoList);
        try
        {
            if (ytChatItem.MembershipDetails != null)
            {
                return MapMembershipEvent(
                    ytChatItem,
                    originatingAccountId,
                    utcTimestamp,
                    authorId,
                    authorName,
                    profileImageUrl,
                    badgeInfoList,
                    isOwner,
                    usernameColor
                );
            }
            else if (ytChatItem.Superchat != null)
            {
                return MapDonationEvent(
                    ytChatItem,
                    originatingAccountId,
                    utcTimestamp,
                    authorId,
                    authorName,
                    profileImageUrl,
                    badgeInfoList,
                    isOwner,
                    usernameColor
                );
            }
            else
            {
                string rawMessage = string.Join("", ytChatItem.Message?.Select(p => p.ToString()) ?? []);
                List<MessageSegment> parsedMessage = MapYtMessageParts(ytChatItem.Message);
                return new ChatMessageEvent
                {
                    Id = ytChatItem.Id,
                    Platform = "YouTube",
                    Timestamp = utcTimestamp,
                    OriginatingAccountId = originatingAccountId,
                    UserId = authorId,
                    Username = authorName,
                    RawMessage = rawMessage,
                    ParsedMessage = parsedMessage,
                    UsernameColor = usernameColor,
                    Badges = badgeInfoList,
                    ProfileImageUrl = profileImageUrl,
                    IsOwner = isOwner,
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "[{AccountId}] Failed to map YTLiveChat item (ID: {YtItemId}) to common event.",
                originatingAccountId,
                ytChatItem.Id
            );
            return null;
        }
    }

    private static MembershipEvent MapMembershipEvent(
        YTLiveChatChatItem ytChatItem,
        string originatingAccountId,
        DateTime timestamp,
        string authorId,
        string authorName,
        string? profileImageUrl,
        List<BadgeInfo> badges,
        bool isOwner,
        string? usernameColor
    )
    {
        YTLiveChatMembershipDetails details = ytChatItem.MembershipDetails!;
        MembershipEventType type = details.EventType switch
        {
            YTLiveChat.Contracts.Models.MembershipEventType.New => MembershipEventType.New,
            YTLiveChat.Contracts.Models.MembershipEventType.Milestone => MembershipEventType.Milestone,
            YTLiveChat.Contracts.Models.MembershipEventType.GiftPurchase => MembershipEventType.GiftPurchase,
            YTLiveChat.Contracts.Models.MembershipEventType.GiftRedemption => MembershipEventType.GiftRedemption,
            _ => MembershipEventType.Unknown,
        };
        List<MessageSegment> parsedUserComment = MapYtMessageParts(ytChatItem.Message);
        return new MembershipEvent
        {
            Id = ytChatItem.Id,
            Platform = "YouTube",
            Timestamp = timestamp,
            OriginatingAccountId = originatingAccountId,
            UserId = authorId,
            Username = authorName,
            MembershipType = type,
            LevelName = details.LevelName,
            MilestoneMonths = details.MilestoneMonths,
            GifterUsername = details.GifterUsername,
            GiftCount = details.GiftCount,
            HeaderText = details.HeaderPrimaryText,
            ParsedMessage = parsedUserComment,
            Badges = badges,
            UsernameColor = usernameColor,
            ProfileImageUrl = profileImageUrl,
            IsOwner = isOwner,
        };
    }

    private DonationEvent MapDonationEvent(
        YTLiveChatChatItem ytChatItem,
        string originatingAccountId,
        DateTime timestamp,
        string authorId,
        string authorName,
        string? profileImageUrl,
        List<BadgeInfo> badges,
        bool isOwner,
        string? usernameColor
    )
    {
        YTLiveChatSuperchat superchat = ytChatItem.Superchat!;
        DonationType donationType = (superchat.Sticker != null) ? DonationType.SuperSticker : DonationType.SuperChat;
        string rawUserComment = string.Join("", ytChatItem.Message?.Select(p => p.ToString()) ?? []);
        List<MessageSegment> parsedUserComment = MapYtMessageParts(ytChatItem.Message);
        decimal amount = 0M;
        string cleanAmount =
            superchat.AmountString?.Replace(",", "").Replace("€", "").Replace("$", "").Replace("£", "").Replace("¥", "").Trim() ?? "0";
        if (decimal.TryParse(cleanAmount, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal parsedAmount))
        {
            amount = parsedAmount;
        }
        else
        {
            _logger.LogWarning("Could not parse SuperChat amount '{Amount}' after cleaning.", superchat.AmountString);
        }

        string currency = ParseCurrencyFromAmountString(superchat.AmountString);
        return new DonationEvent
        {
            Id = ytChatItem.Id,
            Platform = "YouTube",
            Timestamp = timestamp,
            OriginatingAccountId = originatingAccountId,
            DonationId = ytChatItem.Id,
            UserId = authorId,
            Username = authorName,
            Amount = amount,
            Currency = currency,
            RawMessage = rawUserComment,
            ParsedMessage = parsedUserComment,
            Type = donationType,
            Badges = badges,
            UsernameColor = usernameColor,
            ProfileImageUrl = profileImageUrl,
            IsOwner = isOwner,
            BodyBackgroundColor = FormatColor(superchat.BodyBackgroundColor),
            HeaderBackgroundColor = FormatColor(superchat.HeaderBackgroundColor),
            HeaderTextColor = FormatColor(superchat.HeaderTextColor),
            BodyTextColor = FormatColor(superchat.BodyTextColor),
            AuthorNameTextColor = FormatColor(superchat.AuthorNameTextColor),
            StickerImageUrl = superchat.Sticker?.Url,
            StickerAltText = superchat.Sticker?.Alt,
        };
    }

    private static List<BadgeInfo> MapYtAuthorToBadges(YTLiveChatChatItem chatItem)
    {
        var badges = new List<BadgeInfo>();
        if (chatItem == null)
            return badges;
        if (chatItem.IsOwner)
            badges.Add(new BadgeInfo("youtube/owner/1", null));
        if (chatItem.IsModerator)
            badges.Add(new BadgeInfo("youtube/moderator/1", null));
        if (chatItem.IsMembership)
            badges.Add(new BadgeInfo("youtube/member/1", chatItem.Author.Badge?.Thumbnail?.Url));
        if (chatItem.IsVerified)
            badges.Add(new BadgeInfo("youtube/verified/1", null));
        return badges;
    }

    private static List<MessageSegment> MapYtMessageParts(IEnumerable<YTLiveChatMessagePart>? parts)
    {
        if (parts == null)
            return [];
        List<MessageSegment> segments = [];
        foreach (YTLiveChatMessagePart part in parts)
        {
            if (part is YTLiveChat.Contracts.Models.TextPart tp)
            {
                if (!string.IsNullOrEmpty(tp.Text))
                {
                    segments.Add(new TextSegment { Text = WebUtility.HtmlDecode(tp.Text) });
                }
            }
            else if (part is YTLiveChat.Contracts.Models.EmojiPart ep)
            {
                segments.Add(
                    new EmoteSegment
                    {
                        Id = $"youtube_{ep.EmojiText}",
                        Name = ep.Alt ?? ep.EmojiText,
                        ImageUrl = ep.Url,
                        Platform = "YouTube",
                    }
                );
            }
            else if (part is YTLiveChat.Contracts.Models.ImagePart ip)
            {
                segments.Add(
                    new EmoteSegment
                    {
                        Id = $"youtube_img_{ip.Alt ?? "image"}",
                        Name = ip.Alt ?? "[image]",
                        ImageUrl = ip.Url,
                        Platform = "YouTube",
                    }
                );
            }
        }

        return segments;
    }

    private string ParseCurrencyFromAmountString(string? amountString)
    {
        if (string.IsNullOrWhiteSpace(amountString))
            return "USD";
        amountString = amountString.Trim();
        if (amountString.StartsWith('€'))
            return "EUR";
        if (amountString.StartsWith('$'))
            return "USD";
        if (amountString.StartsWith('£'))
            return "GBP";
        if (amountString.StartsWith('¥'))
            return "JPY";
        if (amountString.EndsWith(" USD"))
            return "USD";
        if (amountString.EndsWith(" EUR"))
            return "EUR";
        if (amountString.EndsWith(" GBP"))
            return "GBP";
        if (amountString.EndsWith(" JPY"))
            return "JPY";
        if (amountString.EndsWith(" CAD"))
            return "CAD";
        if (amountString.EndsWith(" AUD"))
            return "AUD";
        _logger.LogWarning("Could not determine currency from amount string: '{AmountString}'. Defaulting to USD.", amountString);
        return "USD";
    }

    // --- Helpers ---
    private void UpdateWrapperStatus(YouTubeClientWrapper wrapper, ConnectionStatus status, string? message = null)
    {
        bool changed = wrapper.Status != status || wrapper.StatusMessage != (message ?? wrapper.StatusMessage);
        wrapper.Status = status;
        wrapper.StatusMessage = message ?? wrapper.StatusMessage;
        if (changed)
        {
            _logger.LogInformation(
                "[{AccountId}] Status updated: {ConnectionStatus} | Msg: {StatusMessage}",
                wrapper.AccountId,
                status,
                wrapper.StatusMessage
            );
            UpdateAccountModelStatus(wrapper.AccountId, status, wrapper.StatusMessage);
        }
        else
        {
            _logger.LogTrace(
                "[{AccountId}] Status update skipped (no change): {ConnectionStatus} | Msg: {StatusMessage}",
                wrapper.AccountId,
                status,
                wrapper.StatusMessage
            );
        }
    }

    private static bool IsQuotaError(GoogleApiException ex) =>
        ex.Error?.Errors?.Any(e => e.Reason == "quotaExceeded" || e.Reason == "rateLimitExceeded" || e.Domain == "usageLimits") ?? false;

    private void UpdateAccountModelStatus(string accountId, ConnectionStatus status, string? message)
    {
        YouTubeAccount? accountModel = _settingsService.CurrentSettings?.Connections?.YouTubeAccounts?.FirstOrDefault(a => a.ChannelId == accountId);
        if (accountModel != null)
        {
            bool changed = false;
            if (accountModel.Status != status)
            {
                accountModel.Status = status;
                changed = true;
            }

            if (accountModel.StatusMessage != message)
            {
                accountModel.StatusMessage = message;
                changed = true;
            }

            if (changed)
            {
                _logger.LogTrace(
                    "Updated UI Model status for YouTube {ChannelName}: {Status} ('{Message}')",
                    accountModel.ChannelName,
                    status,
                    message
                );
            }
        }
        else
        {
            _logger.LogWarning("Could not find YouTubeAccount model for ID {AccountId} to update UI status.", accountId);
        }
    }

    private static string? CalculateYouTubeUsernameColor(List<BadgeInfo> badges)
    {
        if (badges == null || badges.Count == 0)
            return null;
        string? highestPriorityColor = null;
        int highestPriority = -1;
        foreach (BadgeInfo badge in badges)
        {
            string[] parts = badge.Identifier.Split('/');
            if (parts.Length >= 2 && parts[0].Equals("youtube", StringComparison.OrdinalIgnoreCase))
            {
                string badgeName = parts[1];
                if (s_youTubeBadgeColorPriority.TryGetValue(badgeName, out (int Priority, string Color) priorityInfo))
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

    private static string? FormatColor(string? color)
    {
        if (string.IsNullOrWhiteSpace(color))
            return null;
        if (color.StartsWith('#'))
            return color;
        if (color.Length == 6)
            return '#' + color;

        // Handle YouTube's 8-digit hex ARGB color format
        if (color.Length == 8)
        {
            // Convert ARGB to #AARRGGBB
            return '#' + color;
        }

        return null;
    }

    // --- Public Getters ---
    public ConnectionStatus GetStatus(string accountId) =>
        _activeClients.TryGetValue(accountId, out YouTubeClientWrapper? wrapper) ? wrapper.Status : ConnectionStatus.Disconnected;

    public string? GetStatusMessage(string accountId) =>
        _activeClients.TryGetValue(accountId, out YouTubeClientWrapper? wrapper) ? wrapper.StatusMessage : "Account not connected";

    public string? GetActiveVideoId(string accountId) =>
        _activeClients.TryGetValue(accountId, out YouTubeClientWrapper? wrapper) ? wrapper.ActiveVideoId : null;

    public string? GetAssociatedLiveChatId(string accountId) =>
        _activeClients.TryGetValue(accountId, out YouTubeClientWrapper? wrapper) ? wrapper.AssociatedLiveChatId : null;

    // --- Moderation Methods IMPLEMENTATIONS ---
    public async Task DeleteMessageAsync(string moderatorAccountId, string messageId)
    {
        const string actionName = "DeleteMessage";
        if (!_activeClients.TryGetValue(moderatorAccountId, out YouTubeClientWrapper? wrapper))
        {
            _logger.LogWarning("[{AccountId}] Cannot {Action}, client wrapper not found.", moderatorAccountId, actionName);
            return;
        }

        if (wrapper.OfficialApiService == null)
        {
            _logger.LogError("[{AccountId}] Cannot {Action}, official API service instance is null.", moderatorAccountId, actionName);
            return;
        }

        if (wrapper.Status == ConnectionStatus.Limited)
        {
            _logger.LogWarning("[{AccountId}] Cannot {Action}: Client is in Limited (Read-Only) state.", moderatorAccountId, actionName);
            return;
        }

        _logger.LogInformation("[{AccountId}] Attempting {Action} for Message ID: {MessageId}", moderatorAccountId, actionName, messageId);
        try
        {
            LiveChatMessagesResource.DeleteRequest request = wrapper.OfficialApiService.LiveChatMessages.Delete(messageId);
            string emptyResponse = await request.ExecuteAsync();
            _logger.LogInformation("[{AccountId}] {Action} successful for Message ID: {MessageId}.", moderatorAccountId, actionName, messageId);
        }
        catch (GoogleApiException apiEx) when (IsQuotaError(apiEx))
        {
            _logger.LogWarning(apiEx, "[{AccountId}] Quota error during {Action}. Entering Limited state.", moderatorAccountId, actionName);
            UpdateWrapperStatus(wrapper, ConnectionStatus.Limited, "Read-Only (API Quota Reached)");
            _messenger.Send(new ConnectionsUpdatedMessage());
        }
        catch (GoogleApiException apiEx)
        {
            string errorDetail = apiEx.Error?.Message ?? apiEx.Message;
            _logger.LogError(
                apiEx,
                "[{AccountId}] API Error during {Action} for Message ID {MessageId}. Status: {StatusCode}, Message: {ErrorDetail}",
                moderatorAccountId,
                actionName,
                messageId,
                apiEx.HttpStatusCode,
                errorDetail
            );
            if (apiEx.HttpStatusCode == HttpStatusCode.Forbidden)
                _logger.LogWarning("--> Moderator likely lacks permission to delete this message.");
            else if (apiEx.HttpStatusCode == HttpStatusCode.NotFound)
                _logger.LogWarning("--> Message ID {MessageId} not found or already deleted.", messageId);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "[{AccountId}] Unexpected error during {Action} for Message ID {MessageId}",
                moderatorAccountId,
                actionName,
                messageId
            );
        }
    }

    public async Task TimeoutUserAsync(string moderatorAccountId, string liveChatId, string userIdToTimeout, uint durationSeconds)
    {
        const string actionName = "TimeoutUser";
        if (!_activeClients.TryGetValue(moderatorAccountId, out YouTubeClientWrapper? wrapper))
        {
            _logger.LogWarning("[{AccountId}] Cannot {Action}, client wrapper not found.", moderatorAccountId, actionName);
            return;
        }

        if (wrapper.OfficialApiService == null)
        {
            _logger.LogError("[{AccountId}] Cannot {Action}, official API service instance is null.", moderatorAccountId, actionName);
            return;
        }

        if (wrapper.Status == ConnectionStatus.Limited)
        {
            _logger.LogWarning("[{AccountId}] Cannot {Action}: Client is in Limited (Read-Only) state.", moderatorAccountId, actionName);
            return;
        }

        _logger.LogInformation(
            "[{AccountId}] Attempting {Action} for User ID: {TargetUserId} in Chat ID: {LiveChatId} for {Duration}s",
            moderatorAccountId,
            actionName,
            userIdToTimeout,
            liveChatId,
            durationSeconds
        );

        var liveChatBan = new LiveChatBan
        {
            Snippet = new LiveChatBanSnippet
            {
                LiveChatId = liveChatId,
                Type = "temporary", // Indicate a timeout
                BannedUserDetails = new ChannelProfileDetails { ChannelId = userIdToTimeout },
                BanDurationSeconds = durationSeconds,
            },
        };

        try
        {
            LiveChatBansResource.InsertRequest request = wrapper.OfficialApiService.LiveChatBans.Insert(liveChatBan, "snippet");
            LiveChatBan responseBan = await request.ExecuteAsync();
            _logger.LogInformation(
                "[{AccountId}] {Action} successful for User ID: {TargetUserId}. Ban ID: {BanId}",
                moderatorAccountId,
                actionName,
                userIdToTimeout,
                responseBan.Id
            );
        }
        catch (GoogleApiException apiEx) when (IsQuotaError(apiEx))
        {
            _logger.LogWarning(apiEx, "[{AccountId}] Quota error during {Action}. Entering Limited state.", moderatorAccountId, actionName);
            UpdateWrapperStatus(wrapper, ConnectionStatus.Limited, "Read-Only (API Quota Reached)");
            _messenger.Send(new ConnectionsUpdatedMessage());
        }
        catch (GoogleApiException apiEx)
        {
            string errorDetail = apiEx.Error?.Message ?? apiEx.Message;
            _logger.LogError(
                apiEx,
                "[{AccountId}] API Error during {Action} for User ID {TargetUserId}. Status: {StatusCode}, Message: {ErrorDetail}",
                moderatorAccountId,
                actionName,
                userIdToTimeout,
                apiEx.HttpStatusCode,
                errorDetail
            );
            if (apiEx.HttpStatusCode == HttpStatusCode.Forbidden)
                _logger.LogWarning("--> Moderator likely lacks permission to timeout this user.");
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "[{AccountId}] Unexpected error during {Action} for User ID {TargetUserId}",
                moderatorAccountId,
                actionName,
                userIdToTimeout
            );
        }
    }

    public async Task BanUserAsync(string moderatorAccountId, string liveChatId, string userIdToBan)
    {
        const string actionName = "BanUser";
        if (!_activeClients.TryGetValue(moderatorAccountId, out YouTubeClientWrapper? wrapper))
        {
            _logger.LogWarning("[{AccountId}] Cannot {Action}, client wrapper not found.", moderatorAccountId, actionName);
            return;
        }

        if (wrapper.OfficialApiService == null)
        {
            _logger.LogError("[{AccountId}] Cannot {Action}, official API service instance is null.", moderatorAccountId, actionName);
            return;
        }

        if (wrapper.Status == ConnectionStatus.Limited)
        {
            _logger.LogWarning("[{AccountId}] Cannot {Action}: Client is in Limited (Read-Only) state.", moderatorAccountId, actionName);
            return;
        }

        _logger.LogInformation(
            "[{AccountId}] Attempting {Action} for User ID: {TargetUserId} in Chat ID: {LiveChatId}",
            moderatorAccountId,
            actionName,
            userIdToBan,
            liveChatId
        );

        var liveChatBan = new LiveChatBan
        {
            Snippet = new LiveChatBanSnippet
            {
                LiveChatId = liveChatId,
                Type = "permanent", // Indicate a permanent ban
                BannedUserDetails = new ChannelProfileDetails { ChannelId = userIdToBan },
            },
        };

        try
        {
            LiveChatBansResource.InsertRequest request = wrapper.OfficialApiService.LiveChatBans.Insert(liveChatBan, "snippet");
            LiveChatBan responseBan = await request.ExecuteAsync();
            _logger.LogInformation(
                "[{AccountId}] {Action} successful for User ID: {TargetUserId}. Ban ID: {BanId}",
                moderatorAccountId,
                actionName,
                userIdToBan,
                responseBan.Id
            );
        }
        catch (GoogleApiException apiEx) when (IsQuotaError(apiEx))
        {
            _logger.LogWarning(apiEx, "[{AccountId}] Quota error during {Action}. Entering Limited state.", moderatorAccountId, actionName);
            UpdateWrapperStatus(wrapper, ConnectionStatus.Limited, "Read-Only (API Quota Reached)");
            _messenger.Send(new ConnectionsUpdatedMessage());
        }
        catch (GoogleApiException apiEx)
        {
            string errorDetail = apiEx.Error?.Message ?? apiEx.Message;
            _logger.LogError(
                apiEx,
                "[{AccountId}] API Error during {Action} for User ID {TargetUserId}. Status: {StatusCode}, Message: {ErrorDetail}",
                moderatorAccountId,
                actionName,
                userIdToBan,
                apiEx.HttpStatusCode,
                errorDetail
            );
            if (apiEx.HttpStatusCode == HttpStatusCode.Forbidden)
                _logger.LogWarning("--> Moderator likely lacks permission to ban this user.");
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "[{AccountId}] Unexpected error during {Action} for User ID {TargetUserId}",
                moderatorAccountId,
                actionName,
                userIdToBan
            );
        }
    }

    // --- Poll Methods ---

    /// <inheritdoc/>
    public async Task<string?> CreatePollAsync(string moderatorAccountId, string liveChatId, string question, List<string> options)
    {
        const string actionName = "CreatePoll";
        // Parameter Validation
        if (string.IsNullOrWhiteSpace(question) || question.Length > 100)
        {
            _logger.LogError("[{AccountId}] Cannot {Action}: Invalid question (empty or >100 chars).", moderatorAccountId, actionName);
            return null;
        }

        if (options == null || options.Count < 2 || options.Count > 5)
        {
            _logger.LogError(
                "[{AccountId}] Cannot {Action}: Invalid options count ({Count}). Must be 2-5.",
                moderatorAccountId,
                actionName,
                options?.Count ?? 0
            );
            return null;
        }

        if (options.Any(opt => string.IsNullOrWhiteSpace(opt) || opt.Length > 30))
        {
            _logger.LogError("[{AccountId}] Cannot {Action}: One or more options are empty or >30 chars.", moderatorAccountId, actionName);
            return null;
        }

        // Client Checks
        if (!_activeClients.TryGetValue(moderatorAccountId, out YouTubeClientWrapper? wrapper))
        {
            _logger.LogWarning("[{AccountId}] Cannot {Action}, client wrapper not found.", moderatorAccountId, actionName);
            return null;
        }

        if (wrapper.OfficialApiService == null)
        {
            _logger.LogError("[{AccountId}] Cannot {Action}, official API service instance is null.", moderatorAccountId, actionName);
            return null;
        }

        if (wrapper.Status == ConnectionStatus.Limited)
        {
            _logger.LogWarning("[{AccountId}] Cannot {Action}: Client is in Limited (Read-Only) state.", moderatorAccountId, actionName);
            return null;
        }

        if (wrapper.Status != ConnectionStatus.Connected)
        {
            _logger.LogWarning(
                "[{AccountId}] Cannot {Action}: Client not connected (Status: {Status}).",
                moderatorAccountId,
                actionName,
                wrapper.Status
            );
            return null;
        }

        _logger.LogInformation(
            "[{AccountId}] Attempting {Action} in Chat ID: {LiveChatId} with Question: '{Question}'",
            moderatorAccountId,
            actionName,
            liveChatId,
            question
        );

        var pollMessage = new LiveChatMessage
        {
            Snippet = new LiveChatMessageSnippet
            {
                LiveChatId = liveChatId,
                Type = "newPollEvent",
                PollDetails = new LiveChatPollDetails
                {
                    Metadata = new LiveChatPollDetailsPollMetadata
                    {
                        QuestionText = question,
                        Options = [.. options.Select(opt => new LiveChatPollDetailsPollMetadataPollOption { OptionText = opt })],
                        // MetadataType = "creator" // Optional, might be useful?
                    },
                },
            },
        };

        try
        {
            LiveChatMessagesResource.InsertRequest request = wrapper.OfficialApiService.LiveChatMessages.Insert(pollMessage, "snippet");
            LiveChatMessage responseMessage = await request.ExecuteAsync();
            _logger.LogInformation(
                "[{AccountId}] {Action} successful. Poll Message ID: {PollMessageId}",
                moderatorAccountId,
                actionName,
                responseMessage.Id
            );
            // TODO: Store poll state locally if needed for later management (e.g., ending it)
            return responseMessage.Id; // Return the ID of the created poll message
        }
        catch (GoogleApiException apiEx) when (IsQuotaError(apiEx))
        {
            _logger.LogWarning(apiEx, "[{AccountId}] Quota error during {Action}. Entering Limited state.", moderatorAccountId, actionName);
            UpdateWrapperStatus(wrapper, ConnectionStatus.Limited, "Read-Only (API Quota Reached)");
            _messenger.Send(new ConnectionsUpdatedMessage());
            return null;
        }
        catch (GoogleApiException apiEx)
        {
            string errorDetail = apiEx.Error?.Message ?? apiEx.Message;
            _logger.LogError(
                apiEx,
                "[{AccountId}] API Error during {Action}. Status: {StatusCode}, Message: {ErrorDetail}",
                moderatorAccountId,
                actionName,
                apiEx.HttpStatusCode,
                errorDetail
            );
            if (apiEx.HttpStatusCode == HttpStatusCode.Forbidden)
                _logger.LogWarning("--> Moderator likely lacks permission to create polls.");
            if (apiEx.HttpStatusCode == HttpStatusCode.BadRequest)
                _logger.LogWarning("--> Bad Request during poll creation. Check question/option lengths or format.");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{AccountId}] Unexpected error during {Action}", moderatorAccountId, actionName);
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> EndPollAsync(string moderatorAccountId, string pollMessageId, string status = "ended")
    {
        const string actionName = "EndPoll";
        if (!_activeClients.TryGetValue(moderatorAccountId, out YouTubeClientWrapper? wrapper))
        {
            _logger.LogWarning("[{AccountId}] Cannot {Action}, client wrapper not found.", moderatorAccountId, actionName);
            return false;
        }

        if (wrapper.OfficialApiService == null)
        {
            _logger.LogError("[{AccountId}] Cannot {Action}, official API service instance is null.", moderatorAccountId, actionName);
            return false;
        }

        if (wrapper.Status == ConnectionStatus.Limited)
        {
            _logger.LogWarning("[{AccountId}] Cannot {Action}: Client is in Limited (Read-Only) state.", moderatorAccountId, actionName);
            return false;
        }

        if (wrapper.Status != ConnectionStatus.Connected)
        {
            _logger.LogWarning(
                "[{AccountId}] Cannot {Action}: Client not connected (Status: {Status}).",
                moderatorAccountId,
                actionName,
                wrapper.Status
            );
            return false;
        }

        _logger.LogInformation(
            "[{AccountId}] Attempting {Action} for Poll Message ID: {PollMessageId} to Status: '{Status}'",
            moderatorAccountId,
            actionName,
            pollMessageId,
            status
        );

        try
        {
            LiveChatMessagesResource.TransitionRequest request = wrapper.OfficialApiService.LiveChatMessages.Transition();
            request.Id = pollMessageId; // Set the target message ID
            request.Status = LiveChatMessagesResource.TransitionRequest.StatusEnum.Closed; // Set the target status
            LiveChatMessage responseMessage = await request.ExecuteAsync();
            _logger.LogInformation(
                "[{AccountId}] {Action} successful for Poll Message ID: {PollMessageId}. New Status: {NewStatus}",
                moderatorAccountId,
                actionName,
                pollMessageId,
                responseMessage.Snippet?.PollDetails.Status ?? "Unknown"
            );
            // TODO: Update local poll state if stored
            return true;
        }
        catch (GoogleApiException apiEx) when (IsQuotaError(apiEx))
        {
            _logger.LogWarning(apiEx, "[{AccountId}] Quota error during {Action}. Entering Limited state.", moderatorAccountId, actionName);
            UpdateWrapperStatus(wrapper, ConnectionStatus.Limited, "Read-Only (API Quota Reached)");
            _messenger.Send(new ConnectionsUpdatedMessage());
            return false;
        }
        catch (GoogleApiException apiEx)
        {
            string errorDetail = apiEx.Error?.Message ?? apiEx.Message;
            _logger.LogError(
                apiEx,
                "[{AccountId}] API Error during {Action} for Poll ID {PollMessageId}. Status: {StatusCode}, Message: {ErrorDetail}",
                moderatorAccountId,
                actionName,
                pollMessageId,
                apiEx.HttpStatusCode,
                errorDetail
            );
            if (apiEx.HttpStatusCode == HttpStatusCode.Forbidden)
                _logger.LogWarning("--> Moderator likely lacks permission to transition this poll.");
            if (apiEx.HttpStatusCode == HttpStatusCode.BadRequest)
                _logger.LogWarning("--> Bad Request during poll transition. Check if Poll ID is correct and transition is valid.");
            if (apiEx.HttpStatusCode == HttpStatusCode.NotFound)
                _logger.LogWarning("--> Poll Message ID {PollMessageId} not found.", pollMessageId);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "[{AccountId}] Unexpected error during {Action} for Poll ID {PollMessageId}",
                moderatorAccountId,
                actionName,
                pollMessageId
            );
            return false;
        }
    }

    // --- Dispose Method ---
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_isDisposed)
            return;
        if (disposing)
        {
            _logger.LogInformation("Disposing YouTubeService...");
            var accountIds = _activeClients.Keys.ToList();
            foreach (string id in accountIds)
            {
                Task.Run(() => DisconnectAsync(id)).Wait(TimeSpan.FromSeconds(3));
            }

            _activeClients.Clear();
            _logger.LogInformation("YouTubeService disposed.");
        }

        _isDisposed = true;
    }
}
