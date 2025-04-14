using System.Diagnostics;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using StreamWeaver.Core.Messaging;
using StreamWeaver.Core.Models.Events;
using StreamWeaver.Core.Services.Settings;

namespace StreamWeaver.Modules.Subathon;

/// <summary>
/// Manages the subathon timer logic, adding time based on events and publishing updates.
/// </summary>
public partial class SubathonService : IRecipient<NewEventMessage>, IDisposable
{
    private readonly ILogger<SubathonService> _logger;
    private readonly ISettingsService _settingsService;
    private readonly IMessenger _messenger;
    private Timer? _timer;
    private DateTime _timerEndTime = DateTime.MinValue;
    private bool _isRunning = false;
    private SubathonSettings _moduleSettings = new();
    private bool _isDisposed = false;
    private long _lastUpdateTimeTicks = 0;

    private static readonly TimeSpan s_updateInterval = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Initializes a new instance of the <see cref="SubathonService"/> class.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="settingsService">Service for accessing application settings.</param>
    /// <param name="messenger">Messenger for inter-component communication.</param>
    public SubathonService(ILogger<SubathonService> logger, ISettingsService settingsService, IMessenger messenger)
    {
        _logger = logger;
        _settingsService = settingsService;
        _messenger = messenger;

        LoadSettings();
        _settingsService.SettingsUpdated += OnSettingsUpdated;
        _messenger.Register<NewEventMessage>(this);

        if (_moduleSettings.Enabled)
        {
            _ = Task.Run(StartTimerAsync);
        }

        _logger.LogInformation("Initialized.");
    }

    /// <summary>
    /// Loads the subathon-specific settings from the main settings service.
    /// </summary>
    private void LoadSettings()
    {
        _moduleSettings = _settingsService.CurrentSettings.Modules.Subathon;
        _logger.LogDebug("Settings Loaded. Enabled={IsEnabled}", _moduleSettings.Enabled);
    }

    /// <summary>
    /// Handles updates to the application settings, enabling/disabling or adjusting the timer as needed.
    /// </summary>
    private async void OnSettingsUpdated(object? sender, EventArgs e)
    {
        if (_isDisposed)
            return;
        _logger.LogDebug("Settings updated event received.");
        bool oldEnabledState = _moduleSettings.Enabled;
        LoadSettings();

        if (!oldEnabledState && _moduleSettings.Enabled)
        {
            _logger.LogInformation("Subathon module enabled via settings change. Starting timer.");
            await StartTimerAsync();
        }
        else if (oldEnabledState && !_moduleSettings.Enabled)
        {
            _logger.LogInformation("Subathon module disabled via settings change. Stopping timer.");
            await StopTimerAsync();
        }

        if (_isRunning || (oldEnabledState && !_moduleSettings.Enabled))
        {
            PublishTimerUpdate();
        }
    }

    /// <summary>
    /// Receives new events and adds time to the subathon timer if applicable and configured.
    /// </summary>
    /// <param name="message">The incoming event message.</param>
    public void Receive(NewEventMessage message)
    {
        if (_isDisposed || !_moduleSettings.Enabled || !_isRunning)
            return;

        TimeSpan timeToAdd = TimeSpan.Zero;
        switch (message.Value)
        {
            case SubscriptionEvent se:
                timeToAdd = GetTimeForSubscription(se);
                break;
            case DonationEvent de:
                timeToAdd = GetTimeForDonation(de);
                break;
        }

        if (timeToAdd > TimeSpan.Zero)
        {
            AddTime(timeToAdd);
        }
    }

    /// <summary>
    /// Starts or resumes the subathon timer. Loads persisted state if available.
    /// </summary>
    private async Task StartTimerAsync()
    {
        if (_isDisposed || _isRunning)
            return;
        _logger.LogInformation("Attempting to start timer...");

        bool loadedFromPersistence = false;

        if (_moduleSettings.PersistedEndTimeUtcTicks > 0)
        {
            try
            {
                DateTime persistedEndTime = new(_moduleSettings.PersistedEndTimeUtcTicks, DateTimeKind.Utc);
                if (persistedEndTime > DateTime.UtcNow)
                {
                    _timerEndTime = persistedEndTime;
                    loadedFromPersistence = true;
                    _logger.LogInformation("Resuming timer from persistence. Ends at (UTC): {EndTime}", _timerEndTime);

                    _moduleSettings.PersistedEndTimeUtcTicks = 0;
                    await _settingsService.SaveSettingsAsync(_settingsService.CurrentSettings);
                }
                else
                {
                    _logger.LogInformation("Persisted end time is in the past ({PersistedTimeUtc}). Ignoring and clearing.", persistedEndTime);
                    _moduleSettings.PersistedEndTimeUtcTicks = 0;
                    await _settingsService.SaveSettingsAsync(_settingsService.CurrentSettings);
                }
            }
            catch (ArgumentOutOfRangeException ex)
            {
                _logger.LogWarning(
                    ex,
                    "Persisted end time ticks ({Ticks}) are invalid. Ignoring and clearing.",
                    _moduleSettings.PersistedEndTimeUtcTicks
                );
                _moduleSettings.PersistedEndTimeUtcTicks = 0;
                await _settingsService.SaveSettingsAsync(_settingsService.CurrentSettings);
            }
        }

        if (!loadedFromPersistence)
        {
            if (_timerEndTime <= DateTime.UtcNow)
            {
                if (_moduleSettings.InitialDurationMinutes > 0)
                {
                    _timerEndTime = DateTime.UtcNow.AddMinutes(_moduleSettings.InitialDurationMinutes);
                    _logger.LogInformation(
                        "Starting new timer with initial duration {InitialDuration} mins. Ends at (UTC): {EndTime}",
                        _moduleSettings.InitialDurationMinutes,
                        _timerEndTime
                    );
                }
                else
                {
                    _logger.LogWarning("Cannot start timer: No persisted time found, end time is in the past, and no initial duration is set.");
                    _isRunning = false;
                    PublishTimerUpdate();
                    return;
                }
            }
            else
            {
                _logger.LogInformation("Timer end time ({EndTimeUtc}) already set and in future. Resuming timer tick.", _timerEndTime);
            }
        }

        _isRunning = true;
        _timer?.Dispose();

        _timer = new Timer(TimerTick, null, TimeSpan.Zero, TimeSpan.FromMilliseconds(250));
        _logger.LogInformation("Timer started/resumed.");
        PublishTimerUpdate();
    }

    /// <summary>
    /// Stops the subathon timer and optionally persists the remaining time.
    /// </summary>
    /// <param name="persistState">Whether to save the current end time for potential resumption later.</param>
    private async Task StopTimerAsync(bool persistState = true)
    {
        if (_isDisposed || !_isRunning)
            return;

        _isRunning = false;
        _timer?.Dispose();
        _timer = null;
        _logger.LogInformation("Timer stopped.");

        if (persistState)
        {
            if (_timerEndTime > DateTime.UtcNow)
            {
                _logger.LogInformation("Persisting end time...");
                _moduleSettings.PersistedEndTimeUtcTicks = _timerEndTime.ToUniversalTime().Ticks;
                await _settingsService.SaveSettingsAsync(_settingsService.CurrentSettings);
                _logger.LogInformation("Persisted EndTime Ticks: {EndTimeTicks}", _moduleSettings.PersistedEndTimeUtcTicks);
            }
            else
            {
                _logger.LogInformation("Timer already expired, clearing any previous persisted end time.");

                if (_moduleSettings.PersistedEndTimeUtcTicks != 0)
                {
                    _moduleSettings.PersistedEndTimeUtcTicks = 0;
                    await _settingsService.SaveSettingsAsync(_settingsService.CurrentSettings);
                }
            }
        }

        PublishTimerUpdate();
    }

    /// <summary>
    /// Adds a specified amount of time to the timer's end time, respecting configured caps.
    /// </summary>
    /// <param name="timeToAdd">The amount of time to add.</param>
    private void AddTime(TimeSpan timeToAdd)
    {
        if (_isDisposed)
            return;

        if (!_isRunning)
        {
            _logger.LogInformation("AddTime called while timer stopped. Attempting to start timer before adding time.");
            _ = Task.Run(StartTimerAsync);
        }

        if (_timerEndTime == DateTime.MinValue)
        {
            _logger.LogWarning("AddTime called but timer end time is not set (MinValue). Ignoring add for {SecondsToAdd}s.", timeToAdd.TotalSeconds);
            return;
        }

        _logger.LogDebug("Attempting to add {SecondsToAdd}s.", timeToAdd.TotalSeconds);

        TimeSpan currentRemaining = _timerEndTime - DateTime.UtcNow;
        if (currentRemaining < TimeSpan.Zero)
            currentRemaining = TimeSpan.Zero;

        TimeSpan cappedTimeToAdd = timeToAdd;
        if (_moduleSettings.MaximumDurationMinutes > 0)
        {
            TimeSpan maxDuration = TimeSpan.FromMinutes(_moduleSettings.MaximumDurationMinutes);
            TimeSpan potentialNewDuration = currentRemaining + timeToAdd;
            if (potentialNewDuration > maxDuration)
            {
                cappedTimeToAdd = maxDuration - currentRemaining;
                if (cappedTimeToAdd < TimeSpan.Zero)
                    cappedTimeToAdd = TimeSpan.Zero;
                _logger.LogInformation(
                    "Time add capped by maximum duration ({MaxMinutes} mins). Actual time added: {ActualSecondsAdded}s",
                    _moduleSettings.MaximumDurationMinutes,
                    cappedTimeToAdd.TotalSeconds
                );
            }
        }

        _timerEndTime = _timerEndTime.Add(cappedTimeToAdd);
        _logger.LogInformation(
            "Successfully added {ActualSecondsAdded}s. New EndTime (UTC): {NewEndTime}",
            cappedTimeToAdd.TotalSeconds,
            _timerEndTime
        );

        PublishTimerUpdate();
    }

    /// <summary>
    /// Callback executed by the timer at regular intervals. Checks remaining time and triggers updates or stops the timer.
    /// </summary>
    private void TimerTick(object? state)
    {
        if (_isDisposed || !_isRunning)
        {
            _timer?.Change(Timeout.Infinite, Timeout.Infinite);
            return;
        }

        TimeSpan remaining = _timerEndTime - DateTime.UtcNow;

        if (remaining <= TimeSpan.Zero)
        {
            _logger.LogInformation("Timer reached zero.");

            _ = Task.Run(() => StopTimerAsync(persistState: true));
        }
        else
        {
            long nowTicks = Stopwatch.GetTimestamp();
            TimeSpan elapsedSinceLastUpdate = Stopwatch.GetElapsedTime(nowTicks, _lastUpdateTimeTicks);

            bool isTimeLow = remaining < TimeSpan.FromSeconds(10);
            if (isTimeLow || elapsedSinceLastUpdate >= s_updateInterval)
            {
                PublishTimerUpdate(remaining);
                _lastUpdateTimeTicks = nowTicks;
            }
        }
    }

    /// <summary>
    /// Publishes the current state of the subathon timer via the messenger.
    /// </summary>
    /// <param name="remaining">Optional pre-calculated remaining time. If null, it will be calculated.</param>
    private void PublishTimerUpdate(TimeSpan? remaining = null)
    {
        if (_isDisposed)
            return;

        remaining ??= _isRunning ? (_timerEndTime - DateTime.UtcNow) : TimeSpan.Zero;
        if (remaining < TimeSpan.Zero)
            remaining = TimeSpan.Zero;

        int remainingSeconds = (int)Math.Round(remaining.Value.TotalSeconds);

        _logger.LogTrace("Publishing timer update. IsRunning={IsRunning}, RemainingSeconds={Seconds}", _isRunning, remainingSeconds);

        SubTimerUpdateEvent updateEvent = new() { RemainingSeconds = remainingSeconds, IsRunning = _isRunning };
        _messenger.Send(new NewEventMessage(updateEvent));
    }

    /// <summary>
    /// Calculates the time to add for a subscription event based on settings.
    /// </summary>
    private TimeSpan GetTimeForSubscription(SubscriptionEvent sub)
    {
        if (!_moduleSettings.AddTimeForSubs)
            return TimeSpan.Zero;

        double secondsToAdd;
        if (sub.IsGift)
        {
            secondsToAdd = _moduleSettings.SecondsPerGiftSub * sub.GiftCount;
            _logger.LogDebug("Calculated {Seconds}s for {GiftCount} gift subs.", secondsToAdd, sub.GiftCount);
        }
        else
        {
            secondsToAdd = sub.Tier?.ToLowerInvariant() switch
            {
                "tier 2" or "2000" => _moduleSettings.SecondsPerSubTier2,
                "tier 3" or "3000" => _moduleSettings.SecondsPerSubTier3,
                _ => _moduleSettings.SecondsPerSubTier1,
            };
            _logger.LogDebug("Calculated {Seconds}s for non-gift sub (Tier: {Tier}).", secondsToAdd, sub.Tier ?? "Unknown/T1/Prime");
        }

        return TimeSpan.FromSeconds(secondsToAdd);
    }

    /// <summary>
    /// Calculates the time to add for a donation event (Bits or Monetary) based on settings.
    /// </summary>
    private TimeSpan GetTimeForDonation(DonationEvent donation)
    {
        double secondsToAdd = 0;
        if (donation.Type == DonationType.Bits)
        {
            if (!_moduleSettings.AddTimeForBits)
                return TimeSpan.Zero;

            if (_moduleSettings.BitsPerSecond > 0)
            {
                secondsToAdd = (double)donation.Amount / _moduleSettings.BitsPerSecond;
                _logger.LogDebug("Calculated {Seconds}s for {Amount} bits.", secondsToAdd, donation.Amount);
            }
        }
        else
        {
            if (!_moduleSettings.AddTimeForDonations)
                return TimeSpan.Zero;

            if (_moduleSettings.AmountPerSecond > 0)
            {
                secondsToAdd = (double)(donation.Amount / _moduleSettings.AmountPerSecond);
                _logger.LogDebug(
                    "Calculated {Seconds}s for {Amount} {Currency} donation (currency ignored).",
                    secondsToAdd,
                    donation.Amount,
                    donation.Currency ?? "N/A"
                );
            }
        }

        return TimeSpan.FromSeconds(secondsToAdd);
    }

    /// <summary>
    /// Cleans up resources, stops the timer, persists state, and unregisters messages.
    /// </summary>
    public void Dispose()
    {
        if (_isDisposed)
            return;
        _isDisposed = true;
        _logger.LogInformation("Disposing...");
        _settingsService.SettingsUpdated -= OnSettingsUpdated;
        _messenger.UnregisterAll(this);

        try
        {
            Task.Run(() => StopTimerAsync(persistState: true)).Wait(TimeSpan.FromSeconds(2));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception during StopTimerAsync wait in Dispose.");
        }

        _timer?.Dispose();
        _logger.LogInformation("Dispose finished.");
        GC.SuppressFinalize(this);
    }
}

// TODO: Consider placing this in a shared Models/Events namespace if used across modules:
/// <summary>
/// Represents an update to the state of the subathon timer.
/// </summary>
public class SubTimerUpdateEvent : BaseEvent
{
    /// <summary>
    /// Gets the remaining time in seconds, rounded to the nearest second.
    /// </summary>
    public int RemainingSeconds { get; init; }

    /// <summary>
    /// Gets a value indicating whether the timer is currently running.
    /// </summary>
    public bool IsRunning { get; init; }

    /// <summary>
    /// Gets the specific type identifier for this event.
    /// </summary>
    public string EventType { get; init; }

    /// <summary>
    /// Initializes a new instance of the <see cref="SubTimerUpdateEvent"/> class.
    /// </summary>
    public SubTimerUpdateEvent()
    {
        Platform = "Module";
        EventType = nameof(SubTimerUpdateEvent);
    }
}
