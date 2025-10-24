using StreamWeaver.Modules.Goals;
using StreamWeaver.Modules.Subathon;
using System.Text.Json.Serialization;

namespace StreamWeaver.Core.Models.Events;

/// <summary>
/// Base class for all real-time events (chat, subs, donations, etc.).
/// </summary>
/// [JsonDerivedType(typeof(BotMessageEvent), typeDiscriminator: "BotMessageEvent")]
[JsonDerivedType(typeof(ChatMessageEvent), typeDiscriminator: "ChatMessageEvent")]
[JsonDerivedType(typeof(CommandInvocationEvent), typeDiscriminator: "CommandInvocationEvent")]
[JsonDerivedType(typeof(DonationEvent), typeDiscriminator: "DonationEvent")]
[JsonDerivedType(typeof(FollowEvent), typeDiscriminator: "FollowEvent")]
[JsonDerivedType(typeof(HostEvent), typeDiscriminator: "HostEvent")]
[JsonDerivedType(typeof(MembershipEvent), typeDiscriminator: "MembershipEvent")]
[JsonDerivedType(typeof(ModerationActionEvent), typeDiscriminator: "ModerationActionEvent")] // Include even if not rendered yet
[JsonDerivedType(typeof(RaidEvent), typeDiscriminator: "RaidEvent")]
[JsonDerivedType(typeof(SubscriptionEvent), typeDiscriminator: "SubscriptionEvent")]
[JsonDerivedType(typeof(SystemMessageEvent), typeDiscriminator: "SystemMessageEvent")]
[JsonDerivedType(typeof(UserStatusEvent), typeDiscriminator: "UserStatusEvent")] // Include even if not rendered yet
[JsonDerivedType(typeof(WhisperEvent), typeDiscriminator: "WhisperEvent")] // Include even if not rendered yet
[JsonDerivedType(typeof(YouTubePollUpdateEvent), typeDiscriminator: "YouTubePollUpdateEvent")]
[JsonDerivedType(typeof(GoalUpdateEvent), typeDiscriminator: "GoalUpdateEvent")] // From Modules
[JsonDerivedType(typeof(SubTimerUpdateEvent), typeDiscriminator: "SubTimerUpdateEvent")] // From Modules
public abstract class BaseEvent
{
    public string Id { get; init; } = Guid.NewGuid().ToString(); // Unique ID for this event instance in the app
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public string Platform { get; init; } = "Unknown"; // "Twitch", "YouTube", "Streamlabs", etc.

    /// <summary>
    /// Identifier for the specific account connection that received this event (e.g., Twitch User ID, YouTube Channel ID).
    /// Can be null for system messages or events not tied to a specific connection instance.
    /// </summary>
    public string? OriginatingAccountId { get; init; }
}
