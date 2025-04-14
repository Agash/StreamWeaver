using Microsoft.UI.Xaml.Data;
using StreamWeaver.Core.Models.Events;

namespace StreamWeaver.UI.Converters;

// Returns " to [RecipientName]" if IsGift is true and RecipientUsername exists, otherwise empty string.
public partial class SubEventRecipientConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is SubscriptionEvent subEvent && subEvent.IsGift && !string.IsNullOrEmpty(subEvent.RecipientUsername))
        {
            return $" to {subEvent.RecipientUsername}";
        }

        return string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
}
