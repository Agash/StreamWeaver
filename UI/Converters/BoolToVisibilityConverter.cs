using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace StreamWeaver.UI.Converters;

public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        bool invert = parameter is string s && s.Equals("Invert", StringComparison.OrdinalIgnoreCase);
        bool boolValue = value is bool b && b;

        return invert
            ? boolValue
                ? Visibility.Collapsed
                : Visibility.Visible
            : (object)(boolValue ? Visibility.Visible : Visibility.Collapsed);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
}
