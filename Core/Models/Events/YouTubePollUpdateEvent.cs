using StreamWeaver.Core.Models.Events.Messages;

namespace StreamWeaver.Core.Models.Events;

/// <summary>
/// Represents an update for a YouTube Live Chat Poll.
/// This event is typically generated when YouTube sends a message
/// displaying the current state of an active or recently closed poll.
/// </summary>
public class YouTubePollUpdateEvent : BaseEvent
{
    /// <summary>
    /// A unique identifier for the poll instance within the live chat.
    /// </summary>
    public required string PollId { get; init; }

    /// <summary>
    /// The question text of the poll.
    /// </summary>
    public required string Question { get; init; }

    /// <summary>
    /// The list of options available in the poll, potentially including vote counts/percentages.
    /// </summary>
    public List<PollOption> Options { get; init; } = [];

    /// <summary>
    /// Indicates whether the poll is currently active or closed.
    /// </summary>
    public bool IsActive { get; init; }

    public YouTubePollUpdateEvent() => Platform = "YouTube";
}
