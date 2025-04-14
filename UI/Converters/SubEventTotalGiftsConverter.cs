using Microsoft.UI.Xaml.Data;
using StreamWeaver.Core.Models.Events;

namespace StreamWeaver.UI.Converters;

// Shows total gifts if IsGift is true and TotalGiftCount > 0
public partial class SubEventTotalGiftsConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is SubscriptionEvent subEvent && subEvent.IsGift && subEvent.TotalGiftCount > 0)
        {
            if (subEvent.GiftCount != subEvent.TotalGiftCount)
            {
                return $" (Total Gifts: {subEvent.TotalGiftCount})";
            }
        }

        return string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
}
