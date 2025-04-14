using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace StreamWeaver.UI.Converters;

/// <summary>
/// Converts a Platform string to Visibility. Visible for "YouTube", Collapsed otherwise.
/// Can be inverted using parameter "Invert".
/// </summary>
public partial class PlatformToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        bool invert = parameter is string s && s.Equals("Invert", StringComparison.OrdinalIgnoreCase);
        bool isYouTube = value is string platform && platform.Equals("YouTube", StringComparison.OrdinalIgnoreCase);

        if (invert)
        {
            isYouTube = !isYouTube;
        }

        return isYouTube ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
}
