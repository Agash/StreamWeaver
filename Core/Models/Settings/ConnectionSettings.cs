using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace StreamWeaver.Core.Models.Settings;

public partial class ConnectionSettings : ObservableObject
{
    [ObservableProperty]
    public partial ObservableCollection<TwitchAccount> TwitchAccounts { get; set; } = [];

    [ObservableProperty]
    public partial ObservableCollection<YouTubeAccount> YouTubeAccounts { get; set; } = [];

    [ObservableProperty]
    public partial string? StreamlabsTokenId { get; set; }

    [ObservableProperty]
    public partial bool EnableStreamlabs { get; set; } = false;

    [ObservableProperty]
    public partial string? DebugYouTubeLiveChatId { get; set; } = null;
}
