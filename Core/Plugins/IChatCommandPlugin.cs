using Microsoft.Extensions.Logging;
using StreamWeaver.Core.Models.Events;

namespace StreamWeaver.Core.Plugins;

public record ChatCommandContext
{
    public required string Command { get; init; }
    public required string Arguments { get; init; }
    public required ChatMessageEvent OriginalEvent { get; init; }
    public required IPluginHost Host { get; init; }

    /// <summary>
    /// (Optional Helper) Sends an explicit reply message back.
    /// The primary reply mechanism is returning a non-null string from HandleCommandAsync.
    /// Use this helper primarily for sending *additional* messages or performing
    /// follow-up actions beyond the initial reply string.
    /// </summary>
    public Task ReplyAsync(string message)
    {
        ILogger<ChatCommandContext> logger = App.GetService<ILogger<ChatCommandContext>>();

        if (Host == null)
        {
            logger.LogError("Cannot ReplyAsync: Host is null.");
            return Task.CompletedTask;
        }

        if (OriginalEvent.OriginatingAccountId == null)
        {
            logger.LogError("Cannot ReplyAsync: OriginatingAccountId on original event is null.");
            return Task.CompletedTask;
        }

        string? target = OriginalEvent.Platform switch
        {
            "Twitch" => OriginalEvent.Username,
            "YouTube" => OriginalEvent.OriginatingAccountId,
            _ => null,
        };

        if (target == null)
        {
            logger.LogWarning("Cannot determine reply target for platform '{Platform}'.", OriginalEvent.Platform);
            return Task.CompletedTask;
        }

        logger.LogInformation(
            "Sending explicit reply via {Platform} account {AccountId} to target {Target}: {Message}",
            OriginalEvent.Platform,
            OriginalEvent.OriginatingAccountId,
            target,
            message
        );

        return Host.SendChatMessageAsync(OriginalEvent.Platform, OriginalEvent.OriginatingAccountId, target, message);
    }
}

public interface IChatCommandPlugin : IPlugin
{
    IEnumerable<string> Commands { get; }

    /// <summary>
    /// Called asynchronously by StreamWeaver when a chat message matches one of the commands.
    /// </summary>
    /// <param name="context">Contextual information about the executed command.</param>
    /// <returns>
    /// A task resolving to: bool: true if the command was handled successfully and original message should be suppressed.
    /// </returns>
    Task<bool> HandleCommandAsync(ChatCommandContext context);
}
