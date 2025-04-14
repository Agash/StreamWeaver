namespace StreamWeaver.Core.Models.Events;

public enum SystemMessageLevel
{
    Info,
    Warning,
    Error,
}

public class SystemMessageEvent : BaseEvent
{
    public string Message { get; set; } = string.Empty;
    public SystemMessageLevel Level { get; set; } = SystemMessageLevel.Info;

    public SystemMessageEvent() => Platform = "System";
}
