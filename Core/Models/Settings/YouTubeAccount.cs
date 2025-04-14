using CommunityToolkit.Mvvm.ComponentModel;

namespace StreamWeaver.Core.Models.Settings;

public partial class YouTubeAccount : ObservableObject
{
    [ObservableProperty]
    public partial string ChannelName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? ChannelId { get; set; }

    [ObservableProperty]
    public partial bool AutoConnect { get; set; } = true;

    /// <summary>
    /// Gets or sets an optional YouTube Live ID (Video ID) to monitor directly,
    /// bypassing the automatic active stream lookup. Useful for testing or when API lookup fails.
    /// </summary>
    [ObservableProperty]
    public partial string? OverrideVideoId { get; set; }

    // Not saved, only in-memory
    [ObservableProperty]
    [System.Text.Json.Serialization.JsonIgnore]
    public partial ConnectionStatus Status { get; set; } = ConnectionStatus.Disconnected;

    [ObservableProperty]
    [System.Text.Json.Serialization.JsonIgnore]
    public partial string? StatusMessage { get; set; }
}
