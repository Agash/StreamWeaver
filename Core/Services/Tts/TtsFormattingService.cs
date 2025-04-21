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
public partial class TtsFormattingService(ILogger<TtsFormattingService> logger, ITextNormalizer textNormalizer)
{
    private readonly ILogger<TtsFormattingService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly ITextNormalizer _textNormalizer = textNormalizer ?? throw new ArgumentNullException(nameof(textNormalizer));

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
        _logger.LogDebug(
            "Formatted TTS message: \"{FormattedMessage}\", Normalized: \"{NormalizedMessage}\"",
            rawFormattedMessage,
            normalizedMessage
        );
        return normalizedMessage;
    }

    // Helper method to determine the correct format string based on event type and settings
    private static string? GetFormatString(BaseEvent eventData, TtsSettings settings) =>
        eventData switch
        {
            // Handle Donations based on type and minimum amounts
            DonationEvent de when ShouldReadDonation(de, settings) => de.Type switch
            {
                DonationType.Bits => settings.BitsMessageFormat,
                DonationType.SuperChat or DonationType.SuperSticker => settings.SuperChatMessageFormat,
                _ => settings.DonationMessageFormat, // Includes Streamlabs and Other
            },

            // Handle Twitch Subscriptions based on type (new, resub, gift, bomb)
            SubscriptionEvent se when settings.ReadTwitchSubs => (se.IsGift, se.GiftCount > 1, se.CumulativeMonths > 0) switch
            {
                (true, true, _) => settings.GiftBombMessageFormat, // Gift bomb takes precedence
                (true, false, _) => settings.GiftSubMessageFormat, // Single gift sub
                (false, _, true) => settings.ResubMessageFormat, // Resub (not a gift, has months)
                (false, _, false) => settings.NewSubMessageFormat, // New sub (not a gift, 0 or fewer months)
            },

            // Handle YouTube Memberships based on type and thresholds
            MembershipEvent me => me.MembershipType switch
            {
                MembershipEventType.New when settings.ReadYouTubeNewMembers => settings.NewMemberMessageFormat,
                MembershipEventType.Milestone when settings.ReadYouTubeMilestones && me.MilestoneMonths >= settings.MinimumMilestoneMonthsToRead =>
                    settings.MemberMilestoneFormat,
                MembershipEventType.GiftPurchase when settings.ReadYouTubeGiftPurchases && me.GiftCount >= settings.MinimumGiftCountToRead =>
                    settings.GiftedMemberPurchaseFormat,
                MembershipEventType.GiftRedemption when settings.ReadYouTubeGiftRedemptions => settings.GiftedMemberRedemptionFormat,
                _ => null, // Unknown or filtered out membership type
            },

            // Handle Follows
            FollowEvent _ when settings.ReadFollows => settings.FollowMessageFormat,

            // Handle Raids (with minimum viewer check)
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

    // FormatMessage remains largely the same as it already handles the placeholders correctly
    // based on the event data passed to it. The filtering logic is now handled by GetFormatString.
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
                evt?.GetType().Name ?? "Unknown"
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
                        string? valueString = (eventData, placeholder.ToLowerInvariant()) switch
                        {
                            // --- Generic Placeholders ---
                            (DonationEvent { Username: var uname }, "username") => uname,
                            (SubscriptionEvent { Username: var uname }, "username") => uname, // Gifter if gift, Subber if not
                            (MembershipEvent { Username: var uname }, "username") => uname, // Member or Recipient (for GiftRedemption), Gifter (for GiftPurchase)
                            (FollowEvent { Username: var uname }, "username") => uname,
                            (RaidEvent { RaiderUsername: var rname }, "username") => rname,

                            (DonationEvent de, "amount") => $"{de.Amount} {de.Currency}", // Pass amount AND currency to normalizer
                            (SubscriptionEvent { IsGift: true, GiftCount: var count } se, "amount") when count > 1 => $"{count:N0}", // Gift Bomb Count
                            (MembershipEvent { MembershipType: MembershipEventType.GiftPurchase, GiftCount: { } giftCount } me, "amount") =>
                                $"{giftCount:N0}", // YouTube Gift Purchase Count
                            (RaidEvent re, "amount") => $"{re.ViewerCount:N0}", // Raid Viewers

                            (DonationEvent { RawMessage: var msg }, "message") => msg,
                            (SubscriptionEvent { IsGift: false, Message: var msg }, "message") => msg, // User message only for Resubs
                            (MembershipEvent { MembershipType: MembershipEventType.Milestone, ParsedMessage: var segments }, "message") =>
                                string.Join(" ", segments.OfType<TextSegment>().Select(ts => ts.Text)), // User comment only for Milestones

                            // --- Twitch Specific Placeholders ---
                            (SubscriptionEvent { IsGift: true, RecipientUsername: var rcp }, "recipient") => rcp,
                            (SubscriptionEvent { IsGift: false, CumulativeMonths: var m } se, "months") when m > 0 => m.ToString(),
                            (SubscriptionEvent { Tier: var t }, "tier") => t,

                            // --- YouTube Specific Placeholders ---
                            (MembershipEvent { MembershipType: MembershipEventType.Milestone, MilestoneMonths: { } mm }, "months") => mm.ToString(),
                            (MembershipEvent { LevelName: var lvl }, "tier") => lvl,
                            (MembershipEvent { MembershipType: MembershipEventType.GiftPurchase, GifterUsername: var gname }, "gifter") => gname,

                            // --- Default Case ---
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
        }
    }

    [GeneratedRegex(@"\{(\w+)\}", RegexOptions.Compiled | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex PlaceholderRegex();

    [GeneratedRegex(@"\s{2,}", RegexOptions.Compiled, matchTimeoutMilliseconds: 100)]
    private static partial Regex ConsecutiveSpacesRegex();
}
