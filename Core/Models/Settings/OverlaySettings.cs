using CommunityToolkit.Mvvm.ComponentModel;

namespace StreamWeaver.Core.Models.Settings;

public partial class OverlaySettings : ObservableObject
{
    [ObservableProperty]
    public partial int WebServerPort { get; set; } = 5080;

    [ObservableProperty]
    public partial ChatOverlaySettings Chat { get; set; } = new();
}
