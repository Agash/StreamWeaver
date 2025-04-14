using CommunityToolkit.Mvvm.ComponentModel;

namespace StreamWeaver.Modules.Subathon;

public partial class SubathonSettings : ObservableObject
{
    [ObservableProperty]
    public partial bool Enabled { get; set; } = false;

    [ObservableProperty]
    public partial int InitialDurationMinutes { get; set; } = 60;

    [ObservableProperty]
    public partial int MaximumDurationMinutes { get; set; } = 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSubConfigEnabled))]
    public partial bool AddTimeForSubs { get; set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBitsConfigEnabled))]
    public partial bool AddTimeForBits { get; set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDonationConfigEnabled))]
    public partial bool AddTimeForDonations { get; set; } = true;

    [ObservableProperty]
    public partial int SecondsPerSubTier1 { get; set; } = 30;

    [ObservableProperty]
    public partial int SecondsPerSubTier2 { get; set; } = 60;

    [ObservableProperty]
    public partial int SecondsPerSubTier3 { get; set; } = 120;

    [ObservableProperty]
    public partial int SecondsPerGiftSub { get; set; } = 30;

    [ObservableProperty]
    public partial int BitsPerSecond { get; set; } = 10;

    [ObservableProperty]
    public partial decimal AmountPerSecond { get; set; } = 0.10m;

    [ObservableProperty]
    public partial string DonationCurrencyAssumption { get; set; } = "USD";

    [ObservableProperty]
    public partial long PersistedEndTimeUtcTicks { get; set; } = 0;

    public bool IsSubConfigEnabled => AddTimeForSubs;
    public bool IsBitsConfigEnabled => AddTimeForBits;
    public bool IsDonationConfigEnabled => AddTimeForDonations;
}
