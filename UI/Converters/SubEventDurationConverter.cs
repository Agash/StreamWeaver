using Microsoft.UI.Xaml.Data;
using StreamWeaver.Core.Models.Events;

namespace StreamWeaver.UI.Converters;

// Creates duration string like " for X months!" or " (X months)" etc. based on SubEvent properties
public partial class SubEventDurationConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is SubscriptionEvent subEvent)
        {
            if (subEvent.IsGift)
            {
                if (subEvent.Months > 1)
                    return $" ({subEvent.Months} months)";
            }
            else
            {
                if (subEvent.CumulativeMonths > 1)
                    return $" for {subEvent.CumulativeMonths} months!";
            }
        }

        return string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
}
