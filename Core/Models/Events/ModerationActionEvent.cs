namespace StreamWeaver.Core.Models.Events;

public enum ModerationActionType
{
    Ban,
    Timeout,
    ClearMessage,
    ClearChat,
}

public class ModerationActionEvent : BaseEvent
{
    public string Channel { get; set; } = string.Empty;
    public ModerationActionType Action { get; set; }
    public string? TargetUsername { get; set; } // User acted upon (if applicable)
    public string? TargetUserId { get; set; }
    public int? DurationSeconds { get; set; } // For timeouts
    public string? TargetMessageId { get; set; } // For message clear
    public string? ModeratorUsername { get; set; } // Usually not provided by basic IRC events
    public string? Reason { get; set; } // Usually not provided by basic IRC events
    public string? Message { get; set; } // Context message (e.g., for cleared msg)
}
