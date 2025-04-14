namespace StreamWeaver.Core.Models.Events;

public class FollowEvent : BaseEvent
{
    public string Username { get; init; } = string.Empty;
    public string? UserId { get; init; }
    // Maybe add FollowerCount if available from platform?
}
