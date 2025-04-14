using Microsoft.UI;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace StreamWeaver.UI.Converters;

/// <summary>
/// Converts a platform string identifier ("Twitch", "YouTube", etc.) into a SolidColorBrush
/// representing the platform's brand color.
/// </summary>
public partial class PlatformToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush s_twitchBrush = new(Color.FromArgb(255, 145, 70, 255));
    private static readonly SolidColorBrush s_youTubeBrush = new(Color.FromArgb(255, 255, 0, 0));
    private static readonly SolidColorBrush s_streamlabsBrush = new(Color.FromArgb(255, 128, 245, 160));
    private static readonly SolidColorBrush s_systemBrush = new(Colors.Gray);
    private static readonly SolidColorBrush s_defaultBrush = new(Colors.DimGray);

    public object Convert(object? value, Type targetType, object parameter, string language) =>
        (value as string)?.ToLowerInvariant() switch
        {
            "twitch" => s_twitchBrush,
            "youtube" => s_youTubeBrush,
            "streamlabs" => s_streamlabsBrush,
            "system" => s_systemBrush,
            _ => s_defaultBrush,
        };

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
}
