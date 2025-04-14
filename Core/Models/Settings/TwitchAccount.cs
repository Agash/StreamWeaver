using CommunityToolkit.Mvvm.ComponentModel;

namespace StreamWeaver.Core.Models.Settings;

public partial class TwitchAccount : ObservableObject
{
    [ObservableProperty]
    public partial string Username { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? UserId { get; set; }

    [ObservableProperty]
    public partial bool AutoConnect { get; set; } = true;

    // Not saved, only in-memory
    [ObservableProperty]
    [System.Text.Json.Serialization.JsonIgnore]
    public partial ConnectionStatus Status { get; set; } = ConnectionStatus.Disconnected;

    [ObservableProperty]
    [System.Text.Json.Serialization.JsonIgnore]
    public partial string? StatusMessage { get; set; }
}
