using CommunityToolkit.Mvvm.ComponentModel;

namespace StreamWeaver.Core.Models.Settings;

public partial class ApiCredentials : ObservableObject
{
    [ObservableProperty]
    public partial string? TwitchApiClientId { get; set; }

    [ObservableProperty]
    public partial string? TwitchApiClientSecret { get; set; }

    [ObservableProperty]
    public partial string? YouTubeApiClientId { get; set; }

    [ObservableProperty]
    public partial string? YouTubeApiClientSecret { get; set; }

    public bool IsTwitchConfigured =>
        !string.IsNullOrWhiteSpace(TwitchApiClientId)
        && !string.IsNullOrWhiteSpace(TwitchApiClientSecret)
        && !TwitchApiClientId.StartsWith("YOUR_")
        && !TwitchApiClientSecret.StartsWith("YOUR_");
    public bool IsYouTubeConfigured =>
        !string.IsNullOrWhiteSpace(YouTubeApiClientId)
        && !string.IsNullOrWhiteSpace(YouTubeApiClientSecret)
        && !YouTubeApiClientId.StartsWith("YOUR_")
        && !YouTubeApiClientSecret.StartsWith("YOUR_");

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (e.PropertyName is (nameof(TwitchApiClientId)) or (nameof(TwitchApiClientSecret)))
        {
            OnPropertyChanged(nameof(IsTwitchConfigured));
        }
        else if (e.PropertyName is (nameof(YouTubeApiClientId)) or (nameof(YouTubeApiClientSecret)))
        {
            OnPropertyChanged(nameof(IsYouTubeConfigured));
        }
    }
}
