using System.Globalization;
using System.Speech.Synthesis;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using StreamWeaver.Core.Messaging;
using StreamWeaver.Core.Models.Events;
using StreamWeaver.Core.Models.Events.Messages;
using StreamWeaver.Core.Models.Settings;
using StreamWeaver.Core.Services.Settings;

namespace StreamWeaver.Core.Services.Tts;

/// <summary>
/// Provides Text-to-Speech (TTS) functionality using the Windows built-in SpeechSynthesizer.
/// Listens for application events and speaks relevant information based on user settings.
/// </summary>
public sealed partial class WindowsTtsService : ITtsService, IRecipient<NewEventMessage>, IDisposable
{
    private readonly SpeechSynthesizer? _synthesizer;
    private readonly ISettingsService _settingsService;
    private readonly IMessenger _messenger;
    private readonly ILogger<WindowsTtsService> _logger;
    private bool _isDisposed = false;

    /// <summary>
    /// Initializes a new instance of the <see cref="WindowsTtsService"/> class.
    /// Attempts to initialize the SpeechSynthesizer and subscribes to events.
    /// </summary>
    /// <param name="settingsService">The service for accessing TTS settings.</param>
    /// <param name="messenger">The application messenger for receiving events.</param>
    /// <param name="logger">The logger instance.</param>
    /// <exception cref="ArgumentNullException">Thrown if settingsService, messenger, or logger is null.</exception>
    public WindowsTtsService(ISettingsService settingsService, IMessenger messenger, ILogger<WindowsTtsService> logger)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _messenger = messenger ?? throw new ArgumentNullException(nameof(messenger));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        try
        {
            _logger.LogInformation("Initializing SpeechSynthesizer...");
            _synthesizer = new SpeechSynthesizer();
            ApplySettings();
            _settingsService.SettingsUpdated += OnSettingsUpdated;
            _logger.LogInformation("SpeechSynthesizer initialized and settings applied.");
        }
        catch (PlatformNotSupportedException pnsEx)
        {
            _logger.LogCritical(
                pnsEx,
                "Failed to initialize SpeechSynthesizer: Platform Not Supported. System.Speech might require Desktop Runtime components. TTS will be disabled."
            );
            // Notify user that TTS is unavailable (e.g., missing Windows components?)
            // TODO: Implement user notification mechanism
            _synthesizer = null;
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Failed to initialize SpeechSynthesizer. TTS will be disabled.");
            // TODO: Implement user notification mechanism
            _synthesizer = null;
        }

        // Subscribe to new events via the messenger *only* if synthesizer initialized successfully
        if (_synthesizer != null)
        {
            _messenger.Register<NewEventMessage>(this);
            _logger.LogInformation("Registered for NewEventMessage.");
        }
        else
        {
            _logger.LogWarning("TTS Service will not register for messages as synthesizer failed to initialize.");
        }
    }

    /// <summary>
    /// Applies the current TTS settings (Volume, Rate, Voice) to the SpeechSynthesizer instance.
    /// </summary>
    private void ApplySettings()
    {
        if (_synthesizer == null)
        {
            _logger.LogTrace("ApplySettings skipped: Synthesizer not available.");
            return;
        }

        try
        {
            TtsSettings settings = _settingsService.CurrentSettings.TextToSpeech;
            _logger.LogDebug(
                "Applying TTS Settings: Vol={Volume}, Rate={Rate}, Voice='{Voice}'",
                settings.Volume,
                settings.Rate,
                settings.SelectedVoice
            );
            SetVolume(settings.Volume);
            SetRate(settings.Rate);
            if (!string.IsNullOrEmpty(settings.SelectedVoice))
            {
                SetVoice(settings.SelectedVoice);
            }
            else
            {
                _logger.LogDebug("No specific voice selected in settings, using system default.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error applying TTS settings.");
        }
    }

    /// <summary>
    /// Event handler for when application settings are updated. Re-applies TTS settings.
    /// </summary>
    private void OnSettingsUpdated(object? sender, EventArgs e)
    {
        _logger.LogInformation("Settings updated, re-applying TTS settings.");
        ApplySettings();
    }

    /// <summary>
    /// Asynchronously retrieves a list of enabled, installed speech synthesis voices.
    /// </summary>
    /// <returns>A Task resulting in an enumerable collection of voice names.</returns>
    public Task<IEnumerable<string>> GetInstalledVoicesAsync()
    {
        if (_synthesizer == null)
        {
            _logger.LogWarning("Cannot get installed voices: Synthesizer not available.");
            return Task.FromResult(Enumerable.Empty<string>());
        }

        return Task.Run(() =>
        {
            try
            {
                _logger.LogDebug("Fetching installed and enabled voices...");
                var voices = _synthesizer.GetInstalledVoices().Where(v => v.Enabled).Select(v => v.VoiceInfo.Name).ToList();
                _logger.LogDebug("Found {VoiceCount} enabled voices.", voices.Count);
                return voices.AsEnumerable();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting installed voices.");
                return [];
            }
        });
    }

    /// <summary>
    /// Sets the active voice for the SpeechSynthesizer.
    /// </summary>
    /// <param name="voiceName">The name of the voice to select.</param>
    public void SetVoice(string voiceName)
    {
        if (_synthesizer == null)
            return;
        if (string.IsNullOrWhiteSpace(voiceName))
        {
            _logger.LogWarning("SetVoice called with null or empty voice name.");
            return;
        }

        try
        {
            _logger.LogDebug("Attempting to set TTS voice to: {VoiceName}", voiceName);
            // Check if voice exists and is enabled before setting
            // Note: GetInstalledVoices can be slow, consider caching if needed, but settings changes aren't frequent.
            bool voiceExists = _synthesizer
                .GetInstalledVoices()
                .Any(v => v.Enabled && v.VoiceInfo.Name.Equals(voiceName, StringComparison.OrdinalIgnoreCase));
            if (voiceExists)
            {
                _synthesizer.SelectVoice(voiceName);
                _logger.LogInformation("TTS Voice set to: {VoiceName}", voiceName);
            }
            else
            {
                _logger.LogWarning("TTS Voice '{VoiceName}' not found or is not enabled.", voiceName);
                // Optionally, fall back to the default voice explicitly if the desired one isn't found.
                // _synthesizer.SelectVoice(null); // Uncomment to select default if desired voice is missing
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting TTS voice to '{VoiceName}'.", voiceName);
        }
    }

    /// <summary>
    /// Sets the volume for the SpeechSynthesizer.
    /// </summary>
    /// <param name="volume">The volume level (0-100).</param>
    public void SetVolume(int volume)
    {
        if (_synthesizer == null)
            return;
        try
        {
            int clampedVolume = Math.Clamp(volume, 0, 100);
            _synthesizer.Volume = clampedVolume;
            _logger.LogTrace("TTS Volume set to: {Volume}", clampedVolume);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting TTS volume to {Volume}.", volume);
        }
    }

    /// <summary>
    /// Sets the speaking rate for the SpeechSynthesizer.
    /// </summary>
    /// <param name="rate">The rate (-10 to 10).</param>
    public void SetRate(int rate)
    {
        if (_synthesizer == null)
            return;
        try
        {
            int clampedRate = Math.Clamp(rate, -10, 10);
            _synthesizer.Rate = clampedRate;
            _logger.LogTrace("TTS Rate set to: {Rate}", clampedRate);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting TTS rate to {Rate}.", rate);
        }
    }

    /// <summary>
    /// Asynchronously speaks the provided text using the configured synthesizer settings.
    /// Cancels any previously ongoing speech.
    /// </summary>
    /// <param name="textToSpeak">The text to synthesize and speak.</param>
    /// <returns>A Task representing the asynchronous operation.</returns>
    public async Task SpeakAsync(string textToSpeak)
    {
        if (_synthesizer == null)
        {
            _logger.LogWarning("SpeakAsync called but synthesizer is not available.");
            return;
        }

        if (string.IsNullOrWhiteSpace(textToSpeak))
        {
            _logger.LogTrace("SpeakAsync called with empty text, skipping.");
            return;
        }

        // Sanitize text slightly for better speech synthesis flow
        string sanitizedText = SanitizeForSpeech(textToSpeak);
        if (string.IsNullOrWhiteSpace(sanitizedText))
        {
            _logger.LogDebug("Text became empty after sanitization, skipping speech. Original: '{OriginalText}'", textToSpeak);
            return;
        }

        // Use Task.Run to ensure speech synthesis occurs off the main thread,
        // preventing blocking, especially when called from event handlers.
        await Task.Run(() =>
        {
            try
            {
                // Cancel previous speech before starting new one to prevent overlap/queuing issues.
                // Consider PromptBuilder/Speak(Prompt) for more complex queuing later on (tbd, don't know when I'll do that).
                if (_synthesizer.State == SynthesizerState.Speaking)
                {
                    _logger.LogTrace("Cancelling previous speech before speaking new text.");
                    _synthesizer.SpeakAsyncCancelAll();
                    // Small delay might be needed after cancellation before starting new speech, test if required.
                    // Task.Delay(50).Wait();
                }

                _logger.LogInformation("Speaking: \"{SanitizedText}\"", sanitizedText);
                _synthesizer.SpeakAsync(sanitizedText); // Fire and forget
            }
            catch (ObjectDisposedException)
            {
                _logger.LogWarning("SpeakAsync failed: SpeechSynthesizer was disposed.");
            }
            catch (InvalidOperationException opEx)
            {
                _logger.LogError(opEx, "Invalid operation during SpeakAsync, synthesizer might be in an unexpected state.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during SpeakAsync execution: {ErrorMessage}", ex.Message);
            }
        });
    }

    /// <summary>
    /// Performs basic sanitization on text intended for speech synthesis.
    /// Removes URLs and excessive punctuation.
    /// </summary>
    /// <param name="text">The input text.</param>
    /// <returns>Sanitized text suitable for speech, or empty string if input was null/empty.</returns>
    private static string SanitizeForSpeech(string text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        // Remove URLs to prevent reading them out literally
        text = UrlRegex().Replace(text, " link ");

        // Collapse multiple exclamation marks or question marks to single ones
        text = ExcessiveExclamationRegex().Replace(text, "!");
        text = ExcessiveQuestionMarkRegex().Replace(text, "?");

        // Replace multiple spaces created by replacements with single spaces
        text = ConsecutiveSpacesRegex().Replace(text, " ");

        // Consider removing other noisy characters or symbols if needed
        return text.Trim();
    }

    /// <summary>
    /// Receives new application events via the messenger.
    /// </summary>
    /// <param name="message">The message containing the event data.</param>
    public void Receive(NewEventMessage message) => ProcessEventForTts(message.Value);

    /// <summary>
    /// Processes a received application event to determine if TTS should be triggered
    /// based on current settings and event type/content.
    /// </summary>
    /// <param name="eventData">The event data to process.</param>
    public void ProcessEventForTts(BaseEvent eventData)
    {
        if (_synthesizer == null)
            return;
        ArgumentNullException.ThrowIfNull(eventData);

        TtsSettings ttsSettings = _settingsService.CurrentSettings.TextToSpeech;
        if (!ttsSettings.Enabled)
        {
            _logger.LogTrace("TTS processing skipped: TTS is disabled in settings.");
            return;
        }

        string? textToSpeak = null;
        string format;

        switch (eventData)
        {
            case DonationEvent de:
                if (ShouldReadDonation(de, ttsSettings))
                {
                    format = de.Type switch
                    {
                        DonationType.Bits => ttsSettings.BitsMessageFormat,
                        DonationType.SuperChat or DonationType.SuperSticker => ttsSettings.SuperChatMessageFormat,
                        _ => ttsSettings.DonationMessageFormat,
                    };
                    textToSpeak = FormatMessage(format, de);
                    _logger.LogDebug(
                        "Processing DonationEvent for TTS. Type: {DonationType}, Amount: {Amount} {Currency}",
                        de.Type,
                        de.Amount,
                        de.Currency
                    );
                }

                break;

            case SubscriptionEvent se:
                if (ttsSettings.ReadTwitchSubs)
                {
                    if (se.IsGift)
                    {
                        // Gift count might not be reliably available for single vs bomb distinction here.
                        // Use separate formats if available, otherwise use general gift format.
                        format = se.GiftCount > 1 ? ttsSettings.GiftBombMessageFormat : ttsSettings.GiftSubMessageFormat;
                    }
                    else
                    {
                        // Use resub format if cumulative months > 0 (TwitchLib might send 0 for first month of resub message?)
                        // Treat CumulativeMonths = 1 also as potentially new/first resub announcement.
                        format = se.CumulativeMonths > 0 ? ttsSettings.ResubMessageFormat : ttsSettings.NewSubMessageFormat;
                    }

                    textToSpeak = FormatMessage(format, se);
                    _logger.LogDebug("Processing SubscriptionEvent for TTS. IsGift: {IsGift}, Months: {Months}", se.IsGift, se.CumulativeMonths);
                }

                break;

            case MembershipEvent me:
                if (ttsSettings.ReadYouTubeMemberships)
                {
                    // Months > 0 indicates a milestone message typically
                    format = me.MilestoneMonths > 0 ? ttsSettings.MemberMilestoneFormat : ttsSettings.NewMemberMessageFormat;
                    textToSpeak = FormatMessage(format, me);
                    _logger.LogDebug("Processing MembershipEvent for TTS. Months: {MilestoneMonths}", me.MilestoneMonths);
                }

                break;

            case FollowEvent fe:
                if (ttsSettings.ReadFollows)
                {
                    format = ttsSettings.FollowMessageFormat;
                    textToSpeak = FormatMessage(format, fe);
                    _logger.LogDebug("Processing FollowEvent for TTS. Username: {Username}", fe.Username);
                }

                break;

            case RaidEvent re:
                if (ttsSettings.ReadRaids && re.ViewerCount >= ttsSettings.MinimumRaidViewersToRead) // Check viewer threshold
                {
                    format = ttsSettings.RaidMessageFormat;
                    textToSpeak = FormatMessage(format, re);
                    _logger.LogDebug("Processing RaidEvent for TTS. Raider: {Username}, Viewers: {ViewerCount}", re.RaiderUsername, re.ViewerCount);
                }

                break;
            // Add cases for HostEvent, etc., if needed
            default:
                _logger.LogTrace("Ignoring event type {EventType} for TTS.", eventData.GetType().Name);
                break; // Ignore other event types
        }

        // If text was generated, speak it
        if (!string.IsNullOrWhiteSpace(textToSpeak))
        {
            // SpeakAsync handles threading internally
            _ = SpeakAsync(textToSpeak);
        }
        else
        {
            _logger.LogTrace("No text generated for event type {EventType}, TTS skipped.", eventData.GetType().Name);
        }
    }

    /// <summary>
    /// Determines if a donation event should be read aloud based on type and configured minimum amounts.
    /// </summary>
    /// <param name="donation">The donation event.</param>
    /// <param name="settings">The current TTS settings.</param>
    /// <returns>True if the donation meets the criteria to be read, false otherwise.</returns>
    private static bool ShouldReadDonation(DonationEvent donation, TtsSettings settings) =>
        donation.Type switch
        {
            DonationType.Streamlabs or DonationType.Other => settings.ReadStreamlabsDonations
                && donation.Amount >= (decimal)settings.MinimumDonationAmountToRead,
            DonationType.SuperChat or DonationType.SuperSticker => settings.ReadSuperChats
                && donation.Amount >= (decimal)settings.MinimumSuperChatAmountToRead, // Group SuperSticker with SuperChat
            DonationType.Bits => settings.ReadTwitchBits && donation.Amount >= settings.MinimumBitAmountToRead,
            _ => false, // Ignore unknown types
        };

    /// <summary>
    /// Formats a message string using placeholders based on the properties of the provided event data.
    /// Placeholders are in the format {PropertyName}. Case-insensitive.
    /// </summary>
    /// <param name="format">The format string containing placeholders.</param>
    /// <param name="eventData">The event data object containing values for the placeholders.</param>
    /// <returns>The formatted string, or empty string if the format is empty.</returns>
    private string FormatMessage(string format, BaseEvent eventData)
    {
        if (string.IsNullOrWhiteSpace(format))
        {
            _logger.LogWarning("FormatMessage called with empty format string for event type {EventType}.", eventData.GetType().Name);
            return string.Empty;
        }

        string processedMessage = format;

        // Use Regex to find all placeholders like {word}
        try
        {
            processedMessage = PlaceholderRegex()
                .Replace(
                    processedMessage,
                    match =>
                    {
                        string placeholder = match.Groups[1].Value.ToLowerInvariant(); // Get placeholder name (lowercase)
                        object? propertyValue = null;
                        string? valueString = null;

                        // Handle special placeholders first
                        switch (placeholder)
                        {
                            case "amount":
                                if (eventData is DonationEvent de)
                                {
                                    valueString = FormatAmountForSpeech(de.Amount, de.Currency);
                                }
                                else if (eventData is SubscriptionEvent se && se.IsGift) // Use GiftCount for amount in GiftBomb
                                {
                                    // Use GiftCount for GiftBomb, or ViewerCount for Raid
                                    valueString = $"{se.GiftCount:N0}";
                                }
                                else if (eventData is RaidEvent re)
                                {
                                    valueString = $"{re.ViewerCount:N0}";
                                }
                                // else: other events might not have a relevant amount
                                break;

                            case "message":
                                if (eventData is DonationEvent de_msg)
                                    valueString = de_msg.RawMessage;
                                else if (eventData is SubscriptionEvent se_msg)
                                    valueString = se_msg.Message;
                                else if (eventData is MembershipEvent me_msg)
                                    valueString = string.Join(" ", me_msg.ParsedMessage.OfType<TextSegment>().Select(ts => ts.Text));
                                // else: other events might not have a user message
                                break;

                            case "recipient": // Alias for SubscriptionEvent.RecipientUsername
                                if (eventData is SubscriptionEvent se_r)
                                    valueString = se_r.RecipientUsername;
                                break;

                            case "gifter": // Alias for SubscriptionEvent.Username when IsGift is true
                                if (eventData is SubscriptionEvent { IsGift: true } se_g)
                                    valueString = se_g.Username;
                                break;

                            // Add more special cases/aliases as needed (e.g., months, tier)
                        }

                        // If not handled by special cases, use reflection for standard properties
                        if (valueString == null)
                        {
                            System.Reflection.PropertyInfo? propertyInfo = eventData
                                .GetType()
                                .GetProperty(
                                    placeholder,
                                    System.Reflection.BindingFlags.IgnoreCase
                                        | System.Reflection.BindingFlags.Public
                                        | System.Reflection.BindingFlags.Instance
                                );
                            if (propertyInfo != null)
                            {
                                propertyValue = propertyInfo.GetValue(eventData);
                                valueString = propertyValue?.ToString() ?? string.Empty;
                            }
                            else
                            {
                                _logger.LogWarning(
                                    "TTS Format placeholder '{{{Placeholder}}}' not found on event type {EventType}.",
                                    placeholder,
                                    eventData.GetType().Name
                                );
                                valueString = string.Empty; // Placeholder not found
                            }
                        }

                        // Basic sanitization for the final value (especially for user messages)
                        return SanitizeForSpeech(valueString ?? string.Empty);
                    }
                );

            // Remove potential consecutive spaces resulting from empty replacements or sanitization
            processedMessage = ConsecutiveSpacesRegex().Replace(processedMessage, " ");
            return processedMessage.Trim();
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error processing TTS format string for event type {EventType}. Format: '{FormatString}'",
                eventData.GetType().Name,
                format
            );
            return string.Empty; // Return empty on error to avoid speaking incorrect text
        }
    }

    /// <summary>
    /// Formats a monetary amount or bit count for speech.
    /// </summary>
    /// <param name="amount">The numeric amount.</param>
    /// <param name="currency">The currency code (e.g., "USD", "EUR", "Bits").</param>
    /// <returns>A string formatted for speech.</returns>
    private static string FormatAmountForSpeech(decimal amount, string currency)
    {
        if (string.Equals(currency, "Bits", StringComparison.OrdinalIgnoreCase))
        {
            // Format bits with no decimal places
            return $"{amount:N0} {(amount == 1 ? "bit" : "bits")}";
        }
        else
        {
            // Format monetary amount with two decimal places and full currency name
            string formattedAmount = amount.ToString("N2", CultureInfo.InvariantCulture); // Use invariant culture for consistency
            string fullCurrencyName = GetFullCurrencyName(currency);
            // Simple pluralization for common currencies
            if (amount != 1.00m && (fullCurrencyName.EndsWith("Dollar") || fullCurrencyName.EndsWith("Euro") || fullCurrencyName.EndsWith("Pound")))
            {
                fullCurrencyName += "s";
            }

            return $"{formattedAmount} {fullCurrencyName}";
        }
    }

    /// <summary>
    /// Gets the full name for a given currency code.
    /// </summary>
    /// <param name="code">The currency code (e.g., "USD").</param>
    /// <returns>The full currency name (e.g., "US Dollar") or the original code if not found.</returns>
    private static string GetFullCurrencyName(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return string.Empty;
        // Add more mappings as needed
        return code.ToUpperInvariant() switch
        {
            "USD" => "US Dollar",
            "EUR" => "Euro",
            "GBP" => "Pound",
            "CAD" => "Canadian Dollar",
            "AUD" => "Australian Dollar",
            "JPY" => "Japanese Yen",
            // Add more common currencies
            _ => code, // Return the code itself if no mapping found
        };
    }

    /// <summary>
    /// Disposes the service, unregistering from events and disposing the SpeechSynthesizer.
    /// </summary>
    public void Dispose()
    {
        if (_isDisposed)
            return;
        _isDisposed = true;
        _logger.LogInformation("Disposing TTS Service...");
        if (_synthesizer != null)
        {
            _messenger.UnregisterAll(this);
            _settingsService.SettingsUpdated -= OnSettingsUpdated;
            try
            {
                _synthesizer.SpeakAsyncCancelAll();
                _synthesizer.Dispose();
                _logger.LogInformation("SpeechSynthesizer disposed.");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Exception during SpeechSynthesizer disposal.");
            }
        }

        _logger.LogInformation("TTS Service disposed.");
        GC.SuppressFinalize(this);
    }

    [GeneratedRegex(@"\{(\w+)\}", RegexOptions.Compiled)]
    private static partial Regex PlaceholderRegex();

    [GeneratedRegex(@"\s{2,}", RegexOptions.Compiled)]
    private static partial Regex ConsecutiveSpacesRegex();

    [GeneratedRegex(
        @"(http|ftp|https):\/\/([\w_-]+(?:(?:\.[\w_-]+)+))([\w.,@?^=%&:\/~+#-]*[\w@?^=%&\/~+#-])?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled
    )]
    private static partial Regex UrlRegex();

    [GeneratedRegex(@"[!]{2,}", RegexOptions.Compiled)]
    private static partial Regex ExcessiveExclamationRegex();

    [GeneratedRegex(@"[?]{2,}", RegexOptions.Compiled)]
    private static partial Regex ExcessiveQuestionMarkRegex();
}
