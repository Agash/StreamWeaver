using System.Text;
using Microsoft.UI.Xaml.Data;
using StreamWeaver.Core.Models.Events;

namespace StreamWeaver.UI.Converters;

/// <summary>
/// Converts a MembershipEvent object into a formatted string containing relevant details
/// like Level, Milestone Months, Gifter, and Gift Count, conditionally including parts based on the event data.
/// </summary>
public partial class MembershipDetailsConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not MembershipEvent me)
        {
            return string.Empty;
        }

        StringBuilder details = new();

        // Always show Level Name if available
        if (!string.IsNullOrWhiteSpace(me.LevelName))
        {
            details.Append($"Level: {me.LevelName}");
        }

        // Add Milestone Months if applicable
        if (me.MilestoneMonths is > 0)
        {
            if (details.Length > 0) details.Append(" | "); // Separator
            details.Append($"Months: {me.MilestoneMonths}");
        }

        // Add Gifter if applicable (Gift Purchase event)
        if (!string.IsNullOrWhiteSpace(me.GifterUsername))
        {
            if (details.Length > 0) details.Append(" | "); // Separator
            details.Append($"Gifter: {me.GifterUsername}");
        }

        // Add Gift Count if applicable (Gift Purchase event)
        if (me.GiftCount is > 0)
        {
            if (details.Length > 0) details.Append(" | "); // Separator
            details.Append($"Count: {me.GiftCount}");
        }


        return details.ToString();
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        // This converter is one-way.
        throw new NotImplementedException();
    }
}
