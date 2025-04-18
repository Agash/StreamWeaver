using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using StreamWeaver.Core.Models.Events;
using StreamWeaver.Core.Models.Events.Messages;
using StreamWeaver.Core.Models.Settings;
using TTSTextNormalization.Abstractions;

namespace StreamWeaver.Core.Services.Tts;

/// <summary>
/// Service responsible for formatting BaseEvent data into speakable strings
/// based on user-defined templates and normalizing the text for TTS engines.
/// </summary>
public partial class TtsFormattingService
{
    private readonly ILogger<TtsFormattingService> _logger;
    private readonly ITextNormalizer _textNormalizer;

    // Keep Regex definitions here as they are part of formatting placeholders
    [GeneratedRegex(@"\{(\w+)\}", RegexOptions.Compiled | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex PlaceholderRegex();

    [GeneratedRegex(@"\s{2,}", RegexOptions.Compiled, matchTimeoutMilliseconds: 100)]
    private static partial Regex ConsecutiveSpacesRegex();

    public TtsFormattingService(ILogger<TtsFormattingService> logger, ITextNormalizer textNormalizer)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _textNormalizer = textNormalizer ?? throw new ArgumentNullException(nameof(textNormalizer));
        _logger.LogInformation("TtsFormattingService Initialized.");
    }

    /// <summary>
    /// Formats an event into a speakable string based on settings, then normalizes it.
    /// </summary>
    /// <param name="eventData">The event to format.</param>
    /// <param name="settings">The current TTS settings containing format templates.</param>
    /// <returns>A normalized, speakable string, or null if the event shouldn't be spoken.</returns>
    public string? FormatAndNormalizeEvent(BaseEvent eventData, TtsSettings settings)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        ArgumentNullException.ThrowIfNull(settings);

        string? format = GetFormatString(eventData, settings);
        if (format is null)
        {
            _logger.LogTrace("No format configured or event filtered out for type {EventType}, formatting skipped.", eventData.GetType().Name);
            return null;
        }

        string rawFormattedMessage = FormatMessage(format, eventData);
        if (string.IsNullOrWhiteSpace(rawFormattedMessage))
        {
            _logger.LogTrace("Empty message generated after formatting for event type {EventType}, normalization skipped.", eventData.GetType().Name);
            return null;
        }

        // Normalize the formatted message using the injected normalizer
        string normalizedMessage = _textNormalizer.Normalize(rawFormattedMessage);

        _logger.LogDebug("Formatted TTS message: \"{FormattedMessage}\", Normalized: \"{NormalizedMessage}\"", rawFormattedMessage, normalizedMessage);

        return normalizedMessage;
    }


    // Helper method to determine the correct format string based on event type and settings
    private string? GetFormatString(BaseEvent eventData, TtsSettings settings) =>
        eventData switch
        {
            DonationEvent de when ShouldReadDonation(de, settings) => de.Type switch
            {
                DonationType.Bits => settings.BitsMessageFormat,
                DonationType.SuperChat or DonationType.SuperSticker => settings.SuperChatMessageFormat,
                _ => settings.DonationMessageFormat, // Includes Streamlabs and Other
            },
            SubscriptionEvent se when settings.ReadTwitchSubs => (se.IsGift, se.GiftCount > 1, se.CumulativeMonths > 0) switch
            {
                (true, true, _) => settings.GiftBombMessageFormat, // Gift bomb takes precedence
                (true, false, _) => settings.GiftSubMessageFormat, // Single gift sub
                (false, _, > 0) => settings.ResubMessageFormat, // Resub (not a gift, has months) - Changed condition slightly
                (false, _, <= 0) => settings.NewSubMessageFormat, // New sub (not a gift, 0 or fewer months)
            },
            MembershipEvent me when settings.ReadYouTubeMemberships => me.MilestoneMonths > 0
                ? settings.MemberMilestoneFormat
                : settings.NewMemberMessageFormat,
            FollowEvent fe when settings.ReadFollows => settings.FollowMessageFormat,
            RaidEvent re when settings.ReadRaids && re.ViewerCount >= settings.MinimumRaidViewersToRead => settings.RaidMessageFormat,
            // Default case: Event type not configured or filtered out
            _ => null,
        };

    // Keep ShouldReadDonation helper as it encapsulates logic nicely
    private static bool ShouldReadDonation(DonationEvent donation, TtsSettings settings) =>
        donation.Type switch
        {
            DonationType.Streamlabs or DonationType.Other => settings.ReadStreamlabsDonations
                && donation.Amount >= (decimal)settings.MinimumDonationAmountToRead,
            DonationType.SuperChat or DonationType.SuperSticker => settings.ReadSuperChats
                && donation.Amount >= (decimal)settings.MinimumSuperChatAmountToRead,
            DonationType.Bits => settings.ReadTwitchBits && donation.Amount >= settings.MinimumBitAmountToRead,
            _ => false, // Unknown donation type should not be read
        };

    // ## REFACTORED FormatMessage using Pattern Matching ##
    private string FormatMessage(string format, BaseEvent eventData)
    {
        // Pre-validate format string
        if (string.IsNullOrWhiteSpace(format))
        {
            _logger.LogWarning("FormatMessage called with empty format string for event type {EventType}.", eventData.GetType().Name);
            return string.Empty;
        }

        string LogUnhandledPlaceholderAndReturnEmpty(BaseEvent? evt, string? ph)
        {
            _logger.LogWarning(
                "TTS Format placeholder '{{{Placeholder}}}' not handled or not applicable for event type {EventType}.",
                ph, // Use captured placeholder
                evt?.GetType().Name
            );
            return string.Empty; // Return empty string for unhandled placeholders
        }

        try
        {
            string processedMessage = PlaceholderRegex()
                .Replace(
                    format,
                    match =>
                    {
                        // Extract placeholder name (case-insensitive handled by switch)
                        string placeholder = match.Groups[1].Value; // Keep original case for logging if needed, lowercase in switch

                        // Use a switch expression on a tuple of (eventData, placeholderName.ToLowerInvariant())
                        // This allows matching both event type and placeholder simultaneously.
                        string? valueString = (eventData, placeholder.ToLowerInvariant()) switch
                        {
                            // 'username' placeholder (most common)
                            (DonationEvent { Username: var uname }, "username") => uname,
                            (SubscriptionEvent { Username: var uname }, "username") => uname,
                            (MembershipEvent { Username: var uname }, "username") => uname,
                            (FollowEvent { Username: var uname }, "username") => uname,
                            (RaidEvent { RaiderUsername: var rname }, "username") => rname, // Note different property

                            // 'amount' placeholder
                            (DonationEvent de, "amount") => $"{de.Amount}|{de.Currency}", // Pass amount AND currency to normalizer
                            (SubscriptionEvent { IsGift: true } se, "amount") => $"{se.GiftCount:N0}", // Gift count for subs
                            (RaidEvent re, "amount") => $"{re.ViewerCount:N0}", // Viewer count for raids

                            // 'message' placeholder
                            (DonationEvent { RawMessage: var msg }, "message") => msg,
                            (SubscriptionEvent { Message: var msg }, "message") => msg,
                            (MembershipEvent me, "message") => string.Join(" ", me.ParsedMessage.OfType<TextSegment>().Select(ts => ts.Text)), // Special handling

                            // 'recipient' placeholder (Gift Subs)
                            (SubscriptionEvent { RecipientUsername: var rcp }, "recipient") => rcp,

                            // 'gifter' placeholder (Gift Subs) - Matches only if IsGift is true
                            (SubscriptionEvent { IsGift: true, Username: var gname }, "gifter") => gname,

                            // 'months' placeholder
                            (SubscriptionEvent { CumulativeMonths: var m }, "months") => m.ToString(),
                            (MembershipEvent { MilestoneMonths: { } mm }, "months") => mm.ToString(), // Use {} pattern for not-null check

                            // 'tier' placeholder
                            (SubscriptionEvent { Tier: var t }, "tier") => t,
                            (MembershipEvent { LevelName: var lvl }, "tier") => lvl,

                            // --- Default Case ---
                            // Placeholder not explicitly handled for this event type or unknown placeholder
                            var (evt, ph) => LogUnhandledPlaceholderAndReturnEmpty(evt, ph),
                        };

                        // Return the raw value string. Normalization happens *after* all replacements.
                        return valueString ?? string.Empty;
                    }
                );

            // Final cleanup of potential extra spaces after replacements
            processedMessage = ConsecutiveSpacesRegex().Replace(processedMessage, " ");
            return processedMessage.Trim();
        }
        catch (RegexMatchTimeoutException rex)
        {
            _logger.LogError(rex, "Regex timed out during TTS format processing for event type {EventType}.", eventData.GetType().Name);
            return format; // Return original format on timeout (will be normalized later)
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error processing TTS format string for event type {EventType}. Format: '{FormatString}'",
                eventData.GetType().Name,
                format
            );
            // Return empty string or a generic error message on failure
            return string.Empty;
            // Alternative: return "Error formatting message.";
        }
    }
}
