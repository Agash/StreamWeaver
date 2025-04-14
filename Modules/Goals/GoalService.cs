using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using StreamWeaver.Core.Messaging;
using StreamWeaver.Core.Models.Events;
using StreamWeaver.Core.Services.Settings;

namespace StreamWeaver.Modules.Goals;

/// <summary>
/// Manages the logic for tracking progress towards defined goals (e.g., subscriptions, followers, donations).
/// Listens for relevant events and publishes updates when progress changes.
/// </summary>
public partial class GoalService : IRecipient<NewEventMessage>, IDisposable
{
    private readonly ILogger<GoalService> _logger;
    private readonly ISettingsService _settingsService;
    private readonly IMessenger _messenger;
    private GoalSettings _moduleSettings = new();
    private bool _isDisposed;

    private int _currentSubGoalCount = 0;

    /// <summary>
    /// Initializes a new instance of the <see cref="GoalService"/> class.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="settingsService">Service for accessing application settings.</param>
    /// <param name="messenger">Messenger for inter-component communication.</param>
    public GoalService(ILogger<GoalService> logger, ISettingsService settingsService, IMessenger messenger)
    {
        _logger = logger;
        _settingsService = settingsService;
        _messenger = messenger;

        LoadSettings();
        _settingsService.SettingsUpdated += OnSettingsUpdated;
        _messenger.Register(this);

        LoadInitialProgress();

        _logger.LogInformation("Initialized.");
    }

    /// <summary>
    /// Loads the goal-specific settings from the main settings service.
    /// </summary>
    private void LoadSettings()
    {
        _moduleSettings = _settingsService.CurrentSettings.Modules.Goals;
        _logger.LogDebug("Goal settings loaded/reloaded.");
    }

    /// <summary>
    /// Loads the initial progress for the currently configured goal type.
    /// </summary>
    private void LoadInitialProgress()
    {
        _currentSubGoalCount = _moduleSettings.PersistedSubCount;

        _logger.LogInformation("Initial goal progress loaded. Current Sub Count: {SubCount}", _currentSubGoalCount);
        PublishGoalUpdate();
    }

    /// <summary>
    /// Persists the current goal progress.
    /// **Caution:** Avoid calling this too frequently if saving directly to main settings file.
    /// </summary>
    private void PersistProgress()
    {
        if (_moduleSettings.PersistedSubCount != _currentSubGoalCount)
        {
            _moduleSettings.PersistedSubCount = _currentSubGoalCount;

            _logger.LogDebug("Persisting goal progress. Sub Count: {SubCount}", _currentSubGoalCount);
        }
    }

    /// <summary>
    /// Handles updates to the application settings. Reloads goal settings and publishes updates.
    /// </summary>
    private void OnSettingsUpdated(object? sender, EventArgs e)
    {
        _logger.LogDebug("SettingsUpdated event received.");
        GoalSettings oldSettings = _moduleSettings;
        LoadSettings();

        if (oldSettings.GoalType != _moduleSettings.GoalType)
        {
            _logger.LogInformation(
                "Goal type changed from {OldType} to {NewType}. Resetting progress.",
                oldSettings.GoalType,
                _moduleSettings.GoalType
            );

            _currentSubGoalCount = 0;

            LoadInitialProgress();
        }
        else if (
            oldSettings.SubGoalTarget != _moduleSettings.SubGoalTarget /* || other targets changed */
        )
        {
            _logger.LogInformation("Goal target changed.");
        }

        PublishGoalUpdate();
    }

    /// <summary>
    /// Receives new events from the application's core message bus.
    /// Updates goal progress based on relevant event types.
    /// </summary>
    /// <param name="message">The wrapper containing the event details.</param>
    public void Receive(NewEventMessage message)
    {
        if (!_moduleSettings.Enabled)
            return;

        bool progressChanged = false;

        switch (message.Value)
        {
            case SubscriptionEvent se when _moduleSettings.GoalType == GoalType.Subscriptions:

                int subsToAdd = se.GiftCount > 0 ? se.GiftCount : 1;
                _currentSubGoalCount += subsToAdd;
                progressChanged = true;
                _logger.LogDebug(
                    "Subscription event processed. Goal progress: {Current}/{Target}",
                    _currentSubGoalCount,
                    _moduleSettings.SubGoalTarget
                );
                break;

            case FollowEvent when _moduleSettings.GoalType == GoalType.Followers:
                break;

            case DonationEvent when _moduleSettings.GoalType == GoalType.Donations:
                break;
        }

        if (progressChanged)
        {
            PublishGoalUpdate();
            PersistProgress();
        }
    }

    /// <summary>
    /// Calculates current goal state based on settings and internal counters,
    /// then publishes a <see cref="GoalUpdateEvent"/> via the messenger.
    /// </summary>
    private void PublishGoalUpdate()
    {
        if (!_moduleSettings.Enabled)
            return;

        decimal currentValue = 0;
        decimal targetValue = 0;
        string label = _moduleSettings.GoalLabel;

        switch (_moduleSettings.GoalType)
        {
            case GoalType.Subscriptions:
                currentValue = _currentSubGoalCount;
                targetValue = _moduleSettings.SubGoalTarget;
                if (string.IsNullOrWhiteSpace(label))
                    label = "Sub Goal";
                break;
            case GoalType.Followers:

                if (string.IsNullOrWhiteSpace(label))
                    label = "Follower Goal";

                break;
            case GoalType.Donations:

                if (string.IsNullOrWhiteSpace(label))
                    label = "Donation Goal";

                break;
        }

        _logger.LogDebug("Publishing Goal Update: Label='{Label}', Current={Current}, Target={Target}", label, currentValue, targetValue);

        GoalUpdateEvent updateEvent = new()
        {
            Label = label,
            CurrentValue = currentValue,
            TargetValue = targetValue,
        };

        _messenger.Send(new NewEventMessage(updateEvent));
    }

    /// <summary>
    /// Cleans up resources, persists final progress, and unregisters from messages.
    /// </summary>
    public void Dispose()
    {
        if (_isDisposed)
            return;
        _isDisposed = true;

        _logger.LogInformation("Disposing...");
        PersistProgress();
        _settingsService.SettingsUpdated -= OnSettingsUpdated;
        _messenger.UnregisterAll(this);
        _logger.LogInformation("Dispose finished.");
        GC.SuppressFinalize(this);
    }
}

// TODO: Consider placing this definition in a shared Models/Events namespace if used across modules.
/// <summary>
/// Represents an update to the state of a tracked goal.
/// This event is published by the <see cref="GoalService"/>.
/// </summary>
public class GoalUpdateEvent : BaseEvent
{
    /// <summary>
    /// Gets the display label for the goal (e.g., "Sub Goal", "Charity Target").
    /// </summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>
    /// Gets the current progress value towards the goal.
    /// </summary>
    public decimal CurrentValue { get; init; } = 0;

    /// <summary>
    /// Gets the target value for the goal.
    /// </summary>
    public decimal TargetValue { get; init; } = 0;

    /// <summary>
    /// Initializes a new instance of the <see cref="GoalUpdateEvent"/> class.
    /// </summary>
    public GoalUpdateEvent() => Platform = "Module";
}
