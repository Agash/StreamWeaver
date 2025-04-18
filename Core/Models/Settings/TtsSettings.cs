using CommunityToolkit.Mvvm.ComponentModel;

namespace StreamWeaver.Core.Models.Settings;

public enum TtsEngine
{
    WindowsEngine,
    KokoroEngine,
    PiperEngine,
}

public partial class TtsSettings : ObservableObject
{
    public const string WindowsEngine = "Windows";
    public const string KokoroEngine = "Kokoro";

    [ObservableProperty]
    public partial bool Enabled { get; set; } = false;

    [ObservableProperty]
    public partial string SelectedEngine { get; set; } = WindowsEngine;

    [ObservableProperty]
    public partial string? SelectedWindowsVoice { get; set; }

    [ObservableProperty]
    public partial string? SelectedKokoroVoice { get; set; }

    [ObservableProperty]
    public partial int Volume { get; set; } = 80;

    [ObservableProperty]
    public partial int Rate { get; set; } = 0;

    [ObservableProperty]
    public partial bool ReadTwitchSubs { get; set; } = true;

    [ObservableProperty]
    public partial bool ReadTwitchBits { get; set; } = true;

    [ObservableProperty]
    public partial bool ReadYouTubeMemberships { get; set; } = true;

    [ObservableProperty]
    public partial bool ReadSuperChats { get; set; } = true;

    [ObservableProperty]
    public partial bool ReadStreamlabsDonations { get; set; } = true;

    [ObservableProperty]
    public partial bool ReadFollows { get; set; } = false;

    [ObservableProperty]
    public partial bool ReadRaids { get; set; } = false;

    [ObservableProperty]
    public partial int MinimumBitAmountToRead { get; set; } = 100;

    [ObservableProperty]
    public partial int MinimumRaidViewersToRead { get; set; } = 10;

    [ObservableProperty]
    public partial double MinimumSuperChatAmountToRead { get; set; } = 1.00;

    [ObservableProperty]
    public partial double MinimumDonationAmountToRead { get; set; } = 1.00;

    [ObservableProperty]
    public partial string DonationMessageFormat { get; set; } = "{username} donated {amount}! {message}";

    [ObservableProperty]
    public partial string BitsMessageFormat { get; set; } = "{username} cheered {amount}! {message}";

    [ObservableProperty]
    public partial string SuperChatMessageFormat { get; set; } = "{username} sent a Super Chat for {amount}! {message}";

    [ObservableProperty]
    public partial string NewSubMessageFormat { get; set; } = "{username} just subscribed!";

    [ObservableProperty]
    public partial string ResubMessageFormat { get; set; } = "{username} subscribed for {months} months! {message}";

    [ObservableProperty]
    public partial string GiftSubMessageFormat { get; set; } = "{username} gifted a sub to {recipient}!";

    [ObservableProperty]
    public partial string GiftBombMessageFormat { get; set; } = "{username} gifted {amount} subs!";

    [ObservableProperty]
    public partial string NewMemberMessageFormat { get; set; } = "{username} just became a member!";

    [ObservableProperty]
    public partial string MemberMilestoneFormat { get; set; } = "{username} has been a member for {months} months! {message}";

    [ObservableProperty]
    public partial string FollowMessageFormat { get; set; } = "{username} just followed!";

    [ObservableProperty]
    public partial string RaidMessageFormat { get; set; } = "{username} is raiding with {amount} viewers!";
}
