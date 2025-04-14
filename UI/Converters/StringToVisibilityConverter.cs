using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace StreamWeaver.UI.Converters;

// Converts a string to Visibility (Visible if not null/empty, Collapsed if null/empty, or inverted)
public partial class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        bool invert = parameter is string s && s.Equals("Invert", StringComparison.OrdinalIgnoreCase);
        bool isVisible = !string.IsNullOrEmpty(value as string);

        if (invert)
        {
            isVisible = !isVisible;
        }

        return isVisible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
}
