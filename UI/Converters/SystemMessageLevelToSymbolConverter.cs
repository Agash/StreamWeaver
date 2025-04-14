using Microsoft.UI.Xaml.Data;
using StreamWeaver.Core.Models.Events;

namespace StreamWeaver.UI.Converters;

public class SystemMessageLevelToSymbolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is SystemMessageLevel level)
        {
            // Using Segoe Fluent Icons hex codes
            return level switch
            {
                SystemMessageLevel.Info => "\uE946", // Info
                SystemMessageLevel.Warning => "\uE7BA", // Warning
                SystemMessageLevel.Error => "\uEA39", // ErrorBadge
                _ => "\uE946", // Default to Info
            };
        }

        return "\uE946"; // Default Info
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
}
