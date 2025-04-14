using CommunityToolkit.Mvvm.ComponentModel;

namespace StreamWeaver.Core.Models.Settings;

public partial class ChatOverlaySettings : ObservableObject
{
    [ObservableProperty]
    public partial int MaxMessages { get; set; } = 20;

    [ObservableProperty]
    public partial string Font { get; set; } = "Segoe UI";

    [ObservableProperty]
    public partial int FontSize { get; set; } = 14;

    [ObservableProperty]
    public partial string TextColor { get; set; } = "#FFFFFF";

    [ObservableProperty]
    public partial string BackgroundColor { get; set; } = "rgba(0, 0, 0, 0.5)";

    [ObservableProperty]
    public partial bool ShowBadges { get; set; } = true;

    [ObservableProperty]
    public partial bool ShowPlatformIcons { get; set; } = true;

    [ObservableProperty]
    public partial bool ShowEmotes { get; set; } = true;

    [ObservableProperty]
    public partial bool FadeMessages { get; set; } = true;

    [ObservableProperty]
    public partial int FadeDelaySeconds { get; set; } = 30;

    [ObservableProperty]
    public partial bool UsePlatformColors { get; set; } = true;

    [ObservableProperty]
    public partial string TimestampFormat { get; set; } = "HH:mm";

    [ObservableProperty]
    public partial string HighlightColor { get; set; } = "#FFD700";

    [ObservableProperty]
    public partial string SubColor { get; set; } = "#8A2BE2";

    [ObservableProperty]
    public partial string DonationColor { get; set; } = "#1E90FF";
}
