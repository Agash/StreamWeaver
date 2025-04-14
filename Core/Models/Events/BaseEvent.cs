namespace StreamWeaver.Core.Models.Events;

/// <summary>
/// Base class for all real-time events (chat, subs, donations, etc.).
/// </summary>
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
