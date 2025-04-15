using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using StreamWeaver.Core.Models.Events;

namespace StreamWeaver.UI.Selectors;

public partial class EventTemplateSelector : DataTemplateSelector
{
    public DataTemplate? ChatMessageTemplate { get; set; }
    public DataTemplate? SubscriptionTemplate { get; set; }
    public DataTemplate? DonationTemplate { get; set; }
    public DataTemplate? MembershipTemplate { get; set; }
    public DataTemplate? FollowTemplate { get; set; }
    public DataTemplate? RaidTemplate { get; set; }
    public DataTemplate? HostTemplate { get; set; }
    public DataTemplate? SystemMessageTemplate { get; set; }
    public DataTemplate? ModerationActionTemplate { get; set; }
    public DataTemplate? WhisperTemplate { get; set; }
    public DataTemplate? BotMessageTemplate { get; set; }
    public DataTemplate? CommandInvocationTemplate { get; set; }
    public DataTemplate? YouTubePollUpdateTemplate { get; set; }
    public DataTemplate? DefaultTemplate { get; set; }

    protected override DataTemplate? SelectTemplateCore(object item) => SelectTemplateCore(item, null);

    protected override DataTemplate? SelectTemplateCore(object item, DependencyObject? container) =>
        item switch
        {
            ChatMessageEvent _ => ChatMessageTemplate,
            SubscriptionEvent _ => SubscriptionTemplate,
            DonationEvent _ => DonationTemplate,
            MembershipEvent _ => MembershipTemplate,
            FollowEvent _ => FollowTemplate,
            RaidEvent _ => RaidTemplate,
            HostEvent _ => HostTemplate,
            SystemMessageEvent _ => SystemMessageTemplate,
            YouTubePollUpdateEvent _ => YouTubePollUpdateTemplate,
            ModerationActionEvent _ => ModerationActionTemplate,
            WhisperEvent _ => WhisperTemplate,
            BotMessageEvent _ => BotMessageTemplate,
            CommandInvocationEvent _ => CommandInvocationTemplate,
            _ => DefaultTemplate ?? base.SelectTemplateCore(item, container),
        };
}
