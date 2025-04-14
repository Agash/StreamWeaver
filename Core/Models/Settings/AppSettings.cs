using CommunityToolkit.Mvvm.ComponentModel;

namespace StreamWeaver.Core.Models.Settings;

public partial class AppSettings : ObservableObject
{
    [ObservableProperty]
    public partial ApiCredentials Credentials { get; set; } = new();

    [ObservableProperty]
    public partial ConnectionSettings Connections { get; set; } = new();

    [ObservableProperty]
    public partial TtsSettings TextToSpeech { get; set; } = new();

    [ObservableProperty]
    public partial OverlaySettings Overlays { get; set; } = new();

    [ObservableProperty]
    public partial ModuleSettings Modules { get; set; } = new();

    public AppSettings()
    {
        Modules ??= new();
        Modules.Subathon ??= new();
        Modules.Goals ??= new();
        Overlays ??= new();
        Overlays.Chat ??= new();
    }
}
