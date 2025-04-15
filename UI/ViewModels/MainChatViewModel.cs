using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using StreamWeaver.Core.Messaging;
using StreamWeaver.Core.Models.Events;
using StreamWeaver.Core.Models.Settings;
using StreamWeaver.Core.Services;
using StreamWeaver.Core.Services.Platforms;
using StreamWeaver.Core.Services.Settings;
using StreamWeaver.UI.Dialogs;

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
    [NotifyPropertyChangedFor(nameof(CanCreateYouTubePoll))]
    [NotifyPropertyChangedFor(nameof(CanEndYouTubePoll))]
    [NotifyCanExecuteChangedFor(nameof(ShowCreatePollDialogCommand))]
    [NotifyCanExecuteChangedFor(nameof(EndPollCommand))]
    public partial SendTarget? SelectedSendTarget { get; set; }

    /// <summary>
    /// Stores the message ID of the currently active YouTube poll, if any.
    /// Required for the End Poll API call.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEndYouTubePoll))]
    [NotifyCanExecuteChangedFor(nameof(EndPollCommand))]
    public partial string? ActivePollMessageId { get; set; }

    /// <summary>
    /// Gets a value indicating whether the currently selected target allows creating a YouTube poll.
    /// </summary>
    public bool CanCreateYouTubePoll => SelectedSendTarget?.Platform is "YouTube" or "All";

    /// <summary>
    /// Gets a value indicating whether an active YouTube poll can be ended based on the current state.
    /// </summary>
    public bool CanEndYouTubePoll => CanCreateYouTubePoll && !string.IsNullOrWhiteSpace(ActivePollMessageId);

    public MainChatViewModel(
        ILogger<MainChatViewModel> logger,
        IMessenger messenger,
        UnifiedEventService unifiedEventService,
        ISettingsService settingsService,
        ITwitchClient twitchClient,
        IYouTubeClient youTubeClient,
        DispatcherQueue dispatcherQueue
    )
    {
        _logger = logger;
        _messenger = messenger;
        _unifiedEventService = unifiedEventService;
        _settingsService = settingsService;
        _twitchClient = twitchClient;
        _youTubeClient = youTubeClient;
        _dispatcherQueue = dispatcherQueue;
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

             // --- Twitch Targets ---
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

             // --- YouTube Targets ---
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

             // --- Add 'All Connected' Target ---
             if (connectedTargetsCount > 1)
             {
                 _logger.LogTrace("Adding 'All' target.");
                 newTargets.Insert(
                     0,
                     new SendTarget
                     {
                         DisplayName = "All",
                         Platform = "All",
                         AccountId = null, // Represents 'All'
                         AccountChannelName = null,
                     }
                 );
             }

             // --- Update ObservableCollection ---
             SendTarget? previouslySelected = SelectedSendTarget;
             var currentTargetKeys = SendTargets.Select(t => $"{t.Platform}_{t.AccountId ?? "all"}").ToHashSet();
             var newTargetKeys = newTargets.Select(t => $"{t.Platform}_{t.AccountId ?? "all"}").ToHashSet();

             var targetsToRemove = SendTargets.Where(t => !newTargetKeys.Contains($"{t.Platform}_{t.AccountId ?? "all"}")).ToList();
             foreach (SendTarget? target in targetsToRemove)
             {
                 SendTargets.Remove(target);
             }

             var targetsToAdd = newTargets.Where(t => !currentTargetKeys.Contains($"{t.Platform}_{t.AccountId ?? "all"}")).ToList();
             foreach (SendTarget? target in targetsToAdd)
             {
                 // Maintain order if possible (add specific accounts after 'All')
                 if (target.Platform == "All")
                     SendTargets.Insert(0, target);
                 else
                     SendTargets.Add(target);
             }

             // --- Restore Selection ---
             SendTarget? newSelection = null;
             if (previouslySelected != null)
             {
                 newSelection = SendTargets.FirstOrDefault(t =>
                     t.Platform == previouslySelected.Platform && t.AccountId == previouslySelected.AccountId
                 );
             }
             // Default to first item if previous selection removed or null
             SelectedSendTarget = newSelection ?? SendTargets.FirstOrDefault();

             _logger.LogInformation(
                 "Send targets updated. Count: {Count}. Selected: {SelectedTarget}",
                 SendTargets.Count,
                 SelectedSendTarget?.DisplayName ?? "None"
             );

             // Notify dependent properties/commands
             OnPropertyChanged(nameof(SelectedSendTarget));
             OnPropertyChanged(nameof(CanCreateYouTubePoll));
             OnPropertyChanged(nameof(CanEndYouTubePoll));
             ShowCreatePollDialogCommand.NotifyCanExecuteChanged();
             EndPollCommand.NotifyCanExecuteChanged();
             SendMessageCommand.NotifyCanExecuteChanged();
         });

    // Receive methods omitted for brevity...
    public void Receive(NewEventMessage message)
    {
        if (_isDisposed) return;
        _dispatcherQueue.TryEnqueue(() =>
        {
            if (_isDisposed) return;
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
                _logger.LogTrace("Max messages ({MaxCount}) reached. Removing oldest event {EventType} ({EventId}).", MAX_MESSAGES, removed.GetType().Name, removed.Id);
                Events.RemoveAt(0);
            }
        });
    }

    public void Receive(ConnectionsUpdatedMessage message)
    {
        if (_isDisposed) return;
        _logger.LogInformation("Received ConnectionsUpdatedMessage. Refreshing send targets.");
        UpdateSendTargets();
    }

    // Send Message Logic
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
        MessageToSend = string.Empty; // Clear input immediately

        _logger.LogInformation("Send requested. Target: {TargetDisplayName}, Message: '{MessageContent}'", selectedTargetInfo.DisplayName, message);

        List<SendTarget> targetsToSendTo = [];
        bool isSendAll = selectedTargetInfo.Platform == "All";

        if (isSendAll)
        {
            // Add all *specific*, connected accounts when 'All' is selected
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
            MessageToSend = message; // Restore message if send failed
            return;
        }

        List<Task> sendTasks = [];
        foreach (SendTarget individualTarget in targetsToSendTo)
        {
            string? targetId = null;
            if (individualTarget.Platform == "Twitch")
            {
                // For Twitch, the target is the channel name, which we assume is the same as the account name for now
                // A more robust system might need to look up the channel to join/send to.
                targetId = individualTarget.AccountChannelName;
            }
            else if (individualTarget.Platform == "YouTube" && individualTarget.AccountId != null)
            {
                // For YouTube, the target is the LiveChatId
                targetId = await GetYouTubeLiveChatIdAsync(individualTarget.AccountId, "SendMessage");
                if (targetId == null)
                {
                    _logger.LogError("Skipping send for YouTube account {AccId}: Could not determine LiveChatId.", individualTarget.AccountId);
                    // TODO: Consider user feedback here
                    continue; // Skip this target
                }
            }

            if (string.IsNullOrEmpty(targetId) || individualTarget.AccountId == null)
            {
                _logger.LogWarning("Skipping send for {TargetDisplayName}: Could not determine target ID or Account ID is missing.", individualTarget.DisplayName);
                continue;
            }

            _logger.LogDebug("Queueing send task for {Platform} account {AccountId} to target '{TargetId}'.", individualTarget.Platform, individualTarget.AccountId, targetId);
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
            // TODO: User feedback about send failure
        }
    }

    // IsAccountConnected method omitted for brevity...
    private bool IsAccountConnected(string? accountId, string platform)
    {
        if (string.IsNullOrEmpty(accountId)) return false;
        bool isConnected = platform.ToLowerInvariant() switch
        {
            "twitch" => _twitchClient.GetStatus(accountId) == ConnectionStatus.Connected,
            "youtube" => _youTubeClient.GetStatus(accountId) is ConnectionStatus.Connected or ConnectionStatus.Limited, // Treat Limited as connected for sending
            _ => false
        };
        _logger.LogTrace("Connectivity check for {Platform} account {AccountId}: {IsConnected}", platform, accountId, isConnected);
        return isConnected;
    }


    // GetYouTubeLiveChatIdAsync method omitted for brevity...
    private async Task<string?> GetYouTubeLiveChatIdAsync(string moderatorAccountId, string actionName)
    {
        string? liveChatId = _youTubeClient.GetAssociatedLiveChatId(moderatorAccountId);
        if (!string.IsNullOrEmpty(liveChatId))
        {
            _logger.LogDebug("{Action}: Found cached AssociatedLiveChatId: {LiveChatId}", actionName, liveChatId);
            return liveChatId;
        }

        _logger.LogWarning("{Action}: AssociatedLiveChatId not found for account {AccountId}. Attempting on-demand lookup...", actionName, moderatorAccountId);

        // Try to get the Video ID being actively monitored first
        string? videoId = _youTubeClient.GetActiveVideoId(moderatorAccountId);

        // If not monitoring, check account override or global override
        if (string.IsNullOrEmpty(videoId))
        {
            YouTubeAccount? account = _settingsService.CurrentSettings?.Connections?.YouTubeAccounts?.FirstOrDefault(a => a.ChannelId == moderatorAccountId);
            if (account != null && !string.IsNullOrEmpty(account.OverrideVideoId))
            {
                videoId = account.OverrideVideoId;
                _logger.LogDebug("{Action}: Using account OverrideVideoId '{VideoId}' for lookup.", actionName, videoId);
            }
            else if (!string.IsNullOrEmpty(_settingsService.CurrentSettings?.Connections?.DebugYouTubeLiveChatId))
            {
                videoId = _settingsService.CurrentSettings.Connections.DebugYouTubeLiveChatId;
                _logger.LogDebug("{Action}: Using global DebugYouTubeLiveChatId '{VideoId}' for lookup.", actionName, videoId);
            }
        }

        if (string.IsNullOrEmpty(videoId))
        {
            _logger.LogError("{Action} failed: Cannot lookup LiveChatId because no active VideoID is being monitored or configured for account {AccountId}.", actionName, moderatorAccountId);
            SendSystemMessage($"Cannot perform '{actionName}': YouTube stream is not currently being monitored.", SystemMessageLevel.Error);
            return null;
        }

        liveChatId = await _youTubeClient.LookupAndStoreLiveChatIdAsync(moderatorAccountId, videoId);
        if (string.IsNullOrEmpty(liveChatId))
        {
            _logger.LogError("{Action} failed: Could not retrieve LiveChatId for monitored VideoID {VideoId} using account {AccountId}.", actionName, videoId, moderatorAccountId);
            SendSystemMessage($"Cannot perform '{actionName}': Failed to get LiveChatId for video '{videoId}'.", SystemMessageLevel.Error);
            return null;
        }

        _logger.LogInformation("{Action}: Successfully looked up and stored LiveChatId: {LiveChatId}", actionName, liveChatId);
        return liveChatId;
    }


    // CanExecuteYouTubeAction method omitted for brevity...
    private bool CanExecuteYouTubeAction(ChatMessageEvent? message)
    {
        if (message == null || message.Platform != "YouTube" || string.IsNullOrEmpty(message.OriginatingAccountId))
        {
            return false;
        }
        // Allow actions even in limited state as long as API client is available
        ConnectionStatus status = _youTubeClient.GetStatus(message.OriginatingAccountId);
        return status is ConnectionStatus.Connected or ConnectionStatus.Limited;
    }


    // TimeoutUserAsync method omitted for brevity...
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
        uint durationSeconds = 600; // 10 minutes

        _logger.LogInformation("{Action}: Attempting for user {Username} ({UserId}) for {Duration}s via account {AccountId}", actionName, message.Username, userIdToTimeout, durationSeconds, moderatorAccountId);

        string? liveChatId = await GetYouTubeLiveChatIdAsync(moderatorAccountId, actionName);
        if (liveChatId == null) return; // Error handled in GetYouTubeLiveChatIdAsync

        try
        {
            await _youTubeClient.TimeoutUserAsync(moderatorAccountId, liveChatId, userIdToTimeout, durationSeconds);
            _logger.LogInformation("{Action} request sent successfully for user {UserId}.", actionName, userIdToTimeout);
            // Optional: Add a system message confirming the action locally
            // SendSystemMessage($"Timed out {message.Username} for 10 minutes.", SystemMessageLevel.Info);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{Action} API call failed for user {UserId}", actionName, userIdToTimeout);
            SendSystemMessage($"Error timing out {message.Username}: {ex.Message}", SystemMessageLevel.Error);
        }
    }


    // BanUserAsync method omitted for brevity...
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

        _logger.LogInformation("{Action}: Attempting for user {Username} ({UserId}) via account {AccountId}", actionName, message.Username, userIdToBan, moderatorAccountId);

        string? liveChatId = await GetYouTubeLiveChatIdAsync(moderatorAccountId, actionName);
        if (liveChatId == null) return; // Error handled in GetYouTubeLiveChatIdAsync

        try
        {
            await _youTubeClient.BanUserAsync(moderatorAccountId, liveChatId, userIdToBan);
            _logger.LogInformation("{Action} request sent successfully for user {UserId}.", actionName, userIdToBan);
            // SendSystemMessage($"Banned {message.Username}.", SystemMessageLevel.Info);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{Action} API call failed for user {UserId}", actionName, userIdToBan);
            SendSystemMessage($"Error banning {message.Username}: {ex.Message}", SystemMessageLevel.Error);
        }
    }


    // DeleteMessageAsync method omitted for brevity...
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
        // IMPORTANT: Use the message ID from the event, NOT the originating account ID
        string messageId = message.Id;

        _logger.LogInformation("{Action}: Attempting for message {MessageId} via account {AccountId}", actionName, messageId, moderatorAccountId);

        // Note: Delete message doesn't require LiveChatId, only the Message ID
        try
        {
            await _youTubeClient.DeleteMessageAsync(moderatorAccountId, messageId);
            _logger.LogInformation("{Action} request sent successfully for message {MessageId}.", actionName, messageId);
            // SendSystemMessage($"Deleted message from {message.Username}.", SystemMessageLevel.Info);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{Action} API call failed for message {MessageId}", actionName, messageId);
            SendSystemMessage($"Error deleting message from {message.Username}: {ex.Message}", SystemMessageLevel.Error);
        }
    }


    // --- POLL COMMANDS ---

    [RelayCommand(CanExecute = nameof(CanCreateYouTubePoll))]
    private async Task ShowCreatePollDialogAsync()
    {
        const string actionName = "CreatePoll";
        if (SelectedSendTarget == null)
        {
            _logger.LogError("{Action} cannot proceed: No send target selected.", actionName);
            return;
        }

        // Determine the specific YouTube account ID to use
        string? moderatorAccountId = GetModeratorAccountId(actionName);
        if (string.IsNullOrEmpty(moderatorAccountId)) return; // Error logged in helper

        // Get LiveChatId using the helper
        string? liveChatId = await GetYouTubeLiveChatIdAsync(moderatorAccountId, actionName);
        if (string.IsNullOrEmpty(liveChatId)) return; // Error handled in helper

        // Show Dialog
        var dialogViewModel = new CreatePollDialogViewModel();
        var dialog = new CreatePollDialog
        {
            DataContext = dialogViewModel,
            XamlRoot = App.MainWindow?.Content?.XamlRoot
        };

        if (dialog.XamlRoot == null)
        {
            _logger.LogError("{Action} cancelled: Cannot show dialog because XamlRoot is null.", actionName);
            SendSystemMessage("Error showing poll dialog.", SystemMessageLevel.Error);
            return;
        }

        ContentDialogResult result = await dialog.ShowAsync();

        if (result == ContentDialogResult.Primary)
        {
            if (dialogViewModel.TryGetValidatedData(out string? question, out List<string>? options))
            {
                _logger.LogInformation("{Action}: Dialog confirmed and data is valid. Question: '{Question}', Options: {OptionCount}", actionName, question, options.Count);
                try
                {
                    string? pollMessageId = await _youTubeClient.CreatePollAsync(moderatorAccountId, liveChatId, question, options);
                    if (!string.IsNullOrEmpty(pollMessageId))
                    {
                        _logger.LogInformation("{Action} succeeded. Poll Message ID: {PollMessageId}", actionName, pollMessageId);
                        ActivePollMessageId = pollMessageId; // Store the active poll ID
                        SendSystemMessage($"YouTube poll created successfully: '{question}'", SystemMessageLevel.Info);
                        // No need to explicitly notify CanEnd/EndPollCommand - happens via [ObservableProperty]
                    }
                    else
                    {
                        _logger.LogError("{Action} failed: CreatePollAsync returned null/empty ID.", actionName);
                        SendSystemMessage($"Failed to create YouTube poll: '{question}'. API call failed.", SystemMessageLevel.Error);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "{Action} failed during API call.", actionName);
                    SendSystemMessage($"Error creating YouTube poll: {ex.Message}", SystemMessageLevel.Error);
                }
            }
            else
            {
                // This case shouldn't happen if PrimaryButtonClick logic is correct, but log just in case.
                _logger.LogError("{Action}: Dialog returned Primary but validation failed unexpectedly.", actionName);
            }
        }
        else
        {
            _logger.LogInformation("{Action}: Dialog cancelled by user.", actionName);
        }
    }

    [RelayCommand(CanExecute = nameof(CanEndYouTubePoll))]
    private async Task EndPollAsync()
    {
        const string actionName = "EndPoll";
        if (string.IsNullOrWhiteSpace(ActivePollMessageId))
        {
            _logger.LogError("{Action} cannot proceed: ActivePollMessageId is null or empty.", actionName);
            return;
        }

        if (SelectedSendTarget == null)
        {
            _logger.LogError("{Action} cannot proceed: No send target selected.", actionName);
            return;
        }

        // Determine the specific YouTube account ID to use
        string? moderatorAccountId = GetModeratorAccountId(actionName);
        if (string.IsNullOrEmpty(moderatorAccountId)) return; // Error logged in helper

        _logger.LogInformation("{Action}: Attempting for Poll Message ID: {PollId} using account {AccountId}", actionName, ActivePollMessageId, moderatorAccountId);

        try
        {
            bool success = await _youTubeClient.EndPollAsync(moderatorAccountId, ActivePollMessageId);
            if (success)
            {
                _logger.LogInformation("{Action} succeeded for Poll Message ID: {PollId}", actionName, ActivePollMessageId);
                SendSystemMessage("YouTube poll ended successfully.", SystemMessageLevel.Info);
                ActivePollMessageId = null; // Clear the active poll ID
                // No need to explicitly notify CanEnd/EndPollCommand - happens via [ObservableProperty]
            }
            else
            {
                _logger.LogError("{Action} failed: EndPollAsync returned false for Poll Message ID: {PollId}", actionName, ActivePollMessageId);
                SendSystemMessage("Failed to end the YouTube poll. API call failed.", SystemMessageLevel.Error);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{Action} failed during API call for Poll Message ID: {PollId}", actionName, ActivePollMessageId);
            SendSystemMessage($"Error ending YouTube poll: {ex.Message}", SystemMessageLevel.Error);
        }
    }

    /// <summary>
    /// Helper to determine the moderator account ID based on the SelectedSendTarget.
    /// </summary>
    private string? GetModeratorAccountId(string actionName)
    {
        if (SelectedSendTarget == null)
        {
            _logger.LogError("{Action} cannot proceed: No send target selected.", actionName);
            return null;
        }

        string? moderatorAccountId = null;
        if (SelectedSendTarget.Platform == "YouTube" && SelectedSendTarget.AccountId != null)
        {
            moderatorAccountId = SelectedSendTarget.AccountId;
        }
        else if (SelectedSendTarget.Platform == "All")
        {
            // Find the *first* connected YouTube account to act as the moderator
            moderatorAccountId = _settingsService.CurrentSettings?.Connections?.YouTubeAccounts?
                .FirstOrDefault(acc => !string.IsNullOrEmpty(acc.ChannelId) && IsAccountConnected(acc.ChannelId, "YouTube"))?
                .ChannelId;
        }

        if (string.IsNullOrEmpty(moderatorAccountId))
        {
            _logger.LogError("{Action} cannot proceed: Could not determine a valid YouTube account ID from selection '{SelectedTarget}'.", actionName, SelectedSendTarget.DisplayName);
            SendSystemMessage($"Cannot {actionName}: No connected YouTube account selected or available.", SystemMessageLevel.Error);
            return null;
        }

        return moderatorAccountId;
    }


    // SendSystemMessage method omitted for brevity...
    private void SendSystemMessage(string message, SystemMessageLevel level)
    {
        SystemMessageEvent systemEvent = new()
        {
            Level = level,
            Message = message
        };
        _messenger.Send(new NewEventMessage(systemEvent));
    }


    // Dispose method omitted for brevity...
    public void Dispose()
    {
        if (_isDisposed) return;
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

// SendTarget class definition omitted for brevity...
public class SendTarget
{
    public string DisplayName { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty; // "Twitch", "YouTube", "All"
    public string? AccountId { get; set; } // Specific ID (UserID for Twitch, ChannelID for YouTube), null for "All"
    public string? AccountChannelName { get; set; } // Username (Twitch), Channel Name (YouTube)

    // Helper to check if this target represents a specific account vs. "All"
    public bool IsSpecificAccount => Platform != "All" && !string.IsNullOrEmpty(AccountId);
}
