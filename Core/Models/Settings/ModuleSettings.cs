using CommunityToolkit.Mvvm.ComponentModel;
using StreamWeaver.Modules.Goals;
using StreamWeaver.Modules.Subathon;

namespace StreamWeaver.Core.Models.Settings;

public partial class ModuleSettings : ObservableObject
{
    [ObservableProperty]
    public partial SubathonSettings Subathon { get; set; } = new();

    [ObservableProperty]
    public partial GoalSettings Goals { get; set; } = new();
}
