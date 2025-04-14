namespace StreamWeaver.Core.Models.Events;

public enum UserStatus
{
    Joined,
    Left,
}

public class UserStatusEvent : BaseEvent
{
    public string Username { get; set; } = string.Empty;
    public string Channel { get; set; } = string.Empty;
    public UserStatus Status { get; set; }
}
