using CommunityToolkit.Mvvm.ComponentModel;

namespace StreamWeaver.Modules.Goals;

public enum GoalType
{
    Subscriptions,
    Followers,
    Donations,
}

public partial class GoalSettings : ObservableObject
{
    [ObservableProperty]
    public partial bool Enabled { get; set; } = false;

    [ObservableProperty]
    public partial GoalType GoalType { get; set; } = GoalType.Subscriptions;

    [ObservableProperty]
    public partial string GoalLabel { get; set; } = string.Empty;

    [ObservableProperty]
    public partial int SubGoalTarget { get; set; } = 50;

    [ObservableProperty]
    public partial int FollowerGoalTarget { get; set; } = 100;

    [ObservableProperty]
    public partial decimal DonationGoalTarget { get; set; } = 250.00m;

    [ObservableProperty]
    public partial string DonationGoalCurrency { get; set; } = "USD";

    [ObservableProperty]
    public partial int PersistedSubCount { get; set; } = 0;
}
