namespace StreamWeaver.Core.Models.Events;

public class RaidEvent : BaseEvent
{
    public string RaiderUsername { get; init; } = string.Empty;
    public string? RaiderUserId { get; init; }
    public int ViewerCount { get; init; } = 0;
}
