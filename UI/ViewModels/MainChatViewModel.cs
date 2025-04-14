using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using StreamWeaver.Core.Messaging;
using StreamWeaver.Core.Models.Events;
using StreamWeaver.Core.Models.Settings;
using StreamWeaver.Core.Services;
using StreamWeaver.Core.Services.Platforms;
using StreamWeaver.Core.Services.Settings;

namespace StreamWeaver.UI.ViewModels;

/// <summary>
/// ViewModel for the main chat view, managing the display of incoming events,
/// handling message sending, and updating the list of available sending accounts.
/// </summary>
public partial class MainChatViewModel : ObservableObject, IRecipient<NewEventMessage>, IRecipient<ConnectionsUpdatedMessage>, IDisposable
{
    private const int MAX_MESSAGES = 200;
    private readonly ILogger<MainChatViewModel> _logger;
    private readonly IMessenger _messenger;
    private readonly UnifiedEventService _unifiedEventService;
    private readonly ISettingsService _settingsService;
    private readonly ITwitchClient _twitchClient;
    private readonly IYouTubeClient _youTubeClient;
    private readonly DispatcherQueue _dispatcherQueue;
    private bool _isDisposed = false;

    [ObservableProperty]
    public partial ObservableCollection<BaseEvent> Events { get; set; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendMessageCommand))]
    public partial string? MessageToSend { get; set; }

    [ObservableProperty]
    public partial ObservableCollection<SendTarget> SendTargets { get; set; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendMessageCommand))]
    public partial SendTarget? SelectedSendTarget { get; set; }

    public MainChatViewModel(
        ILogger<MainChatViewModel> logger,
        IMessenger messenger,
        UnifiedEventService unifiedEventService,
        ISettingsService settingsService,
        ITwitchClient twitchClient,
        IYouTubeClient youTubeClient
    )
    {
        _logger = logger;
        _messenger = messenger;
        _unifiedEventService = unifiedEventService;
        _settingsService = settingsService;
        _twitchClient = twitchClient;
        _youTubeClient = youTubeClient;
        _dispatcherQueue =
            DispatcherQueue.GetForCurrentThread()
            ?? throw new InvalidOperationException($"{nameof(MainChatViewModel)} must be initialized on the UI thread.");

        _messenger.Register<NewEventMessage>(this);
        _messenger.Register<ConnectionsUpdatedMessage>(this);
        UpdateSendTargets();
        _logger.LogInformation("Initialized and registered for messages.");
    }

    private void UpdateSendTargets() =>
        _dispatcherQueue.TryEnqueue(() =>
        {
            if (_isDisposed)
                return;
            _logger.LogDebug("Updating send targets...");
            AppSettings currentSettings = _settingsService.CurrentSettings;
            List<SendTarget> newTargets = [];
            int connectedTargetsCount = 0;
            if (currentSettings.Connections?.TwitchAccounts != null)
            {
                foreach (TwitchAccount twitchAcc in currentSettings.Connections.TwitchAccounts)
                {
                    if (!string.IsNullOrEmpty(twitchAcc.UserId) && IsAccountConnected(twitchAcc.UserId, "Twitch"))
                    {
                        newTargets.Add(
                            new SendTarget
                            {
                                DisplayName = $"Twitch: {twitchAcc.Username}",
                                Platform = "Twitch",
                                AccountId = twitchAcc.UserId,
                                AccountChannelName = twitchAcc.Username,
                            }
                        );
                        connectedTargetsCount++;
                        _logger.LogTrace("Added connected Twitch target: {Username} ({UserId})", twitchAcc.Username, twitchAcc.UserId);
                    }
                }
            }
            if (currentSettings.Connections?.YouTubeAccounts != null)
            {
                foreach (YouTubeAccount ytAcc in currentSettings.Connections.YouTubeAccounts)
                {
                    if (!string.IsNullOrEmpty(ytAcc.ChannelId) && IsAccountConnected(ytAcc.ChannelId, "YouTube"))
                    {
                        newTargets.Add(
                            new SendTarget
                            {
                                DisplayName = $"YouTube: {ytAcc.ChannelName}",
                                Platform = "YouTube",
                                AccountId = ytAcc.ChannelId,
                                AccountChannelName = ytAcc.ChannelName,
                            }
                        );
                        connectedTargetsCount++;
                        _logger.LogTrace("Added connected YouTube target: {ChannelName} ({ChannelId})", ytAcc.ChannelName, ytAcc.ChannelId);
                    }
                }
            }
            if (connectedTargetsCount > 1)
            {
                _logger.LogTrace("Adding 'All Connected' target.");
                newTargets.Insert(
                    0,
                    new SendTarget
                    {
                        DisplayName = "All Connected",
                        Platform = "All",
                        AccountId = null,
                        AccountChannelName = null,
                    }
                );
            }
            SendTarget? previouslySelected = SelectedSendTarget;
            var targetsToRemove = SendTargets
                .Where(existing => !newTargets.Any(nt => nt.Platform == existing.Platform && nt.AccountId == existing.AccountId))
                .ToList();
            foreach (SendTarget? target in targetsToRemove)
                SendTargets.Remove(target);
            var targetsToAdd = newTargets
                .Where(nt => !SendTargets.Any(existing => nt.Platform == existing.Platform && nt.AccountId == existing.AccountId))
                .ToList();
            foreach (SendTarget? target in targetsToAdd)
                SendTargets.Add(target);
            SendTarget? newSelection = null;
            if (previouslySelected != null)
            {
                newSelection = SendTargets.FirstOrDefault(t =>
                    t.Platform == previouslySelected.Platform && t.AccountId == previouslySelected.AccountId
                );
            }
            SelectedSendTarget = newSelection ?? SendTargets.FirstOrDefault();
            _logger.LogInformation(
                "Send targets updated. Count: {Count}. Selected: {SelectedTarget}",
                SendTargets.Count,
                SelectedSendTarget?.DisplayName ?? "None"
            );
            SendMessageCommand.NotifyCanExecuteChanged();
        });

    public void Receive(NewEventMessage message)
    {
        if (_isDisposed)
            return;
        _dispatcherQueue.TryEnqueue(() =>
        {
            if (_isDisposed)
                return;
            if (Events.Any(e => e.Id == message.Value.Id))
            {
                _logger.LogWarning("Attempted to add duplicate event with ID {EventId}. Skipping.", message.Value.Id);
                return;
            }
            Events.Add(message.Value);
            _logger.LogTrace("Added event {EventType} ({EventId}) to display collection.", message.Value.GetType().Name, message.Value.Id);
            while (Events.Count > MAX_MESSAGES)
            {
                BaseEvent removed = Events[0];
                _logger.LogTrace(
                    "Max messages ({MaxCount}) reached. Removing oldest event {EventType} ({EventId}).",
                    MAX_MESSAGES,
                    removed.GetType().Name,
                    removed.Id
                );
                Events.RemoveAt(0);
            }
        });
    }

    public void Receive(ConnectionsUpdatedMessage message)
    {
        if (_isDisposed)
            return;
        _logger.LogInformation("Received ConnectionsUpdatedMessage. Refreshing send targets.");
        UpdateSendTargets();
    }

    private bool CanSendMessage() => !string.IsNullOrWhiteSpace(MessageToSend) && SelectedSendTarget != null && SendTargets.Count > 0;

    [RelayCommand(CanExecute = nameof(CanSendMessage))]
    private async Task SendMessageAsync()
    {
        [MemberNotNullWhen(true, nameof(MessageToSend), nameof(SelectedSendTarget))]
        bool CanSendGuard() => CanSendMessage();

        if (!CanSendGuard())
        {
            _logger.LogWarning("SendMessageAsync called but CanSendMessage returned false or critical properties are null.");
            return;
        }

        string message = MessageToSend;
        SendTarget selectedTargetInfo = SelectedSendTarget;
        MessageToSend = string.Empty;

        _logger.LogInformation("Send requested. Target: {TargetDisplayName}, Message: '{MessageContent}'", selectedTargetInfo.DisplayName, message);

        List<SendTarget> targetsToSendTo = [];
        bool isSendAll = selectedTargetInfo.Platform == "All";

        if (isSendAll)
        {
            targetsToSendTo.AddRange(SendTargets.Where(t => t.IsSpecificAccount && IsAccountConnected(t.AccountId, t.Platform)));
            _logger.LogDebug("Sending to 'All Connected'. Found {Count} specific connected targets.", targetsToSendTo.Count);
        }
        else if (selectedTargetInfo.IsSpecificAccount && IsAccountConnected(selectedTargetInfo.AccountId, selectedTargetInfo.Platform))
        {
            targetsToSendTo.Add(selectedTargetInfo);
            _logger.LogDebug("Sending to specific target: {TargetDisplayName}", selectedTargetInfo.DisplayName);
        }

        if (targetsToSendTo.Count == 0)
        {
            _logger.LogWarning("No valid/connected targets found for sending the message.");
            MessageToSend = message;

            return;
        }

        List<Task> sendTasks = [];
        foreach (SendTarget individualTarget in targetsToSendTo)
        {
            string? targetId = null;

            if (individualTarget.Platform == "Twitch")
            {
                targetId = individualTarget.AccountChannelName;
            }
            else if (individualTarget.Platform == "YouTube" && individualTarget.AccountId != null)
            {
                targetId = await GetYouTubeLiveChatIdAsync(individualTarget.AccountId, "SendMessage");
                if (targetId == null)
                {
                    _logger.LogError("Skipping send for YouTube account {AccId}: Could not determine LiveChatId.", individualTarget.AccountId);

                    continue;
                }
            }

            if (string.IsNullOrEmpty(targetId) || individualTarget.AccountId == null)
            {
                _logger.LogWarning(
                    "Skipping send for {TargetDisplayName}: Could not determine target ID or Account ID is missing.",
                    individualTarget.DisplayName
                );
                continue;
            }

            _logger.LogDebug(
                "Queueing send task for {Platform} account {AccountId} to target '{TargetId}'.",
                individualTarget.Platform,
                individualTarget.AccountId,
                targetId
            );
            sendTasks.Add(_unifiedEventService.SendChatMessageAsync(individualTarget.Platform, individualTarget.AccountId, targetId, message));
        }

        try
        {
            await Task.WhenAll(sendTasks);
            _logger.LogInformation("{Count} send operations completed.", sendTasks.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred during batch message sending process.");
        }
    }

    private bool IsAccountConnected(string? accountId, string platform)
    {
        if (string.IsNullOrEmpty(accountId))
            return false;

        bool isConnected = platform.ToLowerInvariant() switch
        {
            "twitch" => _twitchClient.GetStatus(accountId) == ConnectionStatus.Connected,
            "youtube" => _youTubeClient.GetStatus(accountId) == ConnectionStatus.Connected,
            _ => false,
        };
        _logger.LogTrace("Connectivity check for {Platform} account {AccountId}: {IsConnected}", platform, accountId, isConnected);
        return isConnected;
    }

    /// <summary>
    /// Retrieves the required LiveChatId for a YouTube action, attempting an on-demand lookup if necessary.
    /// </summary>
    /// <param name="moderatorAccountId">The Channel ID of the account performing the action.</param>
    /// <param name="actionName">The name of the action for logging purposes.</param>
    /// <returns>The LiveChatId if found or looked up successfully, otherwise null.</returns>
    private async Task<string?> GetYouTubeLiveChatIdAsync(string moderatorAccountId, string actionName)
    {
        string? liveChatId = _youTubeClient.GetAssociatedLiveChatId(moderatorAccountId);
        if (!string.IsNullOrEmpty(liveChatId))
        {
            _logger.LogDebug("{Action}: Found cached AssociatedLiveChatId: {LiveChatId}", actionName, liveChatId);
            return liveChatId;
        }
        _logger.LogWarning(
            "{Action}: AssociatedLiveChatId not found for account {AccountId}. Attempting on-demand lookup...",
            actionName,
            moderatorAccountId
        );
        string? videoId = _youTubeClient.GetActiveVideoId(moderatorAccountId);
        if (string.IsNullOrEmpty(videoId))
        {
            _logger.LogError(
                "{Action} failed: Cannot lookup LiveChatId because no active VideoID is being monitored for account {AccountId}.",
                actionName,
                moderatorAccountId
            );

            return null;
        }

        liveChatId = await _youTubeClient.LookupAndStoreLiveChatIdAsync(moderatorAccountId, videoId);
        if (string.IsNullOrEmpty(liveChatId))
        {
            _logger.LogError(
                "{Action} failed: Could not retrieve LiveChatId for monitored VideoID {VideoId} using account {AccountId}.",
                actionName,
                videoId,
                moderatorAccountId
            );

            return null;
        }
        _logger.LogInformation("{Action}: Successfully looked up and stored LiveChatId: {LiveChatId}", actionName, liveChatId);
        return liveChatId;
    }

    private bool CanExecuteYouTubeAction(ChatMessageEvent? message)
    {
        if (message == null || message.Platform != "YouTube" || string.IsNullOrEmpty(message.OriginatingAccountId))
        {
            return false;
        }
        return _youTubeClient.GetStatus(message.OriginatingAccountId) == ConnectionStatus.Connected;
    }

    [RelayCommand(CanExecute = nameof(CanExecuteYouTubeAction))]
    private async Task TimeoutUserAsync(ChatMessageEvent? message)
    {
        const string actionName = "TimeoutUser";
        if (message?.UserId == null || message.OriginatingAccountId == null)
        {
            _logger.LogWarning("{Action} cancelled: Message, UserID, or OriginatingAccountID is null.", actionName);
            return;
        }

        string moderatorAccountId = message.OriginatingAccountId;
        string userIdToTimeout = message.UserId;
        uint durationSeconds = 600;

        _logger.LogInformation(
            "{Action}: Attempting for user {Username} ({UserId}) for {Duration}s via account {AccountId}",
            actionName,
            message.Username,
            userIdToTimeout,
            durationSeconds,
            moderatorAccountId
        );

        string? liveChatId = await GetYouTubeLiveChatIdAsync(moderatorAccountId, actionName);
        if (liveChatId == null)
            return;

        try
        {
            await _youTubeClient.TimeoutUserAsync(moderatorAccountId, liveChatId, userIdToTimeout, durationSeconds);
            _logger.LogInformation("{Action} request sent successfully for user {UserId}.", actionName, userIdToTimeout);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{Action} API call failed for user {UserId}", actionName, userIdToTimeout);
        }
    }

    [RelayCommand(CanExecute = nameof(CanExecuteYouTubeAction))]
    private async Task BanUserAsync(ChatMessageEvent? message)
    {
        const string actionName = "BanUser";
        if (message?.UserId == null || message.OriginatingAccountId == null)
        {
            _logger.LogWarning("{Action} cancelled: Message, UserID, or OriginatingAccountID is null.", actionName);
            return;
        }

        string moderatorAccountId = message.OriginatingAccountId;
        string userIdToBan = message.UserId;

        _logger.LogInformation(
            "{Action}: Attempting for user {Username} ({UserId}) via account {AccountId}",
            actionName,
            message.Username,
            userIdToBan,
            moderatorAccountId
        );

        string? liveChatId = await GetYouTubeLiveChatIdAsync(moderatorAccountId, actionName);
        if (liveChatId == null)
            return;

        try
        {
            await _youTubeClient.BanUserAsync(moderatorAccountId, liveChatId, userIdToBan);
            _logger.LogInformation("{Action} request sent successfully for user {UserId}.", actionName, userIdToBan);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{Action} API call failed for user {UserId}", actionName, userIdToBan);
        }
    }

    [RelayCommand(CanExecute = nameof(CanExecuteYouTubeAction))]
    private async Task DeleteMessageAsync(ChatMessageEvent? message)
    {
        const string actionName = "DeleteMessage";

        if (message?.Id == null || message.OriginatingAccountId == null)
        {
            _logger.LogWarning("{Action} cancelled: Message, Message.Id, or OriginatingAccountID is null.", actionName);
            return;
        }

        string moderatorAccountId = message.OriginatingAccountId;
        string messageId = message.Id;

        _logger.LogInformation("{Action}: Attempting for message {MessageId} via account {AccountId}", actionName, messageId, moderatorAccountId);

        try
        {
            await _youTubeClient.DeleteMessageAsync(moderatorAccountId, messageId);
            _logger.LogInformation("{Action} request sent successfully for message {MessageId}.", actionName, messageId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{Action} API call failed for message {MessageId}", actionName, messageId);
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
            return;
        _isDisposed = true;
        _logger.LogInformation("Disposing...");
        _messenger.UnregisterAll(this);
        _dispatcherQueue.TryEnqueue(() =>
        {
            SendTargets?.Clear();
            Events?.Clear();
        });
        _logger.LogInformation("Disposed and Unregistered.");
        GC.SuppressFinalize(this);
    }
}

public class SendTarget
{
    public string DisplayName { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty;
    public string? AccountId { get; set; }
    public string? AccountChannelName { get; set; }
    public bool IsSpecificAccount => Platform != "All" && !string.IsNullOrEmpty(AccountId);
}
