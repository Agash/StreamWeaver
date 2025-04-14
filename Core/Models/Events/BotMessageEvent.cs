using StreamWeaver.Core.Models.Events.Messages;

namespace StreamWeaver.Core.Models.Events;

/// <summary>
/// Represents a message sent by the StreamWeaver application or one of its plugins.
/// This is used for local display in the chat view.
/// </summary>
public class BotMessageEvent : BaseEvent
{
    /// <summary>
    /// The display name of the bot/account that sent the message.
    /// </summary>
    public required string SenderDisplayName { get; init; }

    /// <summary>
    /// The account ID (e.g., Twitch UserID, YouTube ChannelID) that sent the message.
    /// </summary>
    public required string SenderAccountId { get; init; }

    /// <summary>
    /// The raw text content of the message that was sent.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// The target channel (Twitch) or chat ID (YouTube) where the message was sent.
    /// </summary>
    public required string Target { get; init; }

    /// <summary>
    /// Optional: Parsed message segments if we want to support emotes/formatting in bot replies.
    /// For now, we might just use the raw Message.
    /// </summary>
    public List<MessageSegment> ParsedMessage { get; init; } = [];

    public BotMessageEvent()
    {
        // Platform should reflect the platform it was sent TO
        // SenderDisplayName/SenderAccountId identify the sending identity
    }
}
