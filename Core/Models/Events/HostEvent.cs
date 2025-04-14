namespace StreamWeaver.Core.Models.Events;

public class HostEvent : BaseEvent
{
    public bool IsHosting { get; init; } // True if WE started hosting someone, False if WE are being hosted or hosting stopped
    public string? HosterUsername { get; init; } // Channel hosting us
    public string? HostedChannel { get; init; } // Channel we are hosting
    public int ViewerCount { get; init; } = 0;
    public bool IsAutoHost { get; init; } = false;
}
