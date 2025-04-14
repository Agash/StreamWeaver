using Microsoft.UI.Xaml.Data;

namespace StreamWeaver.UI.Converters;

public partial class PlatformToIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        (value as string) switch
        {
            "Twitch" => "\uE8A7", // Placeholder - Replace with actual Twitch icon glyph/image later
            "YouTube" => "\uE714", // Placeholder - YouTube glyph
            "Streamlabs" => "\uE734", // Placeholder - Money Bag glyph
            "System" => "\uE946", // Settings glyph
            _ => "\uE9CE", // Help/Question mark
        };

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
}
