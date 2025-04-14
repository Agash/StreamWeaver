using Microsoft.UI.Xaml.Data;

namespace StreamWeaver.UI.Converters;

// Converts IsGift (bool) to " gifted a " or " subscribed with a "
public class SubEventVerbConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is bool isGift
            ? isGift
                ? " gifted a "
                : " subscribed with a "
            : (object)" subscribed with a ";

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
}
