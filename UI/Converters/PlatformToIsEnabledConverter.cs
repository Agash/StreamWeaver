using Microsoft.UI.Xaml.Data;

namespace StreamWeaver.UI.Converters;

/// <summary>
/// Converts a Platform string ("YouTube", "Twitch", etc.) to a Boolean value.
/// Returns true if the platform is "YouTube" (case-insensitive), false otherwise.
/// Can be inverted using parameter "Invert".
/// </summary>
public partial class PlatformToIsEnabledConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        bool invert = parameter is string s && s.Equals("Invert", StringComparison.OrdinalIgnoreCase);
        bool isYouTube = value is string platform && platform.Equals("YouTube", StringComparison.OrdinalIgnoreCase);

        if (invert)
        {
            isYouTube = !isYouTube;
        }

        return isYouTube;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
}
