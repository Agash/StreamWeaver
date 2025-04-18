using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using StreamWeaver.Core.Models.Events;

namespace StreamWeaver.UI.Selectors;

public partial class EventTemplateSelector : DataTemplateSelector
{
    private static readonly Lazy<ILogger<EventTemplateSelector>?> s_lazyLogger = new(() =>
    {
        try
        {
            return App.GetService<ILogger<EventTemplateSelector>>();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[EventTemplateSelector.StaticInit] Failed to get ILogger: {ex.Message}");
            return null;
        }
    });
    private static ILogger<EventTemplateSelector>? Logger => s_lazyLogger.Value;

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

    protected override DataTemplate? SelectTemplateCore(object item, DependencyObject? container)
    {
        string itemType = item?.GetType().Name ?? "null";
        DataTemplate? selectedTemplate = item switch
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
            _ => DefaultTemplate,
        };

        string templateName = selectedTemplate switch
        {
            _ when selectedTemplate == ChatMessageTemplate => nameof(ChatMessageTemplate),
            _ when selectedTemplate == SubscriptionTemplate => nameof(SubscriptionTemplate),
            _ when selectedTemplate == DonationTemplate => nameof(DonationTemplate),
            _ when selectedTemplate == MembershipTemplate => nameof(MembershipTemplate),
            _ when selectedTemplate == FollowTemplate => nameof(FollowTemplate),
            _ when selectedTemplate == RaidTemplate => nameof(RaidTemplate),
            _ when selectedTemplate == HostTemplate => nameof(HostTemplate),
            _ when selectedTemplate == SystemMessageTemplate => nameof(SystemMessageTemplate),
            _ when selectedTemplate == YouTubePollUpdateTemplate => nameof(YouTubePollUpdateTemplate),
            _ when selectedTemplate == ModerationActionTemplate => nameof(ModerationActionTemplate),
            _ when selectedTemplate == WhisperTemplate => nameof(WhisperTemplate),
            _ when selectedTemplate == BotMessageTemplate => nameof(BotMessageTemplate),
            _ when selectedTemplate == CommandInvocationTemplate => nameof(CommandInvocationTemplate),
            _ when selectedTemplate == DefaultTemplate => nameof(DefaultTemplate),
            _ => "Unknown/Null",
        };

        Logger?.LogTrace("SelectTemplateCore for item type '{ItemType}' returned template: {TemplateName}", itemType, templateName);

        return selectedTemplate ?? DefaultTemplate ?? base.SelectTemplateCore(item, container);
    }
}
