namespace StreamWeaver.Core.Models.Events;

/// <summary>
/// Represents the invocation of a command by a user and the subsequent reply (if any) by the bot/plugin.
/// This event is used to group the command and reply visually in the chat log.
/// </summary>
public class CommandInvocationEvent : BaseEvent
{
    /// <summary>
    /// The original chat message event that triggered the command.
    /// </summary>
    public required ChatMessageEvent OriginalCommandMessage { get; init; }

    /// <summary>
    /// The text message sent back by the bot/plugin as a reply.
    /// Can be null or empty if the command was handled without generating a textual reply.
    /// </summary>
    public string? ReplyMessage { get; init; }

    /// <summary>
    /// The display name of the bot/account that is considered to have sent the reply.
    /// </summary>
    public required string BotSenderDisplayName { get; init; }

    public CommandInvocationEvent()
    {
        // Inherit Platform and OriginatingAccountId from the OriginalCommandMessage
        Platform = OriginalCommandMessage?.Platform ?? "System";
        OriginatingAccountId = OriginalCommandMessage?.OriginatingAccountId;
    }
}
