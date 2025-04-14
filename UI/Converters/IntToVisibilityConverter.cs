using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace StreamWeaver.UI.Converters;

public partial class IntToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        bool invert = parameter is string s && s.Equals("Invert", StringComparison.OrdinalIgnoreCase);
        int count = 0;
        if (value is int i)
        {
            count = i;
        }

        bool isVisible = count > 0;
        if (invert)
        {
            isVisible = !isVisible;
        }

        return isVisible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
}
