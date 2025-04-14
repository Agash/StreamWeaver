using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using StreamWeaver.Core.Models.Events.Messages;

namespace StreamWeaver.UI.Selectors;

public partial class MessageSegmentTemplateSelector : DataTemplateSelector
{
    public DataTemplate? TextSegmentTemplate { get; set; }
    public DataTemplate? EmoteSegmentTemplate { get; set; }

    protected override DataTemplate? SelectTemplateCore(object item) =>
        item switch
        {
            TextSegment _ => TextSegmentTemplate,
            EmoteSegment _ => EmoteSegmentTemplate,
            _ => base.SelectTemplateCore(item),
        };

    protected override DataTemplate? SelectTemplateCore(object item, DependencyObject container) => SelectTemplateCore(item);
}
