using Microsoft.UI.Xaml.Data;
using StreamWeaver.Core.Models.Events;

namespace StreamWeaver.UI.Converters;

// Returns " to [RecipientName]" if IsGift is true and RecipientUsername exists, otherwise empty string.
public partial class SubEventRecipientConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) => value is SubscriptionEvent subEvent && subEvent.IsGift && !string.IsNullOrEmpty(subEvent.RecipientUsername)
            ? $" to {subEvent.RecipientUsername}"
            : (object)string.Empty;

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
}
