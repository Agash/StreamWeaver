namespace StreamWeaver.Core.Models.Events.Messages;

/// <summary>
/// Represents a single option within a poll.
/// </summary>
/// <param name="Text">The text content of the poll option.</param>
/// <param name="VotePercentage">The formatted string representing the percentage of votes this option has (e.g., "50%"). Null if unavailable.</param>
/// <param name="VoteCount">The approximate number of votes this option has. Null if unavailable.</param>
public record PollOption(string Text, string? VotePercentage, ulong? VoteCount);
